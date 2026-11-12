using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;
public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/logout", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync(new object[] { access.GetUserId(context.User) }, ct);
            if (user is null) return Results.Unauthorized();
            user.AuthVersion++; await db.SaveChangesAsync(ct); return Results.NoContent();
        }).RequireAuthorization();
        app.MapPost("/api/auth/change-password", async (ChangePasswordRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, PasswordHasher<AppUser> hasher, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync(new object[] { access.GetUserId(context.User) }, ct);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed) return Results.BadRequest(new { message = "Mật khẩu hiện tại không đúng." });
            if (request.NewPassword.Length < 12) return Results.BadRequest(new { message = "Mật khẩu mới phải có ít nhất 12 ký tự." });
            user.PasswordHash = hasher.HashPassword(user, request.NewPassword); user.AuthVersion++;
            await db.SaveChangesAsync(ct); return Results.Ok(new { message = "Đã đổi mật khẩu và thu hồi các phiên. Hãy đăng nhập lại." });
        }).RequireAuthorization().RequireRateLimiting("login");
        app.MapPost("/api/users/invitations", async (InvitationRequest request, HttpContext context, AppDbContext db, CompanyAccessService access, AuditService audit, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            var email = request.Email.Trim().ToLowerInvariant(); var role = request.Role.Trim().ToUpperInvariant();
            if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || !await db.Roles.AnyAsync(x => x.Name == role, ct)) return Results.BadRequest(new { message = "Email hoặc vai trò không hợp lệ." });
            if (await db.CompanyInvitations.AnyAsync(x => x.CompanyId == companyId && x.Email == email && x.Status == "PENDING" && x.ExpiresAt > DateTime.UtcNow, ct)) return Results.Conflict(new { message = "Đã có lời mời còn hiệu lực." });
            var invitation = new CompanyInvitation { CompanyId = companyId, Email = email, RoleName = role, InvitedByUserId = access.GetUserId(context.User), ExpiresAt = DateTime.UtcNow.AddDays(7) };
            db.CompanyInvitations.Add(invitation); await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "INVITE", "CompanyInvitation", invitation.Id, null, new { email, role }, ct);
            return Results.Ok(new { invitation.Id, invitation.Email, invitation.RoleName, invitation.ExpiresAt, message = "Lời mời hiển thị trong tài khoản người nhận. Chưa gửi email." });
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin));
        app.MapGet("/api/auth/invitations", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var email = user.FindFirstValue(ClaimTypes.Email);
            return Results.Ok(await db.CompanyInvitations.AsNoTracking().Where(x => x.Email == email && x.Status == "PENDING" && x.ExpiresAt > DateTime.UtcNow)
                .Select(x => new { x.Id, x.RoleName, x.ExpiresAt, company = db.Companies.Where(c => c.Id == x.CompanyId).Select(c => c.Name).FirstOrDefault() }).ToListAsync(ct));
        }).RequireAuthorization();
        app.MapPost("/api/auth/invitations/{id:guid}/accept", async (Guid id, HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email);
            var invitation = await db.CompanyInvitations.FirstOrDefaultAsync(x => x.Id == id && x.Email == email && x.Status == "PENDING" && x.ExpiresAt > DateTime.UtcNow, ct);
            if (invitation is null) return Results.NotFound();
            var userId = access.GetUserId(context.User);
            if (await db.CompanyUsers.AnyAsync(x => x.CompanyId == invitation.CompanyId && x.UserId == userId, ct)) return Results.Conflict(new { message = "Đã có tư cách thành viên. Admin cần cập nhật thành viên hiện tại." });
            db.CompanyUsers.Add(new CompanyUser { CompanyId = invitation.CompanyId, UserId = userId, RoleName = invitation.RoleName });
            invitation.Status = "ACCEPTED"; await db.SaveChangesAsync(ct);
            return Results.Ok(new { invitation.CompanyId, message = "Đã chấp nhận. Tải lại trang để chọn doanh nghiệp." });
        }).RequireAuthorization();
    }
}
