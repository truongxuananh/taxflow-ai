# TaxFlow AI V5 — hỏi dữ liệu bằng ngôn ngữ tự nhiên

## 1. Bạn cần làm gì?

Bản V5 đã thêm lớp Gemini Function Calling cho trợ lý. Bạn không cần tự viết thêm điều kiện cho từng cách hỏi, cũng không cần tạo nhiều API key.

1. Giải nén toàn bộ bản V5 vào một thư mục riêng.
2. Nếu đang chạy phiên bản cũ, sao lưu dữ liệu trước bằng `scripts/Backup.ps1` và đọc `HUONG-DAN-V4.html`. Giữ file `.env`, thư mục `storage` và Docker volume MySQL đang dùng. Không chạy `docker compose down -v` khi nâng cấp.
3. Dùng `.env` đang hoạt động của bạn trong thư mục chứa `docker-compose.yml`. Không chép đè `.env` cũ bằng `.env.example`; file mẫu để người cài mới tham khảo.
4. Kiểm tra các biến dưới đây trong `.env`; giữ nguyên key của bạn. Model phải là model mà tài khoản được phép sử dụng.

```dotenv
ASSISTANT_MODE=natural
GEMINI_API_KEY=key_cua_ban
GEMINI_MODEL=model_tai_khoan_cua_ban_ho_tro
```

Không nhập nguyên các giá trị minh họa ở trên. Key chỉ nằm phía backend; không đưa vào biến `VITE_*` hay mã frontend.

5. Mở Docker Desktop, mở PowerShell tại thư mục chứa `docker-compose.yml`, chạy:

```powershell
docker compose up -d --build
docker compose ps
```

6. Khi các dịch vụ sẵn sàng, mở `http://localhost:3000`, nhấn Ctrl + Shift + R, đăng nhập lại và chọn đúng doanh nghiệp.
7. Vào **Trợ lý & AI tài chính → Hỏi dữ liệu**. Gửi một câu hỏi. Dòng **Gemini hiểu câu hỏi · dữ liệu từ hệ thống** dưới câu trả lời cho biết lần đó đã đi qua Gemini và công cụ dữ liệu. Dòng thông báo “đã cấu hình” phía trên chỉ xác nhận có cấu hình, chưa xác nhận API kết nối được.
8. Nếu lịch sử cũ đang lẫn nhiều chủ đề, bấm “Xóa lịch sử của tôi” để bắt đầu lại. Thao tác này chỉ xóa hội thoại của bạn trong doanh nghiệp đang chọn.

Nâng cấp V5 không thêm bảng/cột mới so với V4.1. Vẫn cần migration ContextJson của V4.1 nếu đi từ bản cũ hơn. Backend kiểm tra/khởi tạo database theo cơ chế có sẵn; xem `docs/DATABASE_MIGRATIONS.md` nếu log báo lỗi schema. Không tự chạy SQL xóa bảng để xử lý lỗi.

## 2. Thử đúng tình huống bạn gặp

Bạn có thể hỏi theo chuỗi:

- “Ai đang thiếu tiền bên mình nhiều nhất?”
- “Khoản gì thế?”
- “Họ trễ mấy ngày rồi?”
- “Đã trả được bao nhiêu?”
- “Cho xem chi tiết lại.”

Không cần gõ đúng một câu mẫu. Gemini chọn ý định và tham số; backend dùng khách hàng đã xác định trong ngữ cảnh của bạn để đọc lại sổ công nợ. Nếu người đó đã trả tiền sau câu hỏi đầu tiên, câu tiếp theo phải dùng số dư mới. Nếu có nhiều khách hàng trùng tên, hệ thống yêu cầu bạn chọn tên/mã rõ hơn.

Các câu hỏi khác có thể thử:

| Nhu cầu | Ví dụ |
|---|---|
| Doanh thu theo kỳ | Tháng trước bán được bao nhiêu, chưa tính VAT? |
| So sánh | So với tháng trước nữa thì chênh bao nhiêu? |
| Lợi nhuận | Quý trước lời bao nhiêu sau giá vốn và chi phí vận hành? |
| Thu/chi thực tế | Tuần này tiền vào và tiền ra thế nào? |
| VAT | VAT tháng mười gồm những số nào? |
| Hóa đơn | Cho xem hóa đơn mới nhất, rồi xem chi tiết cái đó. |
| Tồn kho | Mặt hàng nào gần hết? |
| Cảnh báo | Có cảnh báo hóa đơn nào đang lưu? |
| Tìm khách | Tìm giúp khách có tên Nhi. |

Nếu chưa nói kỳ thời gian, trợ lý có thể hỏi “Bạn muốn xem trong kỳ nào?”. Bạn có thể trả lời “tháng trước” hoặc nêu khoảng ngày. Nếu đổi chủ đề, hãy nói rõ chỉ tiêu/đối tượng mới để giảm mơ hồ.

## 3. Phần đã thay đổi

### Hiểu câu hỏi bằng model

`GeminiFunctionPlanner` gửi câu hỏi, tối đa 12 tin nhắn gần nhất và ngữ cảnh của đúng người dùng/doanh nghiệp đến Gemini. Ngày hiện tại và múi giờ Việt Nam được cung cấp để model xác định các kỳ tương đối. Lịch sử là ngữ cảnh để hiểu câu, không phải nguồn số liệu hiện tại.

Model chỉ được đề xuất các hàm đã khai báo. Không nhận SQL, câu lệnh cập nhật hay `companyId` từ model. Có tối đa ba công cụ cho một câu hỏi; các công cụ được chạy lần lượt để có thể dùng ngữ cảnh của kết quả trước. Nếu cần làm rõ thì ưu tiên hỏi lại.

### Dữ liệu và tính toán ở backend

| Công cụ | Dữ liệu/chức năng |
|---|---|
| get_receivables | Tổng phải thu, khách nợ nhiều nhất, chi tiết, quá hạn, thanh toán |
| get_financial_metrics | Doanh thu, tiền vào/ra, chi phí vận hành, giá vốn, dòng tiền, lợi nhuận, VAT, so sánh kỳ |
| get_invoices | Hóa đơn theo từ khóa, hướng, khoảng ngày; hóa đơn mới nhất và chi tiết |
| get_inventory | Tồn kho hiện tại, mặt hàng dưới ngưỡng |
| get_alerts | Cảnh báo đã lưu |
| find_customers | Tìm khách để xác định đối tượng |
| explain_concept | Giải thích khái niệm và khả năng phần mềm |
| clarify_question | Hỏi thông tin còn thiếu hoặc giới hạn hiện tại |

Các phép tính và phần số liệu trong câu trả lời do C# dựng từ dữ liệu truy vấn. Không có bước cho model tự viết lại số tiền. Vì vậy cách trả lời thiên về rõ nguồn, con số và công thức; đây không phải chatbot tư vấn mọi lĩnh vực.

Ngữ cảnh được lưu ở `ChatMessages.ContextJson`, tách theo CompanyId/UserId. ID khách/hóa đơn trong ngữ cảnh vẫn phải kiểm tra lại quyền và doanh nghiệp trước khi đọc. Số dư được truy vấn lại, không tái sử dụng số tiền trong lịch sử.

### Kỳ và ý nghĩa chỉ tiêu

- Khoảng ngày tài chính dùng ngày lịch Việt Nam, bao gồm cả ngày đầu và cuối; backend đổi sang UTC để truy vấn. Mỗi khoảng tối đa 1.830 ngày.
- Doanh thu là đơn hoàn tất sau giảm giá, chưa VAT; khác tiền thực tế đã thu.
- Lợi nhuận quản trị ước tính = doanh thu chưa VAT − giá vốn snapshot − chi phí vận hành chưa VAT. Mua hàng nhập kho và hoàn tiền không bị trừ lần hai như chi phí vận hành.
- Tiền vào/ra lấy từ sổ thu/chi, khác doanh thu/chi phí kế toán.
- VAT đầu vào cần được rà soát; đầu ra cần xác nhận phát hành. Số này chưa phải tờ khai chính thức.
- Công nợ/tồn kho là số dư hiện tại. Chưa có chức năng khôi phục số dư lịch sử chỉ từ một ngày quá khứ.
- Ngày đối chiếu quá hạn của công cụ công nợ hiện dùng UTC và được ghi rõ trong câu trả lời; không nhầm với khoảng ngày báo cáo theo giờ Việt Nam.
- So sánh thể hiện chênh lệch. Chưa đủ dữ liệu để tự kết luận nguyên nhân hoặc dự báo chắc chắn.

## 4. Khi API gặp vấn đề

Nếu Gemini không khả dụng, backend dùng lớp dự phòng V4.1, không gọi model lần hai để kéo dài thời gian chờ. Câu trả lời và lịch sử hiển thị rõ rằng khả năng hiểu câu đang hạn chế.

| Chế độ trả về | Cần kiểm tra |
|---|---|
| gemini-functions+database | Đã nhận lời gọi công cụ từ Gemini và xử lý ở backend |
| fallback-missing_key | GEMINI_API_KEY chưa vào container backend |
| fallback-http_400 / 404 | Tên model, API hoặc khả năng Function Calling của model |
| fallback-http_401 / 403 | Key, quyền truy cập, trạng thái project |
| fallback-http_503 | Dịch vụ Gemini tạm không sẵn sàng; thử lại sau |
| fallback-http_429 | Hạn mức/tốc độ/chi phí của project; thử lại sau hoặc điều chỉnh gói |
| fallback-unavailable | Kết nối mạng, timeout hoặc phản hồi không đọc được |
| fallback-legacy | ASSISTANT_MODE=legacy đang được chọn |
| fallback-invalid_call / incomplete_response / missing_call | Model không trả lời đúng dạng công cụ; thử lại hoặc đổi model tương thích |

`GET /api/ai/assistant-status` cần đăng nhập và chọn đúng doanh nghiệp; chỉ trả model, chế độ và có key hay chưa. Không trả key, không tự thực hiện lời gọi Google để xác minh kết nối. Không cần thêm nhiều key để sửa lỗi hiểu hội thoại. Các key trong cùng project thường vẫn chịu hạn mức chung.

Sau khi sửa `.env`, chạy lại `docker compose up -d --build` để container nhận cấu hình. Không chụp/gửi toàn bộ `.env` hoặc log chứa bí mật khi nhờ hỗ trợ.

## 5. Giới hạn cần hiểu

Không có thiết kế nào bảo đảm “hỏi thế nào cũng chính xác 100%”. Model có thể chọn sai ý định/kỳ; dữ liệu đầu vào có thể sai hoặc thiếu; câu hỏi có thể mơ hồ. Bản này giảm rủi ro bằng hàm được giới hạn, kiểm tra tham số/quyền, tính số ở backend, nguồn rõ ràng và hỏi lại thay vì tự bịa.

Chỉ hỗ trợ các nghiệp vụ trong bảng công cụ. Không tự chuyển tiền, đánh dấu đã thanh toán, xóa dữ liệu, kết luận gian lận, tư vấn pháp lý/thuế tùy tình huống hoặc tra cứu tin tức bên ngoài. Các thao tác ghi vẫn thực hiện qua màn hình nghiệp vụ và quyền tương ứng.

Gemini nhận câu hỏi/lịch sử gần nhất để hiểu ý định. Không gửi toàn bộ cơ sở dữ liệu cho model, nhưng tin nhắn của bạn có thể chứa thông tin doanh nghiệp; hãy sử dụng tài khoản/project phù hợp với cách bạn muốn quản lý dữ liệu.

## 6. Kiểm chứng và mã nguồn

Kết quả chạy cụ thể ở `validation/RESULTS.md` và `validation/results.json`. Các bài kiểm tra API giả lập kiểm chứng hợp đồng Function Calling, quyền, ngữ cảnh, tính toán và lỗi provider; không được xem là bằng chứng cho mọi cách diễn đạt ngoài đời. Có thử riêng Gemini thật bằng câu hỏi/ngữ cảnh tổng hợp: 2 yêu cầu thành công; 1 timeout và 1 HTTP503. Không gửi dữ liệu database thật để thực hiện phép kiểm tra đó. Xem chi tiết trong kết quả kiểm tra.

Các file chính:

- `backend/Services/GeminiFunctionPlanner.cs`: hợp đồng Gemini và kiểm tra phản hồi.
- `backend/Services/GeminiBusinessTools.cs`: công cụ dữ liệu chỉ đọc.
- `backend/Services/NaturalAssistantService.cs`: lịch sử, thực thi, lưu hội thoại, chế độ dự phòng.
- `backend/Services/ReceivableConversationService.cs`: truy vấn chi tiết nợ/thanh toán.
- `frontend/src/pages/AssistantPage.tsx`: hội thoại, trạng thái cấu hình và nhãn chế độ trả lời.
- `tests/backend/NaturalLanguageTests.cs`: các tình huống mới.

Tài liệu chính thức tham khảo: https://ai.google.dev/gemini-api/docs/generate-content/function-calling và https://ai.google.dev/gemini-api/docs/rate-limits.

Các hướng dẫn giao diện, OCR, backup, phân quyền và nghiệp vụ của V4/V4.1 vẫn có trong gói này. Chưa kiểm chứng Docker/MySQL và trình duyệt thực tế trên máy của bạn; cần chạy các bước trên và đối chiếu với dữ liệu thật trước khi dùng kết quả để quyết định nghiệp vụ.
