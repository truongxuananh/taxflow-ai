using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;
public sealed record BrowseRow { public Guid Id { get; init; } public string Reference { get; init; } = ""; public string Label { get; init; } = ""; public DateTime Date { get; init; } public decimal Amount { get; init; } public string Status { get; init; } = ""; }
public static class BrowseEndpoints
{
    public static void MapBrowseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/browse/{dataset}", async (string dataset, string? search, DateTime? from, DateTime? to, string? status, int? page, int? pageSize, string? sort, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            if (context.User.IsInRole(Roles.Employee) && dataset is not ("orders" or "customers" or "suppliers")) return Results.Forbid();
            IQueryable<BrowseRow>? query = dataset switch
            {
                "bank" => db.BankTransactions.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.ExternalReference ?? "", Label = x.Description, Date = x.TransactionDate, Amount = x.Amount, Status = x.MatchedPaymentId.HasValue ? "MATCHED" : "UNMATCHED" }),
                "orders" => db.Orders.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Number, Label = x.Customer == null ? "Khách lẻ" : x.Customer.Name, Date = x.CreatedAt, Amount = x.Total, Status = x.Status }),
                "invoices" => db.Invoices.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Number, Label = x.PartyName, Date = x.InvoiceDate, Amount = x.Total, Status = x.Status }),
                "incomes" => db.IncomeTransactions.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Reference ?? "", Label = x.Description, Date = x.TransactionDate, Amount = x.Amount, Status = x.Category }),
                "expenses" => db.ExpenseTransactions.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Reference ?? "", Label = x.Description, Date = x.TransactionDate, Amount = x.Amount, Status = x.Category }),
                "receivables" => db.Receivables.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Id.ToString(), Label = x.Customer.Name, Date = x.DueDate, Amount = x.RemainingAmount, Status = x.Status }),
                "payables" => db.Payables.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Id.ToString(), Label = x.Supplier.Name, Date = x.DueDate, Amount = x.RemainingAmount, Status = x.Status }),
                "customers" => db.Customers.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Code, Label = x.Name, Date = x.CreatedAt, Amount = 0, Status = x.TaxCode ?? "" }),
                "suppliers" => db.Suppliers.Where(x => x.CompanyId == cid).Select(x => new BrowseRow { Id = x.Id, Reference = x.Code, Label = x.Name, Date = x.CreatedAt, Amount = 0, Status = x.TaxCode ?? "" }),
                _ => null
            };
            if (query is null) return Results.BadRequest(new { message = "Dataset không hỗ trợ." });
            if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.Reference.Contains(term) || x.Label.Contains(term)); }
            if (from.HasValue) query = query.Where(x => x.Date >= from.Value.Date);
            if (to.HasValue) { var end = to.Value.Date.AddDays(1); query = query.Where(x => x.Date < end); }
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
            var count = await query.CountAsync(ct); var size = Math.Clamp(pageSize ?? 20, 5, 100); var selected = Math.Max(1, page ?? 1);
            query = sort == "amount" ? query.OrderByDescending(x => x.Amount).ThenBy(x => x.Id) : query.OrderByDescending(x => x.Date).ThenBy(x => x.Id);
            return Results.Ok(new { total = count, page = selected, pageSize = size, items = await query.Skip((selected - 1) * size).Take(size).ToListAsync(ct) });
        }).RequireAuthorization();
    }
}
