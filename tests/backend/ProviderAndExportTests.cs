using System.Text;
using TaxFlow.Api.Services;
using Xunit;

namespace TaxFlow.Tests;

public sealed class ProviderAndExportTests
{
    private static readonly ExportTable Table = new(
        "Báo cáo thử",
        new[] { "Mã", "Số tiền" },
        new List<IReadOnlyList<string>> { new[] { "HD-001", "125000" } });

    [Fact]
    public void CsvExport_IsUtf8AndContainsHeaders()
    {
        var bytes = new ExportService().ToCsv(Table);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Contains("Số tiền", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void XlsxExport_IsAValidZipContainer()
    {
        var bytes = new ExportService().ToXlsx(Table);
        Assert.True(bytes.Length > 200);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public void PdfExport_HasPdfSignature()
    {
        var bytes = new ExportService().ToPdf(Table);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task DemoProvider_UsesFilenameHintsWithoutInventingAmounts()
    {
        var result = await new DemoAIProvider().AnalyzeInvoiceAsync(
            new InvoiceDocumentInput("HD-ABC123_0402198765.pdf", "application/pdf", new byte[] { 1, 2 }),
            CancellationToken.None);

        Assert.Equal("0402198765", result.SupplierTaxCode);
        Assert.Equal("ABC123", result.InvoiceNumber);
        Assert.Equal(0, result.Total);
        Assert.True(result.ConfidenceScore < 0.65m);
    }
}
