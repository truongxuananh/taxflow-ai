export type Role = 'ADMIN' | 'ACCOUNTANT' | 'EMPLOYEE' | 'DIRECTOR'

export interface CompanySummary { id: string; name: string; taxCode: string; role?: Role }
export interface User { id: string; email: string; fullName: string; role: Role }
export interface AuthResponse {
  accessToken: string
  tokenType: string
  expiresIn: number
  user: User
  companies: CompanySummary[]
  defaultCompanyId: string
}

export interface Product {
  id: string
  sku: string
  barcode?: string
  name: string
  unit: string
  purchasePrice: number
  sellingPrice: number
  vatRate: number
  stock: number
  minimumStock: number
  isActive: boolean
  category: { id: string; name: string }
  stockStatus: 'LOW' | 'OK'
}

export interface DashboardData {
  period: { from: string; to: string; days: number }
  kpis: {
    revenue: number
    expense: number
    estimatedProfit: number
    currentCash: number
    receivables: number
    payables: number
    outputVat: number
    inputVat: number
  }
  trends: Array<{ date: string; revenue: number; expense: number; cashFlow: number }>
  topProducts: Array<{ id: string; name: string; quantity: number; revenue: number }>
  alerts: AlertItem[]
  disclaimer: string
}

export interface AlertItem {
  id: string
  type: string
  severity: 'LOW' | 'MEDIUM' | 'HIGH' | 'INFO'
  title: string
  message: string
  isRead?: boolean
  createdAt?: string
}

export interface Invoice {
  id: string
  direction: 'INCOMING' | 'OUTGOING'
  number: string
  serial?: string
  invoiceDate: string
  partyName: string
  partyTaxCode?: string
  subtotal: number
  vatAmount: number
  total: number
  status: string
  provider: string
  isLegallyIssued: boolean
  accountingReviewed?: boolean
  confidenceScore?: number
  itemCount: number
}

export interface Debt {
  id: string
  customer?: string
  supplier?: string
  originalAmount: number
  remainingAmount: number
  dueDate: string
  status: string
  overdueDays: number
}

export interface CashFlow {
  openingBalance: number
  cashInflow: number
  cashOutflow: number
  netCashFlow: number
  closingBalance: number
  daily: Array<{ date: string; inflow: number; outflow: number; net: number }>
}
