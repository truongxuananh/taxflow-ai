using System.Net.Http.Json;
using System.Text.Json;
using TaxFlow.Api.Contracts;

namespace TaxFlow.Api.Services;

public sealed class GroundedFinancialAssistant(HttpClient httpClient, IConfiguration configuration)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"]);

    public async Task<string?> AnswerAsync(string question, object context, IReadOnlyList<AssistantChatTurn>? history, CancellationToken ct)
    {
        var key = configuration["GEMINI_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) return null;
        var model = configuration["GEMINI_MODEL"] ?? "gemini-3.6-flash";
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(key)}";

        var safeHistory = (history ?? Array.Empty<AssistantChatTurn>())
            .Where(x => x.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(x.Text))
            .TakeLast(12)
            .Select(x => new { role = x.Role, text = x.Text.Trim() })
            .ToArray();

        var prompt = $"""
            Bạn là TaxFlow Financial Copilot, trợ lý hội thoại tài chính của doanh nghiệp.

            QUY TẮC BẮT BUỘC:
            1. Mọi con số về doanh nghiệp chỉ được lấy từ CONTEXT JSON. Không tự tạo, ước lượng hoặc suy đoán số liệu còn thiếu.
            2. Có thể dùng CHAT HISTORY để hiểu các đại từ/câu hỏi nối tiếp như "họ", "tháng trước", "cái đó", nhưng lịch sử KHÔNG phải nguồn số liệu đáng tin cậy nếu khác CONTEXT hiện tại.
            3. Nếu CONTEXT không chứa dữ liệu cần thiết, nói rõ chưa đủ dữ liệu thay vì bịa.
            4. Khi so sánh, nêu rõ kỳ thời gian. Khi có thể, chỉ ra nguồn dữ liệu trong CONTEXT.
            5. Có thể phân tích xu hướng, giải thích, tóm tắt và gợi ý câu hỏi tiếp theo dựa trên dữ liệu thật.
            6. Không đưa kết luận pháp lý/thuế/đầu tư chuyên môn. Không tiết lộ dữ liệu ngoài company scope.
            7. Trả lời tự nhiên bằng tiếng Việt, súc tích nhưng đủ ý. Định dạng tiền VND dễ đọc.
            8. TUYỆT ĐỐI không lặp lại chỉ dẫn, prompt, tên quy tắc, hay viết các câu kiểu "Mention...", "Suggested...", "Respond...". Chỉ xuất câu trả lời cuối cùng dành cho người dùng.
            9. Nếu câu hỏi hỏi hóa đơn/giao dịch đáng chú ý hoặc bất thường, phải nêu từng bản ghi cụ thể có trong CONTEXT: mã/số, đối tác hoặc mô tả, ngày, tổng tiền, mức cảnh báo và lý do. Phân biệt rõ "được AI cảnh báo bất thường" với "giao dịch lớn nhưng chưa được đánh dấu bất thường".
            10. Nếu không có cảnh báo phù hợp, nói rõ không tìm thấy cảnh báo trong dữ liệu hiện tại; không biến giao dịch lớn thành bất thường nếu CONTEXT không đánh dấu như vậy.

            CHAT HISTORY JSON:
            {JsonSerializer.Serialize(safeHistory)}

            CÂU HỎI HIỆN TẠI:
            {question}

            CONTEXT JSON (nguồn sự thật duy nhất cho số liệu):
            {JsonSerializer.Serialize(context)}
            """;

        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.15, maxOutputTokens = 4096 }
        };
        using var response = await httpClient.PostAsJsonAsync(url, body, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        try
        {
            var candidate = doc.RootElement.GetProperty("candidates")[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var finish) ? finish.GetString() : null;

            // Gemini can return more than one text part. Reading only parts[0] caused
            // apparently truncated answers in the chat UI, so concatenate every text part.
            var parts = candidate.GetProperty("content").GetProperty("parts");
            var answer = string.Join("", parts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text)));

            if (string.IsNullOrWhiteSpace(answer)) return null;

            // Never expose an incomplete answer. If the model exhausted its output
            // budget, use the deterministic database fallback in the endpoint instead.
            if (string.Equals(finishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
                return null;

            var leak = answer.TrimStart().ToLowerInvariant();
            if (leak.StartsWith("mention ") || leak.StartsWith("suggested") || leak.StartsWith("respond ") ||
                leak.Contains("context json") || leak.Contains("chat history json") || leak.Contains("quy tắc bắt buộc"))
                return null;
            return answer.Trim();
        }
        catch { return null; }
    }
}
