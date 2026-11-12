using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using Xunit;

namespace TaxFlow.Tests;
public sealed class ConversationTests
{
    private static async Task SetupDebt(ApiFactory app)
    {
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var debt = await db.Receivables.Include(x => x.Customer).SingleAsync(x => x.Id == app.DebtId);
        debt.Customer.Name = "Shop Phương Nhi"; debt.OriginalAmount = 322245; debt.RemainingAmount = 322245; debt.DueDate = DateTime.UtcNow.Date.AddDays(-12);
        var order = new Order { CompanyId = app.CompanyA, CustomerId = debt.CustomerId, Number = "DH-CONTEXT-001", Total = 400000, Subtotal = 400000, PaidAmount = 77755, CreatedAt = DateTime.UtcNow.AddDays(-42) };
        db.Orders.Add(order); debt.OrderId = order.Id;
        db.Payments.Add(new Payment { CompanyId = app.CompanyA, OrderId = order.Id, Amount = 77755, PaidAt = order.CreatedAt, Reference = order.Number, Method = "TRANSFER" });
        await db.SaveChangesAsync();
    }
    private static async Task<string> Ask(HttpClient client, string question)
    {
        // No client-supplied history: follow-up state must come from the server.
        var response = await client.PostAsJsonAsync("/api/ai/assistant", new { question });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(); return result.GetProperty("answer").GetString()!;
    }
    [Theory]
    [InlineData("quá hạn bao lâu")]
    [InlineData("Quá hạn bao lâu?")]
    [InlineData("trễ hạn mấy ngày")]
    public async Task TopDebtorThenOverdueAnswersWithActualDays(string followUp)
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); await SetupDebt(app);
        var first = await Ask(client,"khách hàng nợ nhiều nhất"); Assert.Contains("Shop Phương Nhi",first); Assert.Contains("322.245",first);
        var answer = await Ask(client,followUp);
        Assert.Contains("Shop Phương Nhi",answer); Assert.Contains("12 ngày",answer); Assert.Contains("DH-CONTEXT-001",answer);
        Assert.Contains(DateTime.UtcNow.Date.AddDays(-12).ToString("dd/MM/yyyy"),answer); Assert.DoesNotContain("Tổng quan tháng",answer);
    }
    [Theory] [InlineData("khoản gì")] [InlineData("đơn nào?")] [InlineData("cho tôi xem chi tiết")]
    public async Task FollowUpFindsTheLinkedBusinessRecord(string question)
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); await SetupDebt(app);
        await Ask(client,"Khách hàng nào nợ nhiều nhất?");
        var answer = await Ask(client,question); Assert.Contains("Shop Phương Nhi",answer); Assert.Contains("DH-CONTEXT-001",answer); Assert.Contains("322.245",answer); Assert.DoesNotContain("Tổng quan",answer);
    }
    [Fact] public async Task PaymentQuestionReadsTheLinkedPaymentLedger()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); await SetupDebt(app);
        await Ask(client,"Khách hàng nợ nhiều nhất"); var answer = await Ask(client,"họ đã trả chưa?");
        Assert.Contains("77.755",answer); Assert.Contains("322.245",answer); Assert.Contains("TRANSFER",answer); Assert.Contains("DH-CONTEXT-001",answer);
    }
    [Fact] public async Task FollowUpDoesNotRerankAndDoesNotReuseStaleBalance()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); await SetupDebt(app); await Ask(client,"Khách hàng nợ nhiều nhất");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var debt = await db.Receivables.FindAsync(app.DebtId); debt!.RemainingAmount = 0; debt.Status = "PAID";
            var other = new Customer { CompanyId = app.CompanyA, Code = "KH2", Name = "Khách hàng khác" };
            db.Receivables.Add(new Receivable { CompanyId = app.CompanyA, Customer = other, OriginalAmount = 999999, RemainingAmount = 999999, DueDate = DateTime.UtcNow.AddDays(-30) });
            await db.SaveChangesAsync();
        }
        var answer = await Ask(client,"quá hạn bao lâu"); Assert.Contains("Shop Phương Nhi",answer); Assert.Contains("không có khoản",answer); Assert.DoesNotContain("Khách hàng khác",answer); Assert.DoesNotContain("322.245",answer);
    }
    [Fact] public async Task MissingContextAsksForTheCustomerInsteadOfAMonthlySummary()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync();
        var answer = await Ask(client,"quá hạn bao lâu"); Assert.Contains("khách hàng nào",answer); Assert.DoesNotContain("Tổng quan",answer); Assert.DoesNotContain("0 ₫",answer);
    }
    [Fact] public async Task DeletingHistoryAlsoDeletesFollowUpState()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); await SetupDebt(app);
        await Ask(client,"khách hàng nợ nhiều nhất"); (await client.DeleteAsync("/api/ai/chat-history")).EnsureSuccessStatusCode();
        var answer = await Ask(client,"khoản gì"); Assert.Contains("khách hàng nào",answer); Assert.DoesNotContain("Shop Phương Nhi",answer);
    }
    [Fact] public async Task CompanyAndUserConversationStateAreIsolated()
    {
        using var app = new ApiFactory(); using var client = await app.LoginAsync(); await SetupDebt(app); await Ask(client,"khách hàng nợ nhiều nhất");
        (await client.PostAsJsonAsync("/api/users",new { email="other@test.local",fullName="Other",password="OtherPass123!",role="ACCOUNTANT" })).EnsureSuccessStatusCode();
        using var other = app.CreateClient(); var login=await other.PostAsJsonAsync("/api/auth/login",new { email="other@test.local",password="OtherPass123!" });login.EnsureSuccessStatusCode();
        var auth=await login.Content.ReadFromJsonAsync<JsonElement>();other.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());other.DefaultRequestHeaders.Add("X-Company-Id",app.CompanyA.ToString());
        var otherAnswer=await Ask(other,"quá hạn bao lâu");Assert.Contains("khách hàng nào",otherAnswer);Assert.DoesNotContain("Shop Phương Nhi",otherAnswer);
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var membership=await db.CompanyUsers.SingleAsync(x=>x.CompanyId==app.CompanyB&&x.UserId==app.UserId);membership.RoleName=Roles.Admin;await db.SaveChangesAsync();}
        client.DefaultRequestHeaders.Remove("X-Company-Id");client.DefaultRequestHeaders.Add("X-Company-Id",app.CompanyB.ToString());
        var companyAnswer=await Ask(client,"khoản gì");Assert.Contains("khách hàng nào",companyAnswer);Assert.DoesNotContain("Shop Phương Nhi",companyAnswer);
    }
    [Fact] public async Task ExplicitCustomerNameWorksWithoutPriorContext()
    {
        using var app=new ApiFactory();using var client=await app.LoginAsync();await SetupDebt(app);
        var answer=await Ask(client,"Shop Phương Nhi quá hạn bao lâu?");Assert.Contains("12 ngày",answer);Assert.Contains("DH-CONTEXT-001",answer);
    }
    [Fact] public async Task TamperedForeignCustomerContextCannotReadAnotherCompany()
    {
        using var app=new ApiFactory();using var client=await app.LoginAsync();await SetupDebt(app);
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var foreign=new Customer {CompanyId=app.CompanyB,Code="FOREIGN",Name="Secret foreign customer"};db.Customers.Add(foreign);
            db.ChatMessages.Add(new ChatMessage {CompanyId=app.CompanyA,UserId=app.UserId,Role="assistant",Text="Test",ContextJson=JsonSerializer.Serialize(new {Kind="receivable",CustomerId=foreign.Id})});await db.SaveChangesAsync();
        }
        var answer=await Ask(client,"khoản gì");Assert.Contains("Không tìm thấy",answer);Assert.DoesNotContain("Secret foreign",answer);
    }
}
