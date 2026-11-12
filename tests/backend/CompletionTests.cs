using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;
using Xunit;
 
namespace TaxFlow.Tests;
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public HttpMessageHandler? GeminiHandler { get; init; }
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private string? mysql;
 
    public Guid CompanyA { get; private set; }
    public Guid CompanyB { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid DebtId { get; private set; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var configured = Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) connection.Open();
        else { var cs = new MySqlConnector.MySqlConnectionStringBuilder(configured) { Database = "taxflow_test_" + Guid.NewGuid().ToString("N") }; mysql = cs.ConnectionString; }
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            if (GeminiHandler is not null)
            {
                var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["GEMINI_API_KEY"] = "test-only-key", ["GEMINI_MODEL"] = "test-model" }).Build();
                services.AddSingleton(new GeminiFunctionPlanner(new HttpClient(GeminiHandler), config));
            }
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => { if (mysql is null) options.UseSqlite(connection); else options.UseMySql(mysql, new MySqlServerVersion(new Version(8, 4, 0))); });
        });
    }
    public async Task<HttpClient> LoginAsync()
    {
        var client = CreateClient();
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync())
            {
                var admin = new Role { Name = Roles.Admin }; var employee = new Role { Name = Roles.Employee };
                db.Roles.AddRange(admin, employee, new Role { Name = Roles.Accountant }, new Role { Name = Roles.Director });
                var a = new Company { Name = "A", TaxCode = "0401234567" }; var b = new Company { Name = "B", TaxCode = "0401234568" };
                var user = new AppUser { Email = "admin@test.local", FullName = "Tester", Role = admin };
                user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, "CurrentPass123!");
                db.CompanyUsers.AddRange(new CompanyUser { Company = a, User = user, RoleName = Roles.Admin }, new CompanyUser { Company = b, User = user, RoleName = Roles.Employee });
                var customer = new Customer { CompanyId = a.Id, Name = "Customer", Code = "KH1" };
                var product = new Product { CompanyId = a.Id, Name = "Product", Sku = "SP1", Stock = 10, PurchasePrice = 50, SellingPrice = 100, VatRate = 0, Category = new Category { CompanyId = a.Id, Name = "Group" } };
                var debt = new Receivable { CompanyId = a.Id, Customer = customer, OriginalAmount = 100, RemainingAmount = 100, DueDate = DateTime.UtcNow.AddDays(1) };
                db.Products.Add(product); db.Receivables.Add(debt); db.Warehouses.Add(new Warehouse { CompanyId = a.Id, Name = "Warehouse", Code = "K1" });
                await db.SaveChangesAsync(); CompanyA = a.Id; CompanyB = b.Id; UserId = user.Id; ProductId = product.Id; DebtId = debt.Id;
            }
        }
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "CurrentPass123!" });
        response.EnsureSuccessStatusCode(); var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
        client.DefaultRequestHeaders.Add("X-Company-Id", CompanyA.ToString());
        return client;
    }
    private bool databaseDropped;
    private bool databaseDropped; protected override void Dispose(bool disposing)
    {
        // WebApplicationFactory gọi lại Dispose(true) sau khi đã giải phóng Services,
        // nên chỉ xóa database MySQL tạm ở lần gọi đầu tiên.
        if (disposing && mysql is not null && !databaseDropped)
        {
            databaseDropped = true;
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeleted();
        }
        base.Dispose(disposing); if (disposing) connection.Dispose();
    }
}
public sealed class CompletionTests
{
    private static async Task<HttpResponseMessage> Command(HttpClient client, string path, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key); return await client.SendAsync(request);
    }
    [Fact] public async Task MembershipRoleIsResolvedForEachCompanyAndChangesImmediately()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Company-Id"); client.DefaultRequestHeaders.Add("X-Company-Id", app.CompanyB.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var membership = await db.CompanyUsers.SingleAsync(x => x.CompanyId == app.CompanyA && x.UserId == app.UserId); membership.IsActive = false; await db.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Remove("X-Company-Id"); client.DefaultRequestHeaders.Add("X-Company-Id", app.CompanyA.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
    }
    [Fact] public async Task PasswordChangeRevokesAnAlreadyIssuedToken()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var changed = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "CurrentPass123!", newPassword = "ReplacementPass123!" });
        changed.EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
    }
    [Fact] public async Task IncomeCommandReplaysOnceAndRejectsDifferentPayload()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var body = new { category = "Thu khác", description = "Test", amount = 100, transactionDate = DateTime.UtcNow, paymentMethod = "CASH" };
        (await Command(client, "/api/incomes", body, "income-1")).EnsureSuccessStatusCode();
        var replay = await Command(client, "/api/incomes", body, "income-1"); replay.EnsureSuccessStatusCode(); Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        var changed = await Command(client, "/api/incomes", body with { amount = 200 }, "income-1"); Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var scope = app.Services.CreateScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().IncomeTransactions.CountAsync());
    }
    [Fact] public async Task DebtSettlementCannotBeAppliedTwiceOrOverpaid()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); var path = $"/api/receivables/{app.DebtId}/settle?amount=60";
        (await Command(client, path, new { }, "debt-1")).EnsureSuccessStatusCode();
        (await Command(client, path, new { }, "debt-1")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Command(client, path, new { }, "debt-2")).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(40, (await db.Receivables.FindAsync(app.DebtId))!.RemainingAmount); Assert.Equal(1, await db.IncomeTransactions.CountAsync());
    }
    [Fact] public async Task FullReturnRestoresStockAndRefundsOnlyOnce()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var create = await Command(client, "/api/orders", new { items = new[] { new { productId = app.ProductId, quantity = 2 } }, discount = 0, paidAmount = 200, paymentMethod = "CASH", requestVatInvoice = false }, "order-1");
        create.EnsureSuccessStatusCode(); var order = await create.Content.ReadFromJsonAsync<JsonElement>(); var id = order.GetProperty("id").GetGuid();
        var body = new { reason = "Customer returned all items" };
        (await Command(client, $"/api/orders/{id}/return", body, "return-1")).EnsureSuccessStatusCode();
        (await Command(client, $"/api/orders/{id}/return", body, "return-1")).EnsureSuccessStatusCode();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(10, (await db.Products.FindAsync(app.ProductId))!.Stock); Assert.Equal(1, await db.OrderReturns.CountAsync()); Assert.Equal(1, await db.ExpenseTransactions.CountAsync());
    }
    [Fact] public async Task InvoiceArithmeticMismatchIsBlocking()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<InvoiceValidationService>();
        var issues = await service.ValidateAsync(app.CompanyA, new InvoiceExtractionResult { SupplierName = "Seller", SupplierTaxCode = "0401234567", InvoiceNumber = "001", InvoiceDate = DateTime.UtcNow, Subtotal = 100, VatAmount = 10, Total = 999, ConfidenceScore = .9m }, default);
        Assert.Contains(issues, x => x.Code == "TOTAL_MISMATCH" && x.Severity == "error");
    }
    [Fact] public async Task BrowseEndpointTranslatesAndPaginates()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var response = await client.GetAsync("/api/browse/customers?search=Customer&page=1&pageSize=10"); response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, data.GetProperty("total").GetInt32());
    }
 
    [Fact] public async Task BankMatchDoesNotCreateASecondIncome()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); Guid bankId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var account = new BankAccount { CompanyId = app.CompanyA, BankName = "Test bank", AccountNumber = "123", AccountName = "Test" }; db.BankAccounts.Add(account); await db.SaveChangesAsync(); bankId = account.Id;
        }
        var orderResponse = await Command(client,"/api/orders",new { items = new[] { new { productId = app.ProductId, quantity = 1 } },paidAmount=100,paymentMethod="TRANSFER",discount=0,requestVatInvoice=false },"bank-order"); orderResponse.EnsureSuccessStatusCode();
        var bankResponse = await Command(client,"/api/bank-transactions",new { bankAccountId=bankId,direction="IN",amount=100,transactionDate=DateTime.UtcNow,description="Statement",externalReference="BANK-001" },"bank-entry"); bankResponse.EnsureSuccessStatusCode();
        var bank = await bankResponse.Content.ReadFromJsonAsync<JsonElement>(); var bankEntryId=bank.GetProperty("id").GetGuid();
        var candidates = await client.GetFromJsonAsync<JsonElement>($"/api/bank-transactions/{bankEntryId}/candidates"); Assert.Equal(1,candidates.GetArrayLength());
        var paymentId = candidates[0].GetProperty("id").GetGuid();
        (await Command(client,$"/api/bank-transactions/{bankEntryId}/match",new {paymentId},"bank-match")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await Command(client,$"/api/bank-transactions/{bankEntryId}/match",new {paymentId},"bank-match-2")).StatusCode);
        using var check=app.Services.CreateScope();var checkDb=check.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(1,await checkDb.IncomeTransactions.CountAsync());Assert.Equal(paymentId,(await checkDb.BankTransactions.SingleAsync()).MatchedPaymentId);
    }
    [Fact] public async Task InvitationRequiresTheIntendedRecipientAndCreatesOnlyScopedRole()
    {
        using var app = new ApiFactory(); using var admin = await app.LoginAsync();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await db.Roles.SingleAsync(x => x.Name == Roles.Employee);
            var target = new AppUser { Email = "target@test.local", FullName = "Target", Role = role };
            target.PasswordHash = new PasswordHasher<AppUser>().HashPassword(target, "TargetPass123!");
            db.CompanyUsers.Add(new CompanyUser { CompanyId = app.CompanyB, User = target, RoleName = Roles.Employee }); await db.SaveChangesAsync();
        }
        var sent = await admin.PostAsJsonAsync("/api/users/invitations", new { email = "target@test.local", role = "ACCOUNTANT" }); sent.EnsureSuccessStatusCode();
        var invitation = await sent.Content.ReadFromJsonAsync<JsonElement>(); var id = invitation.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await admin.PostAsJsonAsync($"/api/auth/invitations/{id}/accept", new {})).StatusCode);
        using var recipient = app.CreateClient(); var login = await recipient.PostAsJsonAsync("/api/auth/login", new { email = "target@test.local", password = "TargetPass123!" }); login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<JsonElement>(); recipient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());
        (await recipient.PostAsJsonAsync($"/api/auth/invitations/{id}/accept",new {})).EnsureSuccessStatusCode();
        recipient.DefaultRequestHeaders.Add("X-Company-Id",app.CompanyA.ToString()); Assert.Equal(HttpStatusCode.OK,(await recipient.GetAsync("/api/documents")).StatusCode);
        recipient.DefaultRequestHeaders.Remove("X-Company-Id"); recipient.DefaultRequestHeaders.Add("X-Company-Id",app.CompanyB.ToString()); Assert.Equal(HttpStatusCode.Forbidden,(await recipient.GetAsync("/api/documents")).StatusCode);
    }
    [Fact] public async Task ExistingAccountCannotBeAttachedWithoutConsent()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var response = await client.PostAsJsonAsync("/api/users", new { email = "admin@test.local", fullName = "Other", password = "IgnoredPassword123!", role = "ADMIN" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/users/{app.UserId}/reset-password", new { newPassword = "AttemptedReset123!" })).StatusCode);
    }
    [Fact] public async Task OptimisticConcurrencyRejectsLostStockUpdates()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<AppDbContext>(); var b = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var pa = await a.Products.SingleAsync(x => x.Id == app.ProductId); var pb = await b.Products.SingleAsync(x => x.Id == app.ProductId);
        pa.Stock -= 1; await a.SaveChangesAsync(); pb.Stock -= 2;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }
    [Fact] public async Task MissingCommandKeyCannotWriteMoney()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var response = await client.PostAsJsonAsync("/api/incomes", new { category = "Thu khác", description = "Unkeyed", amount = 100, transactionDate = DateTime.UtcNow, paymentMethod = "CASH" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = app.Services.CreateScope(); Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().IncomeTransactions.CountAsync());
    }
    [Fact] public void PdfExportKeepsVietnameseAndAllRowsAcrossPages()
    {
        var rows = Enumerable.Range(1, 150).Select(i => (IReadOnlyList<string>)new[] { $"Hóa đơn tiếng Việt {i}", i.ToString() }).ToList();
        var bytes = new ExportService().ToPdf(new ExportTable("Báo cáo hóa đơn tiếng Việt", new[] { "Nội dung", "Số tiền" }, rows));
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfSharpCore.Pdf.IO.PdfReader.Open(stream, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1);
        Directory.CreateDirectory("TestResults"); File.WriteAllBytes("TestResults/vietnamese-150-rows.pdf", bytes);
    }
    [Fact] public async Task DiscountReducesVatAndInvoiceTotalsAgree()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var product = await db.Products.FindAsync(app.ProductId); product!.VatRate = 10; await db.SaveChangesAsync(); }
        var response = await Command(client, "/api/orders", new { items = new[] { new { productId = app.ProductId, quantity = 2 } }, discount = 50, paidAmount = 165, paymentMethod = "CASH", requestVatInvoice = true }, "vat-discount-1");
        response.EnsureSuccessStatusCode(); var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(15, data.GetProperty("vatAmount").GetDecimal()); Assert.Equal(165, data.GetProperty("total").GetDecimal());
        using var check = app.Services.CreateScope(); var invoice = await check.ServiceProvider.GetRequiredService<AppDbContext>().Invoices.Include(x => x.Items).SingleAsync();
        Assert.Equal(invoice.Total, invoice.Subtotal + invoice.VatAmount); Assert.Equal(invoice.Total, invoice.Items.Sum(x => x.Total));
    }
 
    [Theory] [InlineData("Doanh thu tháng trước", 2026, 10)] [InlineData("VAT tháng 7/2025", 2025, 7)]
    public void AssistantResolvesTheRequestedMonth(string question, int year, int month)
    {
        var period = AssistantPeriod.Resolve(question, new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc)); Assert.Equal(year, period.From.Year); Assert.Equal(month, period.From.Month);
    }
    [Fact] public void ProfitExcludesInventoryPurchasesAndCountsCostOfGoods() => Assert.Equal(50, new ProfitSummary(200, 100, 50).EstimatedProfit);
}