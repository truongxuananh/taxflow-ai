# TaxFlow AI - Phân quyền RBAC

Hệ thống dùng 4 vai trò: `ADMIN`, `ACCOUNTANT`, `EMPLOYEE`, `DIRECTOR`. Backend là lớp bắt buộc quyền; frontend chỉ ẩn/hiện chức năng để cải thiện UX.

| Nhóm chức năng | ADMIN | ACCOUNTANT | EMPLOYEE | DIRECTOR |
|---|---|---|---|---|
| Dashboard | Toàn quyền | Xem | Xem | Xem |
| User & role | Quản lý | Không | Không | Không |
| Doanh nghiệp | Quản lý | Xem | Không | Xem |
| Tài khoản ngân hàng | Quản lý | Quản lý | Không | Xem |
| Sản phẩm | Quản lý | Thêm/sửa | Xem | Xem |
| Kho | Quản lý | Quản lý | Không | Xem |
| POS | Thao tác | Thao tác | Thao tác | Không |
| Khách hàng / NCC | Thao tác | Thao tác | Thao tác | Xem |
| Hóa đơn & OCR | Thao tác | Thao tác | Không | Không |
| Thu/chi | Thao tác | Thao tác | Không | Xem |
| Công nợ | Thao tác | Thao tác | Không | Xem |
| Đối soát | Xem | Xem | Không | Xem |
| Thuế | Thao tác | Thao tác | Không | Xem |
| Báo cáo | Thao tác | Thao tác | Không | Xem/tạo |
| AI tài chính | Dùng | Dùng | Không | Dùng |
| Audit log | Xem | Xem | Không | Xem |

## Nguyên tắc
- ADMIN: quản trị hệ thống, tài khoản, role và có quyền xử lý nghiệp vụ khi cần.
- ACCOUNTANT: xử lý hóa đơn, OCR, tài chính, công nợ, thuế và báo cáo.
- EMPLOYEE: tập trung POS, sản phẩm để bán hàng và dữ liệu đối tác phục vụ vận hành.
- DIRECTOR: chủ yếu đọc dashboard, tài chính, công nợ, thuế, báo cáo, AI và audit để hỗ trợ quyết định; không sửa dữ liệu kế toán gốc.
