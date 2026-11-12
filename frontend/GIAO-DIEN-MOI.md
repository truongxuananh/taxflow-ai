# TaxFlow AI — Giao diện "Mực & Dấu"

Bản nâng cấp này chỉ thay đổi thư mục `frontend`. Backend, ai-service, database và `.env` giữ nguyên.

## Cách cập nhật

1. Sao lưu thư mục `frontend` cũ (ví dụ đổi tên thành `frontend-cu`).
2. Chép thư mục `frontend` mới vào đúng vị trí, cạnh `docker-compose.yml`.
3. Chạy lại riêng frontend:

```powershell
docker compose up -d --build frontend
```

4. Mở http://localhost:3000 và nhấn Ctrl + Shift + R.

## Thay đổi chính

- Bảng màu: sidebar màu xanh mực đậm, nhấn xanh cobalt, nền giấy xanh nhạt có lưới chấm. Xanh ngọc cho tiền vào/lợi nhuận, đỏ son cho tiền ra/cảnh báo.
- Font Be Vietnam Pro (thiết kế cho tiếng Việt) được đóng gói sẵn, không cần Internet. Số tiền dùng chữ số đều độ rộng.
- Chuyển trang: trang cũ mờ đi, trang mới trượt lên; vạch mực chạy dưới thanh tiêu đề; tiêu đề trang trên header cũng chuyển động.
- Sidebar: ô đánh dấu mục đang chọn trượt mượt giữa các mục; menu điện thoại trượt bằng lò xo, có hiệu ứng đóng.
- Tab dùng chung (`Tabs` trong `components/UI.tsx`) có ô đánh dấu trượt: Thu chi, Công nợ, Hóa đơn, Trợ lý AI, Cảnh báo.
- Đăng nhập: phiếu hóa đơn minh họa được đóng dấu đỏ "Đã đối soát"; nút hiện/ẩn mật khẩu; thông báo lỗi rung nhẹ.
- Dashboard: hero hiển thị lợi nhuận ước tính và tỷ lệ thu/chi; thẻ KPI có vạch màu theo loại chỉ số.
- Trợ lý AI: bong bóng chat xuất hiện có hiệu ứng, có chỉ báo "đang trả lời"; chỉ cuộn khung chat, không kéo cả trang.
- Khi tải trang lần đầu, khung skeleton hiển thị trong vùng nội dung thay vì che toàn bộ màn hình.
- Vẫn tôn trọng cài đặt giảm chuyển động (prefers-reduced-motion) của hệ điều hành.

## Thư viện thêm

- `motion` — hoạt ảnh chuyển trang, ô đánh dấu trượt.
- `@fontsource/be-vietnam-pro` — font đóng gói cùng ứng dụng.

## Kiểm tra đã chạy

- `npm run build`: thành công.
- `npm test` (DOM giả lập, 15 route): PASS, chạy 3 lần liên tiếp.
- Chụp màn hình bằng Chrome với API giả lập: đăng nhập, Dashboard, chuyển trang, Trợ lý AI, điện thoại. Không có lỗi console.
- Chưa kiểm tra với backend/MySQL thật trên máy của bạn.
