# TaxFlow AI Upgrade

## Implemented AI modules

### 1. Hybrid Invoice Intelligence
- Local Tesseract OCR (`ai-service`) for PDF/JPG/PNG.
- Critical-field completeness check: supplier, tax code, invoice number, date, total.
- Confidence normalized to `0..1`.
- Gemini fallback when confidence is below `HYBRID_CONFIDENCE_THRESHOLD`, critical fields are missing, or financial arithmetic is inconsistent.
- If Gemini is unavailable, the result is explicitly marked for User Review instead of incorrectly saying Local OCR passed.
- OCR evidence, validation and user corrections are retained for training dataset export.

### 2. Invoice Anomaly Detection
- `IsolationForest` in `ai-service` (`POST /ml/anomaly`).
- Features: amount, VAT rate, supplier history frequency, amount/supplier average ratio, days since supplier's previous invoice, invoice frequency.
- Backend endpoint: `POST /api/ai/scan-anomalies`.
- Duplicate invoice detection remains a deterministic safety rule.
- Results become `AIAlert` records with severity, risk score and explainable reasons.
- Rule fallback is used if the ML service is unavailable.

### 3. Cash-flow ML Forecast
- `HuberRegressor` for robust trend estimation.
- Day-of-week residual seasonality.
- AI endpoint: `POST /ml/forecast`.
- Backend endpoint: `GET /api/ai/forecast?horizon=7|30|90`.
- Requires at least 14 active historical transaction days.
- Falls back to a 30-day average if the ML service is unavailable.

### 4. Grounded Financial Assistant
- Known questions are mapped directly to database queries.
- Questions about risk/anomaly use persisted `AIAlerts`.
- For other questions, Gemini can be used only with a compact financial context generated from the database.
- The prompt explicitly forbids invented numbers. Without Gemini, the assistant stays deterministic and says when a question is unsupported.

### 5. Human-in-the-loop learning data
- OCR results go through Validation -> User Review -> Confirm -> Database.
- Confirmed corrections are saved in `OCRCorrections`.
- Training dataset can be exported from `GET /api/ai/training-dataset`.

## Configure Gemini (optional)

Local OCR + ML work without Gemini. To enable cloud fallback and grounded free-form assistant, edit `.env`:

```env
GEMINI_API_KEY=your_key_here
GEMINI_MODEL=gemini-2.5-flash
AI_PROVIDER=hybrid
HYBRID_CONFIDENCE_THRESHOLD=0.82
```

Never commit a real API key to Git.

## Docker

```powershell
docker compose up -d --build
docker compose ps
```

Expected services: `mysql`, `ai-service`, `backend`, `frontend`.

## AI tests

Inside the project, the Python tests are:

```bash
python ai-service/test_invoice_financials.py
python ai-service/test_financial_ml.py
```

For end-to-end testing, upload files in `tests/fixtures/`:
- `TaxFlow-Test-Invoice-VALID.pdf`
- `TaxFlow-Test-Invoice-INVALID.pdf`

## Demo flow for presentation

1. Upload a valid invoice.
2. Show OCR confidence and extracted fields.
3. Upload the invalid invoice and show Validation/User Review.
4. Confirm corrected data to save an OCR correction.
5. Open **Trợ lý & AI tài chính -> AI Anomaly** and click **Quét bất thường**.
6. Open **Dự báo ML** and show the forecast model/method.
7. Ask the assistant about revenue, debt, cash flow or AI warnings.
