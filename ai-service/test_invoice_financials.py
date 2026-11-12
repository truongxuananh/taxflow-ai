from PIL import Image

from app.main import _parse


def _word(text: str, left: int, line: int) -> dict:
    return {
        "text": text, "conf": 95.0, "left": left, "top": 10 + line * 20,
        "width": max(10, len(text) * 8), "height": 16,
        "block": 1, "par": 1, "line": line,
    }


def test_financial_totals() -> None:
    image = Image.new("RGB", (1200, 1600), "white")
    lines = [
        ["Đơn", "vị", "bán", "hàng:", "Công", "ty", "Minh", "Tâm", "MST", "0401234567"],
        ["Số", "hóa", "đơn:", "000001"],
        ["Ngày", "lập:", "09/09/2026"],
        ["Cộng", "tiền", "hàng:", "1.300.000"],
        ["Tổng", "thanh", "toán:", "1.300.000"],
    ]
    words = []
    for line_no, tokens in enumerate(lines, start=1):
        left = 10
        for token in tokens:
            words.append(_word(token, left, line_no))
            left += max(30, len(token) * 9)
    text = " ".join(w["text"] for w in words)
    result = _parse(text, [(image, words, 0.95)])
    assert result.total == 1_300_000, result
    assert result.subtotal == 1_300_000, result
    assert result.vatAmount == 0, result


def test_multi_page_lines_do_not_merge() -> None:
    image = Image.new("RGB", (1200, 1600), "white")
    p1 = [_word("Tổng", 10, 1), _word("thanh", 80, 1), _word("toán:", 160, 1), _word("1.000.000", 250, 1)]
    p2 = [_word("Ghi", 10, 1), _word("chú", 80, 1), _word("khác", 150, 1)]
    text = " ".join(w["text"] for w in p1 + p2)
    result = _parse(text, [(image, p1, 0.95), (image, p2, 0.95)])
    assert result.total == 1_000_000, result


if __name__ == "__main__":
    test_financial_totals()
    test_multi_page_lines_do_not_merge()
    print("PASS")
