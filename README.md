# TaxFlow AI V5 — Natural Language Assistant

Mở `HOI-DAP-TU-NHIEN-V5.html` hoặc `docs/NATURAL-LANGUAGE-V5.md` để nâng cấp và thử hội thoại tự nhiên. Gemini chọn công cụ chỉ đọc; backend kiểm tra quyền, truy vấn và tính số liệu. Xem `validation/RESULTS.md` để biết những gì đã/chưa kiểm chứng.

# TaxFlow AI V4.1 — Sửa ngữ cảnh công nợ

Mở **SUA-LOI-HOI-THOAI-V4.1.html** trước. Bản này sửa câu hỏi nối tiếp như “quá hạn bao lâu”, “khoản gì”, “họ đã trả chưa”. Cần rebuild backend để chạy migration mới.

# TaxFlow AI V4 — Bản nâng cấp

Mở **HUONG-DAN-V4.html** để xem hướng dẫn chi tiết, hoặc đọc [docs/COMPLETION-V4.md](docs/COMPLETION-V4.md). Kết quả kiểm tra: [validation/RESULTS.md](validation/RESULTS.md). Nội dung bên dưới là README gốc; ưu tiên tài liệu V4 nếu có khác biệt.

# TAXFLOW AI

Hệ thống quản lý bán hàng, hóa đơn, kho, dòng tiền, công nợ và hỗ trợ quản lý
thuế doanh nghiệp tích hợp AI. Project được thiết kế để chạy trực tiếp bằng Docker Desktop trên **Windows x86-64**
hoặc **Mac Apple Silicon**, không cần cài Node.js, .NET hay MySQL native.

> **Phạm vi an toàn:** TaxFlow AI tạo số liệu quản trị và báo cáo thuế **dự thảo**.
> `DemoInvoiceProvider` không phát hành hóa đơn điện tử hợp pháp. Muốn vận hành
> thực tế phải tích hợp nhà cung cấp hóa đơn điện tử phù hợp, ký số và để kế toán
> rà soát theo quy định hiện hành.

## Quick start — Windows hoặc Mac Apple Silicon

### BƯỚC 1 — Cài Docker Desktop

- Windows: Docker Desktop + WSL2 backend.
- Mac Apple Silicon: chọn bản **Mac with Apple chip**.

### BƯỚC 2 — Kiểm tra Docker

```bash
docker --version
docker compose version
docker info
```

Trong `docker info`, kiểm tra kiến trúc là `aarch64` hoặc `arm64`.

### BƯỚC 3 — Clone hoặc giải nén project

```bash
cd TaxFlow-AI
```

### BƯỚC 4 — Tạo file môi trường

```bash
cp .env.example .env
```

Mở `.env` và thay tối thiểu các giá trị:

- `MYSQL_PASSWORD`
- `MYSQL_ROOT_PASSWORD`
- `JWT_SECRET` — chuỗi ngẫu nhiên tối thiểu 32 ký tự

### BƯỚC 5 — Chạy toàn bộ hệ thống

```bash
docker compose up --build
```

Lần đầu Docker tải image, build source, tạo schema và seed dữ liệu demo nên có
thể mất vài phút. Các lần sau sẽ nhanh hơn.

### BƯỚC 6 — Mở ứng dụng

- Frontend: <http://localhost:3000>
- Backend: <http://localhost:5001>
- Swagger: <http://localhost:5001/swagger>
- Health: <http://localhost:5001/health>
- MySQL: `localhost:3307` (container vẫn dùng `3306`)

Để dừng:

```bash
docker compose down
```

Để dừng và xóa cả dữ liệu MySQL demo:

```bash
docker compose down -v
```

Lệnh cuối sẽ xóa Docker volume database và không thể khôi phục nếu chưa backup.

## Tài khoản demo

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| ADMIN | `admin@example.com` | `Admin@123` |
| ACCOUNTANT | `accountant@example.com` | `Accountant@123` |
| EMPLOYEE | `employee@example.com` | `Employee@123` |
| DIRECTOR | `director@example.com` | `Director@123` |

Hãy thay mật khẩu mặc định trước khi đưa ứng dụng ra mạng.

## Chức năng đã triển khai

- Đăng nhập JWT, hash mật khẩu, phân quyền ADMIN / ACCOUNTANT / EMPLOYEE / DIRECTOR.
- Nhiều doanh nghiệp và kiểm soát quyền theo `CompanyUsers`.
- Hồ sơ doanh nghiệp, tài khoản ngân hàng, khách hàng và nhà cung cấp.
- Sản phẩm, SKU, barcode, nhóm, đơn vị, giá vốn, giá bán, VAT và tồn tối thiểu.
- Nhập, xuất, điều chỉnh kho; lịch sử và cảnh báo sắp hết hàng.
- POS: giỏ hàng, giảm giá, VAT, tiền mặt, chuyển khoản, QR, thẻ, ví điện tử.
- Hoàn tất đơn trong một database transaction: đơn hàng, kho, thanh toán, thu tiền,
  công nợ và hóa đơn VAT dự thảo.
- Hóa đơn đầu ra/đầu vào với Draft, Pending, Issued, Cancelled, Adjusted.
- `IInvoiceProvider` và `DemoInvoiceProvider`; demo không gắn cờ phát hành hợp pháp.
- Upload PDF/JPG/PNG; `IAIProvider`, `DemoAIProvider`, `OpenAIProvider`, confidence score.
- Quy trình bắt buộc `AI → Validation → User Review → Confirm → Database`.
- Kiểm tra MST, ngày, số hóa đơn, tổng tiền, VAT, dữ liệu thiếu và trùng hóa đơn.
- Thu, chi, dòng tiền, phải thu, phải trả và thanh toán công nợ.
- Đối soát ORDER ↔ INVOICE ↔ PAYMENT ↔ BANK TRANSACTION.
- Dashboard KPI và biểu đồ doanh thu, chi phí, dòng tiền, VAT, sản phẩm, công nợ.
- VAT đầu vào/đầu ra/chênh lệch, báo cáo thuế dự thảo và Accountant Review.
- Báo cáo ngày/tuần/tháng; xuất PDF, `.xlsx` và CSV.
- Trợ lý hỏi dữ liệu bằng truy vấn có kiểm soát, không tự tạo số liệu.
- Cảnh báo hóa đơn trùng, khoản chi lớn, công nợ quá hạn và tồn kho thấp.
- Dự báo dòng tiền 7/30/90 ngày; trả `Insufficient historical data` nếu thiếu lịch sử.
- Audit log người dùng, hành động, entity, giá trị cũ/mới và thời gian.
- Seed: 1 doanh nghiệp, 20 sản phẩm, 10 khách hàng, 10 nhà cung cấp, 30 đơn hàng,
  20 hóa đơn đầu vào, 20 hóa đơn đầu ra, thu/chi/công nợ/tồn kho/VAT/AI alerts.

## Multi-architecture verification

Project không ép `linux/amd64` hoặc `arm64`. Các image được chọn là image chính
thức multi-architecture:

```text
node:22-alpine
nginx:1.27-alpine
mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim
mcr.microsoft.com/dotnet/aspnet:8.0
mysql:8.4
```

Kiểm tra cấu hình không khóa cứng AMD64/ARM64 hoặc dependency Windows-only:

```bash
chmod +x tests/verify-architecture.sh tests/smoke.sh
./tests/verify-architecture.sh
```

Kiểm tra kiến trúc image sau khi build:

```bash
docker image inspect taxflow-ai-backend --format '{{.Architecture}}'
docker image inspect taxflow-ai-frontend --format '{{.Architecture}}'
docker image inspect mysql:8.4 --format '{{.Architecture}}'
```

Trên Mac Apple Silicon kết quả là `arm64`; trên Windows x86-64 là `amd64`.

Kiểm tra toàn hệ thống:

```bash
docker compose build
docker compose up -d
docker compose ps
./tests/smoke.sh
docker compose logs --tail=100 backend frontend mysql
```

`docker compose ps` phải hiển thị `healthy` cho MySQL, backend và frontend.

## AI OCR

### Hybrid local — mặc định, không cần API key

```env
AI_PROVIDER=hybrid
AI_SERVICE_URL=http://ai-service:8000
GEMINI_API_KEY=
OPENAI_API_KEY=
```

Mặc định hệ thống dùng OCR local qua `ai-service` (Tesseract tiếng Việt + tiếng Anh).
Nếu cấu hình Gemini, chế độ hybrid có thể dùng Gemini làm fallback khi OCR local thiếu
trường quan trọng hoặc confidence thấp. `DemoAIProvider` chỉ là fallback cuối cùng.

### OpenAI — tùy chọn

```env
AI_PROVIDER=openai
OPENAI_API_KEY=your_key_here
OPENAI_MODEL=gpt-4.1-mini
```

Không commit `.env`. Nếu chọn `openai` nhưng không có key, hệ thống tự hạ về demo
và hiển thị cảnh báo. Model có thể đổi bằng biến môi trường mà không sửa source.

## Development native (tùy chọn)

Docker vẫn là cách chạy được khuyến nghị. Phần này chỉ dành cho phát triển source.

### Frontend

Cài Node.js 22 phù hợp với hệ điều hành. Trên Mac có thể dùng Homebrew:

```bash
brew install node
cd frontend
npm install
npm run dev
```

Frontend chạy tại <http://localhost:3000> và proxy `/api` tới backend Docker hoặc port 5001 khi cấu hình native.

### Backend

Cài .NET 8 SDK phù hợp với hệ điều hành/kiến trúc, sau đó:

```bash
cd backend
dotnet restore
dotnet run
```

### MySQL bằng Docker

Từ thư mục gốc:

```bash
docker compose up mysql
```

Khi chạy backend native, connection string mặc định trong `backend/appsettings.json`
dùng `localhost:3306`. Có thể override bằng:

```bash
export ConnectionStrings__Default='Server=localhost;Port=3306;Database=taxflow;User=taxflow;Password=YOUR_PASSWORD;CharSet=utf8mb4;'
export Jwt__Secret='A_RANDOM_DEVELOPMENT_SECRET_AT_LEAST_32_CHARS'
dotnet run --project backend/TaxFlow.Api.csproj
```

## Tests

```bash
npm --prefix frontend run build
dotnet test tests/backend/TaxFlow.Tests.csproj
docker compose up -d --build
./tests/smoke.sh
```

Smoke test kiểm tra `/health`, đăng nhập tài khoản admin và endpoint `/api/auth/me`.

## Cấu trúc project

```text
TaxFlow-AI/
├── frontend/              React + TypeScript + Vite + Tailwind + Recharts
├── backend/               ASP.NET Core Web API + EF Core + JWT
├── ai-service/            FastAPI OCR local, chạy mặc định trong Docker Compose
├── database/              Ghi chú schema và dữ liệu
├── storage/               File hóa đơn upload qua bind mount
├── tests/                 Unit test, architecture check, smoke test
├── docs/                  Kiến trúc, API, security, checklist ARM64
├── docker-compose.yml
├── .env.example
├── .gitignore
├── README.md
└── LICENSE
```

## Health check và thứ tự khởi động

1. MySQL phải trả lời `mysqladmin ping`.
2. Backend chỉ khởi động sau khi MySQL healthy; `/health` kiểm tra kết nối database.
3. Frontend chỉ khởi động sau khi backend healthy; Nginx trả `/healthz`.

Docker Compose sử dụng `depends_on` kèm `condition: service_healthy`, vì vậy backend
không chạy trước database.

## Dữ liệu và backup

- MySQL: named volume `taxflow-ai_mysql_data`.
- Chứng từ upload: thư mục host `./storage`, mount vào `/app/storage`.

Ví dụ backup database:

```bash
docker compose exec mysql mysqldump -uroot -p taxflow > taxflow-backup.sql
```

Không lưu file backup hoặc chứng từ thật vào Git.

## Tích hợp hóa đơn điện tử thực tế

Implement thêm `IInvoiceProvider`, đăng ký provider trong dependency injection và
chỉ đặt `IsLegallyIssued=true` khi API nhà cung cấp trả xác nhận hợp lệ. Cần xử lý
đầy đủ token, ký số, mẫu số/ký hiệu, tra cứu trạng thái, hủy, thay thế và điều chỉnh
theo hợp đồng API và quy định áp dụng tại thời điểm triển khai.

## Troubleshooting

### Port 3306 đã được dùng

Đổi mapping phía host, ví dụ `3307:3306`, rồi cập nhật connection string khi chạy
backend native. Các container vẫn giao tiếp qua `mysql:3306`.

### Docker kéo nhầm image kiến trúc

Project không khóa kiến trúc. Nếu Docker cache image không phù hợp, xóa image cũ rồi build lại:

```bash
docker compose down
docker image rm mysql:8.4
docker compose pull
docker compose up --build
```

### Reset dữ liệu demo

```bash
docker compose down -v
docker compose up --build
```

Thao tác này xóa database hiện tại.

## License

MIT — xem [LICENSE](LICENSE).

## Hybrid OCR (2026 completion)

The project includes a local FastAPI OCR service and a Hybrid provider. Recommended `.env` values:

```env
AI_PROVIDER=hybrid
AI_SERVICE_URL=http://ai-service:8000
HYBRID_CONFIDENCE_THRESHOLD=0.82
GEMINI_MODEL=gemini-2.5-flash
```

Run with `docker compose up --build`. Open the frontend at `http://localhost:3000`. For JPG/PNG invoice review, focus a field, drag a box over the source image, then choose **OCR vùng** to retry only that region. See `docs/OCR_REVIEW_STUDIO.md`.

## AI/ML upgrade

The project includes Hybrid OCR, Human-in-the-loop corrections, Isolation Forest invoice anomaly detection, ML cash-flow forecasting, and a grounded financial assistant. See `AI-UPGRADE.md` for architecture, configuration and demo steps.
