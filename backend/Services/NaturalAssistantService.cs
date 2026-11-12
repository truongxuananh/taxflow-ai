using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;

public sealed class NaturalAssistantService(AppDbContext db, GeminiFunctionPlanner planner, GeminiBusinessTools tools, FinancialAssistantService fallback, IConfiguration configuration)
{
    public async Task<AssistantAnswer> AnswerAsync(Guid cid, Guid uid, AssistantRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4000)
            throw new InvalidOperationException("Câu hỏi phải có từ 1 đến 4000 ký tự.");
        if (string.Equals(configuration["ASSISTANT_MODE"], "legacy", StringComparison.OrdinalIgnoreCase))
            return await FallbackAsync(cid, uid, request, "legacy", ct);
        if (!planner.IsAvailable) return await FallbackAsync(cid, uid, request, "missing_key", ct);
        var history = await db.ChatMessages.AsNoTracking().Where(x => x.CompanyId == cid && x.UserId == uid)
            .OrderByDescending(x => x.CreatedAt).Take(12).ToListAsync(ct);
        var contextJson = history.FirstOrDefault(x => x.Role == "assistant")?.ContextJson ?? "{}";
        NaturalContext? context = null;
        try { context = JsonSerializer.Deserialize<NaturalContext>(contextJson); } catch (JsonException) { contextJson = "{}"; }
        IReadOnlyList<GeminiToolCall> calls;
        try
        {
            calls = await planner.PlanAsync(request.Question, history.OrderBy(x => x.CreatedAt).Select(x => new { x.Role, x.Text }).ToArray(), contextJson, ct);
        }
        catch (Exception ex) when (ex is GeminiPlanningException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            var reason = ex is GeminiPlanningException problem ? problem.Message : "unavailable";
            return await FallbackAsync(cid, uid, request, reason, ct);
        }
        var results = new List<BusinessToolResult>();
        // A clarification is exclusive; never append unrelated figures to a request that needs clarification.
        if (calls.Any(x => x.Name == "clarify_question")) calls = calls.Where(x => x.Name == "clarify_question").Take(1).ToArray();
        try
        {
            foreach (var call in calls)
            {
                var result = await tools.ExecuteAsync(cid, uid, call, context, ct);
                results.Add(result);
                context = result.Context;
                if (result.Source is "Ngữ cảnh hội thoại" or "Cần xác định kỳ thời gian" or "Cần làm rõ yêu cầu") break;
            }
        }
        catch (ToolInputException ex)
        {
            results.Clear(); results.Add(new(ex.Message, "Cần làm rõ yêu cầu")); context = null;
        }
        var answer = string.Join("\n\n", results.Select(x => x.Answer));
        var source = string.Join("; ", results.Select(x => x.Source).Distinct());
        if (source.Length > 250) source = source[..250];
        var now = DateTime.UtcNow;
        db.ChatMessages.AddRange(
            new ChatMessage { CompanyId = cid, UserId = uid, Role = "user", Text = request.Question.Trim(), CreatedAt = now.AddMilliseconds(-1) },
            new ChatMessage { CompanyId = cid, UserId = uid, Role = "assistant", Text = answer, Source = source, ContextJson = context is null ? "{}" : JsonSerializer.Serialize(context), CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return new(answer, source, now, "gemini-functions+database");
    }
    private async Task<AssistantAnswer> FallbackAsync(Guid cid, Guid uid, AssistantRequest request, string reason, CancellationToken ct)
    {
        var result = await fallback.AnswerAsync(cid, uid, request, ct, allowCloud: false);
        // Update the saved message too, so reloading history does not hide the provider failure.
        var prefix = reason == "legacy" ? "Đang dùng chế độ truy vấn dự phòng.\n\n" : "Gemini chưa sẵn sàng; đang dùng truy vấn dự phòng với khả năng hiểu câu hạn chế.\n\n";
        var message = await db.ChatMessages.Where(x => x.CompanyId == cid && x.UserId == uid && x.Role == "assistant")
            .OrderByDescending(x => x.CreatedAt).FirstAsync(ct);
        message.Text = prefix + result.Answer;
        await db.SaveChangesAsync(ct);
        return result with { Answer = message.Text, Mode = "fallback-" + reason };
    }
}
