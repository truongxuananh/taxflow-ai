using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaxFlow.Api.Data;
using TaxFlow.Api.Models;
using Xunit;
namespace TaxFlow.Tests;
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION")!)) Skip = "Requires a dedicated disposable MySQL server via TEST_MYSQL_CONNECTION."; }
}
public sealed class MySqlMigrationTests
{
    [MySqlFact]
    public async Task UpgradePreservesLegacyMembershipRoleAndActivatesIt()
    {
        var cs = new MySqlConnector.MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION")!) { Database = "taxflow_migration_" + Guid.NewGuid().ToString("N") };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseMySql(cs.ConnectionString, new MySqlServerVersion(new Version(8,4,0))).Options;
        await using var db = new AppDbContext(options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260909150647_AddOcrCorrections");
            var role = Guid.NewGuid(); var user = Guid.NewGuid(); var company = Guid.NewGuid(); var membership = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Roles (Id,Name,CreatedAt,UpdatedAt) VALUES ({role},{"ACCOUNTANT"},{DateTime.UtcNow},{DateTime.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id,Email,PasswordHash,FullName,IsActive,RoleId,CreatedAt,UpdatedAt) VALUES ({user},{"legacy@example.com"},{"test-only"},{"Legacy"},{true},{role},{DateTime.UtcNow},{DateTime.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Companies (Id,TaxCode,Name,Address,Phone,Email,Representative,BusinessType,TaxMethod,TaxFilingPeriod,CreatedAt,UpdatedAt) VALUES ({company},{"0401234567"},{"Legacy company"},{""},{""},{""},{""},{""},{""},{""},{DateTime.UtcNow},{DateTime.UtcNow})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CompanyUsers (Id,CompanyId,UserId,CreatedAt,UpdatedAt) VALUES ({membership},{company},{user},{DateTime.UtcNow},{DateTime.UtcNow})");
            await migrator.MigrateAsync();
            var migrated = await db.CompanyUsers.AsNoTracking().SingleAsync(); Assert.True(migrated.IsActive); Assert.Equal(Roles.Accountant,migrated.RoleName);
            Assert.Equal(1,await db.Users.CountAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
