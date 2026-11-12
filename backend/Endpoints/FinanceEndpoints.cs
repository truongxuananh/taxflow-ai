using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;

public static class FinanceEndpoints
{
    private static readonly HashSet<string> ExpenseCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Nhập hàng", "Lương", "Marketing", "Vận hành", "Thuế", "Điện", "Nước", "Thuê mặt bằng", "Chi khác"
    };

    public static void MapFinanceEndpoints(this WebApplication app)
    {
        var incomes = app.MapGroup("/api/incomes").RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Income & expense");
        incomes.MapGet("/", async (DateTime? from, DateTime? to, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var query = db.IncomeTransactions.AsNoTracking().Where(x => x.CompanyId == companyId);
            if (from.HasValue) query = query.Where(x => x.TransactionDate >= from.Value);
            if (to.HasValue) query = query.Where(x => x.TransactionDate < to.Value.Date.AddDays(1));
            return Results.Ok(await query.OrderByDescending(x => x.TransactionDate).Take(500).ToListAsync(ct));
        });
        incomes.MapPost("/", async (IncomeRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Description) ||
                string.IsNullOrWhiteSpace(request.Category) || string.IsNullOrWhiteSpace(request.PaymentMethod))
                return Results.BadRequest(new { message = "Số tiền, nhóm thu, nội dung và phương thức thanh toán là bắt buộc." });
            var item = new IncomeTransaction
            {
                CompanyId = companyId, Category = request.Category.Trim(), Description = request.Description.Trim(),
                Amount = request.Amount, TransactionDate = request.TransactionDate.ToUniversalTime(),
                PaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant(), Reference = EmptyToNull(request.Reference)
            };
            db.IncomeTransactions.Add(item);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CREATE", "IncomeTransaction", item.Id, null, request, ct);
            return Results.Created($"/api/incomes/{item.Id}", item);
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        var expenses = app.MapGroup("/api/expenses").RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Income & expense");
        expenses.MapGet("/", async (DateTime? from, DateTime? to, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var query = db.ExpenseTransactions.AsNoTracking().Where(x => x.CompanyId == companyId);
            if (from.HasValue) query = query.Where(x => x.TransactionDate >= from.Value);
            if (to.HasValue) query = query.Where(x => x.TransactionDate < to.Value.Date.AddDays(1));
            return Results.Ok(await query.OrderByDescending(x => x.TransactionDate).Take(500).ToListAsync(ct));
        });
        expenses.MapPost("/", async (ExpenseRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (request.Amount <= 0 || request.VatAmount < 0 || request.VatAmount > request.Amount ||
                string.IsNullOrWhiteSpace(request.Description) || string.IsNullOrWhiteSpace(request.PaymentMethod) ||
                string.IsNullOrWhiteSpace(request.Category))
                return Results.BadRequest(new { message = "Số tiền, VAT, nhóm chi, nội dung hoặc phương thức thanh toán không hợp lệ." });
            var category = ExpenseCategories.Contains(request.Category) ? request.Category : "Chi khác";
            var item = new ExpenseTransaction
            {
                CompanyId = companyId, Category = category, Description = request.Description.Trim(),
                Amount = request.Amount, VatAmount = request.VatAmount, TransactionDate = request.TransactionDate.ToUniversalTime(),
                PaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant(), Reference = EmptyToNull(request.Reference)
            };
            db.ExpenseTransactions.Add(item);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CREATE", "ExpenseTransaction", item.Id, null, request, ct);
            return Results.Created($"/api/expenses/{item.Id}", item);
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        app.MapGet("/api/cash-flow", async (
            int? days,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var range = Math.Clamp(days ?? 30, 7, 365);
            var from = DateTime.UtcNow.Date.AddDays(-(range - 1));
            var opening = await db.BankAccounts.Where(x => x.CompanyId == companyId && x.IsActive)
                .SumAsync(x => (decimal?)x.OpeningBalance, ct) ?? 0;
            var incomes = await db.IncomeTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TransactionDate >= from)
                .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);
            var expensesData = await db.ExpenseTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TransactionDate >= from)
                .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);
            var beforeIncome = await db.IncomeTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate < from)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var beforeExpense = await db.ExpenseTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate < from)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var openingAtRange = opening + beforeIncome - beforeExpense;
            var daily = Enumerable.Range(0, range).Select(i =>
            {
                var date = from.AddDays(i);
                var inflow = incomes.Where(x => x.TransactionDate.Date == date).Sum(x => x.Amount);
                var outflow = expensesData.Where(x => x.TransactionDate.Date == date).Sum(x => x.Amount);
                return new { date, inflow, outflow, net = inflow - outflow };
            }).ToList();
            var inflowTotal = daily.Sum(x => x.inflow);
            var outflowTotal = daily.Sum(x => x.outflow);
            return Results.Ok(new
            {
                period = new { from, to = DateTime.UtcNow.Date, days = range },
                openingBalance = openingAtRange,
                cashInflow = inflowTotal,
                cashOutflow = outflowTotal,
                netCashFlow = inflowTotal - outflowTotal,
                closingBalance = openingAtRange + inflowTotal - outflowTotal,
                daily
            });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Cash flow");

        app.MapGet("/api/bank-transactions", async (int? take, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var limit = Math.Clamp(take ?? 100, 1, 500);
            return Results.Ok(await db.BankTransactions.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.TransactionDate).Take(limit)
                .Select(x => new { x.Id, bank = x.BankAccount.BankName, x.Direction, x.Amount, x.TransactionDate, x.Description, x.ExternalReference, x.MatchedPaymentId })
                .ToListAsync(ct));
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Bank accounts");

        var receivables = app.MapGroup("/api/receivables").RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Debts");
        receivables.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var today = DateTime.UtcNow.Date;
            var rows = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0)
                .OrderBy(x => x.DueDate).Select(x => new
                {
                    x.Id, x.CustomerId, customer = x.Customer.Name, x.OrderId, x.InvoiceId,
                    x.OriginalAmount, x.RemainingAmount, x.DueDate, x.Status
                }).ToListAsync(ct);
            return Results.Ok(rows.Select(x => new
            {
                x.Id, x.CustomerId, x.customer, x.OrderId, x.InvoiceId, x.OriginalAmount,
                x.RemainingAmount, x.DueDate, x.Status,
                overdueDays = x.DueDate < today ? (today - x.DueDate.Date).Days : 0
            }));
        });
        receivables.MapPost("/{id:guid}/settle", async (Guid id, decimal amount, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var debt = await db.Receivables.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (debt is null) return Results.NotFound();
            if (amount <= 0 || amount > debt.RemainingAmount)
                return Results.BadRequest(new { message = "Số tiền thu không hợp lệ." });

            var now = DateTime.UtcNow;
            var before = debt.RemainingAmount;
            debt.RemainingAmount = decimal.Round(debt.RemainingAmount - amount, 2);
            if (debt.RemainingAmount < 0.01m) debt.RemainingAmount = 0;
            debt.Status = debt.RemainingAmount == 0 ? "PAID" : "PARTIAL";
            debt.UpdatedAt = now;

            Order? order = null;
            if (debt.OrderId.HasValue)
            {
                order = await db.Orders.FirstOrDefaultAsync(x => x.Id == debt.OrderId && x.CompanyId == companyId, ct);
                if (order is not null)
                {
                    order.PaidAmount = Math.Min(order.Total, order.PaidAmount + amount);
                    order.UpdatedAt = now;
                    db.Payments.Add(new Payment
                    {
                        CompanyId = companyId, OrderId = order.Id, Amount = amount, Method = "TRANSFER",
                        Reference = $"DEBT-{debt.Id:N}", PaidAt = now
                    });
                }
            }

            db.IncomeTransactions.Add(new IncomeTransaction
            {
                CompanyId = companyId, Category = "Khách thanh toán", Description = $"Thu công nợ {debt.Customer.Name}",
                Amount = amount, TransactionDate = now, PaymentMethod = "TRANSFER", Reference = debt.Id.ToString()
            });
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "SETTLE", "Receivable", id,
                new { RemainingAmount = before },
                new { debt.RemainingAmount, debt.Status, OrderPaidAmount = order?.PaidAmount }, ct);
            return Results.Ok(new { debt.Id, debt.RemainingAmount, debt.Status, orderPaidAmount = order?.PaidAmount });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        var payables = app.MapGroup("/api/payables").RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Debts");
        payables.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var today = DateTime.UtcNow.Date;
            var rows = await db.Payables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0)
                .OrderBy(x => x.DueDate).Select(x => new
                {
                    x.Id, x.SupplierId, supplier = x.Supplier.Name, x.InvoiceId,
                    x.OriginalAmount, x.RemainingAmount, x.DueDate, x.Status
                }).ToListAsync(ct);
            return Results.Ok(rows.Select(x => new
            {
                x.Id, x.SupplierId, x.supplier, x.InvoiceId, x.OriginalAmount,
                x.RemainingAmount, x.DueDate, x.Status,
                overdueDays = x.DueDate < today ? (today - x.DueDate.Date).Days : 0
            }));
        });
        payables.MapPost("/{id:guid}/settle", async (Guid id, decimal amount, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var debt = await db.Payables.Include(x => x.Supplier).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (debt is null) return Results.NotFound();
            if (amount <= 0 || amount > debt.RemainingAmount) return Results.BadRequest(new { message = "Số tiền chi không hợp lệ." });
            var now = DateTime.UtcNow;
            var before = debt.RemainingAmount;
            debt.RemainingAmount = decimal.Round(debt.RemainingAmount - amount, 2);
            if (debt.RemainingAmount < 0.01m) debt.RemainingAmount = 0;
            debt.Status = debt.RemainingAmount == 0 ? "PAID" : "PARTIAL";
            debt.UpdatedAt = now;
            db.ExpenseTransactions.Add(new ExpenseTransaction
            {
                CompanyId = companyId, Category = "Nhập hàng", Description = $"Thanh toán công nợ {debt.Supplier.Name}",
                Amount = amount, TransactionDate = now, PaymentMethod = "TRANSFER", Reference = debt.Id.ToString()
            });
            db.Payments.Add(new Payment { CompanyId = companyId, InvoiceId = debt.InvoiceId, Amount = -amount, Method = "TRANSFER", Reference = $"PAYABLE-{debt.Id:N}", PaidAt = now });
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "SETTLE", "Payable", id, new { RemainingAmount = before }, new { debt.RemainingAmount }, ct);
            return Results.Ok(new { debt.Id, debt.RemainingAmount, debt.Status });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        app.MapGet("/api/reconciliation", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var ordersWithoutInvoices = await db.Orders.AsNoTracking()
                .Where(o => o.CompanyId == companyId && !db.Invoices.Any(i => i.SourceOrderId == o.Id))
                .Select(o => new { type = "ORDER_WITHOUT_INVOICE", severity = "info", entityId = o.Id, reference = o.Number, amount = o.Total, message = "Đơn hàng chưa có hóa đơn VAT" })
                .Take(50).ToListAsync(ct);
            var unpaidInvoices = await db.Invoices.AsNoTracking()
                .Where(i => i.CompanyId == companyId && i.Direction == "OUTGOING" &&
                            (!i.SourceOrderId.HasValue || !db.Orders.Any(o => o.Id == i.SourceOrderId && o.PaidAmount >= o.Total)))
                .Select(i => new { type = "UNPAID_INVOICE", severity = "warning", entityId = i.Id, reference = i.Number, amount = i.Total, message = "Hóa đơn chưa được thanh toán đủ" })
                .Take(50).ToListAsync(ct);
            var unmatchedPayments = await db.Payments.AsNoTracking()
                .Where(p => p.CompanyId == companyId && !p.OrderId.HasValue && !p.InvoiceId.HasValue)
                .Select(p => new { type = "UNMATCHED_PAYMENT", severity = "warning", entityId = p.Id, reference = p.Reference ?? "-", amount = p.Amount, message = "Thanh toán chưa gắn đơn hàng/hóa đơn" })
                .Take(50).ToListAsync(ct);
            var duplicateBankTransactions = await db.BankTransactions.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ExternalReference != null)
                .GroupBy(x => x.ExternalReference)
                .Where(g => g.Count() > 1)
                .Select(g => new { type = "DUPLICATE_BANK_TRANSACTION", severity = "error", entityId = Guid.Empty, reference = g.Key!, amount = g.Sum(x => x.Amount), message = "Giao dịch ngân hàng trùng mã tham chiếu" })
                .Take(50).ToListAsync(ct);
            var paymentRows = await db.Orders.AsNoTracking().Where(o => o.CompanyId == companyId)
                .Select(o => new
                {
                    o.Id, o.Number, o.PaidAmount,
                    PaymentTotal = db.Payments.Where(p => p.OrderId == o.Id).Sum(p => (decimal?)p.Amount) ?? 0
                }).ToListAsync(ct);
            var paymentMismatches = paymentRows.Where(x => Math.Abs(x.PaidAmount - x.PaymentTotal) > 1m)
                .Select(x => new
                {
                    type = "AMOUNT_MISMATCH", severity = "error", entityId = x.Id, reference = x.Number,
                    amount = Math.Abs(x.PaidAmount - x.PaymentTotal), message = "Số tiền đã thu trên đơn không khớp tổng Payment"
                }).Take(50).ToList();
            var invoiceRows = await db.Invoices.AsNoTracking()
                .Where(i => i.CompanyId == companyId && i.SourceOrderId.HasValue)
                .Select(i => new
                {
                    i.Id, i.Number, i.Total, i.SourceOrderId,
                    OrderTotal = db.Orders.Where(o => o.Id == i.SourceOrderId).Select(o => (decimal?)o.Total).FirstOrDefault() ?? 0
                }).ToListAsync(ct);
            var invoiceMismatches = invoiceRows.Where(x => Math.Abs(x.Total - x.OrderTotal) > 1m)
                .Select(x => new
                {
                    type = "INVOICE_ORDER_MISMATCH", severity = "error", entityId = x.Id, reference = x.Number,
                    amount = Math.Abs(x.Total - x.OrderTotal), message = "Tổng hóa đơn không khớp tổng đơn hàng"
                }).Take(50).ToList();
            return Results.Ok(new
            {
                generatedAt = DateTime.UtcNow,
                summary = new
                {
                    ordersWithoutInvoices = ordersWithoutInvoices.Count,
                    unpaidInvoices = unpaidInvoices.Count,
                    unmatchedPayments = unmatchedPayments.Count,
                    duplicateTransactions = duplicateBankTransactions.Count,
                    amountMismatches = paymentMismatches.Count + invoiceMismatches.Count
                },
                issues = ordersWithoutInvoices.Cast<object>().Concat(unpaidInvoices).Concat(unmatchedPayments)
                    .Concat(duplicateBankTransactions).Concat(paymentMismatches).Concat(invoiceMismatches)
            });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Reconciliation");
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
