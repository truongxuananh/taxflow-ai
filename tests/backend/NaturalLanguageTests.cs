using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;
using Xunit;

namespace TaxFlow.Tests;
public sealed class FakeGemini(params string[] responses) : HttpMessageHandler
{
    private readonly Queue<string> pending = new(responses);
    public List<string> Bodies { get; } = new();
    public string? Key { get; private set; }
    public Uri? Url { get; private set; }
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
        Key=request.Headers.GetValues("x-goog-api-key").Single(); Url=request.RequestUri;
        return new(Status) { Content = new StringContent(pending.Count > 0 ? pending.Dequeue() : "{}") };
    }
    public static string Call(string name, object args) => JsonSerializer.Serialize(new { candidates = new[] { new { finishReason="STOP", content = new { parts = new[] { new { functionCall = new { name, args } } } } } } });
}
public sealed class NaturalLanguageTests
{
    private static GeminiFunctionPlanner Planner(FakeGemini handler) => new(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["GEMINI_API_KEY"]="test-only-key", ["GEMINI_MODEL"]="test-model" }).Build());
    private static async Task<JsonElement> Ask(HttpClient client,string question)
    {
        var response=await client.PostAsJsonAsync("/api/ai/assistant",new {question});
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    [Fact] public async Task PlannerUsesDeclaredToolsAndKeyHeaderWithoutKeyInUrl()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_receivables",new {view="top"}));
        var result=await Planner(fake).PlanAsync("ai thiếu tiền nhiều nhất",Array.Empty<object>(),"{}",default);
        Assert.Equal("get_receivables",Assert.Single(result).Name);Assert.Equal("test-only-key",fake.Key);Assert.DoesNotContain("test-only-key",fake.Url!.ToString());
        using var json=JsonDocument.Parse(Assert.Single(fake.Bodies));Assert.Equal("ANY",json.RootElement.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString());
        Assert.Equal(8,json.RootElement.GetProperty("tools")[0].GetProperty("functionDeclarations").GetArrayLength());
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"invented answer\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"execute_sql\",\"args\":{}}}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[]}}]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"get_invoices\",\"args\":\"bad\"}}]}}]}")]
    public async Task PlannerRejectsUntrustedOrIncompleteResponses(string response)
    {using var fake=new FakeGemini(response);await Assert.ThrowsAsync<GeminiPlanningException>(()=>Planner(fake).PlanAsync("test",Array.Empty<object>(),"{}",default));}
    [Fact] public async Task ParaphraseAndFollowUpUseServerContextAndFreshAmounts()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_receivables",new {view="top"}),FakeGemini.Call("get_receivables",new {view="overdue",useContext=true}));
        using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        using(var scope=app.Services.CreateScope()) {var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var debt=await db.Receivables.Include(x=>x.Customer).SingleAsync();debt.Customer.Name="Shop Phương Nhi";debt.RemainingAmount=322245;debt.OriginalAmount=322245;debt.DueDate=DateTime.UtcNow.Date.AddDays(-12);await db.SaveChangesAsync();}
        var first=await Ask(client,"Ai đang thiếu tiền bên mình nhiều nhất?");Assert.Contains("322.245",first.GetProperty("answer").GetString());Assert.Equal("gemini-functions+database",first.GetProperty("mode").GetString());
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();(await db.Receivables.SingleAsync()).RemainingAmount=300000;await db.SaveChangesAsync();}
        var second=await Ask(client,"Họ trễ mấy bữa rồi?");var answer=second.GetProperty("answer").GetString();Assert.Contains("12 ngày",answer);Assert.Contains("300.000",answer);Assert.DoesNotContain("322.245",answer);
        using var body=JsonDocument.Parse(fake.Bodies[1]);var sent=body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();Assert.Contains("receivable",sent);
        using var finalScope=app.Services.CreateScope();Assert.Equal(4,await finalScope.ServiceProvider.GetRequiredService<AppDbContext>().ChatMessages.CountAsync());
    }
    [Fact] public async Task FinancialDatesUseVietnamBoundariesAndCompareActualLedger()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_financial_metrics",new {metric="income",fromDate="2026-11-01",toDate="2026-11-30",compareFromDate="2026-10-01",compareToDate="2026-10-31"}));
        using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.IncomeTransactions.AddRange(
                new IncomeTransaction {CompanyId=app.CompanyA,Amount=1200,TransactionDate=new DateTime(2026,10,31,18,0,0,DateTimeKind.Utc)},
                new IncomeTransaction {CompanyId=app.CompanyA,Amount=200,TransactionDate=new DateTime(2026,10,31,16,59,0,DateTimeKind.Utc)},
                new IncomeTransaction {CompanyId=app.CompanyB,Amount=9999999,TransactionDate=new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc)});
            await db.SaveChangesAsync();
        }
        var result=await Ask(client,"Tháng mười một thu tiền hơn tháng mười bao nhiêu?");var answer=result.GetProperty("answer").GetString();
        Assert.Contains("1.200",answer);Assert.Contains("200",answer);Assert.Contains("1.000",answer);Assert.Contains("giờ Việt Nam",answer);Assert.DoesNotContain("9.999.999",answer);
    }
    [Theory] [InlineData("missing-context")] [InlineData("extra-company")] [InlineData("invalid-date")]
    public async Task AmbiguousOrInvalidArgumentsFailClosed(string scenario)
    {
        var call=scenario switch {"missing-context"=>FakeGemini.Call("get_receivables",new {view="details",useContext=true}),"extra-company"=>FakeGemini.Call("get_invoices",new {companyId="foreign"}),_=>FakeGemini.Call("get_financial_metrics",new {metric="revenue",fromDate="2026-13-01",toDate="2026-11-01"})};
        using var fake=new FakeGemini(call);using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        var result=await Ask(client,"cho xem tiếp");var answer=result.GetProperty("answer").GetString();Assert.DoesNotContain("Tổng quan",answer);Assert.DoesNotContain("0 ₫",answer);
        Assert.True(answer!.Contains("khách hàng nào")||answer.Contains("chưa được hỗ trợ")||answer.Contains("khoảng ngày hợp lệ"));
    }
    [Fact] public async Task RateLimitIsVisibleAndNeverClaimsGeminiAnswered()
    {
        using var fake=new FakeGemini {Status=HttpStatusCode.TooManyRequests};using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        var result=await Ask(client,"khách hàng nợ nhiều nhất");Assert.Equal("fallback-http_429",result.GetProperty("mode").GetString());Assert.Contains("Gemini chưa sẵn sàng",result.GetProperty("answer").GetString());
        var history=await client.GetStringAsync("/api/ai/chat-history");Assert.Contains("Gemini",history);
    }
    [Fact] public async Task CustomerContextCannotEscapeCompanyScope()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_receivables",new {view="details",useContext=true}));using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var other=new Customer {CompanyId=app.CompanyB,Code="SECRET",Name="Foreign secret"};db.Customers.Add(other);db.ChatMessages.Add(new ChatMessage {CompanyId=app.CompanyA,UserId=app.UserId,Role="assistant",Text="test",ContextJson=JsonSerializer.Serialize(new NaturalContext("receivable",other.Id))});await db.SaveChangesAsync();}
        var result=await Ask(client,"khoản ấy là gì");Assert.Contains("Không tìm thấy",result.GetProperty("answer").GetString());Assert.DoesNotContain("Foreign secret",result.GetProperty("answer").GetString());
    }
    [Fact] public async Task StatusExposesPresenceButNeverTheKeyOrUnverifiedConnection()
    {
        using var fake=new FakeGemini();using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        var result=await client.GetStringAsync("/api/ai/assistant-status");Assert.DoesNotContain("test-only-key",result);using var json=JsonDocument.Parse(result);Assert.True(json.RootElement.GetProperty("keyConfigured").GetBoolean());Assert.False(json.RootElement.GetProperty("connectionVerified").GetBoolean());Assert.Empty(fake.Bodies);
    }
    [Fact] public async Task FinanceFollowUpInheritsPeriodAndUsesNewLedgerValues()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_financial_metrics",new {metric="income",fromDate="2026-11-01",toDate="2026-11-30"}),FakeGemini.Call("get_financial_metrics",new {metric="income",useContext=true}));
        using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();await Ask(client,"tiền vào tháng mười một");
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();db.IncomeTransactions.Add(new IncomeTransaction {CompanyId=app.CompanyA,Amount=765432,TransactionDate=new DateTime(2026,11,3,0,0,0,DateTimeKind.Utc)});await db.SaveChangesAsync();}
        var result=await Ask(client,"xem lại kỳ vừa rồi");var answer=result.GetProperty("answer").GetString();Assert.Contains("765.432",answer);Assert.Contains("01/11/2026",answer);
    }
    [Fact] public async Task MultipleToolsCanUseTheContextOfThePreviousTool()
    {
        var response = """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"functionCall":{"name":"get_receivables","args":{"view":"top"}}},{"functionCall":{"name":"get_receivables","args":{"view":"details","useContext":true}}}]}}]}""";
        using var fake=new FakeGemini(response);using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        var result=await Ask(client,"ai nợ nhiều nhất, khoản nào thế?");var answer=result.GetProperty("answer").GetString();Assert.Contains("nợ nhiều nhất",answer);Assert.Contains("Các khoản phải thu",answer);
    }
    [Fact] public async Task InvoicesAreScopedAndFollowUpRequeriesTheSelectedInvoice()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_invoices",new {latest=true}),FakeGemini.Call("get_invoices",new {useContext=true}));using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();Guid selected;
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var invoice=new Invoice {CompanyId=app.CompanyA,Number="HD-SELECTED",PartyName="Local",InvoiceDate=DateTime.UtcNow,Total=123456};selected=invoice.Id;db.Invoices.AddRange(invoice,new Invoice {CompanyId=app.CompanyB,Number="HD-FOREIGN",PartyName="Secret foreign",InvoiceDate=DateTime.UtcNow.AddDays(1),Total=9999999});await db.SaveChangesAsync();}
        var first=await Ask(client,"hóa đơn vừa nhập đâu?");Assert.Contains("HD-SELECTED",first.GetProperty("answer").GetString());Assert.DoesNotContain("HD-FOREIGN",first.GetProperty("answer").GetString());
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();(await db.Invoices.FindAsync(selected))!.Total=222222;await db.SaveChangesAsync();}
        var second=await Ask(client,"cái đó tổng tiền bao nhiêu?");Assert.Contains("222.222",second.GetProperty("answer").GetString());Assert.DoesNotContain("123.456",second.GetProperty("answer").GetString());
    }
    [Fact] public async Task AmbiguousCustomerNameAsksInsteadOfChoosingArbitrarily()
    {
        using var fake=new FakeGemini(FakeGemini.Call("get_receivables",new {view="details",customerQuery="Nhi"}));using var app=new ApiFactory {GeminiHandler=fake};using var client=await app.LoginAsync();
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();db.Customers.AddRange(new Customer {CompanyId=app.CompanyA,Code="NHI1",Name="Shop Nhi A"},new Customer {CompanyId=app.CompanyA,Code="NHI2",Name="Shop Nhi B"});await db.SaveChangesAsync();}
        var result=await Ask(client,"Nhi còn nợ bao nhiêu");Assert.Contains("Có nhiều khách hàng",result.GetProperty("answer").GetString());Assert.Contains("NHI1",result.GetProperty("answer").GetString());Assert.Contains("NHI2",result.GetProperty("answer").GetString());
    }
    [Fact] public async Task MissingKeyFallbackCanAnswerFinancialQueriesWithoutCloud()
    {
        using var app=new ApiFactory();using var client=await app.LoginAsync();var result=await Ask(client,"doanh thu tháng trước");Assert.Equal("fallback-missing_key",result.GetProperty("mode").GetString());Assert.Contains("Doanh thu",result.GetProperty("answer").GetString());
    }

}
