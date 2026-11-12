# TaxFlow Financial Copilot - Chatbot Fix V2

## Mục tiêu
Chatbot phải trả lời bằng dữ liệu nghiệp vụ cụ thể, đặc biệt với câu hỏi về hóa đơn/giao dịch đáng chú ý hoặc bất thường.

## Thay đổi
- AIAlerts được enrich với bản ghi Invoice/ExpenseTransaction thật bằng EntityId.
- Context gửi Gemini có số hóa đơn, đối tác, ngày, subtotal, VAT, tổng tiền, trạng thái, mức cảnh báo và lý do.
- Phân biệt cảnh báo AI với giao dịch chỉ có giá trị lớn.
- Chặn prompt leakage dạng `Mention...`, `Suggested...`, `Respond...`.
- Khi Gemini lỗi/503 hoặc output không hợp lệ, backend dùng deterministic fallback thay vì snapshot chung.
- Fallback hỗ trợ anomaly/notable, hóa đơn mới nhất, công nợ lớn nhất, tồn kho thấp, VAT, doanh thu, chi phí, dòng tiền và công nợ.

## Test khuyến nghị
1. Vào Trợ lý AI > AI Anomaly và quét bất thường.
2. Hỏi: `Có hóa đơn hay giao dịch nào đáng chú ý?`
3. Hỏi: `Hóa đơn nào có mức bất thường cao nhất?`
4. Hỏi: `Hóa đơn mới nhất là gì?`
5. Hỏi: `Khách hàng nào đang nợ nhiều nhất?`

Nếu AIAlerts có EntityId trỏ tới Invoice/ExpenseTransaction, câu trả lời sẽ nêu bản ghi cụ thể. Alert demo cũ không có EntityId chỉ được dùng như cảnh báo tổng quát, không được bịa ra một giao dịch cụ thể.
