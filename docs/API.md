# API overview

Swagger: `http://localhost:5000/swagger`

| Nhóm | Endpoint chính |
| --- | --- |
| Hệ thống | `GET /health` |
| Xác thực | `POST /api/auth/login`, `GET /api/auth/me` |
| Dashboard | `GET /api/dashboard` |
| Doanh nghiệp | `GET/POST /api/companies`, `GET/POST /api/bank-accounts` |
| Sản phẩm | `GET/POST/PUT/DELETE /api/products` |
| Kho | `GET/POST /api/inventory/transactions`, `GET /api/inventory/low-stock` |
| POS | `POST /api/orders`, `GET /api/orders` |
| Hóa đơn | `GET /api/invoices`, `POST /api/invoices/{id}/issue` |
| OCR | `POST /api/documents/upload`, `POST /api/documents/{id}/confirm` |
| Thu chi | `GET/POST /api/incomes`, `GET/POST /api/expenses`, `GET /api/cash-flow` |
| Công nợ | `GET /api/receivables`, `GET /api/payables` |
| Thuế | `GET /api/tax/summary`, `POST /api/tax/draft-report` |
| AI | `POST /api/ai/assistant`, `GET /api/ai/alerts`, `GET /api/ai/forecast` |
| Báo cáo | `GET /api/reports/summary`, `GET /api/exports/{dataset}` |

Gửi JWT bằng `Authorization: Bearer <token>`. Frontend tự gửi `X-Company-Id`
theo doanh nghiệp đang chọn.
