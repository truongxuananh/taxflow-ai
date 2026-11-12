# TaxFlow AI — fixes applied

Bản này đã được chỉnh để dễ chạy ổn định hơn bằng Docker Desktop trên Windows x86-64 và Mac Apple Silicon.

## Lỗi đã sửa

1. **Backend Dockerfile bị khóa ARM64**
   - Trước: `mcr.microsoft.com/dotnet/sdk:8.0.422-bookworm-slim-arm64v8`
   - Sau: `mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim` (multi-architecture).

2. **Architecture test cho kết quả pass giả**
   - Script trước chỉ bắt `linux/amd64`, không bắt image `arm64v8`.
   - Đã kiểm tra cả pin AMD64/ARM64 và các image tag khóa kiến trúc.

3. **README sai port backend**
   - Docker Compose public backend ở `http://localhost:5001`, không phải `5000`.
   - Swagger: `http://localhost:5001/swagger`.

4. **README mô tả AI provider không khớp cấu hình thực tế**
   - Mặc định đã thống nhất là `AI_PROVIDER=hybrid`, dùng local OCR service trước.

5. **OCR PDF nhiều trang có thể ghép nhầm dòng giữa các trang**
   - Tesseract reset `block/par/line` trên mỗi trang. Code cũ group toàn bộ từ của mọi trang cùng lúc.
   - Đã group line riêng từng trang trước khi parse.

6. **OCR test phụ thuộc file cứng `/tmp/inv/page.png`**
   - Đã thay bằng test self-contained, không cần file ngoài.

7. **File `.env` có khả năng chứa API key thật**
   - Đã thay bằng cấu hình local an toàn, không chứa OpenAI/Gemini key.
   - Nếu cần cloud OCR, tự điền key vào `.env` trên máy của bạn.

8. **Dọn build artifacts**
   - Không đóng gói `node_modules`, `bin`, `obj`, `dist` để tránh lỗi khác kiến trúc/máy.

## Kiểm tra đã chạy trong môi trường sửa file

- `tests/verify-architecture.sh`: PASS
- `python3 -m py_compile ai-service/app/main.py ai-service/test_invoice_financials.py`: PASS
- `python3 ai-service/test_invoice_financials.py`: PASS

Không thể chạy `docker compose up` hoặc `dotnet test` trong môi trường sửa file vì Docker và .NET SDK không được cài ở đây. Hãy dùng Docker Desktop trên máy của bạn theo README để thực hiện smoke test toàn hệ thống.

## Backend hardening/completion pass (2026-09-14)

### POS / Sales
- Fixed the MySQL EF Core retry-strategy crash when POS uses an explicit transaction by wrapping the full transaction in `Database.CreateExecutionStrategy().ExecuteAsync(...)`.
- POS checkout now runs at `Serializable` isolation, re-reads products inside the transaction, rejects overselling, and clears the change tracker before retry attempts.
- Added validation for cart, quantities, payment method, discount, paid amount, customer, warehouse, and inactive/missing products.
- Partial payments now require a customer so receivables remain traceable.
- Stock movements, payment, income, receivable, order, and optional VAT invoice are committed atomically.
- VAT draft invoices now include a discount line when the originating order has a discount, preventing order/invoice reconciliation mismatches.

### Finance / Receivables / Payables
- Receivable settlement now also updates `Order.PaidAmount` and creates a linked `Payment`, keeping order, payment, income, and receivable ledgers consistent.
- Added rounding/zeroing for tiny receivable/payable residuals.
- Added validation for income and expense requests.

### Catalog / Inventory
- Added SKU and barcode duplicate validation for create/update.
- Added product/unit required validation and stricter inventory transaction validation.
- Added safe handling when a company has no warehouse.

### Users / Company / Master data
- Added validation for login, companies, bank accounts, users, reset password, customers, and suppliers.
- Prevented duplicate active bank-account numbers and duplicate party codes.
- Prevented an admin from locking or demoting their own currently logged-in account.
- Added company tax-code duplicate checks on update.

### Invoices / OCR / AI
- Prevented repeatedly issuing invoices that are already Pending/Issued/Cancelled.
- Prevented cancelling an invoice twice.
- Added document-confirm validation before creating accounting records.
- Added item-level OCR validation for description, quantity, prices, VAT and totals.
- Made generated supplier codes collision-resistant.

### Tax / Reporting
- Tax draft creation is now idempotent: repeated generation of the same period refreshes the existing DRAFT instead of creating duplicates.

### Diagnostics / Docker
- Unhandled backend exceptions are now written with method/path/trace id to Docker logs.
- MySQL host mapping is `3307:3306` to avoid the Windows port-3306 conflict; backend containers still use `mysql:3306` internally.
- Added `tests/backend-smoke.ps1` to exercise the main backend flows end-to-end on Windows.

## 2026-09-28 - RBAC hoàn thiện theo 4 vai trò
- Chuẩn hóa quyền ADMIN / ACCOUNTANT / EMPLOYEE / DIRECTOR ở backend.
- Tách quyền đọc và quyền ghi cho kho, tài chính, công nợ, thuế, báo cáo và AI.
- OCR hóa đơn chỉ dành cho ADMIN và ACCOUNTANT.
- DIRECTOR được xem dữ liệu quản trị/tài chính và dùng AI, nhưng không được sửa dữ liệu kế toán gốc.
- EMPLOYEE tập trung POS và dữ liệu phục vụ bán hàng.
- Menu frontend lọc theo role và route có RoleRoute để chặn truy cập trực tiếp bằng URL.
- Các nút ghi dữ liệu được ẩn với DIRECTOR ở Kho, Thu/Chi, Công nợ và Thuế.
- Bổ sung RBAC.md mô tả ma trận quyền.
