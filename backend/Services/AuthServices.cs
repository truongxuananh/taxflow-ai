using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Services;

public sealed class JwtService(IConfiguration configuration)
{
    public string CreateToken(AppUser user)
    {
        var secret = configuration["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret is missing.");
        if (secret.Length < 32)
            throw new InvalidOperationException("JWT secret must contain at least 32 characters.");

        var expiry = configuration.GetValue("Jwt:ExpiryMinutes", 480);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("auth_version", user.AuthVersion.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.Name)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiry),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class CompanyAccessService(AppDbContext db)
{
    public Guid GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? throw new UnauthorizedAccessException("Missing user identifier.");
        return Guid.Parse(raw);
    }

    public async Task<Guid> ResolveCompanyIdAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(context.User);
        Guid? requested = null;
        if (context.Request.Headers.TryGetValue("X-Company-Id", out var header) &&
            Guid.TryParse(header.FirstOrDefault(), out var parsed))
            requested = parsed;

        var query = db.CompanyUsers.AsNoTracking().Where(x => x.UserId == userId && x.IsActive && x.User.IsActive);
        if (requested.HasValue)
        {
            if (!await query.AnyAsync(x => x.CompanyId == requested.Value, cancellationToken))
                throw new UnauthorizedAccessException("Bạn không có quyền truy cập doanh nghiệp này.");
            return requested.Value;
        }

        var companyId = await query.Select(x => x.CompanyId).FirstOrDefaultAsync(cancellationToken);
        if (companyId == Guid.Empty)
            throw new UnauthorizedAccessException("Tài khoản chưa được gán doanh nghiệp.");
        return companyId;
    }
}

public sealed class AuditService(AppDbContext db, CompanyAccessService companyAccess)
{
    public async Task LogAsync(
        HttpContext context,
        Guid companyId,
        string action,
        string entity,
        object? entityId,
        object? oldValue,
        object? newValue,
        CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = companyAccess.GetUserId(context.User),
            UserEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            Action = action,
            Entity = entity,
            EntityId = entityId?.ToString(),
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue),
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
