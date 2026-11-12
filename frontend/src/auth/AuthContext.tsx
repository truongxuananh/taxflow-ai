import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../lib/api'
import type { AuthResponse, CompanySummary, User } from '../types'

interface AuthState {
  user: User | null
  companies: CompanySummary[]
  selectedCompanyId: string | null
  loading: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  selectCompany: (id: string) => void
}

const AuthContext = createContext<AuthState | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [companies, setCompanies] = useState<CompanySummary[]>([])
  const [selectedCompanyId, setSelectedCompanyId] = useState<string | null>(localStorage.getItem('taxflow-company'))
  const [loading, setLoading] = useState(true)

  const applySession = useCallback((data: AuthResponse) => {
    localStorage.setItem('taxflow-token', data.accessToken)
    const existingCompany = localStorage.getItem('taxflow-company')
    const selectedRole = data.companies.find(c => c.id === existingCompany)?.role || data.user.role
    setUser({ ...data.user, role: selectedRole })
    setCompanies(data.companies)
    const existing = localStorage.getItem('taxflow-company')
    const nextCompany = data.companies.some((company) => company.id === existing) ? existing! : data.defaultCompanyId
    localStorage.setItem('taxflow-company', nextCompany)
    setSelectedCompanyId(nextCompany)
  }, [])

  const logout = useCallback(() => {
    const token = localStorage.getItem('taxflow-token')
    if (token) void api.post('/auth/logout', null, { headers: { Authorization: `Bearer ${token}` } }).catch(() => {})
    localStorage.removeItem('taxflow-token')
    localStorage.removeItem('taxflow-company')
    setUser(null)
    setCompanies([])
    setSelectedCompanyId(null)
  }, [])

  useEffect(() => {
    const restore = async () => {
      const token = localStorage.getItem('taxflow-token')
      if (!token) { setLoading(false); return }
      try {
        const { data } = await api.get<AuthResponse>('/auth/me')
        applySession(data)
      } catch {
        logout()
      } finally {
        setLoading(false)
      }
    }
    restore()
    window.addEventListener('taxflow-unauthorized', logout)
    return () => window.removeEventListener('taxflow-unauthorized', logout)
  }, [applySession, logout])

  const login = useCallback(async (email: string, password: string) => {
    const { data } = await api.post<AuthResponse>('/auth/login', { email, password })
    applySession(data)
  }, [applySession])

  const selectCompany = useCallback((id: string) => {
    if (!companies.some((company) => company.id === id)) return
    localStorage.setItem('taxflow-company', id)
    setSelectedCompanyId(id)
    const role = companies.find(company => company.id === id)?.role
    if (role) setUser(current => current ? { ...current, role } : current)
    window.dispatchEvent(new Event('taxflow-company-changed'))
  }, [companies])

  const value = useMemo(() => ({ user, companies, selectedCompanyId, loading, login, logout, selectCompany }),
    [user, companies, selectedCompanyId, loading, login, logout, selectCompany])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
