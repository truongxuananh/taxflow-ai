from __future__ import annotations

import io
import re
from datetime import datetime
from typing import Any

import cv2
import numpy as np
import pytesseract
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import Response
from pdf2image import convert_from_bytes
from PIL import Image, ImageEnhance, ImageFilter, ImageOps
from pydantic import BaseModel, Field, ConfigDict
from pytesseract import Output

app = FastAPI(title="TaxFlow Local OCR", version="1.0.0")


class Box(BaseModel):
    field: str
    value: str | None = None
    page: int = 1
    x: float = 0
    y: float = 0
    width: float = 0
    height: float = 0
    confidence: float | None = None
    source: str = "tesseract"


class Item(BaseModel):
    description: str = ""
    unit: str = ""
    quantity: float = 0
    unitPrice: float = 0
    vatRate: float = 0
    vatAmount: float = 0
    total: float = 0


class Result(BaseModel):
    provider: str = "LocalOCRService"
    supplierName: str = ""
    supplierTaxCode: str | None = None
    invoiceNumber: str = ""
    serial: str | None = None
    invoiceDate: str | None = None
    subtotal: float = 0
    vatAmount: float = 0
    total: float = 0
    confidenceScore: float = 0
    items: list[Item] = Field(default_factory=list)
    evidence: list[Box] = Field(default_factory=list)
    providerWarnings: list[str] = Field(default_factory=list)
    rawText: str = ""
    pageCount: int = 1
    modelVersion: str = "tesseract-vie-system"


def _pages(data: bytes, content_type: str) -> list[Image.Image]:
    if content_type == "application/pdf" or data[:4] == b"%PDF":
        from pdf2image import pdfinfo_from_bytes
        info = pdfinfo_from_bytes(data)
        if int(info.get("Pages", 0)) > 30:
            raise HTTPException(400, "PDF tối đa 30 trang mỗi lần xử lý.")
        return [p.convert("RGB") for p in convert_from_bytes(data, dpi=200)]
    try:
        return [Image.open(io.BytesIO(data)).convert("RGB")]
    except Exception as exc:
        raise HTTPException(400, f"Không đọc được ảnh: {exc}") from exc


def _variants(image: Image.Image) -> list[tuple[str, Image.Image]]:
    gray = ImageOps.grayscale(image)
    contrast = ImageEnhance.Contrast(gray).enhance(1.7)
    sharp = contrast.filter(ImageFilter.SHARPEN)
    arr = np.array(sharp)
    arr = cv2.fastNlMeansDenoising(arr, None, 10, 7, 21)
    otsu = cv2.threshold(arr, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)[1]
    adaptive = cv2.adaptiveThreshold(arr, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 35, 12)
    return [
        ("original", image),
        ("contrast", sharp.convert("RGB")),
        ("otsu", Image.fromarray(otsu).convert("RGB")),
        ("adaptive", Image.fromarray(adaptive).convert("RGB")),
    ]


def _ocr(image: Image.Image) -> tuple[str, list[dict[str, Any]], float]:
    best_text, best_words, best_score = "", [], -1.0
    for _, variant in _variants(image):
        data = pytesseract.image_to_data(variant, lang="vie+eng", config="--psm 6", output_type=Output.DICT)
        words: list[dict[str, Any]] = []
        confs: list[float] = []
        for i, txt in enumerate(data["text"]):
            txt = (txt or "").strip()
            try:
                conf = float(data["conf"][i])
            except Exception:
                conf = -1
            if txt and conf >= 0:
                words.append({"text": txt, "conf": conf, "left": int(data["left"][i]), "top": int(data["top"][i]), "width": int(data["width"][i]), "height": int(data["height"][i]), "block": int(data["block_num"][i]), "par": int(data["par_num"][i]), "line": int(data["line_num"][i])})
                confs.append(conf)
        text = " ".join(w["text"] for w in words)
        score = (sum(confs) / len(confs)) if confs else 0
        # Reward useful invoice tokens, not only OCR confidence.
        score += min(12, 3 * len(re.findall(r"\b\d{10}(?:\d{3})?\b", text)) + 2 * len(re.findall(r"(?i)hóa đơn|invoice|tổng|total|MST", text)))
        if score > best_score:
            best_text, best_words, best_score = text, words, score
    return best_text, best_words, max(0.0, min(1.0, best_score / 100.0))


def _money(value: str) -> float:
    cleaned = re.sub(r"[^0-9,.-]", "", value)
    if not cleaned:
        return 0
    # Vietnamese invoices commonly use dot/comma thousand separators.
    if re.fullmatch(r"\d{1,3}([.,]\d{3})+", cleaned):
        cleaned = re.sub(r"[.,]", "", cleaned)
    elif cleaned.count(",") == 1 and len(cleaned.rsplit(",", 1)[1]) <= 2:
        cleaned = cleaned.replace(".", "").replace(",", ".")
    else:
        cleaned = cleaned.replace(",", "")
    try:
        return float(cleaned)
    except ValueError:
        return 0


def _find_box(words: list[dict[str, Any]], image: Image.Image, value: str | None, field: str, page: int) -> Box | None:
    if not value:
        return None
    tokens = [t for t in re.split(r"\s+", str(value)) if t]
    if not tokens:
        return None
    needle = re.sub(r"\W", "", tokens[-1]).lower()
    for w in words:
        if needle and needle in re.sub(r"\W", "", w["text"]).lower():
            iw, ih = image.size
            return Box(field=field, value=str(value), page=page, x=w["left"]/iw, y=w["top"]/ih, width=w["width"]/iw, height=w["height"]/ih, confidence=max(0, min(1, w["conf"]/100)))
    return None


def _normalize_text_number_token(value: str) -> str:
    return re.sub(r"[^0-9]", "", value or "")


def _parse_vietnamese_amount_words(text: str) -> float:
    """Parse only an explicit amount-in-words field; unknown words fail closed."""
    value = re.sub(r"[^a-zA-ZÀ-ỹ\s]", " ", (text or "").lower())
    value = re.split(r"\bđồng\b", value)[0]
    words = value.split()
    units = {"không": 0, "một": 1, "mốt": 1, "hai": 2, "ba": 3, "bốn": 4, "tư": 4, "năm": 5, "lăm": 5, "sáu": 6, "bảy": 7, "bẩy": 7, "tám": 8, "chín": 9}
    scales = {"nghìn": 1000, "ngàn": 1000, "triệu": 1000000, "tỷ": 1000000000, "tỉ": 1000000000}
    total = current = last_digit = 0
    if not words:
        return 0
    for word in words:
        if word in units:
            last_digit = units[word]
            current += last_digit
        elif word == "trăm":
            current = current - last_digit + last_digit * 100
            last_digit = 0
        elif word == "mươi":
            current = current - last_digit + last_digit * 10
            last_digit = 0
        elif word == "mười":
            current += 10
            last_digit = 0
        elif word in scales:
            scale = scales[word]
            if scale == 1000000000 and total:
                total = (total + current) * scale
            else:
                total += current * scale
            current = last_digit = 0
        elif word in ("linh", "lẻ", "chẵn", "chan"):
            continue
        else:
            return 0
    return float(total + current)


def _group_lines(words: list[dict[str, Any]]) -> list[str]:
    groups: dict[tuple[int, int, int], list[dict[str, Any]]] = {}
    for w in words:
        groups.setdefault((w.get("block", 0), w.get("par", 0), w.get("line", 0)), []).append(w)
    lines = []
    for ws in groups.values():
        ws.sort(key=lambda x: x["left"])
        lines.append(" ".join(x["text"] for x in ws).strip())
    return lines


def _extract_money_candidates(text: str) -> list[float]:
    candidates = []
    for m in re.finditer(r"(?<![A-Za-z0-9])\d{1,3}(?:[.,]\d{3})+(?![A-Za-z0-9])|(?<![A-Za-z0-9])\d{4,}(?![A-Za-z0-9])", text):
        n = _money(m.group(0))
        if n >= 1000:
            candidates.append(n)
    return candidates


def _find_labeled_amount(lines: list[str], patterns: list[str]) -> float:
    rx = re.compile(r"(?i)(?:" + "|".join(patterns) + r")")
    # First: same-line label + amount.
    for line in lines:
        if rx.search(line):
            nums = _extract_money_candidates(line)
            if nums:
                return nums[-1]
    # Second: label on one line and value on the next few OCR lines.
    for idx, line in enumerate(lines):
        if rx.search(line):
            for nxt in lines[idx + 1: idx + 4]:
                nums = _extract_money_candidates(nxt)
                if nums:
                    return nums[-1]
    return 0


def _extract_item_totals(lines: list[str]) -> list[float]:
    """Find likely line-total values from rows containing quantity + 2 money columns."""
    totals: list[float] = []
    for line in lines:
        nums = _extract_money_candidates(line)
        if len(nums) >= 2:
            # In invoice rows the final amount column is normally the last money token.
            if re.search(r"(?i)\b(?:cái|cai|chiếc|kg|g|m|lít|lit|hộp|bộ|dịch vụ|cái)\b", line) or re.search(r"\b\d+[.,]?\d*\s+\d{1,3}(?:[.,]\d{3})+\s+\d{1,3}(?:[.,]\d{3})+\b", line):
                totals.append(nums[-1])
    return totals


def _parse(text: str, words_by_page: list[tuple[Image.Image, list[dict[str, Any]], float]]) -> Result:
    warnings: list[str] = []
    all_words = [w for _, words, _ in words_by_page for w in words]
    # Tesseract restarts block/par/line numbering on every page. Group each page
    # independently; otherwise page 1 line 1 can be merged with page 2 line 1.
    lines = [line for _, words, _ in words_by_page for line in _group_lines(words)]
    tax_matches = re.findall(r"(?<!\d)(\d{10}(?:\d{3})?)(?!\d)", text)
    supplier_tax = tax_matches[0] if tax_matches else None

    inv = re.search(r"(?i)(?:số\s*(?:hóa\s*đơn|hđ)?|invoice\s*(?:number|no\.?|#)|no\.?)\s*[:：]?\s*([A-Z0-9][A-Z0-9./_-]{2,})", text)
    invoice_number = inv.group(1).strip() if inv else ""
    serial_m = re.search(r"(?i)(?:ký\s*hiệu|serial)\s*[:：]?\s*([A-Z0-9][A-Z0-9./_-]+)", text)
    serial = serial_m.group(1).strip() if serial_m else None

    date = None
    date_m = re.search(r"(?i)(?:ngày\s*(?:lập|hóa\s*đơn)?|date)\s*[:：]?\s*(\d{1,2})[/-](\d{1,2})[/-](\d{4})", text)
    if not date_m:
        date_m = re.search(r"(?i)ngày.*?(\d{1,2})\s*(?:tháng|/|-)\s*(\d{1,2})\s*(?:năm|/|-)\s*(\d{4})", text)
    if date_m:
        try:
            date = datetime(int(date_m.group(3)), int(date_m.group(2)), int(date_m.group(1))).date().isoformat()
        except ValueError:
            warnings.append("Ngày hóa đơn OCR được nhưng không hợp lệ.")

    supplier = ""
    sup_m = re.search(r"(?i)(?:đơn\s*vị\s*bán\s*hàng|người\s*bán|seller)\s*[:：]?\s*(.{3,120}?)(?=\s+(?:mã\s*số\s*thuế|mst|tax\s*code|địa\s*chỉ|address)\b)", text)
    if sup_m:
        supplier = re.sub(r"\s+", " ", sup_m.group(1)).strip(" :-")
    if not supplier:
        comp_m = re.search(r"(?i)\b((?:công\s*ty|cty|doanh\s*nghiệp|hộ\s*kinh\s*doanh)\s+.{3,90}?)(?=\s+(?:mã\s*số\s*thuế|mst|địa\s*chỉ)\b)", text)
        supplier = re.sub(r"\s+", " ", comp_m.group(1)).strip() if comp_m else ""

    total = _find_labeled_amount(lines, [r"tổng\s*(?:cộng\s*)?(?:tiền\s*)?(?:thanh\s*toán)?", r"total\s*(?:payment|amount)?", r"cộng\s*tiền\s*bán\s*hàng\s*hóa", r"amount\s*in\s*words"])
    vat = _find_labeled_amount(lines, [r"tiền\s*thuế\s*(?:gtgt|vat)", r"thuế\s*(?:gtgt|vat)", r"vat\s*amount", r"vat"])
    subtotal = _find_labeled_amount(lines, [r"cộng\s*tiền\s*hàng", r"tiền\s*hàng", r"subtotal", r"amount\s*before\s*tax"])

    # Strong fallbacks for real-world invoice layouts where the value is separated from the label,
    # or the text layer misses the total but OCR can still see it in the row.
    amount_words_m = re.search(r"(?i)số\s*tiền\s*viết\s*bằng\s*chữ\s*(?:\(?amount\s*in\s*words\)?)?\s*[:：]?\s*(.+)", text)
    amount_words = _parse_vietnamese_amount_words(amount_words_m.group(1) if amount_words_m else "")
    item_totals = _extract_item_totals(lines)

    if total <= 0 and amount_words > 0:
        total = amount_words
        warnings.append("Tổng tiền được xác nhận thêm từ trường Số tiền viết bằng chữ.")
    if total <= 0 and item_totals:
        total = round(sum(item_totals), 2)
        warnings.append("Tổng tiền được tính từ thành tiền của các dòng hàng vì nhãn tổng tiền không rõ.")
    if subtotal <= 0 and item_totals:
        subtotal = round(sum(item_totals), 2)
    if subtotal <= 0 and total > 0 and vat <= total:
        subtotal = total - vat
    if vat <= 0:
        # Hóa đơn bán hàng/POS thường không có VAT tách riêng; không tự cộng VAT.
        vat = 0

    required = [("tên nhà cung cấp", supplier), ("MST", supplier_tax), ("số hóa đơn", invoice_number), ("ngày hóa đơn", date), ("tổng tiền", total)]
    missing = [name for name, value in required if not value]
    if missing:
        warnings.append("OCR local chưa đọc rõ: " + ", ".join(missing) + ".")

    if total > 0 and subtotal + vat != total:
        if abs(subtotal + vat - total) <= 1:
            subtotal = total - vat
        else:
            warnings.append(f"Kiểm tra lại số tiền: tiền hàng {subtotal:,.0f} + VAT {vat:,.0f} ≠ tổng {total:,.0f}.")

    avg_conf = sum(p[2] for p in words_by_page) / max(1, len(words_by_page))
    completeness = (len(required) - len(missing)) / len(required)
    financial_bonus = 0.08 if total > 0 else 0
    confidence = max(0.15, min(0.97, avg_conf * 0.50 + completeness * 0.42 + financial_bonus))

    evidence: list[Box] = []
    for page_idx, (img, words, _) in enumerate(words_by_page, start=1):
        for field, value in [("supplierName", supplier), ("supplierTaxCode", supplier_tax), ("invoiceNumber", invoice_number), ("serial", serial), ("invoiceDate", date), ("subtotal", subtotal), ("vatAmount", vat), ("total", total)]:
            box = _find_box(words, img, value, field, page_idx)
            if box and not any(b.field == field for b in evidence):
                evidence.append(box)

    return Result(supplierName=supplier, supplierTaxCode=supplier_tax, invoiceNumber=invoice_number, serial=serial, invoiceDate=date,
                  subtotal=round(subtotal, 2), vatAmount=round(vat, 2), total=round(total, 2), confidenceScore=round(confidence, 4), evidence=evidence,
                  providerWarnings=warnings, rawText=text[:30000], pageCount=len(words_by_page))


@app.get("/health")
def health() -> dict[str, Any]:
    return {"status": "healthy", "engine": "tesseract", "languages": str(pytesseract.get_languages(config=""))}



@app.post("/render")
async def render_page(file: UploadFile = File(...), page: int = Form(1), scale: float = Form(2.0)):
    """Render a document page to PNG so the review UI can always display PDFs as images."""
    data = await file.read()
    content_type = file.content_type or "application/octet-stream"
    pages = _pages(data, content_type)
    if page < 1 or page > len(pages):
        raise HTTPException(400, "Trang không hợp lệ")
    image = pages[page - 1]
    scale = max(1.0, min(3.0, float(scale)))
    if scale != 1.0:
        image = image.resize((int(image.width * scale), int(image.height * scale)), Image.Resampling.LANCZOS)
    out = io.BytesIO()
    image.save(out, format="PNG", optimize=True)
    return Response(out.getvalue(), media_type="image/png", headers={"X-Page-Count": str(len(pages))})

@app.post("/preprocess")
async def preprocess(file: UploadFile = File(...), variant: str = Form("contrast")):
    data = await file.read()
    pages = _pages(data, file.content_type or "application/octet-stream")
    variants = dict(_variants(pages[0]))
    image = variants.get(variant, variants["contrast"])
    out = io.BytesIO(); image.save(out, format="PNG")
    from fastapi.responses import Response
    return Response(out.getvalue(), media_type="image/png")


@app.post("/ocr/analyze", response_model=Result)
async def analyze(file: UploadFile = File(...)) -> Result:
    data = await file.read()
    pages = _pages(data, file.content_type or "application/octet-stream")
    words_by_page: list[tuple[Image.Image, list[dict[str, Any]], float]] = []
    texts: list[str] = []
    for page in pages:
        text, words, conf = _ocr(page)
        texts.append(text); words_by_page.append((page, words, conf))
    return _parse("\n".join(texts), words_by_page)


@app.post("/ocr/region")
async def region(file: UploadFile = File(...), page: int = Form(1), x: float = Form(...), y: float = Form(...), width: float = Form(...), height: float = Form(...), field: str = Form("region")):
    data = await file.read(); pages = _pages(data, file.content_type or "application/octet-stream")
    if page < 1 or page > len(pages): raise HTTPException(400, "Trang không hợp lệ")
    img = pages[page - 1]
    x, y, width, height = [max(0.0, min(1.0, v)) for v in (x, y, width, height)]
    if width <= 0.005 or height <= 0.005: raise HTTPException(400, "Vùng OCR quá nhỏ")
    left, top = int(x * img.width), int(y * img.height)
    right, bottom = int(min(1, x + width) * img.width), int(min(1, y + height) * img.height)
    crop = img.crop((left, top, right, bottom))
    text, words, conf = _ocr(crop)
    return {"field": field, "value": text.strip(), "confidence": round(conf, 4), "page": page,
            "boundingBox": {"x": x, "y": y, "width": width, "height": height}, "source": "local-region"}

# ---------------------------------------------------------------------------
# Financial ML endpoints
# ---------------------------------------------------------------------------
from datetime import date, timedelta
from sklearn.ensemble import IsolationForest
from sklearn.linear_model import HuberRegressor


class AnomalyRecord(BaseModel):
    model_config = ConfigDict(allow_inf_nan=False)
    id: str
    amount: float = 0
    vatRate: float = 0
    supplierFrequency: float = 0
    amountVsSupplierAverage: float = 1
    daysSinceLastInvoice: float = 0
    invoiceFrequency: float = 0


class AnomalyRequest(BaseModel):
    records: list[AnomalyRecord] = Field(default_factory=list, max_length=10000)
    contamination: float = 0.08


@app.post('/ml/anomaly')
def anomaly_detection(request: AnomalyRequest) -> dict[str, Any]:
    records = request.records
    if len(records) < 8:
        return {
            'status': 'insufficient_data',
            'method': 'IsolationForest',
            'minimumRecords': 8,
            'availableRecords': len(records),
            'results': []
        }

    X = np.array([[r.amount, r.vatRate, r.supplierFrequency,
                   r.amountVsSupplierAverage, r.daysSinceLastInvoice,
                   r.invoiceFrequency] for r in records], dtype=float)
    # Robust log transform for monetary/frequency magnitude while preserving ratios.
    X[:, 0] = np.log1p(np.maximum(X[:, 0], 0))
    X[:, 2] = np.log1p(np.maximum(X[:, 2], 0))
    X[:, 4] = np.log1p(np.maximum(X[:, 4], 0))
    X[:, 5] = np.log1p(np.maximum(X[:, 5], 0))
    # Standardize without adding another dependency/pipeline object.
    mean = X.mean(axis=0)
    std = X.std(axis=0)
    std[std < 1e-9] = 1.0
    Xs = (X - mean) / std

    contamination = max(0.01, min(0.25, float(request.contamination)))
    model = IsolationForest(n_estimators=220, contamination=contamination,
                            random_state=42, n_jobs=1)
    labels = model.fit_predict(Xs)
    # decision_function: larger is more normal. Convert to a 0..100 risk score.
    raw = -model.decision_function(Xs)
    lo, hi = float(raw.min()), float(raw.max())
    denom = hi - lo if hi - lo > 1e-9 else 1.0
    risk = 25 + ((raw - lo) / denom) * 70

    results = []
    for i, r in enumerate(records):
        score = int(round(max(0, min(100, risk[i]))))
        reasons: list[str] = []
        if r.amountVsSupplierAverage >= 3:
            reasons.append(f'Giá trị cao gấp {r.amountVsSupplierAverage:.1f} lần trung bình nhà cung cấp.')
        if r.vatRate not in (0, 5, 8, 10):
            reasons.append(f'Thuế suất {r.vatRate:g}% không thuộc nhóm phổ biến 0/5/8/10%.')
        if r.supplierFrequency <= 1:
            reasons.append('Nhà cung cấp mới hoặc rất ít lịch sử giao dịch.')
        if r.daysSinceLastInvoice >= 90:
            reasons.append('Khoảng cách từ giao dịch gần nhất của nhà cung cấp lớn.')
        is_anomaly = bool(labels[i] == -1)
        if is_anomaly and not reasons:
            reasons.append('Tổ hợp đặc trưng giao dịch khác biệt đáng kể so với lịch sử.')
        level = 'HIGH' if score >= 75 else 'MEDIUM' if score >= 50 else 'LOW'
        results.append({'id': r.id, 'isAnomaly': is_anomaly, 'riskScore': score,
                        'level': level, 'reasons': reasons})
    return {'status': 'ok', 'method': 'IsolationForest', 'scoreMeaning': 'relative ranking within this batch, not a probability of fraud',
            'model': {'estimators': 220, 'contamination': contamination,
                      'features': ['amount','vatRate','supplierFrequency','amountVsSupplierAverage','daysSinceLastInvoice','invoiceFrequency']},
            'results': results}


class DailyCashPoint(BaseModel):
    model_config = ConfigDict(allow_inf_nan=False)
    date: str
    inflow: float = 0
    outflow: float = 0


class ForecastRequest(BaseModel):
    history: list[DailyCashPoint] = Field(default_factory=list, max_length=1096)
    horizon: int = 30


def _forecast_component(values: np.ndarray, dates: list[date], horizon: int) -> tuple[np.ndarray, str]:
    n = len(values)
    x = np.arange(n, dtype=float)
    # Huber regression is resistant to one-off extreme transactions.
    model = HuberRegressor(epsilon=1.35, max_iter=300)
    model.fit(x.reshape(-1, 1), values)
    future_x = np.arange(n, n + horizon, dtype=float)
    trend = np.maximum(0, model.predict(future_x.reshape(-1, 1)))
    # Add day-of-week residual seasonality when enough history exists.
    fitted = model.predict(x.reshape(-1, 1))
    residual = values - fitted
    weekday_adj = {d: 0.0 for d in range(7)}
    for d in range(7):
        vals = [residual[i] for i, dt in enumerate(dates) if dt.weekday() == d]
        if vals:
            weekday_adj[d] = float(np.median(vals))
    future_dates = [dates[-1] + timedelta(days=i) for i in range(1, horizon + 1)]
    pred = np.array([max(0.0, trend[i] + weekday_adj[dt.weekday()]) for i, dt in enumerate(future_dates)])
    return pred, 'HuberRegressor + weekday residual seasonality'


@app.post('/ml/forecast')
def cashflow_forecast(request: ForecastRequest) -> dict[str, Any]:
    horizon = max(1, min(90, int(request.horizon)))
    parsed: list[tuple[date, float, float]] = []
    for p in request.history:
        try:
            d = datetime.fromisoformat(p.date.replace('Z', '+00:00')).date()
        except ValueError:
            continue
        parsed.append((d, max(0, p.inflow), max(0, p.outflow)))
    if len(parsed) < 14:
        return {'status': 'insufficient_data', 'minimumDays': 14,
                'availableDays': len(parsed), 'horizon': horizon, 'series': []}
    daily: dict[date, tuple[float, float]] = {}
    for d, incoming, outgoing in parsed:
        old_in, old_out = daily.get(d, (0, 0))
        daily[d] = (old_in + incoming, old_out + outgoing)
    first, last = min(daily), max(daily)
    if (last - first).days > 1095:
        raise HTTPException(400, "Lịch sử dự báo tối đa 1095 ngày.")
    parsed = [(first + timedelta(days=i), *daily.get(first + timedelta(days=i), (0, 0))) for i in range((last-first).days + 1)]
    if len(parsed) < 14:
        return {'status': 'insufficient_data', 'minimumDays': 14, 'availableDays': len(parsed), 'horizon': horizon, 'series': []}
    dates = [x[0] for x in parsed]
    inflow = np.array([x[1] for x in parsed], dtype=float)
    outflow = np.array([x[2] for x in parsed], dtype=float)
    pred_in, method = _forecast_component(inflow, dates, horizon)
    pred_out, _ = _forecast_component(outflow, dates, horizon)
    series = []
    for i in range(horizon):
        d = dates[-1] + timedelta(days=i + 1)
        pin, pout = float(pred_in[i]), float(pred_out[i])
        series.append({'date': d.isoformat(), 'projectedInflow': round(pin, 2),
                       'projectedOutflow': round(pout, 2), 'projectedNet': round(pin - pout, 2)})
    # Hold out the final 20% before fitting; no future observation enters training.
    holdout = max(3, len(parsed) // 5)
    split = len(parsed) - holdout
    test_pred, _ = _forecast_component(inflow[:split], dates[:split], holdout)
    out_pred, _ = _forecast_component(outflow[:split], dates[:split], holdout)
    mae = float(np.mean(np.abs(test_pred - inflow[split:])))
    out_mae = float(np.mean(np.abs(out_pred - outflow[split:])))
    baseline = float(np.mean(np.abs(np.mean(inflow[max(0, split-7):split]) - inflow[split:])))
    return {'status': 'ok', 'method': method, 'historicalDays': len(parsed),
            'horizon': horizon, 'quality': {'holdoutDays': holdout, 'inflowMae': round(mae, 2), 'outflowMae': round(out_mae, 2), 'baselineInflowMae': round(baseline, 2), 'metric': 'out-of-sample MAE; lower is better'},
            'series': series}
