using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        const int attempts = 20;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning(ex, "Database chưa sẵn sàng, thử lại {Attempt}/{Attempts}.", attempt, attempts);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        if (await db.Companies.AnyAsync(cancellationToken)) return;

        var now = DateTime.UtcNow;
        var random = new Random(260907);
        var roles = new[]
        {
            new Role { Name = Roles.Admin },
            new Role { Name = Roles.Accountant },
            new Role { Name = Roles.Employee },
            new Role { Name = Roles.Director }
        };
        db.Roles.AddRange(roles);

        var users = new[]
        {
            NewUser("admin@example.com", "Quản trị hệ thống", roles[0]),
            NewUser("accountant@example.com", "Kế toán Nguyễn An", roles[1]),
            NewUser("employee@example.com", "Nhân viên Trần Minh", roles[2]),
            NewUser("director@example.com", "Giám đốc Lê Hà", roles[3])
        };
        var passwords = new[] { "Admin@123", "Accountant@123", "Employee@123", "Director@123" };
        var hasher = new PasswordHasher<AppUser>();
        for (var i = 0; i < users.Length; i++)
            users[i].PasswordHash = hasher.HashPassword(users[i], passwords[i]);
        db.Users.AddRange(users);

        var company = new Company
        {
            TaxCode = "0402198765",
            Name = "TA",
            Address = "125 Nguyễn Văn Linh, Hải Châu, Đà Nẵng",
            Phone = "0236 388 6688",
            Email = "ketoan@minhtam-demo.vn",
            Representative = "Lê Minh Tâm",
            BusinessType = "Thương mại bán lẻ",
            TaxMethod = "Khấu trừ",
            TaxFilingPeriod = "Tháng"
        };
        db.Companies.Add(company);
        db.CompanyUsers.AddRange(users.Select(user => new CompanyUser { Company = company, User = user, RoleName = user.Role.Name }));

        var categories = new[] { "Văn phòng phẩm", "Đồ gia dụng", "Thực phẩm", "Chăm sóc cá nhân" }
            .Select(name => new Category { CompanyId = company.Id, Name = name }).ToArray();
        db.Categories.AddRange(categories);

        var warehouse = new Warehouse
        {
            CompanyId = company.Id,
            Code = "KHO-CHINH",
            Name = "Kho trung tâm",
            Address = company.Address
        };
        db.Warehouses.Add(warehouse);

        var productNames = new[]
        {
            "Giấy in A4 70gsm", "Bút bi xanh", "Sổ lò xo A5", "Bìa hồ sơ", "Băng keo trong",
            "Bình giữ nhiệt 500ml", "Hộp đựng thực phẩm", "Khăn lau đa năng", "Nước rửa chén", "Túi rác tự hủy",
            "Cà phê rang xay 500g", "Trà ô long hộp", "Bánh quy bơ", "Nước khoáng 500ml", "Hạt điều rang muối",
            "Nước rửa tay", "Khẩu trang y tế", "Kem đánh răng", "Dầu gội thảo mộc", "Khăn giấy bỏ túi"
        };
        var products = productNames.Select((name, i) => new Product
        {
            CompanyId = company.Id,
            Sku = $"SP{i + 1:000}",
            Barcode = $"8938500{i + 1:00000}",
            Name = name,
            Category = categories[i / 5],
            Unit = i is 1 or 3 or 6 or 7 or 16 or 19 ? "Cái" : i is 13 ? "Chai" : "Hộp",
            PurchasePrice = 8_000 + i * 3_200,
            SellingPrice = 12_000 + i * 4_500,
            VatRate = i % 4 == 0 ? 5 : 10,
            Stock = i % 7 == 0 ? 4 : 35 + i * 3,
            MinimumStock = 10
        }).ToArray();
        db.Products.AddRange(products);
        db.StockTransactions.AddRange(products.Select(product => new StockTransaction
        {
            CompanyId = company.Id,
            Warehouse = warehouse,
            Product = product,
            Type = "IN",
            Quantity = product.Stock,
            UnitCost = product.PurchasePrice,
            Reference = "OPENING",
            Note = "Tồn đầu kỳ dữ liệu demo",
            CreatedByUserId = users[0].Id,
            CreatedAt = now.AddDays(-100)
        }));

        var customerNames = new[]
        {
            "Công ty An Phú", "Cửa hàng Bình Minh", "Doanh nghiệp Châu Anh", "Nguyễn Hoàng Duy", "Công ty Đại Việt",
            "Siêu thị Gia Hân", "Phạm Khánh Linh", "Công ty Nam Hải", "Shop Phương Nhi", "Công ty Thành Công"
        };
        var customers = customerNames.Select((name, i) => new Customer
        {
            CompanyId = company.Id,
            Code = $"KH{i + 1:000}",
            Name = name,
            TaxCode = i % 3 == 0 ? null : $"04021{i + 10000:00000}",
            Address = $"{20 + i} Hải Phòng, Đà Nẵng",
            Phone = $"090500{i + 1000:0000}",
            Email = $"kh{i + 1}@example.com"
        }).ToArray();
        db.Customers.AddRange(customers);

        var supplierNames = new[]
        {
            "Nhà phân phối Ánh Dương", "Công ty Bảo Minh", "Công ty Cửu Long", "Kho hàng Đông Á", "Công ty Gia Phát",
            "Nhà cung cấp Hưng Thịnh", "Công ty Kim Long", "Công ty Phúc An", "Nhà phân phối Tân Việt", "Công ty Việt Xanh"
        };
        var suppliers = supplierNames.Select((name, i) => new Supplier
        {
            CompanyId = company.Id,
            Code = $"NCC{i + 1:000}",
            Name = name,
            TaxCode = $"03123{i + 45678:00000}",
            Address = $"{100 + i} Điện Biên Phủ, Đà Nẵng",
            Phone = $"091400{i + 1100:0000}",
            Email = $"ncc{i + 1}@example.com"
        }).ToArray();
        db.Suppliers.AddRange(suppliers);

        var bank = new BankAccount
        {
            CompanyId = company.Id,
            BankName = "Vietcombank",
            AccountNumber = "0123456789",
            AccountName = "CONG TY TNHH THUONG MAI MINH TAM",
            OpeningBalance = 280_000_000
        };
        db.BankAccounts.Add(bank);

        var orders = new List<Order>();
        for (var i = 0; i < 30; i++)
        {
            var created = now.Date.AddDays(-(89 - i * 3)).AddHours(2 + i % 8);
            var selected = new[] { products[i % products.Length], products[(i * 3 + 4) % products.Length] };
            var order = new Order
            {
                CompanyId = company.Id,
                Number = $"DH{created:yyMMdd}-{i + 1:000}",
                Customer = customers[i % customers.Length],
                Discount = i % 5 == 0 ? 20_000 : 0,
                PaymentMethod = (i % 4) switch { 0 => "CASH", 1 => "TRANSFER", 2 => "QR", _ => "CARD" },
                Status = "COMPLETED",
                CreatedByUserId = users[2].Id,
                CreatedAt = created,
                UpdatedAt = created
            };
            foreach (var (product, j) in selected.Select((product, index) => (product, index)))
            {
                var quantity = 1 + (i + j) % 4;
                var baseAmount = product.SellingPrice * quantity;
                var vat = decimal.Round(baseAmount * product.VatRate / 100, 0);
                order.Items.Add(new OrderItem
                {
                    Product = product,
                    Quantity = quantity,
                    UnitPrice = product.SellingPrice,
                    UnitCost = product.PurchasePrice,
                    VatRate = product.VatRate,
                    VatAmount = vat,
                    LineTotal = baseAmount + vat,
                    CreatedAt = created,
                    UpdatedAt = created
                });
            }
            order.Subtotal = order.Items.Sum(x => x.UnitPrice * x.Quantity);
            order.VatAmount = order.Items.Sum(x => x.VatAmount);
            order.Total = order.Subtotal + order.VatAmount - order.Discount;
            order.PaidAmount = i % 6 == 0 ? decimal.Round(order.Total * 0.55m, 0) : order.Total;
            orders.Add(order);
            db.Orders.Add(order);
            db.Payments.Add(new Payment
            {
                CompanyId = company.Id,
                OrderId = order.Id,
                Amount = order.PaidAmount,
                Method = order.PaymentMethod,
                Reference = order.Number,
                PaidAt = created,
                CreatedAt = created,
                UpdatedAt = created
            });
            db.IncomeTransactions.Add(new IncomeTransaction
            {
                CompanyId = company.Id,
                Category = "Bán hàng",
                Description = $"Thu tiền {order.Number}",
                Amount = order.PaidAmount,
                TransactionDate = created,
                PaymentMethod = order.PaymentMethod,
                Reference = order.Number,
                CreatedAt = created,
                UpdatedAt = created
            });
            if (order.PaymentMethod is "TRANSFER" or "QR")
                db.BankTransactions.Add(new BankTransaction
                {
                    CompanyId = company.Id,
                    BankAccount = bank,
                    Direction = "IN",
                    Amount = order.PaidAmount,
                    TransactionDate = created,
                    Description = $"Thanh toán {order.Number}",
                    ExternalReference = $"FT{created:MMdd}{i:0000}",
                    CreatedAt = created,
                    UpdatedAt = created
                });
            if (order.PaidAmount < order.Total)
                db.Receivables.Add(new Receivable
                {
                    CompanyId = company.Id,
                    Customer = order.Customer!,
                    OrderId = order.Id,
                    OriginalAmount = order.Total - order.PaidAmount,
                    RemainingAmount = order.Total - order.PaidAmount,
                    DueDate = created.Date.AddDays(30),
                    Status = "OPEN",
                    CreatedAt = created,
                    UpdatedAt = created
                });
        }

        for (var i = 0; i < 20; i++)
        {
            var order = orders[i + 5];
            var outgoing = new Invoice
            {
                CompanyId = company.Id,
                Direction = "OUTGOING",
                Number = $"0000{i + 1:000}",
                Serial = "1C26TFA",
                InvoiceDate = order.CreatedAt,
                Customer = order.Customer,
                PartyName = order.Customer?.Name ?? "Khách lẻ",
                PartyTaxCode = order.Customer?.TaxCode,
                PartyAddress = order.Customer?.Address,
                PartyEmail = order.Customer?.Email,
                Subtotal = order.Subtotal,
                VatAmount = order.VatAmount,
                Total = order.Total,
                Status = InvoiceStatuses.Pending,
                Provider = "DemoInvoiceProvider",
                IsLegallyIssued = false,
                SourceOrderId = order.Id,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.CreatedAt
            };
            foreach (var item in order.Items)
                outgoing.Items.Add(new InvoiceItem
                {
                    Description = item.Product.Name,
                    Unit = item.Product.Unit,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatRate = item.VatRate,
                    VatAmount = item.VatAmount,
                    Total = item.LineTotal,
                    CreatedAt = order.CreatedAt,
                    UpdatedAt = order.CreatedAt
                });
            db.Invoices.Add(outgoing);

            var incomingDate = now.Date.AddDays(-(85 - i * 4));
            var subtotal = 1_500_000 + i * 175_000;
            var vatAmount = decimal.Round(subtotal * 0.10m, 0);
            var incoming = new Invoice
            {
                CompanyId = company.Id,
                Direction = "INCOMING",
                Number = $"NCC-{i + 1:00000}",
                Serial = "1C26NCC",
                InvoiceDate = incomingDate,
                Supplier = suppliers[i % suppliers.Length],
                PartyName = suppliers[i % suppliers.Length].Name,
                PartyTaxCode = suppliers[i % suppliers.Length].TaxCode,
                PartyAddress = suppliers[i % suppliers.Length].Address,
                PartyEmail = suppliers[i % suppliers.Length].Email,
                Subtotal = subtotal,
                VatAmount = vatAmount,
                Total = subtotal + vatAmount,
                Status = InvoiceStatuses.Pending,
                Provider = "ImportedDemo",
                IsLegallyIssued = false,
                ConfidenceScore = 0.91m,
                CreatedAt = incomingDate,
                UpdatedAt = incomingDate
            };
            incoming.Items.Add(new InvoiceItem
            {
                Description = products[i % products.Length].Name,
                Unit = products[i % products.Length].Unit,
                Quantity = 10 + i,
                UnitPrice = subtotal / (10 + i),
                VatRate = 10,
                VatAmount = vatAmount,
                Total = subtotal + vatAmount,
                CreatedAt = incomingDate,
                UpdatedAt = incomingDate
            });
            db.Invoices.Add(incoming);
            db.ExpenseTransactions.Add(new ExpenseTransaction
            {
                CompanyId = company.Id,
                Category = (i % 5) switch { 0 => "Nhập hàng", 1 => "Marketing", 2 => "Vận hành", 3 => "Điện", _ => "Thuê mặt bằng" },
                Description = $"Chi phí kỳ {i + 1:00}",
                Amount = incoming.Total,
                VatAmount = vatAmount,
                TransactionDate = incomingDate,
                PaymentMethod = "TRANSFER",
                Reference = incoming.Number,
                CreatedAt = incomingDate,
                UpdatedAt = incomingDate
            });
            db.Payables.Add(new Payable
            {
                CompanyId = company.Id,
                Supplier = incoming.Supplier!,
                InvoiceId = incoming.Id,
                OriginalAmount = incoming.Total,
                RemainingAmount = i % 4 == 0 ? incoming.Total : 0,
                DueDate = incomingDate.AddDays(25),
                Status = i % 4 == 0 ? "OPEN" : "PAID",
                CreatedAt = incomingDate,
                UpdatedAt = incomingDate
            });
            if (i % 4 != 0)
                db.BankTransactions.Add(new BankTransaction
                {
                    CompanyId = company.Id,
                    BankAccount = bank,
                    Direction = "OUT",
                    Amount = incoming.Total,
                    TransactionDate = incomingDate.AddDays(2),
                    Description = $"Thanh toán {incoming.Number}",
                    ExternalReference = $"OUT{incomingDate:MMdd}{i:0000}",
                    CreatedAt = incomingDate,
                    UpdatedAt = incomingDate
                });
        }

        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        db.TaxRecords.Add(new TaxRecord
        {
            CompanyId = company.Id,
            TaxType = "VAT",
            PeriodStart = monthStart,
            PeriodEnd = monthStart.AddMonths(1).AddTicks(-1),
            InputAmount = 4_250_000,
            OutputAmount = 5_680_000,
            Difference = 1_430_000,
            Status = "DRAFT"
        });
        db.AIAlerts.AddRange(
            new AIAlert
            {
                CompanyId = company.Id,
                Type = "LOW_STOCK",
                Severity = "HIGH",
                Title = "3 sản phẩm sắp hết hàng",
                Message = "Tồn kho đã xuống dưới mức tối thiểu. Kiểm tra trước khi tạo đơn nhập hàng.",
                EntityType = "Product"
            },
            new AIAlert
            {
                CompanyId = company.Id,
                Type = "OVERDUE_RECEIVABLE",
                Severity = "HIGH",
                Title = "Công nợ khách hàng quá hạn",
                Message = "Có khoản phải thu đã quá ngày đến hạn và chưa được tất toán.",
                EntityType = "Receivable"
            },
            new AIAlert
            {
                CompanyId = company.Id,
                Type = "LARGE_EXPENSE",
                Severity = "MEDIUM",
                Title = "Chi phí cần rà soát",
                Message = "Một khoản chi cao hơn đáng kể so với mức chi trung bình gần đây.",
                EntityType = "ExpenseTransaction"
            });
        db.Reports.Add(new Report
        {
            CompanyId = company.Id,
            PeriodType = "MONTHLY",
            PeriodStart = monthStart,
            PeriodEnd = monthStart.AddMonths(1).AddTicks(-1),
            Status = "DRAFT",
            DataJson = JsonSerializer.Serialize(new { notice = "Dữ liệu báo cáo demo; cần kế toán rà soát." })
        });
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = company.Id,
            UserId = users[0].Id,
            UserEmail = users[0].Email,
            Action = "SEED",
            Entity = "System",
            NewValue = JsonSerializer.Serialize(new { products = 20, customers = 10, suppliers = 10, orders = 30 }),
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("TaxFlow AI demo data seeded for company {CompanyId}.", company.Id);
    }

    private static AppUser NewUser(string email, string name, Role role) => new()
    {
        Email = email,
        FullName = name,
        Role = role,
        IsActive = true
    };
}
