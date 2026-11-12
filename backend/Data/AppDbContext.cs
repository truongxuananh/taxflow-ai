using Microsoft.EntityFrameworkCore;
using TaxFlow.Api.Models;

namespace TaxFlow.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyUser> CompanyUsers => Set<CompanyUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<IncomeTransaction> IncomeTransactions => Set<IncomeTransaction>();
    public DbSet<ExpenseTransaction> ExpenseTransactions => Set<ExpenseTransaction>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<Payable> Payables => Set<Payable>();
    public DbSet<TaxRecord> TaxRecords => Set<TaxRecord>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AIAnalysis> AIAnalyses => Set<AIAnalysis>();
    public DbSet<OCRCorrection> OCRCorrections => Set<OCRCorrection>();
    public DbSet<AIAlert> AIAlerts => Set<AIAlert>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<CompanyInvitation> CompanyInvitations => Set<CompanyInvitation>();
    public DbSet<OperationReceipt> OperationReceipts => Set<OperationReceipt>();
    public DbSet<OrderReturn> OrderReturns => Set<OrderReturn>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>().Where(x => x.State == EntityState.Modified))
        {
            entry.Entity.Revision++;
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OperationReceipt>().HasIndex(x => new { x.CompanyId, x.UserId, x.Key }).IsUnique();
        modelBuilder.Entity<OrderReturn>().HasIndex(x => x.OrderId).IsUnique();
        modelBuilder.Entity<ChatMessage>().HasIndex(x => new { x.CompanyId, x.UserId, x.CreatedAt });
        modelBuilder.Entity<CompanyInvitation>().HasIndex(x => new { x.CompanyId, x.Email, x.Status });

        modelBuilder.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<Company>().HasIndex(x => x.TaxCode).IsUnique();
        modelBuilder.Entity<CompanyUser>().HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        modelBuilder.Entity<Supplier>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        modelBuilder.Entity<Product>().HasIndex(x => new { x.CompanyId, x.Sku }).IsUnique();
        modelBuilder.Entity<Product>().HasIndex(x => new { x.CompanyId, x.Barcode }).IsUnique();
        modelBuilder.Entity<Order>().HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.CompanyId, x.Direction, x.Number, x.Serial });

        modelBuilder.Entity<AppUser>()
            .HasOne(x => x.Role).WithMany(x => x.Users).HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CompanyUser>()
            .HasOne(x => x.Company).WithMany(x => x.CompanyUsers).HasForeignKey(x => x.CompanyId);
        modelBuilder.Entity<CompanyUser>()
            .HasOne(x => x.User).WithMany(x => x.CompanyUsers).HasForeignKey(x => x.UserId);

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            if (!foreignKey.IsOwnership && foreignKey.DeleteBehavior == DeleteBehavior.Cascade &&
                foreignKey.DeclaringEntityType.ClrType != typeof(CompanyUser) &&
                foreignKey.DeclaringEntityType.ClrType != typeof(OrderItem) &&
                foreignKey.DeclaringEntityType.ClrType != typeof(InvoiceItem))
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(2);
        }
    }
}
