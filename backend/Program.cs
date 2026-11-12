using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TaxFlow.Api.Data;
using TaxFlow.Api.Endpoints;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0)), mysql =>
        mysql.EnableRetryOnFailure(6, TimeSpan.FromSeconds(5), null)));

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "";
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must contain at least 32 characters.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userId, out var id)) { context.Fail("Invalid account."); return; }
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted);
                if (user is null || !user.IsActive || context.Principal?.FindFirstValue("auth_version") != user.AuthVersion.ToString())
                { context.Fail("Session revoked. Sign in again."); return; }
                var memberships = await db.CompanyUsers.AsNoTracking().Where(x => x.UserId == id && x.IsActive).OrderBy(x => x.CreatedAt).ToListAsync(context.HttpContext.RequestAborted);
                var raw = context.Request.Headers["X-Company-Id"].FirstOrDefault();
                var membership = string.IsNullOrWhiteSpace(raw) ? memberships.FirstOrDefault() : Guid.TryParse(raw, out var companyId) ? memberships.FirstOrDefault(x => x.CompanyId == companyId) : null;
                // /auth endpoints remain usable to accept invitations when there is no active membership.
                if (membership is null && !context.Request.Path.StartsWithSegments("/api/auth")) { context.Fail("No active company membership."); return; }
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    foreach (var claim in identity.FindAll(ClaimTypes.Role).ToArray()) identity.RemoveClaim(claim);
                    if (membership is not null) identity.AddClaim(new Claim(ClaimTypes.Role, membership.RoleName));
                }
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TaxFlow AI API",
        Version = "v1",
        Description = "Sales, invoice, cash-flow and tax-management API. AI figures require human review."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
        }] = Array.Empty<string>()
    });
});

builder.Services.AddScoped<PasswordHasher<AppUser>>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<CompanyAccessService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<InvoiceValidationService>();
builder.Services.AddScoped<SupplierMemoryService>();
builder.Services.AddScoped<DemoAIProvider>();
builder.Services.AddScoped<PaddleOCRProvider>();
builder.Services.AddScoped<TesseractOCRProvider>();
builder.Services.AddHttpClient<OpenAIProvider>(client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddHttpClient<GeminiAIProvider>(client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddHttpClient<LocalAIProvider>(client => client.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient<FinancialMLService>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddHttpClient<GroundedFinancialAssistant>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<HybridAIProvider>();
builder.Services.AddScoped<AIProviderResolver>();
builder.Services.AddHttpClient<GatewayInvoiceProvider>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<IInvoiceProvider>(sp => string.Equals(builder.Configuration["INVOICE_PROVIDER"], "gateway", StringComparison.OrdinalIgnoreCase)
    ? sp.GetRequiredService<GatewayInvoiceProvider>() : new DemoInvoiceProvider());
builder.Services.AddSingleton<ExportService>();
builder.Services.AddScoped<FinancialSummaryService>();
builder.Services.AddScoped<FinancialAssistantService>();
builder.Services.AddHttpClient<GeminiFunctionPlanner>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<GeminiBusinessTools>();
builder.Services.AddScoped<NaturalAssistantService>();
builder.Services.AddScoped<ReceivableConversationService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is not null)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalException");
        logger.LogError(exception, "Unhandled request error {Method} {Path} TraceId={TraceId}",
            context.Request.Method, context.Request.Path, context.TraceIdentifier);
    }
    var (status, title) = exception switch
    {
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Không có quyền truy cập"),
        KeyNotFoundException => (StatusCodes.Status404NotFound, "Không tìm thấy dữ liệu"),
        InvalidOperationException => (StatusCodes.Status400BadRequest, "Yêu cầu không hợp lệ"),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Dữ liệu vừa được thay đổi, hãy tải lại và thử lại"),
        DbUpdateException => (StatusCodes.Status409Conflict, "Dữ liệu bị xung đột"),
        _ => (StatusCodes.Status500InternalServerError, "Lỗi hệ thống")
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new
    {
        type = $"https://httpstatuses.com/{status}",
        title,
        status,
        detail = status == 500 ? "Không thể xử lý yêu cầu. Vui lòng thử lại." : exception?.Message,
        traceId = context.TraceIdentifier
    });
}));

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    var database = await db.Database.CanConnectAsync(ct);
    return database
        ? Results.Ok(new { status = "healthy", database = "healthy", timestamp = DateTime.UtcNow })
        : Results.Json(new { status = "unhealthy", database = "unhealthy" }, statusCode: 503);
}).AllowAnonymous().WithTags("System");

app.MapTaxFlowEndpoints();
app.MapCompletionEndpoints();
app.MapBrowseEndpoints();
app.MapBankCompletionEndpoints();
app.MapDocumentCompletionEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");
    if (app.Environment.IsEnvironment("Testing")) await db.Database.EnsureCreatedAsync();
    else await DbInitializer.InitializeAsync(db, logger);
}

app.Run();

public partial class Program { }
