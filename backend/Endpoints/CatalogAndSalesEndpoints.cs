using System.Data;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;

public static class CatalogAndSalesEndpoints
{
    public static void MapCatalogAndSalesEndpoints(this WebApplication app)
    {
        var categories = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Products");
        categories.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(await db.Categories.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(ct));
        });

        var products = app.MapGroup("/api/products").RequireAuthorization().WithTags("Products");
        products.MapGet("/", async (
            string? search,
            bool? lowStock,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var query = db.Products.AsNoTracking().Include(x => x.Category)
                .Where(x => x.CompanyId == companyId && x.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x => x.Name.Contains(term) || x.Sku.Contains(term) || (x.Barcode != null && x.Barcode.Contains(term)));
            }
            if (lowStock == true) query = query.Where(x => x.Stock <= x.MinimumStock);
            var result = await query.OrderBy(x => x.Name).Select(x => new
            {
                x.Id, x.Sku, x.Barcode, x.Name, x.Unit, x.PurchasePrice, x.SellingPrice,
                x.VatRate, x.Stock, x.MinimumStock, x.IsActive,
                category = new { x.Category.Id, x.Category.Name },
                stockStatus = x.Stock <= x.MinimumStock ? "LOW" : "OK"
            }).ToListAsync(ct);
            return Results.Ok(result);
        });
        products.MapPost("/", async (
            ProductRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var validation = ValidateProduct(request);
            if (validation is not null) return Results.BadRequest(new { message = validation });
            var sku = request.Sku.Trim();
            var barcode = EmptyToNull(request.Barcode);
            if (await db.Products.AnyAsync(x => x.CompanyId == companyId && x.Sku == sku, ct))
                return Results.Conflict(new { message = "SKU đã tồn tại." });
            if (barcode is not null && await db.Products.AnyAsync(x => x.CompanyId == companyId && x.Barcode == barcode, ct))
                return Results.Conflict(new { message = "Barcode đã tồn tại." });
            var category = await ResolveCategoryAsync(db, companyId, request.CategoryId, request.CategoryName, ct);
            var product = new Product
            {
                CompanyId = companyId, Sku = sku, Barcode = barcode, Name = request.Name.Trim(),
                Category = category, Unit = request.Unit.Trim(), PurchasePrice = request.PurchasePrice,
                SellingPrice = request.SellingPrice, VatRate = request.VatRate, Stock = request.Stock,
                MinimumStock = request.MinimumStock
            };
            db.Products.Add(product);
            if (product.Stock > 0)
            {
                var warehouse = await db.Warehouses.FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
                if (warehouse is null) return Results.BadRequest(new { message = "Doanh nghiệp chưa có kho để ghi nhận tồn đầu." });
                db.StockTransactions.Add(new StockTransaction
                {
                    CompanyId = companyId, Warehouse = warehouse, Product = product, Type = "IN",
                    Quantity = product.Stock, UnitCost = product.PurchasePrice, Reference = "PRODUCT-CREATE",
                    CreatedByUserId = access.GetUserId(context.User)
                });
            }
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CREATE", "Product", product.Id, null, request, ct);
            return Results.Created($"/api/products/{product.Id}", new { product.Id, product.Sku, product.Name });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        products.MapPut("/{id:guid}", async (
            Guid id,
            ProductRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var validation = ValidateProduct(request);
            if (validation is not null) return Results.BadRequest(new { message = validation });
            var product = await db.Products.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (product is null) return Results.NotFound();
            var sku = request.Sku.Trim();
            var barcode = EmptyToNull(request.Barcode);
            if (await db.Products.AnyAsync(x => x.CompanyId == companyId && x.Id != id && x.Sku == sku, ct))
                return Results.Conflict(new { message = "SKU đã tồn tại." });
            if (barcode is not null && await db.Products.AnyAsync(x => x.CompanyId == companyId && x.Id != id && x.Barcode == barcode, ct))
                return Results.Conflict(new { message = "Barcode đã tồn tại." });
            var old = new { product.Sku, product.Barcode, product.Name, product.Unit, product.PurchasePrice, product.SellingPrice, product.VatRate, product.MinimumStock };
            var category = await ResolveCategoryAsync(db, companyId, request.CategoryId, request.CategoryName, ct);
            product.Sku = sku; product.Barcode = barcode; product.Name = request.Name.Trim();
            product.Category = category; product.Unit = request.Unit.Trim(); product.PurchasePrice = request.PurchasePrice;
            product.SellingPrice = request.SellingPrice; product.VatRate = request.VatRate;
            product.MinimumStock = request.MinimumStock; product.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "UPDATE", "Product", product.Id, old, request, ct);
            return Results.Ok(new { product.Id, product.Sku, product.Name });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        products.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (product is null) return Results.NotFound();
            var oldActive = product.IsActive;
            product.IsActive = false; product.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "DEACTIVATE", "Product", product.Id, new { IsActive = oldActive }, new { IsActive = false }, ct);
            return Results.NoContent();
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        var inventory = app.MapGroup("/api/inventory")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("Inventory");
        inventory.MapGet("/transactions", async (
            int? take,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var limit = Math.Clamp(take ?? 100, 1, 500);
            return Results.Ok(await db.StockTransactions.AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAt).Take(limit)
                .Select(x => new
                {
                    x.Id, x.Type, x.Quantity, x.UnitCost, x.Reference, x.Note, x.CreatedAt,
                    product = new { x.Product.Id, x.Product.Sku, x.Product.Name, x.Product.Unit },
                    warehouse = x.Warehouse.Name
                }).ToListAsync(ct));
        });
        inventory.MapGet("/low-stock", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(await db.Products.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive && x.Stock <= x.MinimumStock)
                .OrderBy(x => x.Stock).Select(x => new { x.Id, x.Sku, x.Name, x.Stock, x.MinimumStock, x.Unit }).ToListAsync(ct));
        });
        inventory.MapPost("/transactions", async (
            StockTransactionRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (string.IsNullOrWhiteSpace(request.Type))
                return Results.BadRequest(new { message = "Loại giao dịch kho là bắt buộc." });
            if (request.UnitCost < 0)
                return Results.BadRequest(new { message = "Đơn giá vốn không được âm." });
            var type = request.Type.Trim().ToUpperInvariant();
            if (type is not ("IN" or "OUT" or "ADJUSTMENT"))
                return Results.BadRequest(new { message = "Loại giao dịch phải là IN, OUT hoặc ADJUSTMENT." });
            if (request.Quantity == 0 || type != "ADJUSTMENT" && request.Quantity < 0)
                return Results.BadRequest(new { message = "Số lượng không hợp lệ." });
            var product = await db.Products.FirstOrDefaultAsync(x => x.Id == request.ProductId && x.CompanyId == companyId && x.IsActive, ct);
            if (product is null) return Results.NotFound(new { message = "Không tìm thấy sản phẩm." });
            var warehouse = request.WarehouseId.HasValue
                ? await db.Warehouses.FirstOrDefaultAsync(x => x.Id == request.WarehouseId && x.CompanyId == companyId, ct)
                : await db.Warehouses.FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
            if (warehouse is null) return Results.BadRequest(new { message = "Doanh nghiệp chưa có kho." });
            var delta = type switch { "IN" => request.Quantity, "OUT" => -request.Quantity, _ => request.Quantity };
            if (product.Stock + delta < 0) return Results.BadRequest(new { message = "Tồn kho không đủ." });
            var oldStock = product.Stock;
            product.Stock += delta; product.UpdatedAt = DateTime.UtcNow;
            var transaction = new StockTransaction
            {
                CompanyId = companyId, Warehouse = warehouse, Product = product, Type = type,
                Quantity = request.Quantity, UnitCost = request.UnitCost, Reference = EmptyToNull(request.Reference),
                Note = EmptyToNull(request.Note), CreatedByUserId = access.GetUserId(context.User)
            };
            db.StockTransactions.Add(transaction);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "STOCK_" + type, "Product", product.Id, new { Stock = oldStock }, new { product.Stock }, ct);
            return Results.Created($"/api/inventory/transactions/{transaction.Id}", new { transaction.Id, product.Stock });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        var orders = app.MapGroup("/api/orders")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Employee))
            .WithTags("Sales / POS");
        orders.MapGet("/", async (
            int? take,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var limit = Math.Clamp(take ?? 100, 1, 500);
            return Results.Ok(await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAt).Take(limit)
                .Select(x => new
                {
                    x.Id, x.Number, customer = x.Customer == null ? "Khách lẻ" : x.Customer.Name,
                    x.Subtotal, x.Discount, x.VatAmount, x.Total, x.PaidAmount, x.PaymentMethod, x.Status, x.CreatedAt,
                    itemCount = x.Items.Count
                }).ToListAsync(ct));
        });
        orders.MapPost("/", CreateOrderAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Employee));
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        AuditService audit,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "Giỏ hàng đang trống." });
        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            return Results.BadRequest(new { message = "Phương thức thanh toán là bắt buộc." });
        if (request.PaidAmount < 0 || request.Discount < 0)
            return Results.BadRequest(new { message = "Số tiền thanh toán và giảm giá không được âm." });

        if (request.Items.Any(x => x.Quantity <= 0)) return Results.BadRequest(new { message = "Mỗi dòng hàng phải có số lượng dương." });
        var grouped = request.Items.GroupBy(x => x.ProductId)
            .Select(x => new OrderItemRequest(x.Key, x.Sum(y => y.Quantity))).ToList();
        if (grouped.Any(x => x.Quantity <= 0))
            return Results.BadRequest(new { message = "Số lượng phải lớn hơn 0." });

        Customer? customer = null;
        if (request.CustomerId.HasValue)
            customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.CustomerId && x.CompanyId == companyId, ct);
        if (request.CustomerId.HasValue && customer is null)
            return Results.BadRequest(new { message = "Khách hàng không hợp lệ." });

        // EF Core's MySQL retry strategy requires the complete user transaction to run inside ExecuteAsync.
        // Serializable also prevents two concurrent POS requests from overselling the same stock.
        var strategy = db.Database.CreateExecutionStrategy();
        Order? createdOrder = null;
        Invoice? createdInvoice = null;
        decimal change = 0;

        try
        {
            {
                // A retry must start from a clean tracker; otherwise stock/order entities from a failed attempt
                // can be reused and applied twice.
                db.ChangeTracker.Clear();
                createdOrder = null;
                createdInvoice = null;
                change = 0;
                if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Yêu cầu phải đi qua transaction middleware.");

                var ids = grouped.Select(x => x.ProductId).ToList();
                var productList = await db.Products
                    .Where(x => x.CompanyId == companyId && x.IsActive && ids.Contains(x.Id))
                    .ToListAsync(ct);
                if (productList.Count != ids.Count)
                    throw new InvalidOperationException("Có sản phẩm không tồn tại hoặc đã ngừng kinh doanh.");

                foreach (var item in grouped)
                {
                    var product = productList.Single(x => x.Id == item.ProductId);
                    if (product.Stock < item.Quantity)
                        throw new InvalidOperationException($"{product.Name} chỉ còn {product.Stock:0.##} {product.Unit}.");
                }

                var warehouse = await db.Warehouses.FirstOrDefaultAsync(x => x.CompanyId == companyId, ct)
                    ?? throw new InvalidOperationException("Doanh nghiệp chưa có kho để xuất hàng.");
                var trackedCustomer = customer is null
                    ? null
                    : await db.Customers.FirstAsync(x => x.Id == customer.Id && x.CompanyId == companyId, ct);

                var now = DateTime.UtcNow;
                var order = new Order
                {
                    CompanyId = companyId,
                    Number = $"DH{now:yyMMddHHmmssfff}-{Random.Shared.Next(100, 999)}",
                    Customer = trackedCustomer,
                    Discount = request.Discount,
                    PaymentMethod = request.PaymentMethod.Trim().ToUpperInvariant(),
                    Status = "COMPLETED",
                    CreatedByUserId = access.GetUserId(context.User)
                };

                foreach (var groupedItem in grouped)
                {
                    var product = productList.Single(x => x.Id == groupedItem.ProductId);
                    var baseAmount = decimal.Round(product.SellingPrice * groupedItem.Quantity, 0, MidpointRounding.AwayFromZero);
                    var vat = decimal.Round(baseAmount * product.VatRate / 100m, 0, MidpointRounding.AwayFromZero);
                    order.Items.Add(new OrderItem
                    {
                        Product = product, Quantity = groupedItem.Quantity, UnitPrice = product.SellingPrice, UnitCost = product.PurchasePrice,
                        VatRate = product.VatRate, VatAmount = vat, LineTotal = baseAmount + vat
                    });
                }

                order.Subtotal = order.Items.Sum(x => decimal.Round(x.UnitPrice * x.Quantity, 2));
                order.Discount = Math.Min(request.Discount, order.Subtotal);
                decimal allocated = 0;
                var lines = order.Items.ToList();
                for (var index = 0; index < lines.Count; index++)
                {
                    var item = lines[index]; var baseAmount = decimal.Round(item.UnitPrice * item.Quantity, 2);
                    item.Discount = index == lines.Count - 1 ? order.Discount - allocated : order.Subtotal > 0 ? decimal.Round(order.Discount * baseAmount / order.Subtotal, 2) : 0;
                    allocated += item.Discount;
                    item.VatAmount = decimal.Round((baseAmount - item.Discount) * item.VatRate / 100m, 2, MidpointRounding.AwayFromZero);
                    item.LineTotal = baseAmount - item.Discount + item.VatAmount;
                }
                order.VatAmount = order.Items.Sum(x => x.VatAmount);
                order.Total = order.Subtotal - order.Discount + order.VatAmount;
                order.PaidAmount = Math.Min(request.PaidAmount, order.Total);
                change = Math.Max(0, request.PaidAmount - order.Total);

                if (order.PaidAmount < order.Total && trackedCustomer is null)
                    throw new InvalidOperationException("Đơn chưa thanh toán đủ phải chọn khách hàng để theo dõi công nợ.");

                db.Orders.Add(order);
                foreach (var item in order.Items)
                {
                    item.Product.Stock -= item.Quantity;
                    item.Product.UpdatedAt = now;
                    db.StockTransactions.Add(new StockTransaction
                    {
                        CompanyId = companyId, Warehouse = warehouse, Product = item.Product, Type = "OUT",
                        Quantity = item.Quantity, UnitCost = item.Product.PurchasePrice, Reference = order.Number,
                        Note = "Xuất kho bán hàng", CreatedByUserId = order.CreatedByUserId
                    });
                }

                if (order.PaidAmount > 0)
                {
                    db.Payments.Add(new Payment
                    {
                        CompanyId = companyId, OrderId = order.Id, Amount = order.PaidAmount,
                        Method = order.PaymentMethod, Reference = order.Number, PaidAt = now
                    });
                    db.IncomeTransactions.Add(new IncomeTransaction
                    {
                        CompanyId = companyId, Category = "Bán hàng", Description = $"Thu tiền {order.Number}",
                        Amount = order.PaidAmount, TransactionDate = now, PaymentMethod = order.PaymentMethod, Reference = order.Number
                    });
                }

                if (order.PaidAmount < order.Total)
                {
                    db.Receivables.Add(new Receivable
                    {
                        CompanyId = companyId, Customer = trackedCustomer!, OrderId = order.Id,
                        OriginalAmount = order.Total - order.PaidAmount, RemainingAmount = order.Total - order.PaidAmount,
                        DueDate = now.Date.AddDays(30), Status = "OPEN"
                    });
                }

                Invoice? invoice = null;
                if (request.RequestVatInvoice)
                {
                    invoice = new Invoice
                    {
                        CompanyId = companyId, Direction = "OUTGOING", Number = $"DRAFT-{now:yyMMddHHmmssfff}",
                        Serial = "DRAFT", InvoiceDate = now, Customer = trackedCustomer,
                        PartyName = request.CustomerName?.Trim() ?? trackedCustomer?.Name ?? "Khách lẻ",
                        PartyTaxCode = EmptyToNull(request.CustomerTaxCode) ?? trackedCustomer?.TaxCode,
                        PartyAddress = EmptyToNull(request.CustomerAddress) ?? trackedCustomer?.Address,
                        PartyEmail = EmptyToNull(request.CustomerEmail) ?? trackedCustomer?.Email,
                        Subtotal = order.Subtotal - order.Discount, VatAmount = order.VatAmount, Total = order.Total,
                        Status = InvoiceStatuses.Draft, Provider = "DemoInvoiceProvider", IsLegallyIssued = false, SourceOrderId = order.Id
                    };
                    foreach (var item in order.Items)
                        invoice.Items.Add(new InvoiceItem
                        {
                            Description = item.Product.Name, Unit = item.Product.Unit, Quantity = item.Quantity,
                            UnitPrice = item.Quantity > 0 ? (item.UnitPrice * item.Quantity - item.Discount) / item.Quantity : 0, VatRate = item.VatRate, VatAmount = item.VatAmount, Total = item.LineTotal
                        });
                    db.Invoices.Add(invoice);
                }

                await db.SaveChangesAsync(ct);

                createdOrder = order;
                createdInvoice = invoice;
            }
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var resultOrder = createdOrder ?? throw new InvalidOperationException("Không thể tạo đơn hàng.");
        await audit.LogAsync(context, companyId, "CREATE", "Order", resultOrder.Id, null,
            new { resultOrder.Number, resultOrder.Total, resultOrder.PaidAmount, invoiceId = createdInvoice?.Id }, ct);
        return Results.Created($"/api/orders/{resultOrder.Id}", new
        {
            resultOrder.Id, resultOrder.Number, resultOrder.Subtotal, resultOrder.Discount, resultOrder.VatAmount, resultOrder.Total,
            resultOrder.PaidAmount, balance = resultOrder.Total - resultOrder.PaidAmount, change,
            invoice = createdInvoice is null ? null : new { createdInvoice.Id, createdInvoice.Number, createdInvoice.Status, createdInvoice.IsLegallyIssued }
        });
    }

    private static async Task<Category> ResolveCategoryAsync(AppDbContext db, Guid companyId, Guid? id, string? name, CancellationToken ct)
    {
        if (id.HasValue)
        {
            var existing = await db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (existing is not null) return existing;
        }
        var categoryName = string.IsNullOrWhiteSpace(name) ? "Khác" : name.Trim();
        var category = await db.Categories.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Name == categoryName, ct);
        if (category is not null) return category;
        category = new Category { CompanyId = companyId, Name = categoryName };
        db.Categories.Add(category);
        return category;
    }

    private static string? ValidateProduct(ProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sku) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Unit))
            return "SKU, tên sản phẩm và đơn vị tính là bắt buộc.";
        if (request.PurchasePrice < 0 || request.SellingPrice < 0 || request.Stock < 0 || request.MinimumStock < 0) return "Giá và tồn kho không được âm.";
        if (request.VatRate is < 0 or > 100) return "Thuế suất VAT phải từ 0 đến 100%.";
        return null;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
