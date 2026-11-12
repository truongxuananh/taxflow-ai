# Apple Silicon Verification Checklist

- [x] Không có `platform: linux/amd64` trong Docker Compose.
- [x] Dùng image multi-architecture chính thức cho Node, Nginx, .NET và MySQL.
- [x] Backend target `net8.0`, không dùng IIS hay Windows path.
- [x] Storage dùng bind mount `./storage:/app/storage`.
- [x] Ba container mặc định; không bắt buộc local AI container.
- [x] Frontend production build đã được kiểm tra.
- [x] Frontend production dependencies đã qua `npm audit` (0 vulnerability).
- [x] Toàn bộ 15 source C# backend đã biên dịch thành công với Roslyn .NET 8.
- [ ] `docker compose build` trên máy đích Apple Silicon.
- [ ] `docker compose up` và smoke test trên máy đích Apple Silicon.

Hai mục cuối cần Docker daemon ARM64 thực tế. Chạy:

```bash
./tests/verify-architecture.sh
docker compose build
docker compose up -d
./tests/smoke.sh
docker compose down
```
