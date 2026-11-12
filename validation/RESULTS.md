# Kết quả kiểm tra TaxFlow AI V5

| Hạng mục | Kết quả |
|---|---|
| Backend TestServer + SQLite | 53 passed, 1 skipped, 0 failed |
| Bài mới về ngôn ngữ tự nhiên | 19 passed; Gemini được giả lập trong các bài tích hợp này |
| Backend Release publish | Thành công với .NET SDK 8.0.422 |
| Frontend TypeScript + Vite | Build thành công |
| Frontend DOM/API giả lập | PASS, đi qua 15 route |
| Compose và CI YAML | Parse thành công |
| Gemini thật, câu hỏi tổng hợp | 2 thành công; 1 timeout; 1 HTTP 503 |

## Các hành vi mới đã kiểm tra

- Hợp đồng Gemini khai báo 8 công cụ, chế độ ANY; key ở header, không ở URL.
- Chặn công cụ lạ, phản hồi chỉ có văn bản, tham số sai kiểu, phản hồi bị cắt và phản hồi rỗng.
- Cách diễn đạt khác được đưa qua planner rồi thực thi đúng công cụ do phản hồi giả lập chỉ định.
- Hỏi tiếp lấy ngữ cảnh từ server, không cần client gửi lịch sử.
- Thay đổi số dư sau câu đầu thì câu sau dùng số dư mới.
- Hai công cụ trong một lượt có thể dùng ngữ cảnh của nhau.
- So sánh sổ thu theo kỳ đúng biên ngày Việt Nam, không cộng giao dịch doanh nghiệp khác.
- Câu hỏi tài chính tiếp theo kế thừa kỳ; số liệu truy vấn lại.
- Hóa đơn mới nhất và câu hỏi tiếp tách đúng doanh nghiệp, đọc lại tổng tiền mới.
- Tên khách mơ hồ yêu cầu chọn rõ; thiếu context/ngày sai không trả tổng quan thay thế.
- Không nhận companyId từ lời gọi công cụ; ID ngoại doanh nghiệp trong context không đọc được.
- HTTP429 chuyển sang dự phòng có thông báo và lưu thông báo trong lịch sử.
- Endpoint trạng thái không lộ key, không giả vờ đã kiểm tra kết nối.
- Thiếu key vẫn có thể dùng truy vấn dự phòng đã hỗ trợ.
- Các kiểm tra nghiệp vụ/phân quyền/hoàn trả/idempotency của bản trước vẫn qua.

## Gemini thật: phạm vi và kết quả

Đã gọi planner thật với key/model đang có trong cấu hình, chỉ dùng câu hỏi/ngữ cảnh tổng hợp, không gửi dữ liệu database thật.

1. “Ai đang thiếu tiền bên mình nhiều nhất?” → get_receivables, view=top: thành công.
2. “Doanh thu tháng trước là bao nhiêu?” → get_financial_metrics, metric=revenue, kỳ 01–31/10/2026: thành công tại ngày kiểm tra 12/11/2026.
3. “khoản gì” với ngữ cảnh khách giả lập: timeout 25 giây.
4. Thử tiếp “quá hạn bao lâu” với timeout 45 giây: Google trả HTTP503.

Chi tiết phản hồi chỉ chứa tên công cụ/tham số hoặc mã lỗi trong `gemini-live-check.json`; không chứa key. Hai yêu cầu thành công xác nhận API có thể truy cập tại thời điểm thử, không bảo đảm tính sẵn sàng hay hiểu đúng mọi câu. Phần tích hợp hội thoại được kiểm chứng bằng Gemini giả lập; chưa có kiểm tra xuyên suốt Gemini thật + MySQL thật + giao diện trình duyệt thật.

Timeout planner trong bản phát hành là 45 giây, nằm trong giới hạn 60 giây của frontend. Không tự gọi lại model lần hai khi lỗi; hiển thị dự phòng để người dùng biết. HTTP503/timeout không nên được giải quyết bằng việc thêm nhiều key.

## Kết quả giữ từ V4.1, không chạy lại trong V5

- Python AI service: 14 passed.
- PDF tiếng Việt 150 dòng: 6 trang, đọc lại được dòng cuối.
- EF model: trước đó không có thay đổi chưa có migration.

V5 không thay đổi mã Python, bộ dựng PDF hay entity/schema EF; các kết quả này là bằng chứng phiên bản trước, không phải lần chạy mới.

## Chưa thực hiện

- Bài MySQL migration: SKIP do không có server MySQL; SQLite không thay thế kiểm tra khóa/transaction/migration MySQL.
- Chạy Docker Compose trên máy đích, PowerShell backup/restore và CI trên GitHub.
- Kiểm tra trực quan hoạt ảnh, responsive, biểu đồ trên trình duyệt/điện thoại thật. DOM giả lập không có layout; cảnh báo biểu đồ rộng/cao bằng 0 không chứng minh lỗi hiển thị thực tế.
- Gọi gateway hóa đơn/thanh toán thật; đánh giá OCR với tập hóa đơn thật đã gán nhãn.
- Tải cao, độ trễ ổn định, hạn mức Gemini hay độ chính xác cho mọi cách diễn đạt.
