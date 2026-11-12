# Security notes

- Đổi toàn bộ mật khẩu demo và `JWT_SECRET` trước khi dùng ngoài môi trường thử nghiệm.
- Đặt ứng dụng sau HTTPS reverse proxy nếu truy cập qua mạng.
- Hạn chế loại file upload (PDF/JPG/PNG), kích thước tối đa 10 MB và tên file được làm sạch.
- API xác thực quyền theo role và quyền thành viên doanh nghiệp.
- Không ghi API key vào source, log hoặc database.
- Bản demo chưa thay thế phần mềm kế toán, tư vấn thuế hoặc nhà cung cấp hóa đơn điện tử.
- Trước khi vận hành thật cần bổ sung: backup, secret manager, rate limit, malware scan,
  chính sách lưu trữ chứng từ, ký số và kiểm thử xâm nhập.
