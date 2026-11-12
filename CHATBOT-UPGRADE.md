# TaxFlow Financial Copilot

Bản nâng cấp chatbot theo hướng hội thoại nhiều lượt và grounded data.

## Đã nâng cấp
- Gửi tối đa 12 lượt chat gần nhất để hiểu câu hỏi nối tiếp.
- Gemini không còn bị giới hạn bởi chuỗi if/else câu hỏi cố định.
- Backend tạo financial snapshot đã scope theo CompanyId trước khi gửi Gemini.
- Snapshot gồm doanh thu hôm nay/tháng/tháng trước/năm, thu/chi, dòng tiền, phải thu/phải trả, VAT, khách nợ lớn, nhà cung cấp cần trả, hóa đơn/đơn hàng gần đây, tồn kho thấp và cảnh báo AI.
- Gemini được prompt nghiêm ngặt: không tự tạo số liệu, chỉ dùng CONTEXT.
- Frontend hỗ trợ các câu nối tiếp như “so với tháng trước?”, “còn ai nữa?”, “hóa đơn gần đây thì sao?”.

## Chạy
```powershell
docker compose down
docker compose up -d --build
docker compose logs -f backend
```

## Test
1. `Tóm tắt tình hình tài chính tháng này`
2. `So với tháng trước thì sao?`
3. `Khách hàng nào nợ nhiều nhất?`
4. `Họ có khoản nào quá hạn không?`
5. `Có hóa đơn nào đáng chú ý?`
6. `Sản phẩm nào sắp hết hàng?`

Lưu ý: đây là grounded conversational assistant. Tool-calling động/RAG tài liệu có thể bổ sung ở giai đoạn tiếp theo.
