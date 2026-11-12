using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Contracts;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using TaxFlow.Api.Services;

namespace TaxFlow.Api.Endpoints;

public static class AuthAndCompanyEndpoints
{
    public static void MapAuthAndCompanyEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentication");

        auth.MapPost("/login", async (
            LoginRequest request,
            AppDbContext db,
            PasswordHasher<AppUser> hasher,
            JwtService jwt,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.Unauthorized();
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users
                .Include(x => x.Role)
                .Include(x => x.CompanyUsers).ThenInclude(x => x.Company)
                .FirstOrDefaultAsync(x => x.Email == email, ct);
            if (user is null || !user.IsActive ||
                hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
                return Results.Unauthorized();

            return Results.Ok(ToAuthResponse(user, jwt.CreateToken(user)));
        }).AllowAnonymous().RequireRateLimiting("login");

        auth.MapGet("/me", async (HttpContext context, ClaimsPrincipal principal, AppDbContext db, JwtService jwt, CancellationToken ct) =>
        {
            var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(rawId, out var userId)) return Results.Unauthorized();
            var user = await db.Users
                .Include(x => x.Role)
                .Include(x => x.CompanyUsers).ThenInclude(x => x.Company)
                .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(ToAuthResponse(user, jwt.CreateToken(user), Guid.TryParse(context.Request.Headers["X-Company-Id"], out var selected) ? selected : null));
        }).RequireAuthorization();

        var companies = app.MapGroup("/api/companies").RequireAuthorization().WithTags("Companies");
        companies.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = await db.CompanyUsers.AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => new
                {
                    x.Company.Id, x.Company.TaxCode, x.Company.Name, x.Company.Address,
                    x.Company.Phone, x.Company.Email, x.Company.Representative,
                    x.Company.BusinessType, x.Company.TaxMethod, x.Company.TaxFilingPeriod
                }).ToListAsync(ct);
            return Results.Ok(list);
        });
        companies.MapPost("/", async (
            CompanyRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyError = ValidateCompany(request);
            if (companyError is not null) return Results.BadRequest(new { message = companyError });
            if (await db.Companies.AnyAsync(x => x.TaxCode == request.TaxCode.Trim(), ct))
                return Results.Conflict(new { message = "Mã số thuế đã tồn tại." });
            var company = new Company
            {
                TaxCode = request.TaxCode.Trim(), Name = request.Name.Trim(), Address = request.Address.Trim(),
                Phone = request.Phone.Trim(), Email = request.Email.Trim(), Representative = request.Representative.Trim(),
                BusinessType = request.BusinessType.Trim(), TaxMethod = request.TaxMethod.Trim(),
                TaxFilingPeriod = request.TaxFilingPeriod.Trim()
            };
            db.Companies.Add(company);
            db.CompanyUsers.Add(new CompanyUser { Company = company, UserId = access.GetUserId(context.User), RoleName = Roles.Admin });
            db.Warehouses.Add(new Warehouse { CompanyId = company.Id, Code = "KHO-CHINH", Name = "Kho chính", Address = company.Address });
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, company.Id, "CREATE", "Company", company.Id, null, request, ct);
            return Results.Created($"/api/companies/{company.Id}", company);
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        companies.MapPut("/{id:guid}", async (
            Guid id,
            CompanyRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyError = ValidateCompany(request);
            if (companyError is not null) return Results.BadRequest(new { message = companyError });
            var allowedCompany = await access.ResolveCompanyIdAsync(context, ct);
            if (id != allowedCompany) return Results.Forbid();
            var company = await db.Companies.FindAsync(new object[] { id }, ct);
            if (company is null) return Results.NotFound();
            var taxCode = request.TaxCode.Trim();
            if (await db.Companies.AnyAsync(x => x.Id != id && x.TaxCode == taxCode, ct))
                return Results.Conflict(new { message = "Mã số thuế đã tồn tại." });
            var old = new { company.TaxCode, company.Name, company.Address, company.Phone, company.Email };
            company.TaxCode = taxCode; company.Name = request.Name.Trim();
            company.Address = request.Address.Trim(); company.Phone = request.Phone.Trim();
            company.Email = request.Email.Trim(); company.Representative = request.Representative.Trim();
            company.BusinessType = request.BusinessType.Trim(); company.TaxMethod = request.TaxMethod.Trim();
            company.TaxFilingPeriod = request.TaxFilingPeriod.Trim(); company.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, id, "UPDATE", "Company", id, old, request, ct);
            return Results.Ok(company);
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        var bankAccounts = app.MapGroup("/api/bank-accounts")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Director))
            .WithTags("Bank accounts");
        bankAccounts.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(await db.BankAccounts.AsNoTracking().Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.BankName).ToListAsync(ct));
        });
        bankAccounts.MapPost("/", async (
            BankAccountRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (string.IsNullOrWhiteSpace(request.BankName) || string.IsNullOrWhiteSpace(request.AccountNumber) || string.IsNullOrWhiteSpace(request.AccountName))
                return Results.BadRequest(new { message = "Ngân hàng, số tài khoản và tên tài khoản là bắt buộc." });
            var accountNumber = request.AccountNumber.Trim();
            if (await db.BankAccounts.AnyAsync(x => x.CompanyId == companyId && x.AccountNumber == accountNumber && x.IsActive, ct))
                return Results.Conflict(new { message = "Số tài khoản đã tồn tại." });
            var account = new BankAccount
            {
                CompanyId = companyId, BankName = request.BankName.Trim(), AccountNumber = accountNumber,
                AccountName = request.AccountName.Trim(), OpeningBalance = request.OpeningBalance
            };
            db.BankAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CREATE", "BankAccount", account.Id, null, request, ct);
            return Results.Created($"/api/bank-accounts/{account.Id}", account);
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant));

        MapPartyEndpoints(app, "customers", true);
        MapPartyEndpoints(app, "suppliers", false);

        var users = app.MapGroup("/api/users").RequireAuthorization(policy => policy.RequireRole(Roles.Admin)).WithTags("Users & roles");
        users.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            return Results.Ok(await db.CompanyUsers.AsNoTracking().Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.User.FullName).Select(x => new
                {
                    x.User.Id, x.User.Email, x.User.FullName, role = x.RoleName, isActive = x.IsActive, x.User.CreatedAt
                }).ToListAsync(ct));
        });
        users.MapPost("/", async (
            UserRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            PasswordHasher<AppUser> hasher,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.Role))
                return Results.BadRequest(new { message = "Email, họ tên, mật khẩu và vai trò là bắt buộc." });
            var roleName = request.Role.Trim().ToUpperInvariant();
            var role = await db.Roles.FirstOrDefaultAsync(x => x.Name == roleName, ct);
            if (role is null) return Results.BadRequest(new { message = "Vai trò không hợp lệ." });
            if (request.Password.Length < 8) return Results.BadRequest(new { message = "Mật khẩu phải có ít nhất 8 ký tự." });
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
            if (user is not null)
                return Results.Conflict(new { message = "Email đã tồn tại. Hãy gửi lời mời; người nhận phải đăng nhập và chấp nhận." });
            user = new AppUser { Email = email, FullName = request.FullName.Trim(), Role = role, IsActive = true };
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            db.Users.Add(user);
            db.CompanyUsers.Add(new CompanyUser { CompanyId = companyId, User = user, RoleName = roleName });
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CREATE", "User", user.Id, null, new { user.Email, user.FullName, Role = roleName }, ct);
            return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Email, user.FullName, role = roleName, user.IsActive });
        });
        users.MapPut("/{id:guid}", async (
            Guid id,
            UserUpdateRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Role))
                return Results.BadRequest(new { message = "Họ tên và vai trò là bắt buộc." });
            var membership = await db.CompanyUsers.Include(x => x.User).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == id, ct);
            if (membership is null) return Results.NotFound();
            var roleName = request.Role.Trim().ToUpperInvariant();
            if (!await db.Roles.AnyAsync(x => x.Name == roleName, ct)) return Results.BadRequest(new { message = "Vai trò không hợp lệ." });
            if (id == access.GetUserId(context.User) && (!request.IsActive || roleName != Roles.Admin))
                return Results.BadRequest(new { message = "Không thể tự khóa hoặc tự bỏ quyền quản trị." });
            var old = new { membership.RoleName, membership.IsActive };
            membership.RoleName = roleName; membership.IsActive = request.IsActive;
            // Profile and password belong to the account owner, not another tenant's admin.
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "UPDATE_MEMBERSHIP", "CompanyUser", membership.Id, old, new { roleName, membership.IsActive }, ct);
            return Results.Ok(new { membership.User.Id, membership.User.Email, membership.User.FullName, role = roleName, isActive = membership.IsActive });
        });
        users.MapPost("/{id:guid}/reset-password", () => Results.BadRequest(new { message = "Admin doanh nghiệp không được đổi mật khẩu tài khoản chung. Chủ tài khoản dùng chức năng Đổi mật khẩu." }));
        app.MapIdentityEndpoints();

    }

    private static void MapPartyEndpoints(WebApplication app, string route, bool customer)
    {
        var group = app.MapGroup($"/api/{route}")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Employee, Roles.Director))
            .WithTags(customer ? "Customers" : "Suppliers");
        group.MapGet("/", async (HttpContext context, AppDbContext db, CompanyAccessService access, CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (customer)
                return Results.Ok(await db.Customers.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(ct));
            return Results.Ok(await db.Suppliers.AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Name).ToListAsync(ct));
        });
        group.MapPost("/", async (
            PartyRequest request,
            HttpContext context,
            AppDbContext db,
            CompanyAccessService access,
            AuditService audit,
            CancellationToken ct) =>
        {
            var companyId = await access.ResolveCompanyIdAsync(context, ct);
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { message = "Mã và tên đối tác là bắt buộc." });
            var code = request.Code.Trim();
            var duplicated = customer
                ? await db.Customers.AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct)
                : await db.Suppliers.AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct);
            if (duplicated) return Results.Conflict(new { message = "Mã đối tác đã tồn tại." });
            BaseEntity entity;
            if (customer)
            {
                entity = new Customer
                {
                    CompanyId = companyId, Code = code, Name = request.Name.Trim(), TaxCode = request.TaxCode?.Trim(),
                    Address = request.Address?.Trim(), Phone = request.Phone?.Trim(), Email = request.Email?.Trim()
                };
                db.Customers.Add((Customer)entity);
            }
            else
            {
                entity = new Supplier
                {
                    CompanyId = companyId, Code = code, Name = request.Name.Trim(), TaxCode = request.TaxCode?.Trim(),
                    Address = request.Address?.Trim(), Phone = request.Phone?.Trim(), Email = request.Email?.Trim()
                };
                db.Suppliers.Add((Supplier)entity);
            }
            await db.SaveChangesAsync(ct);
            await audit.LogAsync(context, companyId, "CREATE", customer ? "Customer" : "Supplier", entity.Id, null, request, ct);
            return Results.Created($"/api/{route}/{entity.Id}", entity);
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin, Roles.Accountant, Roles.Employee));
    }

    private static string? ValidateCompany(CompanyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaxCode) || string.IsNullOrWhiteSpace(request.Name))
            return "Mã số thuế và tên doanh nghiệp là bắt buộc.";
        if (request.TaxCode.Trim().Length is < 10 or > 14)
            return "Mã số thuế phải có từ 10 đến 14 ký tự.";
        if (string.IsNullOrWhiteSpace(request.Address) || string.IsNullOrWhiteSpace(request.Representative))
            return "Địa chỉ và người đại diện là bắt buộc.";
        return null;
    }

    private static object ToAuthResponse(AppUser user, string token, Guid? selectedCompanyId = null)
    {
        var memberships = user.CompanyUsers.Where(x => x.IsActive).OrderBy(x => x.CreatedAt).ToList();
        var selected = memberships.FirstOrDefault(x => x.CompanyId == selectedCompanyId) ?? memberships.FirstOrDefault();
        return new
        {
            accessToken = token, tokenType = "Bearer", expiresIn = 28_800,
            user = new { user.Id, user.Email, user.FullName, role = selected?.RoleName ?? Roles.Employee },
            companies = memberships.Select(x => new { x.Company.Id, x.Company.Name, x.Company.TaxCode, role = x.RoleName }),
            defaultCompanyId = selected?.CompanyId ?? Guid.Empty
        };
    }
}
