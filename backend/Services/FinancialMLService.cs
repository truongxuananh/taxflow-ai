using System.Net.Http.Json;
using System.Text.Json;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;

public sealed record MlDailyCashPoint(string Date, decimal Inflow, decimal Outflow);
public sealed record MlForecastPoint(DateTime Date, decimal ProjectedInflow, decimal ProjectedOutflow, decimal ProjectedNet);
public sealed record MlForecastResponse(string Status, string? Method, int Horizon, int? HistoricalDays, int? MinimumDays, int? AvailableDays, IReadOnlyList<MlForecastPoint> Series, JsonElement? Quality = null);

public sealed record MlAnomalyRecord(
    string Id,
    decimal Amount,
    decimal VatRate,
    decimal SupplierFrequency,
    decimal AmountVsSupplierAverage,
    decimal DaysSinceLastInvoice,
    decimal InvoiceFrequency);
public sealed record MlAnomalyResult(string Id, bool IsAnomaly, int RiskScore, string Level, IReadOnlyList<string> Reasons);
public sealed record MlAnomalyResponse(string Status, string Method, int? MinimumRecords, int? AvailableRecords, IReadOnlyList<MlAnomalyResult> Results);

public sealed class FinancialMLService(HttpClient httpClient, IConfiguration configuration)
{
    private string BaseUrl => (configuration["AI_SERVICE_URL"] ?? "http://ai-service:8000").TrimEnd('/');

    public async Task<MlForecastResponse?> ForecastAsync(IReadOnlyList<MlDailyCashPoint> history, int horizon, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BaseUrl}/ml/forecast", new { history, horizon }, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<MlForecastResponse>(cancellationToken: ct);
    }

    public async Task<MlAnomalyResponse?> DetectAnomaliesAsync(IReadOnlyList<MlAnomalyRecord> records, CancellationToken ct)
    {
        using var response = await httpClient.PostAsJsonAsync($"{BaseUrl}/ml/anomaly", new { records, contamination = 0.08 }, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<MlAnomalyResponse>(cancellationToken: ct);
    }
}
