using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;
public sealed record AssistantPeriod(DateTime From, DateTime To, string Label)
{
    public static string Normalize(string text) => new string(text.ToLowerInvariant().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd');
    public static AssistantPeriod Resolve(string question, DateTime now)
    {
        var q = Normalize(question); var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        if (q.Contains("tuan nay") || q.Contains("tuan truoc"))
        {
            var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
            var start = q.Contains("tuan truoc") ? monday.AddDays(-7) : monday;
            return new(start, start.AddDays(7), $"tuần {start:dd/MM/yyyy}–{start.AddDays(6):dd/MM/yyyy} (UTC)");
        }
        foreach (var days in new[] { 7, 30, 90 }) if (q.Contains($"{days} ngay")) return new(now.Date.AddDays(1-days), now.Date.AddDays(1), $"{days} ngày gần nhất (UTC)");
        if (q.Contains("quy")) throw new InvalidOperationException("Câu hỏi theo quý chưa được hỗ trợ. Hãy chỉ rõ tháng/năm hoặc 7, 30, 90 ngày gần nhất.");
        if (q.Contains("hom qua")) return new(now.Date.AddDays(-1), now.Date, "hôm qua (UTC)");
        if (q.Contains("hom nay")) return new(now.Date, now.Date.AddDays(1), "hôm nay (UTC)");
        if (q.Contains("thang truoc")) return new(month.AddMonths(-1), month, $"tháng {month.AddMonths(-1):MM/yyyy}");
        if (q.Contains("nam truoc")) return new(new DateTime(now.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), $"năm {now.Year - 1}");
        if (q.Contains("nam nay")) return new(new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), now, $"năm {now.Year}");
        var match = System.Text.RegularExpressions.Regex.Match(q, @"thang\s+(\d{1,2})(?:\s*[/\-]\s*|\s+nam\s+)?(\d{4})?");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var m) && m is >= 1 and <= 12)
        {
            var y = int.TryParse(match.Groups[2].Value, out var yr) ? yr : now.Year;
            if (y is >= 2000 and <= 2200) { var start = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc); return new(start, start.AddMonths(1), $"tháng {m:00}/{y}"); }
        }
        return new(month, month.AddMonths(1), $"tháng {now:MM/yyyy}");
    }
}
public sealed record AssistantAnswer(string Answer, string Source, DateTime CalculatedAt, string Mode);
public sealed class FinancialAssistantService(AppDbContext db, FinancialSummaryService financial, GroundedFinancialAssistant cloud, ReceivableConversationService conversation)
{
    public async Task<AssistantAnswer> AnswerAsync(Guid cid, Guid uid, AssistantRequest request, CancellationToken ct, bool allowCloud = true)
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4000) throw new InvalidOperationException("Câu hỏi phải có từ 1 đến 4000 ký tự.");
        var now = DateTime.UtcNow; var q = AssistantPeriod.Normalize(request.Question);
        var contextual = await conversation.TryAnswerAsync(cid, uid, request.Question, ct);
        if (contextual is not null)
            return await SaveReplyAsync(cid, uid, request.Question, contextual.Answer, contextual.Source, "database-context", now, contextual.Context, ct);
        var period = AssistantPeriod.Resolve(request.Question, now);
        var history = await db.ChatMessages.AsNoTracking().Where(x => x.CompanyId == cid && x.UserId == uid).OrderByDescending(x => x.CreatedAt).Take(12).OrderBy(x => x.CreatedAt).Select(x => new AssistantChatTurn(x.Role, x.Text)).ToListAsync(ct);
        var profit = await financial.ProfitAsync(cid, period.From, period.To, ct);
        var income = await SumAsync(db.IncomeTransactions.Where(x => x.CompanyId == cid && x.TransactionDate >= period.From && x.TransactionDate < period.To).Select(x => x.Amount), ct);
        var expense = await SumAsync(db.ExpenseTransactions.Where(x => x.CompanyId == cid && x.TransactionDate >= period.From && x.TransactionDate < period.To).Select(x => x.Amount), ct);
        var receivables = await SumAsync(db.Receivables.Where(x => x.CompanyId == cid && x.RemainingAmount > 0).Select(x => x.RemainingAmount), ct);
        var payables = await SumAsync(db.Payables.Where(x => x.CompanyId == cid && x.RemainingAmount > 0).Select(x => x.RemainingAmount), ct);
        var vatIn = await SumAsync(db.Invoices.Where(x => x.CompanyId == cid && x.Direction == "INCOMING" && x.AccountingReviewed && x.Status != InvoiceStatuses.Cancelled && x.InvoiceDate >= period.From && x.InvoiceDate < period.To).Select(x => x.VatAmount), ct);
        var vatOut = await SumAsync(db.Invoices.Where(x => x.CompanyId == cid && x.Direction == "OUTGOING" && x.IsLegallyIssued && x.Status != InvoiceStatuses.Cancelled && x.InvoiceDate >= period.From && x.InvoiceDate < period.To).Select(x => x.VatAmount), ct);
        var mode = "database"; string answer;
        if (q.Contains("so sanh") && q.Contains("doanh thu") && q.Contains("thang"))
        {
            var currentStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var current = await financial.ProfitAsync(cid, currentStart, now, ct);
            var previous = await financial.ProfitAsync(cid, currentStart.AddMonths(-1), currentStart, ct);
            var diff = current.NetRevenue - previous.NetRevenue;
            answer = $"Doanh thu chưa VAT tháng {now:MM/yyyy} đến hiện tại: {Money(current.NetRevenue)}. Cả tháng {currentStart.AddMonths(-1):MM/yyyy}: {Money(previous.NetRevenue)}. Chênh lệch: {Money(diff)}. Hai kỳ có độ dài khác nhau; cần lưu ý khi so sánh.";
        }
        else if (q.Contains("doanh thu")) answer = $"Doanh thu chưa VAT {period.Label}: {Money(profit.NetRevenue)}.";
        else if (q.Contains("loi nhuan")) answer = $"Lợi nhuận quản trị ước tính {period.Label}: {Money(profit.EstimatedProfit)} = doanh thu chưa VAT {Money(profit.NetRevenue)} − giá vốn {Money(profit.CostOfGoods)} − chi phí vận hành chưa VAT {Money(profit.OperatingExpense)}. Chưa phải kết quả kế toán đã kiểm toán.";
        else if (q.Contains("chi phi") || q.Contains("dong tien")) answer = $"Trong {period.Label}: tiền vào {Money(income)}, tiền ra {Money(expense)}, dòng tiền ròng {Money(income - expense)}. Chi phí vận hành chưa VAT: {Money(profit.OperatingExpense)}.";
        else if (q.Contains("vat")) answer = $"VAT {period.Label}: đầu vào đã rà soát nội bộ {Money(vatIn)}, đầu ra đã được nhà cung cấp xác nhận phát hành {Money(vatOut)}, chênh lệch {Money(vatOut - vatIn)}. Hóa đơn nháp/chờ chưa được tính vào số này; đây chưa phải tờ khai chính thức.";
        else if (q.Contains("phai thu") || q.Contains("cong no") || q.Contains("phai tra")) answer = $"Số dư hiện tại: phải thu {Money(receivables)}; phải trả {Money(payables)}. Đây là số dư hiện tại, không phải số dư lịch sử của kỳ được hỏi.";
        else if (q.Contains("sap het") || q.Contains("ton kho"))
        {
            var stock = await db.Products.Where(x => x.CompanyId == cid && x.IsActive && x.Stock <= x.MinimumStock).OrderBy(x => x.Stock).Take(20).ToListAsync(ct);
            answer = stock.Count == 0 ? "Không có sản phẩm dưới ngưỡng tồn tối thiểu." : string.Join("\n", stock.Select(x => $"• {x.Sku} — {x.Name}: còn {x.Stock}, ngưỡng {x.MinimumStock}."));
        }
        else if (q.Contains("bat thuong") || q.Contains("canh bao") || q.Contains("dang chu y") || q.Contains("rui ro"))
        {
            var alerts = await db.AIAlerts.Where(x => x.CompanyId == cid).OrderByDescending(x => x.CreatedAt).Take(12).ToListAsync(ct);
            var lines = new List<string>();
            foreach (var alert in alerts)
            {
                var invoice = alert.EntityType == "Invoice" && alert.EntityId.HasValue ? await db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == alert.EntityId && x.CompanyId == cid, ct) : null;
                lines.Add(invoice is null ? $"• [{alert.Severity}] {alert.Title}: {alert.Message}" : $"• [{alert.Severity}] HĐ {invoice.Number} — {invoice.PartyName} — {invoice.InvoiceDate:dd/MM/yyyy} — {Money(invoice.Total)}. {alert.Message}");
            }
            answer = lines.Count == 0 ? "Chưa có cảnh báo trong dữ liệu hiện tại. Điều này không chứng minh tất cả giao dịch đều bình thường." : string.Join("\n", lines);
        }
        else if (q.Contains("hoa don") && (q.Contains("moi nhat") || q.Contains("gan nhat")))
        {
            var invoice = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == cid).OrderByDescending(x => x.InvoiceDate).FirstOrDefaultAsync(ct);
            answer = invoice is null ? "Chưa có hóa đơn." : $"Hóa đơn mới nhất: {invoice.Number} — {invoice.PartyName} — ngày {invoice.InvoiceDate:dd/MM/yyyy}, tổng {Money(invoice.Total)}, trạng thái {invoice.Status}.";
        }
        else
        {
            var facts = new { period = period.Label, netRevenue = Money(profit.NetRevenue), costOfGoods = Money(profit.CostOfGoods), operatingExpense = Money(profit.OperatingExpense), estimatedProfit = Money(profit.EstimatedProfit), cashIn = Money(income), cashOut = Money(expense), receivables = Money(receivables), payables = Money(payables), vatIn = Money(vatIn), vatOut = Money(vatOut) };
            // For a general summary the optional model may explain concepts, but numerical facts are always rendered by the backend.
            string? explanation = null;
            try { if (allowCloud) explanation = await cloud.AnswerAsync(request.Question, facts, history, ct); } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
            var baseline = $"Tổng quan {period.Label}: doanh thu chưa VAT {Money(profit.NetRevenue)}, giá vốn {Money(profit.CostOfGoods)}, chi phí vận hành chưa VAT {Money(profit.OperatingExpense)}, lợi nhuận quản trị ước tính {Money(profit.EstimatedProfit)}. Tiền vào {Money(income)}, tiền ra {Money(expense)}.";
            // Numeric LLM output is not promoted as a verified source; deterministic figures remain authoritative.
            var isSummary = q.Contains("tom tat") || q.Contains("tong quan") || q.Contains("tinh hinh tai chinh");
            answer = isSummary ? baseline : "Mình chưa xác định được dữ liệu bạn muốn hỏi. Bạn hãy nêu khách hàng, khoản nợ hoặc chỉ tiêu và kỳ thời gian cần xem.";
            if (isSummary && !string.IsNullOrWhiteSpace(explanation) && !explanation.Any(char.IsDigit)) { answer += "\n\n" + explanation; mode = "database+gemini-explanation"; }

        }
        return await SaveReplyAsync(cid, uid, request.Question, answer, "Orders, OrderItems, Invoices, IncomeTransactions, ExpenseTransactions, Receivables, Payables, Products, AIAlerts", mode, now, null, ct);
    }
    private async Task<AssistantAnswer> SaveReplyAsync(Guid cid, Guid uid, string question, string answer, string source, string mode, DateTime now, ConversationContext? context, CancellationToken ct)
    {
        db.ChatMessages.AddRange(
            new ChatMessage { CompanyId = cid, UserId = uid, Role = "user", Text = question.Trim(), CreatedAt = now.AddMilliseconds(-1) },
            new ChatMessage { CompanyId = cid, UserId = uid, Role = "assistant", Text = answer, Source = source, ContextJson = context is null ? "{}" : JsonSerializer.Serialize(context), CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return new(answer, source, now, mode);
    }
    private async Task<decimal> SumAsync(IQueryable<decimal> query, CancellationToken ct) => db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? (await query.ToListAsync(ct)).Sum() : await query.SumAsync(ct);
    private static string Money(decimal value) => value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
}
