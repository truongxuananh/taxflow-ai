using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;
namespace TaxFlow.Api.Endpoints;
public sealed record BankEntryRequest(Guid BankAccountId, string Direction, decimal Amount, DateTime TransactionDate, string Description, string? ExternalReference);
public sealed record BankMatchRequest(Guid PaymentId);
public static class BankCompletionEndpoints
{
    public static void MapBankCompletionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/bank-transactions", async (BankEntryRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            if (request.Amount <= 0 || request.Direction is not ("IN" or "OUT") || string.IsNullOrWhiteSpace(request.Description)) return Results.BadRequest(new { message = "Giao dịch ngân hàng không hợp lệ." });
            if (!await db.BankAccounts.AnyAsync(x => x.Id == request.BankAccountId && x.CompanyId == cid && x.IsActive, ct)) return Results.BadRequest(new { message = "Tài khoản không thuộc doanh nghiệp." });
            if (!string.IsNullOrWhiteSpace(request.ExternalReference) && await db.BankTransactions.AnyAsync(x => x.CompanyId == cid && x.ExternalReference == request.ExternalReference, ct)) return Results.Conflict(new { message = "Mã giao dịch đã tồn tại." });
            var entry = new BankTransaction { CompanyId = cid, BankAccountId = request.BankAccountId, Direction = request.Direction, Amount = request.Amount, Description = request.Description.Trim(), ExternalReference = request.ExternalReference?.Trim(), TransactionDate = request.TransactionDate.ToUniversalTime() };
            db.BankTransactions.Add(entry); await db.SaveChangesAsync(ct); await audit.LogAsync(context, cid, "MANUAL_BANK_ENTRY", "BankTransaction", entry.Id, null, request, ct);
            return Results.Ok(new { entry.Id, message = "Đã ghi sổ sao kê để đối soát. Chưa tạo thêm khoản thu/chi để tránh ghi tiền hai lần." });
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
        app.MapGet("/api/bank-transactions/{id:guid}/candidates", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct); var entry = await db.BankTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == cid, ct);
            if (entry is null) return Results.NotFound();
            var amount = entry.Direction == "IN" ? entry.Amount : -entry.Amount;
            return Results.Ok(await db.Payments.AsNoTracking().Where(x => x.CompanyId == cid && x.Amount == amount && !db.BankTransactions.Any(b => b.CompanyId == cid && b.MatchedPaymentId == x.Id)).OrderByDescending(x => x.PaidAt).Take(30).Select(x => new { x.Id, x.Amount, x.Reference, x.PaidAt, x.Method }).ToListAsync(ct));
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
        app.MapPost("/api/bank-transactions/{id:guid}/match", async (Guid id, BankMatchRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct); var entry = await db.BankTransactions.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == cid, ct);
            var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.PaymentId && x.CompanyId == cid, ct);
            if (entry is null || payment is null) return Results.NotFound();
            if (entry.MatchedPaymentId.HasValue) return Results.Conflict(new { message = "Sao kê đã được đối soát." });
            if (Math.Abs(payment.Amount) != entry.Amount || (payment.Amount > 0) != (entry.Direction == "IN")) return Results.BadRequest(new { message = "Số tiền hoặc chiều tiền không khớp." });
            if (await db.BankTransactions.AnyAsync(x => x.CompanyId == cid && x.MatchedPaymentId == payment.Id, ct)) return Results.Conflict(new { message = "Thanh toán đã ghép với sao kê khác." });
            entry.MatchedPaymentId = payment.Id; await db.SaveChangesAsync(ct); await audit.LogAsync(context, cid, "BANK_MATCH", "BankTransaction", id, null, new { payment.Id }, ct);
            return Results.Ok(new { entry.Id, entry.MatchedPaymentId });
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
    }
}
