# Database

Schema được Entity Framework Core tạo tự động ở lần chạy đầu và dữ liệu demo
được seed theo cách lặp lại an toàn. MySQL dùng `utf8mb4_unicode_ci` để lưu tiếng Việt.

Dữ liệu bền vững nằm trong Docker volume `taxflow-ai_mysql_data`. File hóa đơn
tải lên nằm trong `../storage` để có thể sao lưu độc lập.
