using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;

/// <summary>One serializable transaction for each money/stock command, its audit trail and replay receipt.</summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public static bool IsFinancialMutation(string method, string path) => method == "POST" &&
        (path == "/api/bank-transactions" || path.EndsWith("/match", StringComparison.Ordinal) || path == "/api/orders" || path == "/api/inventory/transactions" || path == "/api/incomes" || path == "/api/expenses" ||
         path.EndsWith("/settle", StringComparison.Ordinal) || path.EndsWith("/return", StringComparison.Ordinal) ||
         path.StartsWith("/api/documents/", StringComparison.Ordinal) && path.EndsWith("/confirm", StringComparison.Ordinal));

    public async Task InvokeAsync(HttpContext context, AppDbContext db, CompanyAccessService access)
    {
        if (!IsFinancialMutation(context.Request.Method, context.Request.Path.Value?.TrimEnd('/') ?? "") || context.User.Identity?.IsAuthenticated != true)
        { await next(context); return; }
        var ct = context.RequestAborted;
        var key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = "Gửi Idempotency-Key (tối đa 100 ký tự) để tránh ghi nhận tiền/kho lặp." }, ct); return; }
        var companyId = await access.ResolveCompanyIdAsync(context, ct);
        var userId = access.GetUserId(context.User);
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync(ct);
        context.Request.Body.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{context.Request.Method}|{context.Request.Path}|{context.Request.QueryString}|{requestBody}")));
        var originalBody = context.Response.Body;
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.Response.Body = originalBody;
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var receipt = await db.OperationReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Key == key, ct);
                if (receipt is not null)
                {
                    context.Response.StatusCode = receipt.RequestHash == hash ? receipt.StatusCode : 409;
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["Idempotent-Replayed"] = "true";
                    await context.Response.WriteAsync(receipt.RequestHash == hash ? receipt.ResponseJson : "{\"message\":\"Idempotency-Key đã được dùng cho yêu cầu khác.\"}", ct);
                    return;
                }
                await using var buffer = new MemoryStream();
                context.Response.Body = buffer;
                context.Response.StatusCode = 200;
                context.Request.Body.Position = 0;
                await next(context);
                buffer.Position = 0;
                var responseText = await new StreamReader(buffer, Encoding.UTF8, leaveOpen: true).ReadToEndAsync(ct);
                if (context.Response.StatusCode < 400)
                {
                    db.OperationReceipts.Add(new OperationReceipt { CompanyId = companyId, UserId = userId, Key = key, RequestHash = hash, ResponseJson = responseText, StatusCode = context.Response.StatusCode });
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                else await transaction.RollbackAsync(ct);
                context.Response.Body = originalBody;
                await context.Response.WriteAsync(responseText, ct);
            });
        }
        finally { context.Response.Body = originalBody; }
    }
}
