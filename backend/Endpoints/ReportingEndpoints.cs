using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;

public static class ReportingEndpoints
{
    public static void MapReportingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dashboard", DashboardAsync).RequireAuthorization().WithTags("Dashboard");

        var tax = app.MapGroup("/api/tax")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("Tax");
        tax.MapGet("/summary", TaxSummaryAsync);
        tax.MapPost("/draft-report", CreateTaxDraftAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        tax.MapPost("/{id:guid}/review", async (
            Guid id,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var record = await db.TaxRecords.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (record is null) return Results.NotFound();
            var old = record.Status;
            record.Status = "ACCOUNTANT_REVIEWED";
            record.ReviewedAt = DateTime.UtcNow;
            record.ReviewedByUserId = access.GetUserId(context.User);
            record.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "REVIEW", "TaxRecord", id, new { Status = old }, new { record.Status }, ct);
            return Results.Ok(new { record.Id, record.Status, record.ReviewedAt, notice = "Đã rà soát nội bộ; đây chưa phải tờ khai thuế chính thức." });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        var reports = app.MapGroup("/api/reports")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("Reports");
        reports.MapGet("/summary", ReportSummaryAsync);
        reports.MapPost("/generate", GenerateReportAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director));
        reports.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(await db.Reports.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct));
        });

        app.MapGet("/api/exports/{dataset}", ExportAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("Exports");

        app.MapGet("/api/audit-logs", async (int? take, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var limit = Math.Clamp(take ?? 100, 1, 500);
            return Results.Ok(await db.AuditLogs.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.Timestamp).Take(limit)
                .Select(x => new { x.Id, x.UserEmail, x.Action, x.Entity, x.EntityId, x.OldValue, x.NewValue, x.Timestamp }).ToListAsync(ct));
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director)).WithTags("Audit log");

        app.MapGet("/api/notifications", NotificationsAsync).RequireAuthorization().WithTags("Notifications");
    }

    private static async Task<IResult> DashboardAsync(
        FinancialSummaryService financial,
        int? days,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var range = Math.Clamp(days ?? 30, 7, 90);
        var from = DateTime.UtcNow.Date.AddDays(-(range - 1));
        var orders = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= from)
            .Select(x => new { x.Id, x.CreatedAt, Total = x.Total - x.VatAmount }).ToListAsync(ct);
        var expenses = await db.ExpenseTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TransactionDate >= from)
            .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);
        var incomes = await db.IncomeTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TransactionDate >= from)
            .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);
        var profit = await financial.ProfitAsync(companyId, from, DateTime.UtcNow.AddTicks(1), ct);
        var revenue = profit.NetRevenue;
        var expense = expenses.Sum(x => x.Amount);
        var allIncome = await db.IncomeTransactions.Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var allExpense = await db.ExpenseTransactions.Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var opening = await db.BankAccounts.Where(x => x.CompanyId == companyId && x.IsActive).SumAsync(x => (decimal?)x.OpeningBalance, ct) ?? 0;
        var receivable = await db.Receivables.Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.RemainingAmount, ct) ?? 0;
        var payable = await db.Payables.Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.RemainingAmount, ct) ?? 0;
        var outputVat = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "OUTGOING" && x.InvoiceDate >= from && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var inputVat = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "INCOMING" && x.InvoiceDate >= from && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var trends = Enumerable.Range(0, range).Select(i =>
        {
            var date = from.AddDays(i);
            var dayRevenue = orders.Where(x => x.CreatedAt.Date == date).Sum(x => x.Total);
            var dayExpense = expenses.Where(x => x.TransactionDate.Date == date).Sum(x => x.Amount);
            var inflow = incomes.Where(x => x.TransactionDate.Date == date).Sum(x => x.Amount);
            return new { date, revenue = dayRevenue, expense = dayExpense, cashFlow = inflow - dayExpense };
        }).ToList();
        var topProducts = await db.OrderItems.AsNoTracking().Where(x => x.Order.CompanyId == companyId && x.Order.CreatedAt >= from)
            .GroupBy(x => new { x.ProductId, x.Product.Name })
            .Select(g => new { id = g.Key.ProductId, name = g.Key.Name, quantity = g.Sum(x => x.Quantity), revenue = g.Sum(x => x.LineTotal - x.VatAmount) })
            .OrderByDescending(x => x.revenue).Take(5).ToListAsync(ct);
        var alerts = await db.AIAlerts.AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsRead)
            .OrderByDescending(x => x.CreatedAt).Take(5).Select(x => new { x.Id, x.Type, x.Severity, x.Title, x.Message }).ToListAsync(ct);

        return Results.Ok(new
        {
            period = new { from, to = DateTime.UtcNow, days = range },
            kpis = new
            {
                revenue, expense, costOfGoods = profit.CostOfGoods, operatingExpense = profit.OperatingExpense, estimatedProfit = profit.EstimatedProfit, currentCash = opening + allIncome - allExpense,
                receivables = receivable, payables = payable, outputVat, inputVat
            },
            trends, topProducts, alerts,
            disclaimer = "Lợi nhuận và VAT là số liệu quản trị ước tính, cần kế toán rà soát."
        });
    }

    private static async Task<IResult> TaxSummaryAsync(
        int? year,
        int? month,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var selectedYear = year ?? DateTime.UtcNow.Year;
        var selectedMonth = month ?? DateTime.UtcNow.Month;
        if (selectedMonth is < 1 or > 12 || selectedYear is < 2000 or > 2200)
            return Results.BadRequest(new { message = "Kỳ thuế không hợp lệ." });
        var from = new DateTime(selectedYear, selectedMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);
        var inputVat = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "INCOMING" && x.InvoiceDate >= from && x.InvoiceDate < to && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var outputVat = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "OUTGOING" && x.InvoiceDate >= from && x.InvoiceDate < to && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var records = await db.TaxRecords.AsNoTracking().Where(x => x.CompanyId == companyId && x.PeriodStart == from)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return Results.Ok(new
        {
            period = new { year = selectedYear, month = selectedMonth, from, to = to.AddTicks(-1) },
            vat = new { input = inputVat, output = outputVat, difference = outputVat - inputVat },
            extensions = new[] { "VAT", "TNDN", "TNCN", "Các nghĩa vụ thuế khác" },
            records,
            status = "DRAFT",
            disclaimer = "Đây là số liệu dự thảo phục vụ quản trị, không phải tờ khai thuế chính thức."
        });
    }

    private static async Task<IResult> CreateTaxDraftAsync(
        int? year,
        int? month,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        AuditService audit,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var selectedYear = year ?? DateTime.UtcNow.Year;
        var selectedMonth = month ?? DateTime.UtcNow.Month;
        if (selectedMonth is < 1 or > 12) return Results.BadRequest(new { message = "Tháng không hợp lệ." });
        var from = new DateTime(selectedYear, selectedMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);
        var input = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "INCOMING" && x.InvoiceDate >= from && x.InvoiceDate < to && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var output = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "OUTGOING" && x.InvoiceDate >= from && x.InvoiceDate < to && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var record = await db.TaxRecords.FirstOrDefaultAsync(x =>
            x.CompanyId == companyId && x.TaxType == "VAT" && x.PeriodStart == from && x.Status == "DRAFT", ct);
        var created = record is null;
        object? oldValue = null;
        if (record is null)
        {
            record = new TaxRecord
            {
                CompanyId = companyId, TaxType = "VAT", PeriodStart = from, PeriodEnd = to.AddTicks(-1), Status = "DRAFT"
            };
            db.TaxRecords.Add(record);
        }
        else
        {
            oldValue = new { record.InputAmount, record.OutputAmount, record.Difference };
            record.UpdatedAt = DateTime.UtcNow;
        }
        record.InputAmount = input;
        record.OutputAmount = output;
        record.Difference = output - input;
        record.PeriodEnd = to.AddTicks(-1);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(context, companyId, created ? "CREATE_DRAFT" : "REFRESH_DRAFT", "TaxRecord", record.Id, oldValue,
            new { record.PeriodStart, record.PeriodEnd, record.InputAmount, record.OutputAmount, record.Difference }, ct);
        var payload = new
        {
            record.Id, record.Status, record.InputAmount, record.OutputAmount, record.Difference,
            refreshed = !created, nextStep = "Accountant Review", disclaimer = "Chưa phải tờ khai thuế chính thức."
        };
        return created ? Results.Created($"/api/tax/{record.Id}", payload) : Results.Ok(payload);
    }

    private static async Task<IResult> ReportSummaryAsync(
        FinancialSummaryService financial,
        string? period,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var (periodType, from) = ResolvePeriod(period);
        var to = DateTime.UtcNow;
        var profit = await financial.ProfitAsync(companyId, from, to, ct);
        var revenue = await db.Orders.Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= from)
            .SumAsync(x => (decimal?)x.Total, ct) ?? 0;
        var expense = await db.ExpenseTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate >= from)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var cashIn = await db.IncomeTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate >= from)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var cashOut = expense;
        var outputVat = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "OUTGOING" && x.InvoiceDate >= from && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var inputVat = await db.Invoices.Where(x => x.CompanyId == companyId && x.Direction == "INCOMING" && x.InvoiceDate >= from && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued))
            .SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var receivable = await db.Receivables.Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.RemainingAmount, ct) ?? 0;
        var payable = await db.Payables.Where(x => x.CompanyId == companyId).SumAsync(x => (decimal?)x.RemainingAmount, ct) ?? 0;
        var inventory = await db.Products.Where(x => x.CompanyId == companyId && x.IsActive).SumAsync(x => (decimal?)(x.Stock * x.PurchasePrice), ct) ?? 0;
        return Results.Ok(new
        {
            period = new { type = periodType, from, to }, revenue = profit.NetRevenue, expense, costOfGoods = profit.CostOfGoods, operatingExpense = profit.OperatingExpense, estimatedProfit = profit.EstimatedProfit,
            vat = new { input = inputVat, output = outputVat, difference = outputVat - inputVat },
            cashFlow = new { inflow = cashIn, outflow = cashOut, net = cashIn - cashOut },
            debt = new { receivable, payable }, inventoryValue = inventory,
            disclaimer = "Báo cáo quản trị; lợi nhuận và thuế cần được kế toán rà soát."
        });
    }

    private static async Task<IResult> GenerateReportAsync(
        FinancialSummaryService financial,
        string? period,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        AuditService audit,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var (periodType, from) = ResolvePeriod(period);
        var to = DateTime.UtcNow;
        var profit = await financial.ProfitAsync(companyId, from, to, ct);
        var revenue = await db.Orders.Where(x => x.CompanyId == companyId && x.CreatedAt >= from).SumAsync(x => (decimal?)x.Total, ct) ?? 0;
        var expense = await db.ExpenseTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate >= from).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var report = new Report
        {
            CompanyId = companyId, PeriodType = periodType, PeriodStart = from, PeriodEnd = to,
            Status = "DRAFT", DataJson = JsonSerializer.Serialize(new { revenue = profit.NetRevenue, expense, costOfGoods = profit.CostOfGoods, operatingExpense = profit.OperatingExpense, estimatedProfit = profit.EstimatedProfit })
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(context, companyId, "GENERATE", "Report", report.Id, null, new { report.PeriodType, report.PeriodStart, report.PeriodEnd }, ct);
        return Results.Created($"/api/reports/{report.Id}", report);
    }

    private static async Task<IResult> ExportAsync(
        string dataset,
        string? format,
        DateTime? from, DateTime? to, string? search,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        ExportService exporter,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var table = await BuildExportTableAsync(dataset.Trim().ToLowerInvariant(), companyId, db, ct);
        if (table is null) return Results.NotFound(new { message = "Dataset hỗ trợ: invoices, sales, incomes, expenses, receivables, payables, tax, inventory." });
        var dateIndex = table.Headers.ToList().FindIndex(x => x == "Ngày" || x == "Đến hạn" || x == "Từ");
        table = table with { Rows = table.Rows.Where(row =>
            (string.IsNullOrWhiteSpace(search) || row.Any(cell => cell.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))) &&
            (dateIndex < 0 || !DateTime.TryParse(row[dateIndex], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                ((!from.HasValue || date >= from.Value.Date) && (!to.HasValue || date < to.Value.Date.AddDays(1)))))
            .ToList() };
        var selectedFormat = (format ?? "csv").Trim().ToLowerInvariant();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return selectedFormat switch
        {
            "csv" => Results.File(exporter.ToCsv(table), "text/csv; charset=utf-8", $"taxflow-{dataset}-{stamp}.csv"),
            "xlsx" or "excel" => Results.File(exporter.ToXlsx(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"taxflow-{dataset}-{stamp}.xlsx"),
            "pdf" => Results.File(exporter.ToPdf(table), "application/pdf", $"taxflow-{dataset}-{stamp}.pdf"),
            _ => Results.BadRequest(new { message = "Định dạng hỗ trợ: csv, xlsx, pdf." })
        };
    }

    private static async Task<ExportTable?> BuildExportTableAsync(string dataset, Guid companyId, AppDbContext db, CancellationToken ct)
    {
        var money = (decimal value) => value.ToString("0", CultureInfo.InvariantCulture);
        switch (dataset)
        {
            case "invoices":
            {
                var rows = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.InvoiceDate)
                    .Select(x => new { x.Direction, x.Number, x.Serial, x.InvoiceDate, x.PartyName, x.PartyTaxCode, x.Subtotal, x.VatAmount, x.Total, x.Status, x.IsLegallyIssued }).ToListAsync(ct);
                return new ExportTable("Hóa đơn", new[] { "Loại", "Số", "Ký hiệu", "Ngày", "Đối tác", "MST", "Tiền hàng", "VAT", "Tổng", "Trạng thái", "Pháp lý" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.Direction, x.Number, x.Serial ?? "", x.InvoiceDate.ToString("yyyy-MM-dd"), x.PartyName, x.PartyTaxCode ?? "", money(x.Subtotal), money(x.VatAmount), money(x.Total), x.Status, x.IsLegallyIssued ? "Đã phát hành" : "Chưa phát hành" }).ToList());
            }
            case "sales":
            {
                var rows = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedAt)
                    .Select(x => new { x.Number, Customer = x.Customer == null ? "Khách lẻ" : x.Customer.Name, x.CreatedAt, x.Subtotal, x.Discount, x.VatAmount, x.Total, x.PaidAmount, x.PaymentMethod }).ToListAsync(ct);
                return new ExportTable("Bán hàng", new[] { "Đơn", "Khách hàng", "Ngày", "Tiền hàng", "Giảm", "VAT", "Tổng", "Đã thu", "Phương thức" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.Number, x.Customer, x.CreatedAt.ToString("yyyy-MM-dd HH:mm"), money(x.Subtotal), money(x.Discount), money(x.VatAmount), money(x.Total), money(x.PaidAmount), x.PaymentMethod }).ToList());
            }
            case "incomes":
            {
                var rows = await db.IncomeTransactions.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
                return new ExportTable("Thu", new[] { "Ngày", "Nhóm", "Nội dung", "Số tiền", "Phương thức", "Tham chiếu" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.TransactionDate.ToString("yyyy-MM-dd"), x.Category, x.Description, money(x.Amount), x.PaymentMethod, x.Reference ?? "" }).ToList());
            }
            case "expenses":
            {
                var rows = await db.ExpenseTransactions.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.TransactionDate).ToListAsync(ct);
                return new ExportTable("Chi", new[] { "Ngày", "Nhóm", "Nội dung", "Số tiền", "VAT", "Phương thức", "Tham chiếu" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.TransactionDate.ToString("yyyy-MM-dd"), x.Category, x.Description, money(x.Amount), money(x.VatAmount), x.PaymentMethod, x.Reference ?? "" }).ToList());
            }
            case "receivables":
            {
                var rows = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.DueDate)
                    .Select(x => new { Customer = x.Customer.Name, x.OriginalAmount, x.RemainingAmount, x.DueDate, x.Status }).ToListAsync(ct);
                return new ExportTable("Công nợ phải thu", new[] { "Khách hàng", "Ban đầu", "Còn lại", "Đến hạn", "Trạng thái" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.Customer, money(x.OriginalAmount), money(x.RemainingAmount), x.DueDate.ToString("yyyy-MM-dd"), x.Status }).ToList());
            }
            case "payables":
            {
                var rows = await db.Payables.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.DueDate)
                    .Select(x => new { Supplier = x.Supplier.Name, x.OriginalAmount, x.RemainingAmount, x.DueDate, x.Status }).ToListAsync(ct);
                return new ExportTable("Công nợ phải trả", new[] { "Nhà cung cấp", "Ban đầu", "Còn lại", "Đến hạn", "Trạng thái" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.Supplier, money(x.OriginalAmount), money(x.RemainingAmount), x.DueDate.ToString("yyyy-MM-dd"), x.Status }).ToList());
            }
            case "tax":
            {
                var rows = await db.TaxRecords.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.PeriodStart).ToListAsync(ct);
                return new ExportTable("Thuế dự thảo", new[] { "Loại", "Từ", "Đến", "Đầu vào", "Đầu ra", "Chênh lệch", "Trạng thái" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.TaxType, x.PeriodStart.ToString("yyyy-MM-dd"), x.PeriodEnd.ToString("yyyy-MM-dd"), money(x.InputAmount), money(x.OutputAmount), money(x.Difference), x.Status }).ToList());
            }
            case "inventory":
            {
                var rows = await db.Products.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive).OrderBy(x => x.Name)
                    .Select(x => new { x.Sku, x.Name, Category = x.Category.Name, x.Unit, x.Stock, x.MinimumStock, x.PurchasePrice, x.SellingPrice, x.VatRate }).ToListAsync(ct);
                return new ExportTable("Tồn kho", new[] { "SKU", "Sản phẩm", "Nhóm", "ĐVT", "Tồn", "Tối thiểu", "Giá vốn", "Giá bán", "VAT %" },
                    rows.Select(x => (IReadOnlyList<string>)new[] { x.Sku, x.Name, x.Category, x.Unit, money(x.Stock), money(x.MinimumStock), money(x.PurchasePrice), money(x.SellingPrice), money(x.VatRate) }).ToList());
            }
            default: return null;
        }
    }

    private static async Task<IResult> NotificationsAsync(
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var now = DateTime.UtcNow;
        var alertRows = await db.AIAlerts.AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsRead)
            .OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
        var debtRows = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0 && x.DueDate < now)
            .OrderBy(x => x.DueDate).Take(10).Select(x => new { x.Id, Customer = x.Customer.Name, x.RemainingAmount, x.DueDate }).ToListAsync(ct);
        var stockRows = await db.Products.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive && x.Stock <= x.MinimumStock)
            .OrderBy(x => x.Stock).Take(10).Select(x => new { x.Id, x.Name, x.Stock, x.Unit, x.UpdatedAt }).ToListAsync(ct);
        var documentRows = await db.Documents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && (x.Status == "AI_FAILED" || x.Status == "PENDING_REVIEW"))
            .OrderByDescending(x => x.CreatedAt).Take(10)
            .Select(x => new { x.Id, x.OriginalFileName, x.Status, x.CreatedAt }).ToListAsync(ct);
        var reportRows = await db.Reports.AsNoTracking().Where(x => x.CompanyId == companyId && x.CreatedAt >= now.AddDays(-7))
            .OrderByDescending(x => x.CreatedAt).Take(10)
            .Select(x => new { x.Id, x.PeriodType, x.Status, x.CreatedAt }).ToListAsync(ct);
        var nextTaxDate = new DateTime(now.Year, now.Month, 20, 0, 0, 0, DateTimeKind.Utc);
        if (nextTaxDate < now.Date) nextTaxDate = nextTaxDate.AddMonths(1);
        var notifications = new List<NotificationItem>();
        notifications.AddRange(alertRows.Select(x => new NotificationItem(x.Id, "AI anomaly", x.Severity, x.Title, x.Message, x.CreatedAt)));
        notifications.AddRange(debtRows.Select(x => new NotificationItem(x.Id, "Công nợ quá hạn", "HIGH", "Khoản phải thu quá hạn", $"{x.Customer} còn nợ {x.RemainingAmount:0} đồng.", x.DueDate)));
        notifications.AddRange(stockRows.Select(x => new NotificationItem(x.Id, "Sắp hết hàng", "MEDIUM", "Tồn kho thấp", $"{x.Name} còn {x.Stock:0.##} {x.Unit}.", x.UpdatedAt)));
        notifications.AddRange(documentRows.Select(x => new NotificationItem(x.Id, "Hóa đơn lỗi", x.Status == "AI_FAILED" ? "HIGH" : "MEDIUM", x.Status == "AI_FAILED" ? "OCR hóa đơn thất bại" : "Hóa đơn chờ kiểm tra", x.OriginalFileName, x.CreatedAt)));
        notifications.AddRange(reportRows.Select(x => new NotificationItem(x.Id, "Báo cáo mới", "INFO", "Báo cáo quản trị mới", $"Báo cáo {x.PeriodType} đang ở trạng thái {x.Status}.", x.CreatedAt)));
        notifications.Add(new NotificationItem(Guid.Empty, "Kỳ thuế", "INFO", "Theo dõi hạn kê khai", $"Mốc nhắc nội bộ: {nextTaxDate:dd/MM/yyyy}. Hãy đối chiếu với quy định hiện hành.", nextTaxDate));
        return Results.Ok(notifications.OrderByDescending(x => x.Timestamp));
    }

    private static (string Type, DateTime From) ResolvePeriod(string? period)
    {
        var now = DateTime.UtcNow;
        return period?.Trim().ToLowerInvariant() switch
        {
            "daily" => ("DAILY", now.Date),
            "weekly" => ("WEEKLY", now.Date.AddDays(-6)),
            _ => ("MONTHLY", new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc))
        };
    }

    private sealed record NotificationItem(Guid Id, string Category, string Severity, string Title, string Message, DateTime Timestamp);
}
