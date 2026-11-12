# V4.1 — Sửa hỏi đáp nối tiếp về công nợ

## Lỗi đã được xử lý

V4 lưu tin nhắn nhưng không giữ ID khách hàng cho bộ định tuyến câu hỏi. Do đó, sau “Khách hàng nợ nhiều nhất”, các câu ngắn như “khoản gì” hoặc “quá hạn bao lâu” rơi về tổng quan tài chính tháng. Đây là lỗi logic hội thoại, không phải do thiếu API key Gemini.

## Hành vi mới

- Lưu CustomerId trong ContextJson phía server, theo đúng CompanyId và UserId của người hỏi.
- Câu “khách hàng nợ nhiều nhất” chọn theo tổng khoản phải thu còn dư của từng CustomerId, không gộp các khách hàng chỉ vì trùng tên.
- Các câu hỏi nối tiếp truy vấn lại cùng khách hàng. Không chọn lại khách nợ nhiều nhất khi thứ hạng thay đổi; không dùng số dư cũ trong tin nhắn làm nguồn sự thật.
- Trả từng khoản nợ, mã đơn/hóa đơn hợp lệ, số tiền còn phải thu và ngày đến hạn.
- Số ngày quá hạn = ngày UTC hiện tại trừ ngày đến hạn; khoản đến hạn hôm nay chưa bị tính là quá hạn.
- Câu hỏi thanh toán đọc lại Payments gắn với đơn/hóa đơn của đúng khách hàng.
- Không có bản ghi thanh toán liên kết thì nói rõ thiếu dữ liệu, không kết luận khách chưa từng trả.
- Thiếu hoặc mơ hồ ngữ cảnh thì hỏi lại khách hàng; không đưa tổng quan tháng không liên quan.
- Xóa lịch sử xóa cả ngữ cảnh. Người dùng và doanh nghiệp khác không dùng chung ngữ cảnh.
- Khi chuyển sang chủ đề khác và nhận câu trả lời mới, ngữ cảnh công nợ trước không được giữ để tự đoán.

## Thử lại trên máy của bạn

1. Giữ `.env`, storage và volume MySQL hiện tại; sao lưu trước khi cập nhật.
2. Dùng mã nguồn V4.1, chạy trong thư mục có docker-compose.yml:

```powershell
docker compose up -d --build
```

3. Backend tự thêm cột ContextJson bằng migration `AddAssistantConversationContext`. Migration thêm cột nullable, điền dữ liệu cũ bằng `{}`, rồi mới áp dụng NOT NULL.
4. Tải lại trình duyệt bằng Ctrl + Shift + R.
5. Hỏi lại câu đầu tiên để tạo ngữ cảnh mới. Lịch sử V4 cũ vẫn được giữ, nhưng chưa có CustomerId nên không được tự đoán khách hàng từ tin nhắn cũ.

Ví dụ chuỗi thử:

```text
Khách hàng nợ nhiều nhất
Quá hạn bao lâu?
Khoản gì?
Đơn nào?
Họ đã trả chưa?
Còn bao nhiêu?
```

Có thể hỏi trực tiếp: “Shop Phương Nhi quá hạn bao lâu?”. Nếu có nhiều khách hàng cùng tên khớp, hệ thống yêu cầu xác định lại thay vì tự chọn.

Số ngày và số tiền thực tế phải lấy từ database của bạn. Không có số ngày quá hạn cụ thể nào trong ảnh chụp được giả định để tạo câu trả lời.

## Phạm vi

Bản vá tập trung hội thoại nối tiếp về công nợ khách hàng. Không tuyên bố đã hỗ trợ mọi loại hội thoại, ngữ cảnh hóa đơn/thuế bất kỳ hoặc truy vấn SQL tự do. Ngữ cảnh hiện là lịch sử chung của một người tại một doanh nghiệp, chưa tách thành nhiều hội thoại độc lập trong nhiều tab.

Mỗi phản hồi hiển thị tối đa 20 chi tiết, nêu rõ nếu còn bản ghi khác; tổng số tiền vẫn được tính trên toàn bộ tập phù hợp. Tài khoản bị khóa hoặc không có quyền vẫn bị kiểm soát ở backend như V4.

## Kiểm thử

Có 13 bài hồi quy hội thoại bằng HTTP TestServer/SQLite: câu trong hai ảnh, dấu câu/biến thể tiếng Việt, mã đơn, lịch sử thanh toán, số dư thay đổi, không chọn lại khách hàng khác, thiếu ngữ cảnh, xóa lịch sử, cô lập user/company, hỏi tên trực tiếp và từ chối ID khách hàng ngoài doanh nghiệp.

Bộ kiểm thử có dữ liệu giả 322.245 đồng và đến hạn cách ngày kiểm tra 12 ngày; đây chỉ là fixture để xác minh tính toán, không phải dữ liệu thực của bạn. Chưa thực hiện migration trên MySQL thật trong môi trường tạo bản này. Kết quả tổng hợp nằm trong validation/results.json.

## Gemini hiện có và hiệu năng

Dự án có GeminiAIProvider cho OCR, HybridAIProvider dùng Gemini khi OCR local chưa đủ, và GroundedFinancialAssistant cho giải thích tùy chọn. File `.env` hiện có khai báo GEMINI_API_KEY; chỉ kiểm tra sự hiện diện, không gọi API để xác minh key/quota.

Luồng số liệu và hỏi nối tiếp về công nợ lấy dữ liệu trực tiếp từ database; việc thêm key không sửa logic ngữ cảnh. Mã hiện đọc một GEMINI_API_KEY, chưa có pool nhiều key.

Theo tài liệu Google, hạn mức Gemini được áp dụng theo Google Cloud project, không phải từng API key. Nhiều key cùng project không nhân hạn mức và không tự giảm độ trễ mỗi yêu cầu. Ưu tiên giảm số lượt gọi không cần thiết, gửi đúng dữ liệu liên quan và đo thời gian truy vấn/OCR/model trước khi thay cấu hình.

Nguồn tham khảo: https://ai.google.dev/gemini-api/docs/rate-limits và https://ai.google.dev/gemini-api/docs/optimization.
