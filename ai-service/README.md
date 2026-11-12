# TaxFlow Local OCR Service

FastAPI OCR nội bộ, chạy offline bằng Tesseract (Vietnamese + English), hỗ trợ ảnh và PDF nhiều trang.

Endpoints:
- `GET /health`
- `POST /ocr/analyze` (multipart field `file`)
- `POST /ocr/region` (`file`, `page`, `x`, `y`, `width`, `height`, `field`; tọa độ 0..1)
- `POST /preprocess` (`file`, `variant=original|contrast|otsu|adaptive`)

Service này cố ý dùng dependency ARM64-friendly. PaddleOCR có thể được bổ sung dưới Docker Compose profile riêng sau khi pin được image/dependency phù hợp Apple Silicon.
