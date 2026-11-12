# TaxFlow AI V4 — Hướng dẫn nâng cấp, sử dụng và kiểm chứng

Bản nâng cấp từ dự án TaxFlow AI xanh dương–trắng. Tài liệu này mô tả hành vi thực tế của mã nguồn V4 và thay thế các ghi chú cũ khi có khác biệt. Đây là phần mềm quản trị/đồ án, chưa phải một sản phẩm kế toán đã được chứng nhận triển khai thực tế.

## 1. Đọc trước khi chạy

- Toàn bộ mã nguồn, migration, bộ kiểm thử và tài liệu nằm trong cùng thư mục `TaxFlow-AI-Docker-Ready`.
- Giao diện tiếp tục dùng xanh dương–trắng và hỗ trợ giảm chuyển động theo cài đặt thiết bị.
- Bản nâng cấp thay đổi schema. Database có dữ liệu cần được sao lưu trước.
- Không xóa volume MySQL. Không dùng `docker compose down -v` để cập nhật.
- Sau nâng cấp, đăng nhập lại. JWT cũ thiếu phiên bản xác thực sẽ không còn được chấp nhận.
- Các công thức trong V4 phục vụ quản trị. Kế toán cần đối chiếu chứng từ, phương pháp giá vốn và kỳ ghi nhận thực tế.

## 2. Ma trận xử lý các thiếu sót

| Nhóm vấn đề | Đã triển khai trong V4 | Giới hạn cần biết |
|---|---|---|
| Nhiều doanh nghiệp | Vai trò và trạng thái hoạt động nằm trên CompanyUsers; kiểm tra mỗi request | Chưa có SSO hoặc danh tính liên kết bên ngoài |
| Thu hồi phiên | AuthVersion, đổi mật khẩu/đăng xuất thu hồi phiên; quyền thành viên đọc trực tiếp từ DB | Token vẫn được lưu ở localStorage; cần ngăn XSS và dùng HTTPS khi triển khai |
| Thêm người đã có tài khoản | Lời mời 7 ngày, chỉ đúng người nhận đăng nhập mới được chấp nhận | Chưa gửi email; thông báo nằm trong ứng dụng |
| Tiền và kho | Transaction Serializable, revision chống mất cập nhật, Idempotency-Key và lịch sử phản hồi | Khóa phía trình duyệt giữ trong bộ nhớ 15 phút; sau tải lại trang cần tra cứu lịch sử trước khi gửi lại giao dịch chưa rõ kết quả |
| Hoàn trả | Hoàn toàn bộ đơn chưa có hóa đơn phát hành thật; hoàn tiền, nhập lại kho, xóa nợ còn lại, audit | Chưa có hoàn từng phần hoặc điều chỉnh hóa đơn hợp pháp tự động |
| Công thức tài chính | Doanh thu chưa VAT, giá vốn snapshot lúc bán, loại mua hàng tồn kho khỏi chi phí vận hành; giảm giá phân bổ trước VAT | Chưa phải hệ thống bút toán kép; giá vốn lịch sử được backfill từ giá mua tại lúc nâng cấp và là ước tính |
| Kiểm tra hóa đơn | Chặn sai tổng tiền và sai tổng dòng; giữ confidence OCR gốc khi người dùng xác nhận | Hóa đơn phí/chiết khấu phức tạp cần được nhập, kiểm tra đúng schema hiện tại |
| OCR | PageCount, preview trả số trang, OCR đúng trang đang chọn, sửa dòng hàng, mở chứng từ chờ, chạy lại | Không khẳng định OCR đúng trên mọi mẫu; chưa có job fine-tune OCR tự động |
| Chatbot | Truy vấn kỳ có kiểm soát, trả số trực tiếp từ DB, lưu lịch sử theo người và doanh nghiệp | Chưa hỗ trợ truy vấn SQL tự do; câu hỏi không được hỗ trợ được trả lời rõ giới hạn |
| AI/ML | Backtest dự báo giữ lại 20%, MAE và baseline; giải thích điểm bất thường; CLI đánh giá OCR theo nhãn | Chưa có số đo độ chính xác tổng quát trên bộ dữ liệu thực của người dùng |
| Frontend | Tra cứu phân trang phía server, bộ lọc, chi tiết, chỉnh đối tác, tài khoản/lời mời, quản lý vai trò, đối soát ngân hàng, bảo vệ biểu mẫu | Kiểm thử DOM không thay thế kiểm tra trực quan trên thiết bị thật |
| Xuất báo cáo | PDF nhiều trang có font tiếng Việt, không giới hạn 42 dòng; file theo lọc ngày/từ khóa; chống công thức CSV | Các báo cáo vẫn là báo cáo quản trị; xuất theo trạng thái chưa được hỗ trợ |
| Tích hợp bên ngoài | Adapter hóa đơn qua gateway HTTPS với khóa server và contract rõ ràng | Cần nhà cung cấp thật triển khai gateway; thanh toán QR/thẻ/ví vẫn ghi nhận thủ công |
| Vận hành | CI, script kiểm tra, sao lưu/khôi phục, font license, hạn chế cổng về localhost, HTTP security headers | Chưa chạy Docker/MySQL thực tế trong môi trường xây dựng bản này; cần kiểm tra tại máy đích |

## 3. Công nghệ và các cổng

| Thành phần | Công nghệ | Cổng mặc định ở máy người dùng |
|---|---|---|
| Frontend | React 18, TypeScript, Vite, Tailwind, Recharts, Nginx | http://localhost:3000 |
| Backend | ASP.NET Core 8, EF Core, JWT | http://localhost:5001 |
| Swagger | Tài liệu API của backend | http://localhost:5001/swagger |
| MySQL | MySQL 8.4 | localhost:3307 |
| AI local | Python FastAPI, Tesseract, scikit-learn | http://localhost:8000 |

Các cổng mặc định bind vào `127.0.0.1`. Chỉ đổi `BIND_ADDRESS=0.0.0.0` khi chủ động cho phép truy cập qua mạng và đã có biện pháp bảo vệ phù hợp.

## 4. Cài mới trên Windows

1. Cài và mở Docker Desktop với WSL2.
2. Giải nén ZIP vào đường dẫn dễ tìm, ví dụ `D:\TaxFlow-V4`.
3. Mở PowerShell trong thư mục có `docker-compose.yml`.
4. Nếu chưa có `.env`, sao chép `.env.example` thành `.env`.
5. Cấu hình mật khẩu MySQL và JWT_SECRET. JWT_SECRET tối thiểu 32 ký tự, không dùng chuỗi mẫu khi mở ra mạng.
6. Chạy:

```powershell
docker compose up -d --build
docker compose ps
```

7. Chờ các dịch vụ healthy, mở http://localhost:3000.
8. Nếu giao diện cũ còn cache, bấm Ctrl + Shift + R.

Nếu đã có volume database từ dự án cũ, mật khẩu trong `.env` phải khớp với volume đó. Đổi biến môi trường không tự đổi mật khẩu bên trong MySQL đã được tạo.

## 5. Nâng cấp dự án đang có dữ liệu

1. Giữ nguyên dự án cũ và file `.env` trước khi thử bản mới.
2. Khi hệ thống cũ đang chạy, sao lưu SQL và storage. Có thể dùng script V4 đã được chép riêng sang thư mục cũ:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Backup.ps1
```

3. Dừng các tiến trình ghi dữ liệu khi chuyển phiên bản. Không xóa volume.
4. Thay mã nguồn bằng V4; giữ `.env`, storage và tên Compose project `taxflow-ai`.
5. Chạy `docker compose up -d --build`.
6. Backend tự chạy migration `CompletionSecurityAndLedger`.
7. Đăng nhập lại, kiểm tra doanh nghiệp, vai trò, tồn kho, công nợ và báo cáo.
8. Thử một đơn nhỏ, một lần thanh toán công nợ và một hóa đơn mẫu trước khi dùng tiếp dữ liệu quan trọng.

Migration thêm các cột/bảng mới, backfill vai trò thành viên từ vai trò cũ và trạng thái active từ người dùng. Giá vốn đơn cũ được lấy từ giá mua sản phẩm tại lúc migration; không được diễn giải là giá vốn lịch sử đã xác minh. Hóa đơn đầu vào cũ chưa được tự đánh dấu rà soát kế toán.

## 6. Tài khoản demo

| Vai trò ban đầu | Email | Mật khẩu demo |
|---|---|---|
| Quản trị | admin@example.com | Admin@123 |
| Kế toán | accountant@example.com | Accountant@123 |
| Nhân viên | employee@example.com | Employee@123 |
| Giám đốc | director@example.com | Director@123 |

Các tài khoản trên được tạo khi database chưa có doanh nghiệp. Chúng không tự thay thế tài khoản trong database cũ. Đổi mật khẩu trước khi dùng ngoài môi trường demo.

## 7. Phân quyền và vòng đời tài khoản

- Mỗi CompanyUser có RoleName và IsActive riêng.
- JWT chứa ID người dùng và AuthVersion. Backend kiểm tra user còn active, phiên bản token, tư cách thành viên và quyền tại doanh nghiệp đang chọn.
- Header `X-Company-Id` được kiểm tra với database; không tin vai trò từ frontend.
- Đổi quyền hoặc khóa thành viên tác động tại doanh nghiệp đó ở request tiếp theo.
- Đổi mật khẩu hoặc đăng xuất tăng AuthVersion, thu hồi token đã phát hành của tài khoản.
- Admin doanh nghiệp không được đổi mật khẩu chung hoặc sửa tên toàn cục của tài khoản người khác.
- Tạo tài khoản mới: mục Doanh nghiệp, Thêm người dùng.
- Tài khoản đã tồn tại: mục Tài khoản & lời mời, tạo lời mời; người nhận đăng nhập vào đúng email để chấp nhận.
- Admin đổi vai trò/khóa thành viên trong Doanh nghiệp. Không được tự bỏ quyền admin hoặc tự khóa chính mình.

Mật khẩu và token không được đưa vào báo cáo, ảnh chụp hoặc log công khai. Dữ liệu demo và JWT mặc định không phải cấu hình production.

## 8. Tính tiền, VAT và giá vốn

Đối với đơn mới:

1. Tổng tiền hàng trước giảm giá = tổng số lượng × đơn giá.
2. Giảm giá không vượt quá tiền hàng, được phân bổ theo giá trị dòng.
3. VAT từng dòng tính trên tiền hàng sau phân bổ giảm giá.
4. Tổng thanh toán = tiền hàng − giảm giá + VAT.
5. Giá vốn mỗi dòng = số lượng × UnitCost đã lưu tại lúc bán.
6. Doanh thu quản trị chưa VAT = Tổng thanh toán − VAT.
7. Lợi nhuận quản trị ước tính = Doanh thu chưa VAT − Giá vốn − Chi phí vận hành chưa VAT.

Ví dụ: bán 2 sản phẩm × 100, giảm 50 trước VAT, thuế suất 10% thì tiền hàng sau giảm là 150, VAT 15, tổng thanh toán 165. Bộ kiểm thử kiểm tra đúng ví dụ này và sự khớp giữa đơn và hóa đơn.

Khoản chi nhóm Nhập hàng không được trừ thêm vào chi phí vận hành vì giá vốn được ghi nhận khi bán. Khoản Hoàn tiền không trừ lần hai vào lợi nhuận vì đơn hoàn đã bị loại khỏi doanh thu/giá vốn. Đây là cách tính quản trị giản lược, chưa mô hình hóa đầy đủ phân bổ, khấu hao, doanh thu dồn tích hay khóa sổ.

## 9. Transaction và chống ghi trùng

Các lệnh tạo đơn, thu/chi, thanh toán công nợ, thay đổi kho, xác nhận OCR, hoàn đơn và ghép sao kê chạy trong transaction Serializable. Bản ghi Revision giúp phát hiện hai người cập nhật cùng phiên bản dữ liệu.

Các API lệnh trên yêu cầu `Idempotency-Key`:

- Cùng user + doanh nghiệp + key + nội dung: trả lại kết quả trước, không tạo lệnh mới.
- Cùng key nhưng nội dung khác: trả 409.
- Không có key: trả 400, không ghi tiền/kho.
- Frontend tự tạo key. Khi lỗi mạng, cùng nội dung được giữ key trong bộ nhớ tối đa 15 phút.
- Backend lưu receipt cùng transaction với nghiệp vụ.

Sau mất mạng rồi tải lại trang, hãy tra cứu nghiệp vụ trước khi tạo lại giao dịch. Không nên đoán lệnh đã thất bại chỉ vì trình duyệt chưa nhận được phản hồi.

## 10. Hoàn trả đơn hàng

1. Mở Tra cứu nghiệp vụ, chọn Đơn hàng.
2. Chọn Chi tiết.
3. Ghi lý do và chọn Nhận hàng và hoàn tiền.
4. Hệ thống kiểm tra đơn COMPLETED và chưa có hóa đơn phát hành thật.
5. Nhận lại toàn bộ số lượng vào kho, tạo Payment âm cho số tiền đã thu, ghi khoản chi hoàn tiền, xóa phần nợ còn lại, hủy hóa đơn mô phỏng liên quan, đổi đơn sang RETURNED và lưu audit.
6. Lặp lại cùng key không tạo lần hoàn thứ hai.

Luồng này là đảo toàn bộ đơn ở phạm vi quản trị, không phải quy trình trả hàng từng phần, điều chỉnh liên kỳ hoặc thủ tục hủy hóa đơn pháp lý. Hoàn tiền là ghi nhận nội bộ, không tự chuyển tiền cho khách qua ngân hàng.

## 11. Quản lý chứng từ và OCR

- Upload PDF/JPG/PNG tối đa 10 MB. OCR local giới hạn PDF 30 trang để hạn chế quá tải.
- Hybrid dùng local trước, có thể gọi Gemini khi thiếu trường hoặc confidence thấp.
- Số trang được giữ trong kết quả, preview trả X-Page-Count; OCR vùng gửi đúng trang đang xem.
- Màn hình kiểm tra có thể sửa tên/MST/số/ngày/tiền và từng dòng hàng.
- Tổng tiền lệch hoặc chi tiết không khớp sẽ chặn xác nhận.
- Để sau: chứng từ gốc và kết quả OCR còn trong danh sách chờ, nhưng chỉnh sửa chưa xác nhận trên trình duyệt không được tự lưu thành nháp.
- OCR lại: dùng lại tệp đã lưu, không cần upload lần nữa.
- Chứng từ xử lý bị ngắt có thể thử lại sau 5 phút.
- Confidence được giữ theo kết quả OCR; bước người dùng xác nhận không tự biến điểm thành 100%.
- Sau xác nhận, vào Tra cứu nghiệp vụ/Hóa đơn/Chi tiết để đánh dấu kế toán đã rà soát đối với đầu vào.

Các mẫu chiết khấu, phí, tỷ giá, nhiều loại thuế và điều chỉnh phức tạp vẫn cần được đối chiếu. Không có cam kết mọi hóa đơn đều được đọc chính xác.

## 12. VAT trong báo cáo

- Đầu vào: chỉ lấy hóa đơn INCOMING được kế toán đánh dấu AccountingReviewed và chưa hủy.
- Đầu ra: chỉ lấy hóa đơn OUTGOING đã có IsLegallyIssued do nhà cung cấp xác nhận và chưa hủy.
- Hóa đơn nháp/chờ không được cộng vào số VAT này.
- Mark rà soát nội bộ không tự xác nhận giá trị pháp lý của hóa đơn đầu vào.
- Trong bản demo, đầu ra có thể bằng 0 do chưa tích hợp phát hành thật.

Đây chưa phải bộ máy xác định điều kiện khấu trừ thuế theo mọi trường hợp pháp luật. Cần kế toán và tích hợp nhà cung cấp phù hợp trước khi dùng kê khai thực tế.

## 13. Tra cứu, đối tác và sao kê

Tra cứu nghiệp vụ hỗ trợ tìm mã/tên/nội dung, từ ngày–đến ngày, trạng thái/nhóm chính xác, sắp xếp và phân trang phía server. Với công nợ, ngày lọc là ngày đến hạn. Với đối tác, ngày lọc là ngày tạo.

Có thể xem chi tiết đơn/hóa đơn, lịch sử thanh toán và chỉnh thông tin khách hàng/nhà cung cấp. Admin/Kế toán có thể thêm đối tác. Export theo ngày và từ khóa; khi lọc trạng thái, cần xóa bộ lọc trạng thái trước khi xuất file vì export chưa hỗ trợ bộ lọc này.

Trong Đối soát:

1. Nhập giao dịch sao kê thủ công vào tài khoản ngân hàng của doanh nghiệp.
2. Chọn thanh toán chưa được ghép có cùng số tiền và chiều tiền.
3. Kiểm tra ngày và tham chiếu, xác nhận ghép.
4. Backend kiểm tra hai bản ghi cùng doanh nghiệp, số tiền/chiều tiền khớp và thanh toán chưa được ghép nơi khác.

Nhập sao kê/ghép không tạo thêm thu hoặc chi để tránh ghi tiền lần hai. Chưa kết nối API ngân hàng hoặc import sao kê CSV tự động. Giao dịch chi tự nhập không gắn Payment có thể cần nghiệp vụ thanh toán liên quan trước khi ghép.

## 14. Chatbot và lịch sử

- Các câu hỏi doanh thu, lợi nhuận, thu/chi, VAT, công nợ, tồn kho, cảnh báo và hóa đơn mới nhất được tính trực tiếp từ database.
- Hỗ trợ hôm nay, hôm qua, tháng này, tháng trước, tháng M/YYYY, năm này/năm trước, tuần này/tuần trước, 7/30/90 ngày gần nhất.
- Các khoảng thời gian hiện dùng UTC và được ghi trong câu trả lời khi cần. Chưa có khóa sổ theo múi giờ doanh nghiệp.
- So sánh doanh thu tháng này/tháng trước nêu rõ tháng hiện tại chưa kết thúc.
- Công nợ/tồn kho là số dư hiện tại, không giả lập số dư lịch sử khi chưa có mô hình đó.
- Truy vấn theo quý chưa hỗ trợ; hệ thống yêu cầu nêu tháng hoặc số ngày.
- Gemini chỉ được dùng tùy chọn để bổ sung giải thích không có chữ số; số liệu chính do backend dựng.
- Lịch sử lưu theo user và CompanyId, có thể xóa lịch sử của chính mình.
- Chatbot không thực thi SQL do người dùng nhập, không tự thanh toán hay tự sửa dữ liệu.

Ví dụ: “Doanh thu tháng trước?”, “VAT tháng 7/2026?”, “Khách hàng nào nợ nhiều nhất?”, “Có hóa đơn bất thường nào?”. Các câu hỏi quá chuyên biệt ngoài truy vấn hỗ trợ sẽ nhận tổng quan kèm lời giải thích giới hạn.

## 15. Dự báo và đánh giá AI

Dự báo dùng Huber Regression với phần dư theo thứ trong tuần. Ít nhất 14 ngày có giao dịch tại backend. Service gộp ngày trùng, điền ngày trống và giới hạn lịch sử.

Đánh giá dự báo giữ lại 20% cuối (tối thiểu 3 ngày), chỉ fit trên phần trước rồi tính MAE tiền vào, MAE tiền ra và MAE của baseline trung bình 7 ngày. Mô hình cuối cùng được fit trên toàn bộ lịch sử để dự báo. MAE càng thấp càng tốt; điểm đo trên một chuỗi ngắn không bảo đảm chính xác trong tương lai.

Isolation Forest yêu cầu ít nhất 8 hóa đơn. Điểm rủi ro là thứ hạng tương đối trong tập hiện tại, không phải xác suất gian lận. Khi ML không khả dụng có fallback quy tắc, tên phương pháp được trả rõ. “Không có cảnh báo” không chứng minh giao dịch an toàn.

Dữ liệu sửa OCR xuất JSONL. Đánh giá theo từng trường:

```powershell
cd ai-service
python evaluate_ocr.py ..\ocr-training.jsonl --output ocr-evaluation.json
```

Kết quả chỉ phản ánh các nhãn được cung cấp. Không tạo sẵn phần trăm chính xác trên hóa đơn thật. Chưa có bộ dữ liệu đủ và quy trình kiểm định để fine-tune Tesseract tự động; V4 không tuyên bố đã huấn luyện lại OCR.

## 16. Xuất báo cáo

- CSV: UTF-8 BOM, escape ký tự và chống nội dung bị Excel diễn giải thành công thức.
- XLSX: dữ liệu được ghi dạng chuỗi để bảo toàn mã có số 0 đầu và tránh công thức ngoài ý muốn.
- PDF: font DejaVu Sans đóng gói, tiếng Việt có dấu, nhiều trang, lặp tiêu đề và chia nội dung dài; không cắt ở 42 dòng.
- Báo cáo theo kỳ chuyển từ/to vào export. Tra cứu nghiệp vụ xuất ngày/từ khóa đang chọn.
- Font và giấy phép được lưu trong backend/Assets.

Bài kiểm tra PDF tạo 150 dòng, đọc lại bằng công cụ PDF để kiểm tra dòng đầu/cuối và nhiều trang. Đây là dữ liệu kiểm tra, không phải số liệu doanh nghiệp.

## 17. Gateway hóa đơn điện tử tùy chọn

Mặc định `INVOICE_PROVIDER=demo`. Muốn dùng bridge:

```env
INVOICE_PROVIDER=gateway
INVOICE_GATEWAY_URL=https://your-server.example/invoices/issue
INVOICE_GATEWAY_KEY=your-server-side-key
```

Gateway phải nhận POST JSON hóa đơn/chi tiết, Bearer token và Idempotency-Key bằng InvoiceId. Phản hồi contract:

```json
{
  "isLegallyIssued": true,
  "status": "Issued",
  "message": "Đã được nhà cung cấp xác nhận",
  "providerReference": "vendor-reference"
}
```

V4 chỉ đánh dấu phát hành khi đủ cờ, trạng thái và tham chiếu. Bạn phải tự triển khai bridge tương ứng hợp đồng API của nhà cung cấp, bao gồm xác thực, ký số và xác minh phản hồi. Gateway mẫu không có tài khoản, chứng thư số hay khả năng phát hành thật khi chưa cấu hình. Không gửi khóa gateway xuống frontend.

QR/thẻ/ví điện tử tại POS vẫn là phương thức ghi nhận thủ công. Chưa có cổng thanh toán thật, webhook xác minh chữ ký, xử lý chargeback hoặc chuyển tiền hoàn tự động.

## 18. Kiểm thử và CI

```powershell
npm --prefix frontend ci
npm --prefix frontend run build
npm --prefix frontend test
dotnet test TaxFlow.sln --logger "trx;LogFileName=local.trx"
cd ai-service
pip install -r requirements.txt pytest
python -m pytest -q
```

Hoặc chạy `scripts/Validate.ps1` khi đã cài Node, .NET, Python và Docker.

- Backend tests dùng ASP.NET TestServer và SQLite để kiểm tra HTTP, xác thực, command, transaction và revision.
- Có test migration MySQL được skip nếu chưa cấu hình TEST_MYSQL_CONNECTION.
- CI có service MySQL 8.4 để chạy lại các bài HTTP trên MySQL và test nâng cấp schema cũ. File workflow là cấu hình sẵn; việc tạo file không có nghĩa CI đã được chạy từ GitHub.
- TEST_MYSQL_CONNECTION phải trỏ vào server dành riêng cho test, user có quyền tạo/xóa database. Test tạo database tên taxflow_test_* hoặc taxflow_migration_* rồi xóa chúng.
- Frontend tests dùng DOM giả lập và API fixtures; không thay thế ảnh chụp trực quan, thao tác trên điện thoại hoặc e2e với MySQL thật.
- Python tests bao gồm parser, OCR totals, nhiều trang, dữ liệu không đủ, số không hữu hạn và backtest.
- Kết quả kiểm tra đã chạy được lưu trong `validation/RESULTS.md` và `validation/results.json`.

## 19. Sao lưu và khôi phục

`scripts/Backup.ps1` xuất SQL và ZIP storage, in SHA256 của SQL. Bản sao `.env` nên lưu riêng ở nơi được bảo vệ; không đưa vào báo cáo hoặc Git.

`scripts/Restore.ps1 -SqlFile backups/ten-file.sql` yêu cầu gõ RESTORE trước khi ghi đè database. Cần dừng ghi dữ liệu và tự xác minh đúng máy/db đích. Sau phục hồi SQL cần phục hồi storage tương ứng. Không khôi phục nhầm storage của một thời điểm khác.

Bộ script chưa được chạy trực tiếp trên PowerShell/Docker trong môi trường tạo bản này; hãy kiểm tra phục hồi trên database thử trước. Có script không thay thế việc định kỳ diễn tập phục hồi và lưu bản sao ở vị trí khác máy.

## 20. Xử lý lỗi thường gặp

| Hiện tượng | Cách kiểm tra |
|---|---|
| Đăng nhập lại bị yêu cầu sau nâng cấp | Token cũ đã bị vô hiệu; dùng tài khoản DB hiện tại |
| Backend không healthy | `docker compose logs --tail=100 backend mysql`; kiểm tra mật khẩu volume và lỗi migration |
| Người dùng bị 403 | Kiểm tra doanh nghiệp đang chọn, vai trò và trạng thái thành viên |
| Giao dịch bị 409 | Có thay đổi đồng thời hoặc key bị dùng với nội dung khác; tải lại dữ liệu trước khi thao tác lại |
| API lệnh trả 400 thiếu key | Client ngoài frontend phải gửi Idempotency-Key |
| VAT đầu ra bằng 0 | Chưa có hóa đơn được nhà cung cấp xác nhận phát hành thật |
| Không ghép được sao kê | Kiểm tra số tiền, chiều tiền, Payment và trạng thái ghép trước đó |
| OCR không đọc rõ | Kiểm tra ảnh/PDF, dùng OCR vùng đúng trang, sửa thủ công rồi xác nhận |
| PDF nhiều trang chỉ hiện trang đầu | Kiểm tra ai-service/preview, header X-Page-Count và tải lại frontend |
| AI không có dự báo | Cần đủ lịch sử; xem phương pháp fallback và trạng thái insufficient_data |
| Hiện giao diện cũ | Rebuild frontend và Ctrl + Shift + R |

## 21. Phạm vi chưa được chứng nhận hoàn tất

Chưa có kiểm định pháp lý/kế toán; chưa có thanh toán hoặc hóa đơn điện tử thật nếu chưa cấu hình nhà cung cấp. Chưa fine-tune OCR, chưa đánh giá trên tập hóa đơn thật đại diện cho toàn bộ hoạt động doanh nghiệp. Chưa kiểm tra Docker/MySQL/PowerShell và giao diện trình duyệt thật trên máy đích trong phiên tạo bản này. Chưa hỗ trợ hoàn từng phần, khóa sổ, nhiều tiền tệ và hệ thống bút toán kép.

Các giới hạn này được ghi rõ để phân biệt mã đã triển khai và kiểm thử với hạng mục phụ thuộc dữ liệu, tài khoản hoặc môi trường bên ngoài. Không nên trình bày đồ án rằng tất cả tích hợp thực tế đã hoàn tất.
