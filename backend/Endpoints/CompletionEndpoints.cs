using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;
public static class CompletionEndpoints
{
    public static void MapCompletionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/orders/{id:guid}", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            var order = await db.Orders.AsNoTracking().Where(x => x.CompanyId == cid && x.Id == id).Select(x => new
            {
                x.Id, x.Number, x.CreatedAt, x.Status, x.Subtotal, x.Discount, x.VatAmount, x.Total, x.PaidAmount, x.PaymentMethod,
                customer = x.Customer == null ? "Khách lẻ" : x.Customer.Name,
                items = x.Items.Select(i => new { i.Id, product = i.Product.Name, i.Quantity, i.UnitPrice, i.UnitCost, i.VatRate, i.VatAmount, i.LineTotal }),
                payments = db.Payments.Where(p => p.CompanyId == cid && p.OrderId == id).OrderBy(p => p.PaidAt).Select(p => new { p.Id, p.Amount, p.Method, p.PaidAt, p.Reference }),
                returns = db.OrderReturns.Where(r => r.CompanyId == cid && r.OrderId == id).Select(r => new { r.Reason, r.RefundedAmount, r.ClearedDebt, r.CreatedAt })
            }).FirstOrDefaultAsync(ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant, Roles.Employee, Roles.Director));

        app.MapPost("/api/orders/{id:guid}/return", async (Guid id, ReturnOrderRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            if (string.IsNullOrWhiteSpace(request.Reason)) return Results.BadRequest(new { message = "Cần ghi rõ lý do hoàn trả." });
            var order = await db.Orders.Include(x => x.Items).ThenInclude(x => x.Product).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == cid, ct);
            if (order is null) return Results.NotFound();
            if (order.Status != "COMPLETED") return Results.Conflict(new { message = "Đơn đã được hoàn trả hoặc không ở trạng thái hoàn tất." });
            var invoices = await db.Invoices.Where(x => x.CompanyId == cid && x.SourceOrderId == id).ToListAsync(ct);
            if (invoices.Any(x => x.IsLegallyIssued)) return Results.BadRequest(new { message = "Hóa đơn đã phát hành thật cần điều chỉnh qua nhà cung cấp trước. Hệ thống không tự hủy hóa đơn hợp pháp." });
            var warehouse = await db.Warehouses.FirstOrDefaultAsync(x => x.CompanyId == cid, ct);
            if (warehouse is null) return Results.BadRequest(new { message = "Chưa có kho nhận hàng hoàn." });
            foreach (var item in order.Items)
            {
                item.Product.Stock += item.Quantity;
                db.StockTransactions.Add(new StockTransaction { CompanyId = cid, WarehouseId = warehouse.Id, ProductId = item.ProductId, Type = "IN", Quantity = item.Quantity, UnitCost = item.UnitCost, Reference = order.Number, Note = "Nhận lại toàn bộ hàng hoàn", CreatedByUserId = access.GetUserId(context.User) });
            }
            var debts = await db.Receivables.Where(x => x.CompanyId == cid && x.OrderId == id).ToListAsync(ct);
            var cleared = debts.Sum(x => x.RemainingAmount);
            foreach (var debt in debts) { debt.RemainingAmount = 0; debt.Status = "CANCELLED"; }
            var refund = order.PaidAmount;
            if (refund > 0)
            {
                db.ExpenseTransactions.Add(new ExpenseTransaction { CompanyId = cid, Category = "Hoàn tiền", Description = $"Hoàn trả {order.Number}: {request.Reason}", Amount = refund, TransactionDate = DateTime.UtcNow, PaymentMethod = order.PaymentMethod, Reference = order.Number });
                db.Payments.Add(new Payment { CompanyId = cid, OrderId = order.Id, Amount = -refund, Method = order.PaymentMethod, Reference = $"RETURN-{order.Number}" });
            }
            foreach (var invoice in invoices) invoice.Status = InvoiceStatuses.Cancelled;
            order.Status = "RETURNED"; order.PaidAmount = 0;
            var record = new OrderReturn { CompanyId = cid, OrderId = id, RefundedAmount = refund, ClearedDebt = cleared, Reason = request.Reason.Trim(), CreatedByUserId = access.GetUserId(context.User) };
            db.OrderReturns.Add(record); await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, cid, "FULL_RETURN", "Order", id, null, new { refund, cleared, record.Reason }, ct);
            return Results.Ok(new { record.Id, order.Number, refundedAmount = refund, clearedDebt = cleared, order.Status });
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));

        app.MapGet("/api/invoices/{id:guid}", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            var invoice = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == cid && x.Id == id).Select(x => new { x.Id, x.Number, x.Serial, x.Direction, x.PartyName, x.PartyTaxCode, x.InvoiceDate, x.Subtotal, x.VatAmount, x.Total, x.Status, x.IsLegallyIssued, x.AccountingReviewed, x.ConfidenceScore, items = x.Items.Select(i => new { i.Description, i.Quantity, i.Unit, i.UnitPrice, i.VatRate, i.VatAmount, i.Total }), payments = db.Payments.Where(p => p.CompanyId == cid && (p.InvoiceId == id || x.SourceOrderId.HasValue && p.OrderId == x.SourceOrderId)).Select(p => new { p.Amount, p.PaidAt, p.Method }) }).FirstOrDefaultAsync(ct);
            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director));
        app.MapPost("/api/invoices/{id:guid}/accounting-review", async (Guid id, InvoiceReviewRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, InvoiceValidationService validator, AuditService audit, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            var invoice = await db.Invoices.Include(x => x.Items).FirstOrDefaultAsync(x => x.CompanyId == cid && x.Id == id && x.Direction == "INCOMING", ct);
            if (invoice is null) return Results.NotFound();
            if (invoice.Status == InvoiceStatuses.Cancelled) return Results.BadRequest(new { message = "Hóa đơn đã hủy." });
            if (request.Eligible && Math.Abs(invoice.Subtotal + invoice.VatAmount - invoice.Total) > 1m) return Results.BadRequest(new { message = "Tổng tiền không khớp. Chưa thể rà soát kế toán." });
            invoice.AccountingReviewed = request.Eligible; await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, cid, "ACCOUNTING_REVIEW", "Invoice", id, null, request, ct);
            return Results.Ok(new { invoice.Id, invoice.AccountingReviewed, message = "Đánh dấu rà soát nội bộ; không tự xác nhận tính hợp pháp của chứng từ." });
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
        foreach (var partyType in new[] { "customers", "suppliers" })
        {
            var customer = partyType == "customers";
            app.MapPut($"/api/{partyType}/{{id:guid}}", async (Guid id, PartyRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
            {
                var cid = await access.ResolveCompanyIdAsync(context, ct);
                if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { message = "Mã và tên là bắt buộc." });
                if (customer)
                {
                    var entity = await db.Customers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == cid, ct);
                    if (entity is null) return Results.NotFound();
                    if (await db.Customers.AnyAsync(x => x.Id != id && x.CompanyId == cid && x.Code == request.Code.Trim(), ct)) return Results.Conflict(new { message = "Mã bị trùng." });
                    entity.Code = request.Code.Trim(); entity.Name = request.Name.Trim(); entity.TaxCode = request.TaxCode; entity.Address = request.Address; entity.Email = request.Email; entity.Phone = request.Phone;
                }
                else
                {
                    var entity = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == cid, ct);
                    if (entity is null) return Results.NotFound();
                    if (await db.Suppliers.AnyAsync(x => x.Id != id && x.CompanyId == cid && x.Code == request.Code.Trim(), ct)) return Results.Conflict(new { message = "Mã bị trùng." });
                    entity.Code = request.Code.Trim(); entity.Name = request.Name.Trim(); entity.TaxCode = request.TaxCode; entity.Address = request.Address; entity.Email = request.Email; entity.Phone = request.Phone;
                }
                await db.SaveChangesAsync(ct); await audit.LogAsync(context, cid, "UPDATE", customer ? "Customer" : "Supplier", id, null, request, ct);
                return Results.NoContent();
            }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
        }
    }
}
