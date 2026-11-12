using System.ComponentModel.DataAnnotations;

namespace TaxFlow.Api.Models;

public abstract class BaseEntity
{
    [ConcurrencyCheck] public long Revision { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class Roles
{
    public const string Admin = "ADMIN";
    public const string Accountant = "ACCOUNTANT";
    public const string Employee = "EMPLOYEE";
    public const string Director = "DIRECTOR";
    public const string All = Admin + "," + Accountant + "," + Employee + "," + Director;
    public const string Finance = Admin + "," + Accountant + "," + Director;
}

public static class InvoiceStatuses
{
    public const string Draft = "Draft";
    public const string Pending = "Pending";
    public const string Issued = "Issued";
    public const string Cancelled = "Cancelled";
    public const string Adjusted = "Adjusted";
}

public sealed class Role : BaseEntity
{
    [MaxLength(40)] public string Name { get; set; } = string.Empty;
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}

public sealed class AppUser : BaseEntity
{
    public int AuthVersion { get; set; }
    [MaxLength(160)] public string Email { get; set; } = string.Empty;
    [MaxLength(255)] public string PasswordHash { get; set; } = string.Empty;
    [MaxLength(160)] public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;
    public ICollection<CompanyUser> CompanyUsers { get; set; } = new List<CompanyUser>();
}

public sealed class Company : BaseEntity
{
    [MaxLength(14)] public string TaxCode { get; set; } = string.Empty;
    [MaxLength(255)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string Address { get; set; } = string.Empty;
    [MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [MaxLength(160)] public string Email { get; set; } = string.Empty;
    [MaxLength(160)] public string Representative { get; set; } = string.Empty;
    [MaxLength(100)] public string BusinessType { get; set; } = string.Empty;
    [MaxLength(100)] public string TaxMethod { get; set; } = "Khấu trừ";
    [MaxLength(50)] public string TaxFilingPeriod { get; set; } = "Tháng";
    public ICollection<CompanyUser> CompanyUsers { get; set; } = new List<CompanyUser>();
}

public sealed class CompanyUser : BaseEntity
{
    [MaxLength(40)] public string RoleName { get; set; } = Roles.Employee;
    public bool IsActive { get; set; } = true;
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
}

public sealed class Customer : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(30)] public string Code { get; set; } = string.Empty;
    [MaxLength(255)] public string Name { get; set; } = string.Empty;
    [MaxLength(14)] public string? TaxCode { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(160)] public string? Email { get; set; }
}

public sealed class Supplier : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(30)] public string Code { get; set; } = string.Empty;
    [MaxLength(255)] public string Name { get; set; } = string.Empty;
    [MaxLength(14)] public string? TaxCode { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(160)] public string? Email { get; set; }
}

public sealed class Category : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public sealed class Product : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(50)] public string Sku { get; set; } = string.Empty;
    [MaxLength(80)] public string? Barcode { get; set; }
    [MaxLength(255)] public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    [MaxLength(30)] public string Unit { get; set; } = "Cái";
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal Stock { get; set; }
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Warehouse : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(30)] public string Code { get; set; } = string.Empty;
    [MaxLength(160)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
}

public sealed class StockTransaction : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    [MaxLength(20)] public string Type { get; set; } = "IN";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    [MaxLength(60)] public string? Reference { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public sealed class Order : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(40)] public string Number { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    [MaxLength(30)] public string PaymentMethod { get; set; } = "CASH";
    [MaxLength(30)] public string Status { get; set; } = "COMPLETED";
    public Guid? CreatedByUserId { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public sealed class OrderItem : BaseEntity
{
    public decimal UnitCost { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Invoice : BaseEntity
{
    public bool AccountingReviewed { get; set; }
    public Guid CompanyId { get; set; }
    [MaxLength(20)] public string Direction { get; set; } = "OUTGOING";
    [MaxLength(60)] public string Number { get; set; } = string.Empty;
    [MaxLength(40)] public string? Serial { get; set; }
    public DateTime InvoiceDate { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    [MaxLength(255)] public string PartyName { get; set; } = string.Empty;
    [MaxLength(14)] public string? PartyTaxCode { get; set; }
    [MaxLength(500)] public string? PartyAddress { get; set; }
    [MaxLength(160)] public string? PartyEmail { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    [MaxLength(30)] public string Status { get; set; } = InvoiceStatuses.Draft;
    [MaxLength(60)] public string Provider { get; set; } = "Demo";
    [MaxLength(160)] public string? ProviderReference { get; set; }
    public bool IsLegallyIssued { get; set; }
    public decimal? ConfidenceScore { get; set; }
    public Guid? SourceOrderId { get; set; }
    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}

public sealed class InvoiceItem : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    [MaxLength(255)] public string Description { get; set; } = string.Empty;
    [MaxLength(30)] public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
}

public sealed class Payment : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(30)] public string Method { get; set; } = "CASH";
    [MaxLength(60)] public string? Reference { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}

public sealed class IncomeTransaction : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(60)] public string Category { get; set; } = "Bán hàng";
    [MaxLength(500)] public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    [MaxLength(30)] public string PaymentMethod { get; set; } = "CASH";
    [MaxLength(60)] public string? Reference { get; set; }
}

public sealed class ExpenseTransaction : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(60)] public string Category { get; set; } = "Chi khác";
    [MaxLength(500)] public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    [MaxLength(30)] public string PaymentMethod { get; set; } = "TRANSFER";
    [MaxLength(60)] public string? Reference { get; set; }
}

public sealed class BankAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(100)] public string BankName { get; set; } = string.Empty;
    [MaxLength(50)] public string AccountNumber { get; set; } = string.Empty;
    [MaxLength(160)] public string AccountName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BankTransaction : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public BankAccount BankAccount { get; set; } = null!;
    [MaxLength(20)] public string Direction { get; set; } = "IN";
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string Description { get; set; } = string.Empty;
    [MaxLength(100)] public string? ExternalReference { get; set; }
    public Guid? MatchedPaymentId { get; set; }
}

public sealed class Receivable : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid? OrderId { get; set; }
    public Guid? InvoiceId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime DueDate { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "OPEN";
}

public sealed class Payable : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Guid? InvoiceId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime DueDate { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "OPEN";
}

public sealed class TaxRecord : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(30)] public string TaxType { get; set; } = "VAT";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal InputAmount { get; set; }
    public decimal OutputAmount { get; set; }
    public decimal Difference { get; set; }
    [MaxLength(40)] public string Status { get; set; } = "DRAFT";
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public sealed class Document : BaseEntity
{
    public Guid? ConfirmedInvoiceId { get; set; }
    public Guid CompanyId { get; set; }
    [MaxLength(255)] public string OriginalFileName { get; set; } = string.Empty;
    [MaxLength(255)] public string StoredFileName { get; set; } = string.Empty;
    [MaxLength(100)] public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    [MaxLength(40)] public string Status { get; set; } = "PENDING_REVIEW";
    public Guid UploadedByUserId { get; set; }
}

public sealed class Report : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(30)] public string PeriodType { get; set; } = "MONTHLY";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "DRAFT";
    public string DataJson { get; set; } = "{}";
}

public sealed class AIAnalysis : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? DocumentId { get; set; }
    [MaxLength(60)] public string AnalysisType { get; set; } = "INVOICE_OCR";
    [MaxLength(60)] public string Provider { get; set; } = "Demo";
    public decimal ConfidenceScore { get; set; }
    public string ResultJson { get; set; } = "{}";
    public string ValidationJson { get; set; } = "[]";
    [MaxLength(30)] public string Status { get; set; } = "REVIEW_REQUIRED";
}

public sealed class OCRCorrection : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid CorrectedByUserId { get; set; }
    [MaxLength(60)] public string Provider { get; set; } = string.Empty;
    public decimal OriginalConfidenceScore { get; set; }
    public string OriginalJson { get; set; } = "{}";
    public string CorrectedJson { get; set; } = "{}";
    public string ValidationJson { get; set; } = "[]";
}

public sealed class AIAlert : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(60)] public string Type { get; set; } = string.Empty;
    [MaxLength(20)] public string Severity { get; set; } = "MEDIUM";
    [MaxLength(255)] public string Title { get; set; } = string.Empty;
    [MaxLength(1000)] public string Message { get; set; } = string.Empty;
    [MaxLength(80)] public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public bool IsRead { get; set; }
}

public sealed class AuditLog : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(160)] public string UserEmail { get; set; } = string.Empty;
    [MaxLength(80)] public string Action { get; set; } = string.Empty;
    [MaxLength(80)] public string Entity { get; set; } = string.Empty;
    [MaxLength(80)] public string? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class CompanyInvitation : BaseEntity
{
    public Guid CompanyId { get; set; }
    [MaxLength(160)] public string Email { get; set; } = string.Empty;
    [MaxLength(40)] public string RoleName { get; set; } = Roles.Employee;
    public Guid InvitedByUserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "PENDING";
}
public sealed class OperationReceipt : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string Key { get; set; } = string.Empty;
    [MaxLength(64)] public string RequestHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = "{}";
    public int StatusCode { get; set; }
}
public sealed class OrderReturn : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal ClearedDebt { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
}
public sealed class ChatMessage : BaseEntity
{
    public string ContextJson { get; set; } = "{}";
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(20)] public string Role { get; set; } = "user";
    public string Text { get; set; } = string.Empty;
    [MaxLength(250)] public string? Source { get; set; }
}
