using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;

public static class InvoiceAndAIEndpoints
{
    public static void MapInvoiceAndAIEndpoints(this WebApplication app)
    {
        var invoices = app.MapGroup("/api/invoices")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("Invoices");
        invoices.MapGet("/", async (
            string? direction,
            string? status,
            int? take,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var query = db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId);
            if (!string.IsNullOrWhiteSpace(direction)) query = query.Where(x => x.Direction == direction.Trim().ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim());
            var limit = Math.Clamp(take ?? 100, 1, 500);
            return Results.Ok(await query.OrderByDescending(x => x.InvoiceDate).Take(limit).Select(x => new
            {
                x.Id, x.Direction, x.Number, x.Serial, x.InvoiceDate, x.PartyName, x.PartyTaxCode,
                x.Subtotal, x.VatAmount, x.Total, x.Status, x.Provider, x.ProviderReference,
                x.IsLegallyIssued, x.AccountingReviewed, x.ConfidenceScore, x.SourceOrderId, itemCount = x.Items.Count
            }).ToListAsync(ct));
        });
        invoices.MapPost("/{id:guid}/issue", async (
            Guid id,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            IInvoiceProvider provider,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var invoice = await db.Invoices.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId && x.Direction == "OUTGOING", ct);
            if (invoice is null) return Results.NotFound();
            if (invoice.Status is not (InvoiceStatuses.Draft or InvoiceStatuses.Adjusted))
                return Results.BadRequest(new { message = "Chỉ hóa đơn nháp hoặc hóa đơn điều chỉnh mới được gửi phát hành." });
            var oldStatus = invoice.Status;
            var result = await provider.IssueAsync(invoice, ct);
            invoice.Status = result.Status;
            invoice.Provider = provider.Name;
            invoice.ProviderReference = result.ProviderReference;
            invoice.IsLegallyIssued = result.IsLegallyIssued;
            invoice.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "REQUEST_ISSUE", "Invoice", id,
                new { Status = oldStatus }, new { invoice.Status, invoice.IsLegallyIssued, Provider = provider.Name }, ct);
            return Results.Ok(new { invoice.Id, invoice.Status, invoice.IsLegallyIssued, result.Message, provider = provider.Name });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        invoices.MapPost("/{id:guid}/cancel", async (
            Guid id,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var invoice = await db.Invoices.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (invoice is null) return Results.NotFound();
            if (invoice.IsLegallyIssued)
                return Results.BadRequest(new { message = "Hóa đơn đã phát hành hợp pháp phải hủy/điều chỉnh qua nhà cung cấp điện tử." });
            if (invoice.Status == InvoiceStatuses.Cancelled)
                return Results.Conflict(new { message = "Hóa đơn đã được hủy trước đó." });
            var old = invoice.Status;
            invoice.Status = InvoiceStatuses.Cancelled; invoice.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CANCEL", "Invoice", id, new { Status = old }, new { invoice.Status }, ct);
            return Results.Ok(new { invoice.Id, invoice.Status });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        var documents = app.MapGroup("/api/documents")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant))
            .WithTags("Invoice OCR");
        documents.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(await db.Documents.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new
                {
                    x.Id, x.OriginalFileName, x.ContentType, x.Size, x.Status, x.CreatedAt,
                    analysis = db.AIAnalyses.Where(a => a.DocumentId == x.Id).OrderByDescending(a => a.CreatedAt)
                        .Select(a => new { a.Id, a.Provider, a.ConfidenceScore, a.Status, a.ResultJson, a.ValidationJson }).FirstOrDefault()
                }).ToListAsync(ct));
        });
            documents.MapGet("/{id:guid}/file", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, IConfiguration configuration, CancellationToken ct) =>
            {
                var companyId = await access.ResolveCompanyIdAsync(context, ct);
                var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
                if (document is null) return Results.NotFound();
                var root = configuration["STORAGE_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "storage");
                var path = Path.Combine(root, companyId.ToString("N"), document.StoredFileName);
                return !File.Exists(path) ? Results.NotFound() : Results.File(path, document.ContentType, document.OriginalFileName, enableRangeProcessing: true);
            });
        documents.MapGet("/{id:guid}/preview", async (
            Guid id,
            int? page,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            LocalAIProvider localAI,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (document is null) return Results.NotFound();
            var root = configuration["STORAGE_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "storage");
            var path = Path.Combine(root, companyId.ToString("N"), document.StoredFileName);
            if (!File.Exists(path)) return Results.NotFound();
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var requestedPage = Math.Max(1, page ?? 1);
            var input = new InvoiceDocumentInput(document.OriginalFileName, document.ContentType, bytes);
            try
            {
                var rendered = await localAI.RenderPageAsync(input, requestedPage, ct);
                context.Response.Headers["X-Page-Count"] = rendered.PageCount.ToString();
                return Results.File(rendered.Bytes, rendered.ContentType, $"{Path.GetFileNameWithoutExtension(document.OriginalFileName)}-page-{requestedPage}.png");
            }
            catch (Exception) when (document.ContentType != "application/pdf" && requestedPage == 1)
            {
                return Results.File(bytes, document.ContentType, document.OriginalFileName, enableRangeProcessing: true);
            }
        });

        documents.MapPost("/upload", UploadDocumentAsync).DisableAntiforgery()
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        documents.MapPost("/{id:guid}/confirm", ConfirmDocumentAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        documents.MapPost("/{id:guid}/ocr-region", OcrRegionAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        var ai = app.MapGroup("/api/ai")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("AI");
        ai.MapPost("/assistant", async (AssistantRequest request, HttpContext context, CompanyAccessService access, NaturalAssistantService service, CancellationToken ct) =>
            Results.Ok(await service.AnswerAsync(await access.ResolveCompanyIdAsync(context, ct), access.GetUserId(context.User), request, ct)));
        ai.MapGet("/assistant-status", async (HttpContext context, CompanyAccessService access, GeminiFunctionPlanner planner, IConfiguration config, CancellationToken ct) =>
        {
            await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(new { mode = config["ASSISTANT_MODE"] ?? "natural", keyConfigured = planner.IsAvailable, model = config["GEMINI_MODEL"] ?? "gemini-2.5-flash", connectionVerified = false });
        });
        ai.MapGet("/chat-history", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct); var uid = access.GetUserId(context.User);
            return Results.Ok(await db.ChatMessages.AsNoTracking().Where(x => x.CompanyId == cid && x.UserId == uid).OrderByDescending(x => x.CreatedAt).Take(100).OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Role, x.Text, x.Source, time = x.CreatedAt }).ToListAsync(ct));
        });
        ai.MapDelete("/chat-history", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var cid = await access.ResolveCompanyIdAsync(context, ct); var uid = access.GetUserId(context.User);
            await db.ChatMessages.Where(x => x.CompanyId == cid && x.UserId == uid).ExecuteDeleteAsync(ct); return Results.NoContent();
        });
        ai.MapGet("/forecast", ForecastAsync);
        ai.MapGet("/alerts", async (bool? unreadOnly, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var query = db.AIAlerts.AsNoTracking().Where(x => x.CompanyId == companyId);
            if (unreadOnly == true) query = query.Where(x => !x.IsRead);
            return Results.Ok(await query.OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct));
        });
        ai.MapPost("/alerts/{id:guid}/read", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var alert = await db.AIAlerts.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
            if (alert is null) return Results.NotFound();
            alert.IsRead = true; alert.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        ai.MapGet("/training-dataset", ExportTrainingDatasetAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));
        ai.MapPost("/scan-anomalies", ScanAnomaliesAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director));
    }

    private static async Task<IResult> UploadDocumentAsync(
        IFormFile file,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        AIProviderResolver resolver,
        InvoiceValidationService validator,
        SupplierMemoryService supplierMemory,
        IConfiguration configuration,
        CancellationToken ct)
    {
        const long maxSize = 10 * 1024 * 1024;
        if (file.Length == 0 || file.Length > maxSize)
            return Results.BadRequest(new { message = "Tệp phải có dung lượng từ 1 byte đến 10 MB." });
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new Dictionary<string, string>
        {
            [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png"
        };
        if (!allowed.TryGetValue(extension, out var contentType))
            return Results.BadRequest(new { message = "Chỉ hỗ trợ PDF, JPG và PNG." });

        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        if (!MatchesFileSignature(extension, bytes))
            return Results.BadRequest(new { message = "Nội dung tệp không khớp định dạng PDF/JPG/PNG đã chọn." });
        var storageRoot = configuration["STORAGE_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "storage");
        var companyFolder = Path.Combine(storageRoot, companyId.ToString("N"));
        Directory.CreateDirectory(companyFolder);
        var storedName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(companyFolder, storedName), bytes, ct);

        var document = new Document
        {
            CompanyId = companyId,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedName,
            ContentType = contentType,
            Size = file.Length,
            Status = "PROCESSING",
            UploadedByUserId = access.GetUserId(context.User)
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        var provider = resolver.Current;
        InvoiceExtractionResult result;
        try
        {
            result = await provider.AnalyzeInvoiceAsync(new InvoiceDocumentInput(document.OriginalFileName, contentType, bytes), ct);
        }
        catch (Exception ex)
        {
            document.Status = "AI_FAILED";
            db.AIAnalyses.Add(new AIAnalysis
            {
                CompanyId = companyId, DocumentId = document.Id, AnalysisType = "INVOICE_OCR", Provider = provider.Name,
                ConfidenceScore = 0, ResultJson = "{}", ValidationJson = JsonSerializer.Serialize(new[] { ex.Message }), Status = "FAILED"
            });
            await db.SaveChangesAsync(CancellationToken.None);
            return Results.Problem("Không thể xử lý OCR. Tệp đã được lưu để thử lại.", statusCode: 502);
        }
        result = await supplierMemory.ApplyAsync(companyId, result, ct);
        var issues = (await validator.ValidateAsync(companyId, result, ct)).ToList();
        if (resolver.FellBackToDemo)
            issues.Add(new ValidationIssue("provider", "DEMO_FALLBACK", "Không có OCR provider khả dụng; hệ thống đã dùng DemoAIProvider.", "warning"));
        document.Status = "PENDING_REVIEW";
        var analysis = new AIAnalysis
        {
            CompanyId = companyId, DocumentId = document.Id, AnalysisType = "INVOICE_OCR", Provider = result.Provider,
            ConfidenceScore = result.ConfidenceScore, ResultJson = JsonSerializer.Serialize(result),
            ValidationJson = JsonSerializer.Serialize(issues), Status = "REVIEW_REQUIRED"
        };
        db.AIAnalyses.Add(analysis);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new
        {
            document = new { document.Id, document.OriginalFileName, document.Status },
            extraction = result,
            validation = issues,
            workflow = new[] { "AI", "Validation", "User Review", "Confirm", "Database" }
        });
    }



    private static async Task<IResult> ExportTrainingDatasetAsync(
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var rows = await db.OCRCorrections.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.DocumentId, x.Provider, x.OriginalConfidenceScore, x.OriginalJson, x.CorrectedJson, x.ValidationJson, x.CreatedAt, x.CorrectedByUserId })
            .ToListAsync(ct);
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            object? original; object? corrected; object? validation;
            try { original = JsonSerializer.Deserialize<JsonElement>(row.OriginalJson); } catch { original = row.OriginalJson; }
            try { corrected = JsonSerializer.Deserialize<JsonElement>(row.CorrectedJson); } catch { corrected = row.CorrectedJson; }
            try { validation = JsonSerializer.Deserialize<JsonElement>(row.ValidationJson); } catch { validation = row.ValidationJson; }
            builder.AppendLine(JsonSerializer.Serialize(new
            {
                schemaVersion = "taxflow-ocr-correction-v1",
                verified = true,
                correctionId = row.Id,
                row.DocumentId,
                row.Provider,
                row.OriginalConfidenceScore,
                original,
                corrected,
                validation,
                verifiedAt = row.CreatedAt,
                verifiedByUserId = row.CorrectedByUserId
            }));
        }
        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Results.File(bytes, "application/x-ndjson", $"taxflow-ocr-training-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
    }

    private static async Task<IResult> OcrRegionAsync(
        Guid id,
        OcrRegionRequest request,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        LocalAIProvider local,
        IConfiguration configuration,
        CancellationToken ct)
    {
        if (!local.IsAvailable)
            return Results.Problem("Local AI service chưa được cấu hình.", statusCode: 503);
        if (string.IsNullOrWhiteSpace(request.Field))
            return Results.BadRequest(new { message = "Tên trường OCR là bắt buộc." });
        if (request.Page < 1 || request.X < 0 || request.Y < 0 || request.Width <= 0 || request.Height <= 0 ||
            request.X + request.Width > 1.001m || request.Y + request.Height > 1.001m)
            return Results.BadRequest(new { message = "Tọa độ vùng OCR phải nằm trong 0..1." });

        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
        if (document is null) return Results.NotFound();
        var root = configuration["STORAGE_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "storage");
        var path = Path.Combine(root, companyId.ToString("N"), document.StoredFileName);
        if (!File.Exists(path)) return Results.NotFound(new { message = "Không tìm thấy tệp gốc." });
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var result = await local.AnalyzeRegionAsync(new InvoiceDocumentInput(document.OriginalFileName, document.ContentType, bytes), request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ConfirmDocumentAsync(
        Guid id,
        ConfirmDocumentRequest request,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        InvoiceValidationService validator,
        AuditService audit,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        if (string.IsNullOrWhiteSpace(request.SupplierName) || string.IsNullOrWhiteSpace(request.InvoiceNumber))
            return Results.BadRequest(new { message = "Tên nhà cung cấp và số hóa đơn là bắt buộc." });
        if (request.Total <= 0 || request.Subtotal < 0 || request.VatAmount < 0)
            return Results.BadRequest(new { message = "Giá trị hóa đơn không hợp lệ." });
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, ct);
        if (document is null) return Results.NotFound();
        if (document.Status == "CONFIRMED") return Results.Conflict(new { message = "Chứng từ đã được xác nhận." });
        var originalAnalysis = await db.AIAnalyses.Where(x => x.DocumentId == id && x.CompanyId == companyId).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        var result = new InvoiceExtractionResult
        {
            Provider = "UserReviewed", SupplierName = request.SupplierName.Trim(), SupplierTaxCode = request.SupplierTaxCode?.Trim(),
            InvoiceNumber = request.InvoiceNumber.Trim(), Serial = request.Serial?.Trim(), InvoiceDate = request.InvoiceDate,
            Subtotal = request.Subtotal, VatAmount = request.VatAmount, Total = request.Total, ConfidenceScore = originalAnalysis?.ConfidenceScore ?? 0,
            Items = request.Items ?? Array.Empty<ExtractedInvoiceItem>()
        };
        var issues = await validator.ValidateAsync(companyId, result, ct);
        var blocking = issues.Where(x => x.Severity == "error").ToList();
        if (blocking.Count > 0) return Results.BadRequest(new { message = "Dữ liệu chưa hợp lệ.", validation = issues });

        var supplier = !string.IsNullOrWhiteSpace(request.SupplierTaxCode)
            ? await db.Suppliers.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.TaxCode == request.SupplierTaxCode.Trim(), ct)
            : await db.Suppliers.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Name == request.SupplierName.Trim(), ct);
        if (supplier is null)
        {
            supplier = new Supplier
            {
                CompanyId = companyId, Code = $"NCC-{DateTime.UtcNow:yyMMddHHmmss}-{Guid.NewGuid():N}"[..30], Name = request.SupplierName.Trim(),
                TaxCode = request.SupplierTaxCode?.Trim()
            };
            db.Suppliers.Add(supplier);
        }
        var invoice = new Invoice
        {
            CompanyId = companyId, Direction = "INCOMING", Number = request.InvoiceNumber.Trim(), Serial = request.Serial?.Trim(),
            InvoiceDate = request.InvoiceDate.ToUniversalTime(), Supplier = supplier, PartyName = supplier.Name,
            PartyTaxCode = supplier.TaxCode, PartyAddress = supplier.Address, PartyEmail = supplier.Email,
            Subtotal = request.Subtotal, VatAmount = request.VatAmount, Total = request.Total,
            Status = InvoiceStatuses.Pending, Provider = "UserReviewedImport", IsLegallyIssued = false, ConfidenceScore = originalAnalysis?.ConfidenceScore
        };
        foreach (var item in request.Items ?? Array.Empty<ExtractedInvoiceItem>())
            invoice.Items.Add(new InvoiceItem
            {
                Description = item.Description, Unit = item.Unit, Quantity = item.Quantity, UnitPrice = item.UnitPrice,
                VatRate = item.VatRate, VatAmount = item.VatAmount, Total = item.Total
            });
        db.Invoices.Add(invoice);
        db.Payables.Add(new Payable
        {
            CompanyId = companyId, Supplier = supplier, InvoiceId = invoice.Id,
            OriginalAmount = invoice.Total, RemainingAmount = invoice.Total,
            DueDate = invoice.InvoiceDate.Date.AddDays(30), Status = "OPEN"
        });
        document.ConfirmedInvoiceId = invoice.Id;
        document.Status = "CONFIRMED"; document.UpdatedAt = DateTime.UtcNow;
        var analysis = await db.AIAnalyses.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(x => x.DocumentId == id, ct);
        if (analysis is not null)
        {
            analysis.Status = "CONFIRMED";
            analysis.UpdatedAt = DateTime.UtcNow;
            db.OCRCorrections.Add(new OCRCorrection
            {
                CompanyId = companyId,
                DocumentId = id,
                CorrectedByUserId = access.GetUserId(context.User),
                Provider = analysis.Provider,
                OriginalConfidenceScore = analysis.ConfidenceScore,
                OriginalJson = analysis.ResultJson,
                CorrectedJson = JsonSerializer.Serialize(result),
                ValidationJson = JsonSerializer.Serialize(issues)
            });
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(context, companyId, "CONFIRM_OCR", "Invoice", invoice.Id, null,
            new { invoice.Number, invoice.PartyTaxCode, invoice.Total, DocumentId = id }, ct);
        return Results.Created($"/api/invoices/{invoice.Id}", new
        {
            invoice.Id,
            invoice.Number,
            InvoiceStatus = invoice.Status,
            DocumentStatus = document.Status
        });
    }

    private static async Task<IResult> AnswerAssistantAsync(
        AssistantRequest request,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        GroundedFinancialAssistant groundedAssistant,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.BadRequest(new { message = "Vui lòng nhập câu hỏi." });

        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var now = DateTime.UtcNow;
        var today = now.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var previousMonthStart = monthStart.AddMonths(-1);
        var yearStart = new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Snapshot rộng nhưng có giới hạn: Gemini chỉ diễn giải dữ liệu backend đã scope theo CompanyId.
        var revenueToday = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= today && x.CreatedAt < today.AddDays(1)).SumAsync(x => (decimal?)x.Total, ct) ?? 0;
        var revenueMonth = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= monthStart).SumAsync(x => (decimal?)x.Total, ct) ?? 0;
        var revenuePreviousMonth = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= previousMonthStart && x.CreatedAt < monthStart).SumAsync(x => (decimal?)x.Total, ct) ?? 0;
        var revenueYear = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == "COMPLETED" && x.CreatedAt >= yearStart).SumAsync(x => (decimal?)x.Total, ct) ?? 0;
        var expenseMonth = await db.ExpenseTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TransactionDate >= monthStart).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var incomeMonth = await db.IncomeTransactions.AsNoTracking().Where(x => x.CompanyId == companyId && x.TransactionDate >= monthStart).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var receivable = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0).SumAsync(x => (decimal?)x.RemainingAmount, ct) ?? 0;
        var payable = await db.Payables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0).SumAsync(x => (decimal?)x.RemainingAmount, ct) ?? 0;
        var vatOutput = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId && x.Direction == "OUTGOING" && x.InvoiceDate >= monthStart && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued)).SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;
        var vatInput = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId && x.Direction == "INCOMING" && x.InvoiceDate >= monthStart && x.Status != InvoiceStatuses.Cancelled && (x.Direction == "INCOMING" ? x.AccountingReviewed : x.IsLegallyIssued)).SumAsync(x => (decimal?)x.VatAmount, ct) ?? 0;

        var topDebtors = await db.Receivables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0)
            .GroupBy(x => new { x.CustomerId, x.Customer.Name }).Select(g => new { customerId = g.Key.CustomerId, name = g.Key.Name, amount = g.Sum(x => x.RemainingAmount), overdue = g.Count(x => x.DueDate < now) })
            .OrderByDescending(x => x.amount).Take(8).ToListAsync(ct);
        var topPayables = await db.Payables.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemainingAmount > 0)
            .GroupBy(x => new { x.SupplierId, x.Supplier.Name }).Select(g => new { supplierId = g.Key.SupplierId, name = g.Key.Name, amount = g.Sum(x => x.RemainingAmount), overdue = g.Count(x => x.DueDate < now) })
            .OrderByDescending(x => x.amount).Take(8).ToListAsync(ct);
        var recentInvoices = await db.Invoices.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.InvoiceDate).Take(12).Select(x => new { x.Number, x.Direction, x.PartyName, x.InvoiceDate, x.Subtotal, x.VatAmount, x.Total, x.Status, x.ConfidenceScore }).ToListAsync(ct);
        var recentOrders = await db.Orders.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(12).Select(x => new { x.Number, customer = x.Customer != null ? x.Customer.Name : null, x.Total, x.PaidAmount, x.Status, x.CreatedAt }).ToListAsync(ct);
        var lowStock = await db.Products.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive && x.Stock <= x.MinimumStock)
            .OrderBy(x => x.Stock).Take(10).Select(x => new { x.Sku, x.Name, x.Stock, x.MinimumStock }).ToListAsync(ct);
        var alerts = await db.AIAlerts.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Take(20)
            .Select(x => new { x.Type, x.Severity, x.Title, x.Message, x.EntityType, x.EntityId, x.IsRead, x.CreatedAt }).ToListAsync(ct);

        // Enrich AI alerts with the actual business entity so the assistant can answer
        // "which invoice/transaction?", "whose?", "how much?" instead of returning a generic alert.
        var alertedInvoiceIds = alerts.Where(x => x.EntityType == "Invoice" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();
        var alertedExpenseIds = alerts.Where(x => x.EntityType == "ExpenseTransaction" && x.EntityId.HasValue).Select(x => x.EntityId!.Value).Distinct().ToList();

        var alertedInvoices = await db.Invoices.AsNoTracking()
            .Where(x => x.CompanyId == companyId && alertedInvoiceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Number, x.PartyName, x.PartyTaxCode, x.InvoiceDate, x.Subtotal, x.VatAmount, x.Total, x.Status, x.Direction })
            .ToListAsync(ct);
        var alertedExpenses = await db.ExpenseTransactions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && alertedExpenseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Category, x.Description, x.Amount, x.VatAmount, x.TransactionDate, x.PaymentMethod, x.Reference })
            .ToListAsync(ct);

        var notableInvoiceAlerts = alerts.Where(a => a.EntityType == "Invoice").Select(a => new
        {
            alert = new { a.Type, a.Severity, a.Title, a.Message, a.CreatedAt, a.IsRead },
            invoice = a.EntityId.HasValue ? alertedInvoices.FirstOrDefault(i => i.Id == a.EntityId.Value) : null
        }).ToList();
        var notableExpenseAlerts = alerts.Where(a => a.EntityType == "ExpenseTransaction").Select(a => new
        {
            alert = new { a.Type, a.Severity, a.Title, a.Message, a.CreatedAt, a.IsRead },
            transaction = a.EntityId.HasValue ? alertedExpenses.FirstOrDefault(i => i.Id == a.EntityId.Value) : null
        }).ToList();

        // Large recent transactions are useful context, but they are explicitly NOT anomaly alerts.
        var largeRecentExpenses = await db.ExpenseTransactions.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Amount).Take(8)
            .Select(x => new { x.Id, x.Category, x.Description, x.Amount, x.VatAmount, x.TransactionDate, x.PaymentMethod, x.Reference, classification = "LARGE_TRANSACTION_NOT_AI_ANOMALY" })
            .ToListAsync(ct);

        var source = "Orders, Invoices, IncomeTransactions, ExpenseTransactions, Receivables, Payables, Products, AIAlerts";
        var snapshot = new
        {
            generatedAt = now,
            periods = new { today, monthStart, previousMonthStart, yearStart },
            metrics = new { revenueToday, revenueMonth, revenuePreviousMonth, revenueYear, expenseMonth, incomeMonth, netCashFlowMonth = incomeMonth - expenseMonth, operatingDifferenceMonth = revenueMonth - expenseMonth, receivable, payable, vatOutput, vatInput },
            topDebtors,
            topPayables,
            recentInvoices,
            recentOrders,
            lowStock,
            alerts,
            notableInvoiceAlerts,
            notableExpenseAlerts,
            largeRecentExpenses,
            source
        };

        var grounded = await groundedAssistant.AnswerAsync(request.Question.Trim(), snapshot, request.History, ct);
        if (string.IsNullOrWhiteSpace(grounded))
        {
            grounded = BuildAssistantFallback(request.Question, recentInvoices, notableInvoiceAlerts, notableExpenseAlerts, largeRecentExpenses, topDebtors, lowStock, revenueMonth, expenseMonth, incomeMonth, receivable, payable, vatOutput, vatInput);
        }

        return Results.Ok(new { answer = grounded, value = (decimal?)null, source, calculatedAt = now });
    }

    private static async Task<IResult> ForecastAsync(
        int? horizon,
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        FinancialMLService ml,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var days = horizon ?? 30;
        if (days is not (7 or 30 or 90)) return Results.BadRequest(new { message = "Horizon chỉ nhận 7, 30 hoặc 90 ngày." });

        var end = DateTime.UtcNow.Date;
        var start = end.AddDays(-89);
        var incomes = await db.IncomeTransactions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionDate >= start)
            .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);
        var expenses = await db.ExpenseTransactions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionDate >= start)
            .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);

        var history = Enumerable.Range(0, 90).Select(i => start.AddDays(i)).Select(date => new MlDailyCashPoint(
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            incomes.Where(x => x.TransactionDate.Date == date).Sum(x => x.Amount),
            expenses.Where(x => x.TransactionDate.Date == date).Sum(x => x.Amount))).ToList();
        var activeDays = history.Count(x => x.Inflow > 0 || x.Outflow > 0);
        if (activeDays < 14)
            return Results.Ok(new { status = "Insufficient historical data", requiredActiveDays = 14, availableActiveDays = activeDays, horizon = days, series = Array.Empty<object>() });

        try
        {
            var prediction = await ml.ForecastAsync(history, days, ct);
            if (prediction is not null && prediction.Status == "ok" && prediction.Series.Count > 0)
                return Results.Ok(new
                {
                    status = "Forecast available", method = prediction.Method, historicalActiveDays = activeDays,
                    horizon = days, quality = prediction.Quality, disclaimer = "Dự báo ML tham khảo từ dữ liệu lịch sử; cần người dùng đánh giá trước khi ra quyết định tài chính.",
                    series = prediction.Series.Select(x => new { date = x.Date, projectedInflow = x.ProjectedInflow, projectedOutflow = x.ProjectedOutflow, projectedNet = x.ProjectedNet })
                });
        }
        catch { /* fallback below keeps the dashboard available */ }

        var recent = history.TakeLast(30).ToList();
        var avgIn = recent.Sum(x => x.Inflow) / Math.Max(1, recent.Count);
        var avgOut = recent.Sum(x => x.Outflow) / Math.Max(1, recent.Count);
        var series = Enumerable.Range(1, days).Select(i => new
        {
            date = end.AddDays(i), projectedInflow = decimal.Round(avgIn, 0), projectedOutflow = decimal.Round(avgOut, 0), projectedNet = decimal.Round(avgIn - avgOut, 0)
        });
        return Results.Ok(new
        {
            status = "Forecast available", method = "Fallback: 30-day simple daily average", historicalActiveDays = activeDays, horizon = days,
            disclaimer = "AI service không khả dụng nên hệ thống dùng phương pháp trung bình dự phòng.", series
        });
    }

    private static async Task<IResult> ScanAnomaliesAsync(
        HttpContext context,
        AppDbContext db,
        CompanyAccessService access,
        FinancialMLService ml,
        CancellationToken ct)
    {
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var since = DateTime.UtcNow.AddDays(-180);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Direction == "INCOMING" && x.InvoiceDate >= since)
            .Select(x => new { x.Id, x.Number, x.InvoiceDate, x.Total, x.VatAmount, x.Subtotal, x.SupplierId, SupplierName = x.Supplier != null ? x.Supplier.Name : x.PartyName })
            .OrderBy(x => x.InvoiceDate).ToListAsync(ct);

        var duplicateCreated = 0;
        var duplicateGroups = invoices.GroupBy(x => new { x.Number, x.SupplierId }).Where(g => !string.IsNullOrWhiteSpace(g.Key.Number) && g.Count() > 1);
        foreach (var group in duplicateGroups)
        {
            var first = group.First();
            if (await db.AIAlerts.AnyAsync(x => x.CompanyId == companyId && x.Type == "DUPLICATE_INVOICE" && x.EntityId == first.Id, ct)) continue;
            db.AIAlerts.Add(new AIAlert { CompanyId = companyId, Type = "DUPLICATE_INVOICE", Severity = "HIGH", Title = "Hóa đơn có dấu hiệu trùng", Message = $"Số hóa đơn {group.Key.Number} xuất hiện {group.Count()} lần cho cùng nhà cung cấp.", EntityType = "Invoice", EntityId = first.Id });
            duplicateCreated++;
        }

        var records = new List<MlAnomalyRecord>();
        foreach (var invoice in invoices)
        {
            var supplierHistory = invoices.Where(x => x.SupplierId == invoice.SupplierId && x.InvoiceDate < invoice.InvoiceDate).ToList();
            var supplierAverage = supplierHistory.Count == 0 ? invoice.Total : supplierHistory.Average(x => x.Total);
            var lastDate = supplierHistory.Count == 0 ? invoice.InvoiceDate : supplierHistory.Max(x => x.InvoiceDate);
            var vatRate = invoice.Subtotal > 0 ? decimal.Round(invoice.VatAmount / invoice.Subtotal * 100m, 2) : 0;
            records.Add(new MlAnomalyRecord(invoice.Id.ToString(), invoice.Total, vatRate, supplierHistory.Count,
                supplierAverage > 0 ? invoice.Total / supplierAverage : 1m,
                supplierHistory.Count == 0 ? 365 : (decimal)(invoice.InvoiceDate - lastDate).TotalDays,
                supplierHistory.Count / 6m));
        }

        var mlCreated = 0;
        var scanStatus = "ok";
        string method = "IsolationForest";
        try
        {
            var result = await ml.DetectAnomaliesAsync(records, ct);
            if (result is null) throw new HttpRequestException("ML service unavailable.");
            if (result is not null)
            {
                scanStatus = result.Status;
                method = result.Method;
                foreach (var hit in result.Results.Where(x => x.IsAnomaly || x.RiskScore >= 70).OrderByDescending(x => x.RiskScore).Take(20))
                {
                    if (!Guid.TryParse(hit.Id, out var invoiceId)) continue;
                    if (await db.AIAlerts.AnyAsync(x => x.CompanyId == companyId && x.Type == "ML_INVOICE_ANOMALY" && x.EntityId == invoiceId, ct)) continue;
                    var inv = invoices.First(x => x.Id == invoiceId);
                    var reason = hit.Reasons.Count > 0 ? string.Join(" ", hit.Reasons) : "Mẫu giao dịch khác biệt đáng kể so với lịch sử.";
                    db.AIAlerts.Add(new AIAlert { CompanyId = companyId, Type = "ML_INVOICE_ANOMALY", Severity = hit.Level, Title = $"AI phát hiện hóa đơn rủi ro ({hit.RiskScore}/100)", Message = $"{inv.SupplierName} · HĐ {inv.Number} · {Money(inv.Total)}. {reason}", EntityType = "Invoice", EntityId = invoiceId });
                    mlCreated++;
                }
            }
        }
        catch
        {
            method = "Rule fallback";
            var average = invoices.Count == 0 ? 0 : invoices.Average(x => x.Total);
            foreach (var inv in invoices.Where(x => average > 0 && x.Total >= average * 3).Take(10))
            {
                if (await db.AIAlerts.AnyAsync(x => x.CompanyId == companyId && x.Type == "LARGE_INVOICE" && x.EntityId == inv.Id, ct)) continue;
                db.AIAlerts.Add(new AIAlert { CompanyId = companyId, Type = "LARGE_INVOICE", Severity = "HIGH", Title = "Hóa đơn đầu vào lớn bất thường", Message = $"HĐ {inv.Number}: {Money(inv.Total)}, cao hơn ít nhất 3 lần trung bình.", EntityType = "Invoice", EntityId = inv.Id });
                mlCreated++;
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { scannedAt = DateTime.UtcNow, status = scanStatus, method, invoiceRecords = invoices.Count, newAlerts = duplicateCreated + mlCreated, duplicateAlerts = duplicateCreated, mlAlerts = mlCreated });
    }

    private static string BuildAssistantFallback(
        string question,
        dynamic recentInvoices,
        dynamic notableInvoiceAlerts,
        dynamic notableExpenseAlerts,
        dynamic largeRecentExpenses,
        dynamic topDebtors,
        dynamic lowStock,
        decimal revenueMonth,
        decimal expenseMonth,
        decimal incomeMonth,
        decimal receivable,
        decimal payable,
        decimal vatOutput,
        decimal vatInput)
    {
        var q = Normalize(question);

        if (q.Contains("bat thuong") || q.Contains("dang chu y") || q.Contains("rui ro") || q.Contains("canh bao"))
        {
            var lines = new List<string>();
            foreach (var item in notableInvoiceAlerts)
            {
                if (item.invoice is null) continue;
                lines.Add($"• Hóa đơn {item.invoice.Number} — {item.invoice.PartyName} — {Money((decimal)item.invoice.Total)} — ngày {((DateTime)item.invoice.InvoiceDate):dd/MM/yyyy} — mức {item.alert.Severity}. Lý do: {item.alert.Message}");
            }
            foreach (var item in notableExpenseAlerts)
            {
                if (item.transaction is null) continue;
                var label = string.IsNullOrWhiteSpace((string)item.transaction.Reference) ? (string)item.transaction.Category : (string)item.transaction.Reference;
                lines.Add($"• Giao dịch {label} — {item.transaction.Description} — {Money((decimal)item.transaction.Amount)} — ngày {((DateTime)item.transaction.TransactionDate):dd/MM/yyyy} — mức {item.alert.Severity}. Lý do: {item.alert.Message}");
            }
            if (lines.Count > 0)
                return $"Có {lines.Count} hóa đơn/giao dịch đang được AIAlerts đánh dấu đáng chú ý:\n{string.Join("\n", lines.Take(8))}";

            return "Hiện chưa có hóa đơn hoặc giao dịch nào được AIAlerts đánh dấu bất thường trong dữ liệu của công ty. Các giao dịch có giá trị lớn không tự động được xem là bất thường nếu chưa có cảnh báo AI.";
        }

        if (q.Contains("hoa don") && (q.Contains("moi nhat") || q.Contains("gan nhat")))
        {
            foreach (var inv in recentInvoices)
                return $"Hóa đơn mới nhất là {inv.Number} của {inv.PartyName}, ngày {((DateTime)inv.InvoiceDate):dd/MM/yyyy}, tổng tiền {Money((decimal)inv.Total)}, VAT {Money((decimal)inv.VatAmount)}, trạng thái {inv.Status}.";
            return "Chưa có hóa đơn nào trong dữ liệu hiện tại.";
        }

        if (q.Contains("no nhieu") || q.Contains("no lon") || q.Contains("khach hang nao no"))
        {
            foreach (var d in topDebtors)
                return $"Khách hàng đang có công nợ lớn nhất là {d.name}, còn phải thu {Money((decimal)d.amount)}; có {d.overdue} khoản quá hạn.";
            return "Hiện không có khoản phải thu khách hàng nào còn dư.";
        }

        if (q.Contains("sap het") || q.Contains("ton kho thap"))
        {
            var lines = new List<string>();
            foreach (var p in lowStock) lines.Add($"• {p.Sku} — {p.Name}: còn {p.Stock}, mức tối thiểu {p.MinimumStock}");
            return lines.Count == 0 ? "Hiện không có sản phẩm nào dưới mức tồn kho tối thiểu." : $"Các sản phẩm tồn kho thấp:\n{string.Join("\n", lines)}";
        }

        if (q.Contains("vat"))
            return $"VAT tháng này: đầu ra {Money(vatOutput)}, đầu vào {Money(vatInput)}, chênh lệch {Money(vatOutput - vatInput)}.";
        if (q.Contains("doanh thu")) return $"Doanh thu tháng này là {Money(revenueMonth)}.";
        if (q.Contains("chi phi")) return $"Chi phí tháng này là {Money(expenseMonth)}.";
        if (q.Contains("dong tien")) return $"Tháng này thu {Money(incomeMonth)}, chi {Money(expenseMonth)}, dòng tiền ròng {Money(incomeMonth - expenseMonth)}.";
        if (q.Contains("phai thu") || q.Contains("cong no")) return $"Tổng phải thu hiện tại {Money(receivable)}; tổng phải trả {Money(payable)}.";

        return $"Tôi chưa xác định được truy vấn phù hợp khi Gemini tạm thời không khả dụng. Dữ liệu hiện có gồm hóa đơn, giao dịch thu/chi, công nợ, VAT, tồn kho và AIAlerts. Bạn có thể hỏi cụ thể như: ‘Có hóa đơn nào bất thường?’, ‘Hóa đơn mới nhất?’, ‘Ai đang nợ nhiều nhất?’.";
    }

    private static object MetricAnswer(string question, string answer, decimal value, DateTime from, DateTime to, string source) =>
        new { question, answer, value, period = new { from, to }, source, calculatedAt = DateTime.UtcNow };

    private static string Money(decimal value) => string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0} ₫", value);

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC).Replace('đ', 'd');
    }

    private static bool MatchesFileSignature(string extension, byte[] bytes) => extension switch
    {
        ".pdf" => bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46,
        ".png" => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
        ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        _ => false
    };
}
