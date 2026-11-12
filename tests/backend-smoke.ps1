Write-Warning 'This smoke test writes demo transactions. Use only a disposable/test database.'
$ErrorActionPreference = 'Stop'
$Api = if ($env:API_URL) { $env:API_URL.TrimEnd('/') } else { 'http://localhost:5001' }

function Assert-True([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw $Message }
}
function Invoke-Api([string]$Method, [string]$Path, $Body = $null, $Headers = @{}) {
  $requestHeaders = @{}
  foreach ($key in $Headers.Keys) { $requestHeaders[$key] = $Headers[$key] }
  if ($Method -eq 'POST') { $requestHeaders['Idempotency-Key'] = [Guid]::NewGuid().ToString() }
  $params = @{ Method = $Method; Uri = "$Api$Path"; Headers = $requestHeaders; ContentType = 'application/json' }
  if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }
  Invoke-RestMethod @params
}

Write-Host '1/10 Health...'
$health = Invoke-RestMethod "$Api/health"
Assert-True ($health.status -eq 'healthy') 'Backend/database health failed.'

Write-Host '2/10 Login...'
$login = Invoke-Api POST '/api/auth/login' @{ email='admin@example.com'; password='Admin@123' }
Assert-True (-not [string]::IsNullOrWhiteSpace($login.accessToken)) 'Login did not return a JWT.'
$headers = @{ Authorization = "Bearer $($login.accessToken)"; 'X-Company-Id' = "$($login.defaultCompanyId)" }

Write-Host '3/10 Catalog...'
$products = @(Invoke-Api GET '/api/products' $null $headers)
$customers = @(Invoke-Api GET '/api/customers' $null $headers)
Assert-True ($products.Count -gt 0) 'No products found.'
Assert-True ($customers.Count -gt 0) 'No customers found.'
$product = $products | Where-Object { $_.stock -ge 2 } | Select-Object -First 1
Assert-True ($null -ne $product) 'No product has enough stock for smoke test.'
$customer = $customers | Select-Object -First 1

Write-Host '4/10 Fully-paid POS order...'
$order = Invoke-Api POST '/api/orders' @{
  customerId=$customer.id; items=@(@{ productId=$product.id; quantity=1 }); discount=0;
  paidAmount=$product.sellingPrice * (1 + $product.vatRate / 100); paymentMethod='CASH';
  requestVatInvoice=$false; customerName=$null; customerTaxCode=$null; customerAddress=$null; customerEmail=$null
} $headers
Assert-True ($order.balance -eq 0) 'Fully-paid order still has a balance.'

Write-Host '5/10 Partial POS order + receivable...'
$partial = Invoke-Api POST '/api/orders' @{
  customerId=$customer.id; items=@(@{ productId=$product.id; quantity=1 }); discount=0;
  paidAmount=0; paymentMethod='TRANSFER'; requestVatInvoice=$false;
  customerName=$null; customerTaxCode=$null; customerAddress=$null; customerEmail=$null
} $headers
Assert-True ($partial.balance -gt 0) 'Partial order did not create a balance.'
$receivables = @(Invoke-Api GET '/api/receivables' $null $headers)
$debt = $receivables | Where-Object { $_.orderId -eq $partial.id } | Select-Object -First 1
Assert-True ($null -ne $debt) 'Receivable was not created for partial order.'
$settled = Invoke-Api POST "/api/receivables/$($debt.id)/settle?amount=$($debt.remainingAmount)" $null $headers
Assert-True ($settled.remainingAmount -eq 0) 'Receivable settlement failed.'
Assert-True ($settled.orderPaidAmount -gt 0) 'Order paid amount was not synchronized during settlement.'

Write-Host '6/10 Income/expense...'
$today = [DateTime]::UtcNow.ToString('o')
$income = Invoke-Api POST '/api/incomes' @{ category='Khác'; description='Backend smoke income'; amount=10000; transactionDate=$today; paymentMethod='CASH'; reference='SMOKE' } $headers
$expense = Invoke-Api POST '/api/expenses' @{ category='Vận hành'; description='Backend smoke expense'; amount=5000; vatAmount=0; transactionDate=$today; paymentMethod='CASH'; reference='SMOKE' } $headers
Assert-True ($income.amount -eq 10000) 'Income create failed.'
Assert-True ($expense.amount -eq 5000) 'Expense create failed.'

Write-Host '7/10 Dashboard/cash-flow/debts...'
$null = Invoke-Api GET '/api/dashboard?days=30' $null $headers
$null = Invoke-Api GET '/api/cash-flow?days=30' $null $headers
$null = Invoke-Api GET '/api/payables' $null $headers

Write-Host '8/10 Tax/report...'
$year = [DateTime]::UtcNow.Year; $month = [DateTime]::UtcNow.Month
$null = Invoke-Api GET "/api/tax/summary?year=$year&month=$month" $null $headers
$draft1 = Invoke-Api POST "/api/tax/draft-report?year=$year&month=$month" $null $headers
$draft2 = Invoke-Api POST "/api/tax/draft-report?year=$year&month=$month" $null $headers
Assert-True ($draft1.id -eq $draft2.id) 'Tax draft is not idempotent for the same month.'
$null = Invoke-Api GET '/api/reports/summary?period=month' $null $headers
$null = Invoke-Api POST '/api/reports/generate?period=month' $null $headers

Write-Host '9/10 Reconciliation/AI...'
$null = Invoke-Api GET '/api/reconciliation' $null $headers
$null = Invoke-Api GET '/api/ai/forecast?horizon=7' $null $headers
$assistant = Invoke-Api POST '/api/ai/assistant' @{ question='Doanh thu hôm nay là bao nhiêu?' } $headers
Assert-True (-not [string]::IsNullOrWhiteSpace($assistant.answer)) 'AI assistant did not answer.'

Write-Host '10/10 Audit/export...'
$logs = @(Invoke-Api GET '/api/audit-logs?take=20' $null $headers)
Assert-True ($logs.Count -gt 0) 'Audit log is empty.'
Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri "$Api/api/exports/sales?format=csv" | Out-Null

Write-Host 'TaxFlow backend smoke test PASSED.' -ForegroundColor Green
