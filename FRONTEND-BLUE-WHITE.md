# TaxFlow AI — Giao diện xanh dương & trắng

## Thay đổi giao diện

- Đổi bảng màu nhận diện toàn ứng dụng sang xanh dương, trắng và nền xanh rất nhạt.
- Thiết kế lại sidebar sáng, chia nhóm nghiệp vụ và hiển thị vai trò bằng tiếng Việt.
- Thanh điều hướng mới; chọn doanh nghiệp được hỗ trợ cả trên menu điện thoại.
- Dashboard có lời chào, thẻ KPI, liên kết thao tác nhanh theo quyền và biểu đồ xanh dương.
- Nhãn số dư hiện tại được phân biệt với chỉ số trong kỳ; lỗi làm mới Dashboard được hiển thị.
- Thiết kế lại màn hình đăng nhập với nền xanh, họa tiết và biểu đồ trang trí.
- Chuẩn hóa màu thương hiệu trên các trang tài chính, hóa đơn, báo cáo và trợ lý AI.
- Giữ màu xanh lá cho trạng thái thành công, vàng cho lưu ý và đỏ cho lỗi/cảnh báo.

## Hoạt ảnh

- Hiệu ứng xuất hiện nội dung khi đổi trang và lần lượt trên các thẻ KPI.
- Số liệu KPI chạy tăng dần tới giá trị thực; phần đọc màn hình chỉ đọc giá trị cuối.
- Thẻ KPI nổi nhẹ khi rê chuột; nút chính có hiệu ứng ánh sáng.
- Menu điện thoại trượt vào; hộp thoại mờ dần và phóng nhẹ khi mở.
- Màn hình đăng nhập có biểu đồ trang trí chuyển động. Đây không phải dữ liệu doanh nghiệp.
- Tôn trọng prefers-reduced-motion; tắt hoạt ảnh số và biểu đồ khi người dùng yêu cầu giảm chuyển động.

## Cải thiện thao tác

- Hộp thoại hỗ trợ Escape, giữ focus bàn phím bên trong, trả focus khi đóng và khóa cuộn nền.
- Menu điện thoại hỗ trợ Escape và focus bàn phím.
- Nội dung trang được khởi tạo lại khi đổi doanh nghiệp để tải lại đúng dữ liệu đang chọn.
- Liên kết Dashboard và menu được lọc theo vai trò hiện tại.

## Cách chạy trên Windows

1. Giải nén dự án.
2. Mở PowerShell trong thư mục `TaxFlow-AI-Docker-Ready`, nơi có `docker-compose.yml`.
3. Nếu đang dùng dữ liệu cũ, giữ file `.env` và thư mục `storage` của bạn; sao lưu dữ liệu trước khi thay thư mục dự án.
4. Chạy:

```powershell
docker compose up -d --build
```

5. Truy cập `http://localhost:3000` và bấm `Ctrl + Shift + R` để tải lại giao diện.

Không cần xóa volume MySQL để cập nhật giao diện. Không chạy `docker compose down -v`.

Nếu chỉ phát triển frontend:

```powershell
cd frontend
npm ci
npm run dev
```

Backend vẫn cần chạy theo cấu hình hiện tại của dự án.

## Kiểm tra đã thực hiện

- `npm ci` và `npm run build`: thành công.
- Kiểm thử DOM với API giả lập trên 13 trang: đăng nhập/đăng xuất, render thẻ KPI, phân quyền menu, chuyển doanh nghiệp và tải lại dữ liệu, mở/đóng menu điện thoại, Escape và quản lý focus/khóa cuộn của hộp thoại: thành công.
- Trình duyệt kiểm tra hình ảnh không khởi động được trong môi trường thực hiện. Chưa xác minh trực quan responsive trên thiết bị thật hoặc chạy end-to-end với .NET/MySQL.
- Backend, schema và dữ liệu nghiệp vụ không được chỉnh sửa trong bản cập nhật này.

## Những phần chưa nằm trong bản cập nhật

Bản này tập trung giao diện và hoạt ảnh. Chưa bổ sung phân trang/tìm kiếm mới, lịch sử chatbot lưu lâu dài hay sửa các lỗi OCR/backend đã ghi nhận trước đó.
