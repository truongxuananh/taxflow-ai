using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace TaxFlow.Api.Data;
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseMySql(Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Server=localhost;Database=taxflow;User=taxflow;Password=design-only;", new MySqlServerVersion(new Version(8, 4, 0)));
        return new AppDbContext(options.Options);
    }
}
