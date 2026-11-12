using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;

namespace TaxFlow.Api.Services;
public sealed record ProfitSummary(decimal NetRevenue, decimal CostOfGoods, decimal OperatingExpense)
{
    public decimal EstimatedProfit => NetRevenue - CostOfGoods - OperatingExpense;
}
public sealed class FinancialSummaryService(AppDbContext db)
{
    public async Task<ProfitSummary> ProfitAsync(Guid companyId, DateTime from, DateTime to, CancellationToken ct)
    {
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var orders = await db.Orders.Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= from && x.CreatedAt < to).Select(x => x.Total - x.VatAmount).ToListAsync(ct);
            var costs = await db.OrderItems.Where(x => x.Order.CompanyId == companyId && x.Order.Status == "COMPLETED" && x.Order.CreatedAt >= from && x.Order.CreatedAt < to).Select(x => x.Quantity * x.UnitCost).ToListAsync(ct);
            var expenses = await db.ExpenseTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate >= from && x.TransactionDate < to && x.Category != "Nhập hàng" && x.Category != "Hoàn tiền").Select(x => x.Amount - x.VatAmount).ToListAsync(ct);
            return new(orders.Sum(), costs.Sum(), expenses.Sum());
        }
        var revenue = await db.Orders.Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= from && x.CreatedAt < to)
            .SumAsync(x => (decimal?)(x.Total - x.VatAmount), ct) ?? 0;
        var cost = await db.OrderItems.Where(x => x.Order.CompanyId == companyId && x.Order.Status == "COMPLETED" && x.Order.CreatedAt >= from && x.Order.CreatedAt < to)
            .SumAsync(x => (decimal?)(x.Quantity * x.UnitCost), ct) ?? 0;
        // Inventory purchases are assets until sold. Refunds reverse a sale; do not charge them again as operating expenses.
        var operating = await db.ExpenseTransactions.Where(x => x.CompanyId == companyId && x.TransactionDate >= from && x.TransactionDate < to && x.Category != "Nhập hàng" && x.Category != "Hoàn tiền")
            .SumAsync(x => (decimal?)(x.Amount - x.VatAmount), ct) ?? 0;
        return new ProfitSummary(revenue, cost, operating);
    }
}
