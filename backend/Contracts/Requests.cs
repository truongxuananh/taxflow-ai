namespace TaxFlow.Api.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record UserRequest(string Email, string FullName, string Password, string Role);
public sealed record UserUpdateRequest(string FullName, string Role, bool IsActive);
public sealed record ResetPasswordRequest(string NewPassword);

public sealed record CompanyRequest(
    string TaxCode,
    string Name,
    string Address,
    string Phone,
    string Email,
    string Representative,
    string BusinessType,
    string TaxMethod,
    string TaxFilingPeriod);

public sealed record PartyRequest(
    string Code,
    string Name,
    string? TaxCode,
    string? Address,
    string? Phone,
    string? Email);

public sealed record ProductRequest(
    string Sku,
    string? Barcode,
    string Name,
    Guid? CategoryId,
    string? CategoryName,
    string Unit,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal VatRate,
    decimal Stock,
    decimal MinimumStock);

public sealed record StockTransactionRequest(
    Guid ProductId,
    Guid? WarehouseId,
    string Type,
    decimal Quantity,
    decimal UnitCost,
    string? Reference,
    string? Note);

public sealed record OrderItemRequest(Guid ProductId, decimal Quantity);

public sealed record CreateOrderRequest(
    Guid? CustomerId,
    IReadOnlyList<OrderItemRequest> Items,
    decimal Discount,
    decimal PaidAmount,
    string PaymentMethod,
    bool RequestVatInvoice,
    string? CustomerName,
    string? CustomerTaxCode,
    string? CustomerAddress,
    string? CustomerEmail);

public sealed record IncomeRequest(
    string Category,
    string Description,
    decimal Amount,
    DateTime TransactionDate,
    string PaymentMethod,
    string? Reference);

public sealed record ExpenseRequest(
    string Category,
    string Description,
    decimal Amount,
    decimal VatAmount,
    DateTime TransactionDate,
    string PaymentMethod,
    string? Reference);

public sealed record BankAccountRequest(
    string BankName,
    string AccountNumber,
    string AccountName,
    decimal OpeningBalance);

public sealed record ConfirmDocumentRequest(
    string SupplierName,
    string? SupplierTaxCode,
    string InvoiceNumber,
    string? Serial,
    DateTime InvoiceDate,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total,
    IReadOnlyList<ExtractedInvoiceItem>? Items);

public sealed record OcrRegionRequest(string Field, int Page, decimal X, decimal Y, decimal Width, decimal Height);

public sealed record AssistantChatTurn(string Role, string Text);

public sealed record AssistantRequest(string Question, IReadOnlyList<AssistantChatTurn>? History = null);

public sealed record ExtractedInvoiceItem(
    string Description,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal VatAmount,
    decimal Total);

public sealed record InvitationRequest(string Email, string Role);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ReturnOrderRequest(string Reason);
public sealed record InvoiceReviewRequest(bool Eligible);
