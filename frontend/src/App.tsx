import { lazy, Suspense, type ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import AppLayout from './components/AppLayout'
import { Loading } from './components/UI'
import type { Role } from './types'
const RecordsPage = lazy(() => import('./pages/RecordsPage'))
const AccountPage = lazy(() => import('./pages/AccountPage'))
const LoginPage = lazy(() => import('./pages/LoginPage'))
const DashboardPage = lazy(() => import('./pages/DashboardPage'))
const PosPage = lazy(() => import('./pages/PosPage'))
const ProductsPage = lazy(() => import('./pages/ProductsPage'))
const InventoryPage = lazy(() => import('./pages/InventoryPage'))
const InvoicesPage = lazy(() => import('./pages/InvoicesPage'))
const FinancePage = lazy(() => import('./pages/FinancePage'))
const DebtsPage = lazy(() => import('./pages/DebtsPage'))
const TaxPage = lazy(() => import('./pages/TaxPage'))
const ReportsPage = lazy(() => import('./pages/ReportsPage'))
const AssistantPage = lazy(() => import('./pages/AssistantPage'))
const AlertsPage = lazy(() => import('./pages/OperationsPages').then((module) => ({ default: module.AlertsPage })))
const CompanyPage = lazy(() => import('./pages/OperationsPages').then((module) => ({ default: module.CompanyPage })))
const ReconciliationPage = lazy(() => import('./pages/OperationsPages').then((module) => ({ default: module.ReconciliationPage })))

function RoleRoute({ roles, children }: { roles: Role[]; children: ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <Loading />
  if (!user) return <Navigate to="/login" replace />
  return roles.includes(user.role) ? <>{children}</> : <Navigate to="/" replace />
}

function ProtectedLayout() {
  const { user, loading } = useAuth()
  if (loading) return <div className="min-h-screen bg-canvas"><Loading label="Đang khởi tạo TaxFlow AI..." /></div>
  if (!user) return <Navigate to="/login" replace />
  return <AppLayout />
}

export default function App() {
  const { user, loading, companies } = useAuth()
  return (
    <Suspense fallback={<Loading />}><Routes>
      <Route path="/login" element={loading ? <Loading /> : user ? <Navigate to={companies.length ? "/" : "/account"} replace /> : <LoginPage />} />
      <Route element={<ProtectedLayout />}>
        <Route index element={companies.length ? <DashboardPage /> : <Navigate to="/account" replace />} />
        <Route path="records" element={<RecordsPage />} />
        <Route path="account" element={<AccountPage />} />
        <Route path="pos" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','EMPLOYEE']}><PosPage /></RoleRoute>} />
        <Route path="products" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','EMPLOYEE','DIRECTOR']}><ProductsPage /></RoleRoute>} />
        <Route path="inventory" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><InventoryPage /></RoleRoute>} />
        <Route path="invoices" element={<RoleRoute roles={['ADMIN','ACCOUNTANT']}><InvoicesPage /></RoleRoute>} />
        <Route path="finance" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><FinancePage /></RoleRoute>} />
        <Route path="debts" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><DebtsPage /></RoleRoute>} />
        <Route path="reconciliation" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><ReconciliationPage /></RoleRoute>} />
        <Route path="tax" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><TaxPage /></RoleRoute>} />
        <Route path="reports" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><ReportsPage /></RoleRoute>} />
        <Route path="assistant" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><AssistantPage /></RoleRoute>} />
        <Route path="alerts" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><AlertsPage /></RoleRoute>} />
        <Route path="company" element={<RoleRoute roles={['ADMIN','ACCOUNTANT','DIRECTOR']}><CompanyPage /></RoleRoute>} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes></Suspense>
  )
}
