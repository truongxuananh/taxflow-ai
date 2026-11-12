using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;

public sealed record ConversationContext(string Kind, Guid CustomerId);
public sealed record ContextualReply(string Answer, string Source, ConversationContext? Context);

/// <summary>Resolves debt follow-ups using a server-owned customer ID. Amounts and dates are always queried again.</summary>
public sealed class ReceivableConversationService(AppDbContext db)
{
    private static string Normalize(string text) => Regex.Replace(Regex.Replace(AssistantPeriod.Normalize(text), @"[^\p{L}\p{N}\s]", " "), @"\s+", " ").Trim();
    public static bool IsTopDebtorQuestion(string question)
    {
        var q = Normalize(question);
        return (q.Contains("no nhieu") || q.Contains("no lon") || q.Contains("no cao")) &&
            (q.Contains("khach") || q.StartsWith("ai ") || q is "no nhieu nhat" or "no lon nhat" or "no cao nhat");
    }
    private static bool IsOtherTopic(string q) => new[] { "doanh thu", "loi nhuan", "dong tien", "chi phi", "vat", "thue", "ton kho", "san pham", "hoa don moi nhat", "hoa don gan nhat", "nha cung cap", "phai tra", "tom tat", "tong quan" }.Any(q.Contains);
    private static bool IsFollowUp(string q) => new[] { "qua han", "tre han", "khoan gi", "khoan nao", "no gi", "don nao", "hoa don nao", "chi tiet", "han ngay", "den han", "bao nhieu ngay", "da tra", "tra chua", "tra tien", "thanh toan chua", "lich su thanh toan", "lich su tra", "thanh toan", "tra duoc", "tra roi", "no con lai", "ngay nao", "may ngay", "con no", "con bao nhieu", "no bao nhieu", "khach hang do" }.Any(q.Contains) || q is "bao lau" or "bao nhieu" or "cu the";
    private static bool WantsPayments(string q) => new[] { "da tra", "tra chua", "tra tien", "thanh toan chua", "lich su thanh toan", "lich su tra", "thanh toan", "tra duoc", "tra roi" }.Any(q.Contains);
    public async Task<ContextualReply?> TryAnswerAsync(Guid cid, Guid uid, string question, CancellationToken ct, Guid? selectedCustomerId = null, ConversationContext? contextOverride = null)
    {
        var q = Normalize(question);
        if (IsOtherTopic(q)) return null;
        var top = IsTopDebtorQuestion(question);
        var followUp = IsFollowUp(q);
        Customer? customer = selectedCustomerId.HasValue ? await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == cid && x.Id == selectedCustomerId, ct) : null;
        if (selectedCustomerId.HasValue && customer is null) return Clarify("Không tìm thấy khách hàng trong doanh nghiệp đang chọn.");
        if (customer is null && !top && (followUp || q.Contains("no ") || q.Contains("cong no") || q.Contains("phai thu")))
        {
            // Explicit names override context. Match names only within the current company and fail closed on ambiguity.
            var names = await db.Customers.AsNoTracking().Where(x => x.CompanyId == cid).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
            var matches = names.Where(x => Normalize(x.Name).Length >= 3 && (" " + q + " ").Contains(" " + Normalize(x.Name) + " ")).ToList();
            if (matches.Count > 1) return Clarify("Có nhiều khách hàng cùng khớp tên. Bạn vui lòng nêu mã khách hàng hoặc chọn khách hàng trong mục Công nợ.");
            if (matches.Count == 1) customer = await db.Customers.AsNoTracking().FirstAsync(x => x.CompanyId == cid && x.Id == matches[0].Id, ct);
        }
        if (top)
        {
            var rows = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == cid && x.RemainingAmount > 0 && x.Status != "CANCELLED")
                .Select(x => new { x.CustomerId, x.RemainingAmount }).ToListAsync(ct);
            var largest = rows.GroupBy(x => x.CustomerId).Select(g => new { CustomerId = g.Key, Amount = g.Sum(x => x.RemainingAmount) }).OrderByDescending(x => x.Amount).ThenBy(x => x.CustomerId).FirstOrDefault();
            if (largest is null) return new("Hiện không có khoản phải thu khách hàng nào còn dư.", "Receivables", null);
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == cid && x.Id == largest.CustomerId, ct);
        }
        else if (customer is null && followUp)
        {
            var latest = contextOverride is not null ? JsonSerializer.Serialize(contextOverride) : await db.ChatMessages.AsNoTracking().Where(x => x.CompanyId == cid && x.UserId == uid && x.Role == "assistant")
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => x.ContextJson).FirstOrDefaultAsync(ct);
            ConversationContext? context = null;
            try { if (!string.IsNullOrWhiteSpace(latest)) context = JsonSerializer.Deserialize<ConversationContext>(latest); } catch (JsonException) { }
            if (context?.Kind != "receivable") return Clarify("Bạn đang hỏi khoản nợ của khách hàng nào? Hãy nêu tên khách hàng, hoặc hỏi lại “Khách hàng nợ nhiều nhất” rồi hỏi tiếp.");
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == cid && x.Id == context.CustomerId, ct);
            if (customer is null) return Clarify("Không tìm thấy khách hàng vừa nhắc tới trong doanh nghiệp đang chọn. Bạn vui lòng xác định lại khách hàng.");
        }
        if (customer is null) return top ? Clarify("Chưa tìm thấy khách hàng liên kết hợp lệ cho khoản phải thu này.") : null;
        var today = DateTime.UtcNow.Date;
        var debts = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == cid && x.CustomerId == customer.Id).OrderBy(x => x.DueDate).ThenBy(x => x.Id).ToListAsync(ct);
        var active = debts.Where(x => x.RemainingAmount > 0 && x.Status != "CANCELLED").ToList();
        var total = active.Sum(x => x.RemainingAmount);
        var overdue = active.Where(x => x.DueDate.Date < today).ToList();
        var nextContext = new ConversationContext("receivable", customer.Id);
        if (top) return new($"Khách hàng nợ nhiều nhất hiện tại: {customer.Name}, còn phải thu {Money(total)}; có {overdue.Count} khoản quá hạn tính đến ngày {today:dd/MM/yyyy} (UTC). Bạn có thể hỏi tiếp “quá hạn bao lâu”, “khoản gì”, “đơn nào” hoặc “họ đã trả chưa”.", "Customers, Receivables", nextContext);
        if (WantsPayments(q)) return new(await PaymentAnswerAsync(cid, customer, debts, total, ct), "Customers, Receivables, Orders, Invoices, Payments", nextContext);
        if (q.Contains("qua han") || q.Contains("tre han") || q.Contains("bao nhieu ngay") || q == "bao lau")
        {
            if (overdue.Count == 0) return new($"{customer.Name} hiện không có khoản phải thu còn dư đã quá hạn. Tổng còn phải thu hiện tại: {Money(total)}. Ngày đối chiếu {today:dd/MM/yyyy} (UTC).", "Customers, Receivables", nextContext);
            var lines = new List<string>();
            foreach (var debt in overdue.Take(20))
                lines.Add($"• {await ReferenceAsync(cid, customer.Id, debt, ct)}: còn nợ {Money(debt.RemainingAmount)}, đến hạn {debt.DueDate:dd/MM/yyyy}, đã quá hạn {(today - debt.DueDate.Date).Days} ngày.");
            return new($"{customer.Name} có {overdue.Count} khoản phải thu quá hạn, tổng {Money(overdue.Sum(x => x.RemainingAmount))}, tính đến {today:dd/MM/yyyy} (UTC):\n{string.Join("\n", lines)}" + LimitNote(overdue.Count), "Customers, Receivables, Orders, Invoices", nextContext);
        }
        if (active.Count == 0) return new($"{customer.Name} hiện không còn khoản phải thu có dư nợ. Dữ liệu đã được truy vấn lại; đây là trạng thái hiện tại, không dùng lại số tiền của câu trả lời trước.", "Customers, Receivables", nextContext);
        var details = new List<string>();
        foreach (var debt in active.Take(20))
        {
            var timing = debt.DueDate.Date < today ? $"quá hạn {(today - debt.DueDate.Date).Days} ngày" : debt.DueDate.Date == today ? "đến hạn hôm nay" : $"còn {(debt.DueDate.Date - today).Days} ngày đến hạn";
            details.Add($"• {await ReferenceAsync(cid, customer.Id, debt, ct)}: số nợ ghi nhận ban đầu {Money(debt.OriginalAmount)}, còn phải thu {Money(debt.RemainingAmount)}; đến hạn {debt.DueDate:dd/MM/yyyy}, {timing}; trạng thái {debt.Status}.");
        }
        return new($"Các khoản phải thu của {customer.Name} — tổng còn nợ {Money(total)}:\n{string.Join("\n", details)}" + LimitNote(active.Count), "Customers, Receivables, Orders, Invoices", nextContext);
    }
    private async Task<string> ReferenceAsync(Guid cid, Guid customerId, Receivable debt, CancellationToken ct)
    {
        var parts = new List<string>();
        if (debt.OrderId.HasValue)
        {
            var order = await db.Orders.AsNoTracking().Where(x => x.CompanyId == cid && x.CustomerId == customerId && x.Id == debt.OrderId).Select(x => x.Number).FirstOrDefaultAsync(ct);
            if (order is not null) parts.Add($"Đơn {order}");
        }
        if (debt.InvoiceId.HasValue)
        {
            var invoice = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == cid && x.CustomerId == customerId && x.Id == debt.InvoiceId).Select(x => x.Number).FirstOrDefaultAsync(ct);
            if (invoice is not null) parts.Add($"Hóa đơn {invoice}");
        }
        return parts.Count > 0 ? string.Join(" / ", parts) : $"Khoản phải thu {debt.Id.ToString("N")[..8]} (chưa có đơn/hóa đơn liên kết hợp lệ)";
    }
    private async Task<string> PaymentAnswerAsync(Guid cid, Customer customer, List<Receivable> debts, decimal remaining, CancellationToken ct)
    {
        var linkedOrderIds = debts.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().ToList();
        var linkedInvoiceIds = debts.Where(x => x.InvoiceId.HasValue).Select(x => x.InvoiceId!.Value).Distinct().ToList();
        var validOrders = await db.Orders.AsNoTracking().Where(x => x.CompanyId == cid && x.CustomerId == customer.Id && linkedOrderIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
        var validInvoices = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == cid && x.CustomerId == customer.Id && linkedInvoiceIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(x => x.CompanyId == cid && ((x.OrderId.HasValue && validOrders.Contains(x.OrderId.Value)) || (x.InvoiceId.HasValue && validInvoices.Contains(x.InvoiceId.Value))))
            .OrderByDescending(x => x.PaidAt).ThenBy(x => x.Id).ToListAsync(ct);
        var heading = $"{customer.Name} hiện còn phải thu {Money(remaining)}. ";
        if (payments.Count == 0) return heading + "Chưa tìm thấy bản ghi thanh toán liên kết với các đơn/hóa đơn của những khoản phải thu này. Không thể kết luận khách hàng chưa từng thanh toán chỉ từ việc thiếu bản ghi liên kết.";
        return heading + $"Có {payments.Count} bản ghi thanh toán/hoàn tiền gắn với các đơn/hóa đơn liên quan, tổng ghi nhận ròng {Money(payments.Sum(x => x.Amount))}:\n" +
            string.Join("\n", payments.Take(20).Select(x => $"• Ngày {x.PaidAt:dd/MM/yyyy HH:mm} (UTC): {Money(x.Amount)}, phương thức {x.Method}, tham chiếu {(string.IsNullOrWhiteSpace(x.Reference) ? "chưa có" : x.Reference)}.")) +
            LimitNote(payments.Count) + "\nLịch sử này bao gồm thanh toán của đơn/hóa đơn, có thể gồm tiền đã trả lúc mua; không cộng số tiền này lần nữa vào giảm dư nợ.";
    }
    private static string LimitNote(int count) => count > 20 ? "\nĐang hiển thị 20 bản ghi đầu. Xem mục Công nợ/Tra cứu nghiệp vụ để kiểm tra toàn bộ." : "";
    private static ContextualReply Clarify(string message) => new(message, "Ngữ cảnh hội thoại; chưa đủ thông tin để truy vấn khoản nợ", null);
    private static string Money(decimal value) => value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
}
