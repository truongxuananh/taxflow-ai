using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;
public static class DocumentCompletionEndpoints
{
    public static void MapDocumentCompletionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/documents/{id:guid}/retry", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, AIProviderResolver resolver, InvoiceValidationService validator, SupplierMemoryService memory, IConfiguration configuration, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            var document = await db.Documents.FirstOrDefaultAsync(x => x.CompanyId == cid && x.Id == id, ct);
            if (document is null) return Results.NotFound();
            if (document.Status == "CONFIRMED" || document.Status == "PROCESSING" && document.UpdatedAt > DateTime.UtcNow.AddMinutes(-5)) return Results.Conflict(new { message = "Chứng từ đã xác nhận hoặc đang được xử lý." });
            var path = Path.Combine(configuration["STORAGE_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "storage"), cid.ToString("N"), document.StoredFileName);
            if (!File.Exists(path)) return Results.NotFound(new { message = "Không tìm thấy chứng từ gốc." });
            document.Status = "PROCESSING"; await db.SaveChangesAsync(ct);
            try
            {
                var provider = resolver.Current;
                var extracted = await provider.AnalyzeInvoiceAsync(new InvoiceDocumentInput(document.OriginalFileName, document.ContentType, await File.ReadAllBytesAsync(path, ct)), ct);
                var result = await memory.ApplyAsync(cid, extracted, ct);
                var issues = await validator.ValidateAsync(cid, result, ct);
                db.AIAnalyses.Add(new AIAnalysis { CompanyId = cid, DocumentId = id, Provider = result.Provider, ConfidenceScore = result.ConfidenceScore, ResultJson = JsonSerializer.Serialize(result), ValidationJson = JsonSerializer.Serialize(issues), Status = "REVIEW_REQUIRED" });
                document.Status = "PENDING_REVIEW"; await db.SaveChangesAsync(ct);
                return Results.Ok(new { document = new { document.Id, document.OriginalFileName, document.Status }, extraction = result, validation = issues, workflow = new[] { "AI", "Validation", "User Review", "Confirm", "Database" } });
            }
            catch (Exception ex) when (ex is not DbUpdateConcurrencyException)
            {
                document.Status = "AI_FAILED"; await db.SaveChangesAsync(CancellationToken.None);
                return Results.Problem("OCR chưa thành công. Chứng từ gốc được giữ lại để thử lại.", statusCode: 502);
            }
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
        app.MapGet("/api/documents/{id:guid}/status", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct);
            var document = await db.Documents.AsNoTracking().Where(x => x.Id == id && x.CompanyId == cid).Select(x => new { x.Id, x.Status, x.UpdatedAt, x.ConfirmedInvoiceId }).FirstOrDefaultAsync(ct);
            return document is null ? Results.NotFound() : Results.Ok(document);
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Accountant));
    }
}
