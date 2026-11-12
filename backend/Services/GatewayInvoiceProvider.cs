using System.Net.Http.Json;
using System.Net.Http.Headers;
using TaxFlow.Api.Models;
namespace TaxFlow.Api.Services;

/// <summary>An optional server-side bridge; the actual vendor connector must implement the documented contract.</summary>
public sealed class GatewayInvoiceProvider(HttpClient http, IConfiguration configuration) : IInvoiceProvider
{
    public string Name => "ConfiguredInvoiceGateway";
    public async Task<InvoiceIssueResult> IssueAsync(Invoice invoice, CancellationToken ct)
    {
        var url = configuration["INVOICE_GATEWAY_URL"];
        var key = configuration["INVOICE_GATEWAY_KEY"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Invoice gateway cần URL HTTPS và khóa truy cập phía server.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("Idempotency-Key", invoice.Id.ToString());
        request.Content = JsonContent.Create(new { invoice.Id, invoice.CompanyId, invoice.Number, invoice.Serial, invoice.InvoiceDate, invoice.PartyName, invoice.PartyTaxCode, invoice.PartyAddress, invoice.PartyEmail, invoice.Subtotal, invoice.VatAmount, invoice.Total, items = invoice.Items.Select(x => new { x.Description, x.Unit, x.Quantity, x.UnitPrice, x.VatRate, x.VatAmount, x.Total }) });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Gateway hóa đơn trả lỗi {(int)response.StatusCode}; hóa đơn chưa được đánh dấu phát hành.");
        var result = await response.Content.ReadFromJsonAsync<InvoiceIssueResult>(cancellationToken: ct);
        if (result is null || !result.IsLegallyIssued || result.Status != InvoiceStatuses.Issued || string.IsNullOrWhiteSpace(result.ProviderReference))
            throw new InvalidOperationException("Gateway chưa trả xác nhận phát hành đầy đủ; không đánh dấu hợp pháp.");
        return result;
    }
}
