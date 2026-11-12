namespace TaxFlow.Api.Endpoints;

public static class EndpointMapping
{
    public static void MapTaxFlowEndpoints(this WebApplication app)
    {
        app.MapAuthAndCompanyEndpoints();
        app.MapCatalogAndSalesEndpoints();
        app.MapFinanceEndpoints();
        app.MapInvoiceAndAIEndpoints();
        app.MapReportingEndpoints();
    }
}
