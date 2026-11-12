import { useReducedMotion } from '../hooks/useReducedMotion'
import { useCallback, useEffect, useState } from 'react'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { ArrowDownRight, ArrowUpRight, Banknote, CircleDollarSign, Landmark, Receipt, RefreshCw, Scale, TrendingUp, WalletCards, Sparkles, ArrowRight, ShoppingCart, FileSearch, FileBarChart } from 'lucide-react'
import { Link } from 'react-router-dom'
import { motion } from 'motion/react'
import { useAuth } from '../auth/AuthContext'
import AnimatedMoney from '../components/AnimatedMoney'
import { api } from '../lib/api'
import { longDate, money, shortDate } from '../lib/format'
import type { DashboardData } from '../types'
import { Empty, Loading, PageHeader, StatusBadge } from '../components/UI'

const tooltipStyle = { borderRadius: 12, border: '1px solid #E2E8F2', boxShadow: '0 16px 32px -12px rgba(14,27,61,.25)', fontSize: 13 }
const compactMoney = (value: number) => new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value)

export default function DashboardPage() {
  const reducedMotion = useReducedMotion()
  const { user, selectedCompanyId } = useAuth()
  const [days, setDays] = useState(30)
  const [data, setData] = useState<DashboardData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true); setError('')
    try { const response = await api.get<DashboardData>('/dashboard', { params: { days } }); setData(response.data) }
    catch { setError('Không thể tải dashboard. Kiểm tra kết nối backend.') }
    finally { setLoading(false) }
  }, [days, selectedCompanyId])

  useEffect(() => { load() }, [load])
  useEffect(() => { window.addEventListener('taxflow-company-changed', load); return () => window.removeEventListener('taxflow-company-changed', load) }, [load])

  if (loading && !data) return <Loading />
  if (!data) return <Empty title="Không thể hiển thị dashboard" description={error} />

  const cards = [
    { label: 'Doanh thu', value: data.kpis.revenue, icon: TrendingUp, color: '#2350F0' },
    { label: 'Chi phí', value: data.kpis.expense, icon: ArrowDownRight, color: '#D9452F' },
    { label: 'Lợi nhuận ước tính', value: data.kpis.estimatedProfit, icon: ArrowUpRight, color: '#0F9F6E' },
    { label: 'Tiền hiện tại', value: data.kpis.currentCash, icon: WalletCards, color: '#0E1B3D' },
    { label: 'Phải thu', value: data.kpis.receivables, icon: CircleDollarSign, color: '#7C5CFA' },
    { label: 'Phải trả', value: data.kpis.payables, icon: Scale, color: '#E9A23B' },
    { label: 'VAT đầu ra', value: data.kpis.outputVat, icon: Receipt, color: '#0891B2' },
    { label: 'VAT đầu vào', value: data.kpis.inputVat, icon: Landmark, color: '#4F6BD8' },
  ]

  const profitable = data.kpis.estimatedProfit >= 0
  const flowTotal = Math.abs(data.kpis.revenue) + Math.abs(data.kpis.expense)
  const revenueShare = flowTotal ? Math.round(Math.abs(data.kpis.revenue) / flowTotal * 100) : 0
  const todayLabel = longDate()

  const quickActions = [
    { to: '/pos', label: 'Tạo đơn bán hàng', description: 'Bắt đầu giao dịch mới', icon: ShoppingCart, roles: ['ADMIN','ACCOUNTANT','EMPLOYEE'] },
    { to: '/invoices', label: 'Đọc hóa đơn với OCR', description: 'Tải lên và kiểm tra chứng từ', icon: FileSearch, roles: ['ADMIN','ACCOUNTANT'] },
    { to: '/reports', label: 'Xem báo cáo', description: 'Theo dõi hiệu quả kinh doanh', icon: FileBarChart, roles: ['ADMIN','ACCOUNTANT','DIRECTOR'] },
  ].filter(item => user && item.roles.includes(user.role))

  return (
    <>
      <motion.section initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .55, ease: [.2, .7, .2, 1] }} className="hero-surface mb-8 overflow-hidden rounded-[22px] p-6 text-white shadow-[0_30px_60px_-30px_rgba(11,22,52,.7)] sm:p-8">
        <div className="hero-glow pointer-events-none absolute -right-24 -top-24 h-72 w-72 rounded-full bg-[#5B82FF]/30 blur-3xl" />
        <div className="relative flex flex-col gap-8 lg:flex-row lg:items-end lg:justify-between">
          <div className="max-w-xl">
            <p className="text-sm font-medium text-white/55">{todayLabel}</p>
            <h2 className="mt-2 text-[28px] font-bold leading-tight tracking-[-0.02em] sm:text-[34px]">Xin chào, {user?.fullName || 'bạn'}</h2>
            <p className="mt-3 text-[15px] leading-7 text-white/65">Trong {days} ngày qua, doanh nghiệp {profitable ? 'đang có lãi' : 'đang chi nhiều hơn thu'} theo số liệu quản trị.</p>
            {user?.role !== 'EMPLOYEE' && <Link to="/assistant" className="group mt-6 inline-flex min-h-11 items-center gap-2 rounded-xl bg-white px-4 text-sm font-semibold text-ink shadow-lg shadow-black/20 transition hover:-translate-y-0.5"><Sparkles className="h-4 w-4 text-emerald"/>Hỏi trợ lý AI<ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-1"/></Link>}
          </div>
          <div className="lg:text-right">
            <p className="text-sm font-medium text-white/55">Lợi nhuận ước tính trong kỳ</p>
            <p className={`mt-1 text-[38px] font-bold leading-none tracking-[-0.03em] sm:text-[48px] ${profitable ? 'text-white' : 'text-[#FFB4A6]'}`}><AnimatedMoney value={data.kpis.estimatedProfit}/></p>
            <div className="mt-4 flex gap-5 text-sm lg:justify-end">
              <span className="flex items-center gap-2 text-white/70"><span className="h-2 w-2 rounded-full bg-[#4ADE9B]"/>Thu {compactMoney(data.kpis.revenue)}</span>
              <span className="flex items-center gap-2 text-white/70"><span className="h-2 w-2 rounded-full bg-[#FF8A73]"/>Chi {compactMoney(data.kpis.expense)}</span>
            </div>
            <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-white/10 lg:ml-auto lg:w-72" aria-hidden="true">
              <motion.div className="h-full rounded-full bg-gradient-to-r from-[#4ADE9B] to-[#8EA8FF]" initial={{ width: 0 }} animate={{ width: `${revenueShare}%` }} transition={{ duration: 1.1, ease: [.2, .7, .2, 1], delay: .3 }} />
            </div>
          </div>
        </div>
      </motion.section>
      <PageHeader title="Tổng quan tài chính" description={`Số liệu từ ${shortDate(data.period.from)} đến ${shortDate(data.period.to)} · ${data.disclaimer}`}
        actions={<><select className="field !w-auto" value={days} onChange={(e) => setDays(Number(e.target.value))}><option value={7}>7 ngày</option><option value={30}>30 ngày</option><option value={90}>90 ngày</option></select><button className="btn-secondary" onClick={load} disabled={loading}><RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />Làm mới</button></>} />

      {error && <div role="alert" className="mb-4 rounded-xl border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{error}</div>}
      <div className="stagger-grid grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {cards.map(({ label, value, icon: Icon, color }) => (
          <article key={label} className="panel metric-card p-5 pl-6" style={{ ['--tone' as string]: color }}>
            <div className="flex items-start justify-between"><p className="text-sm font-medium text-slate-500">{label}</p><span className="rounded-xl p-2" style={{ background: `${color}14`, color }}><Icon className="h-[18px] w-[18px]" /></span></div>
            <p className="mt-4 text-[26px] font-bold leading-none tracking-[-0.02em] text-ink"><AnimatedMoney value={value}/></p>
            <p className="mt-3 text-xs text-slate-400">{['Tiền hiện tại', 'Phải thu', 'Phải trả'].includes(label) ? 'Số dư hiện tại' : `Trong ${days} ngày`}</p>
          </article>
        ))}
      </div>

      <div className="mt-5 grid gap-3 sm:grid-cols-3">{quickActions.map(({ to, label, description, icon: Icon }) => <Link to={to} key={to} className="panel quick-link flex items-center gap-3 p-4"><span className="rounded-xl bg-mint p-2.5 text-emerald"><Icon className="h-5 w-5"/></span><div className="min-w-0 flex-1"><p className="text-sm font-semibold text-ink">{label}</p><p className="mt-0.5 text-xs text-slate-400">{description}</p></div><ArrowRight className="quick-arrow h-4 w-4 text-slate-300"/></Link>)}</div>
      <div className="mt-6 grid gap-6 xl:grid-cols-[1.6fr_1fr]">
        <section className="panel p-5 sm:p-6">
          <div className="mb-6 flex items-center justify-between"><div><h2 className="font-bold text-ink">Doanh thu và chi phí</h2><p className="mt-1 text-sm text-slate-500">Theo ngày trong kỳ</p></div><Banknote className="h-5 w-5 text-emerald" /></div>
          <div className="h-[310px] w-full">
            <ResponsiveContainer width="100%" height="100%"><AreaChart data={data.trends} margin={{ top: 5, right: 5, left: -12, bottom: 0 }}>
              <defs><linearGradient id="rev" x1="0" y1="0" x2="0" y2="1"><stop offset="5%" stopColor="#2350F0" stopOpacity={0.22}/><stop offset="95%" stopColor="#2350F0" stopOpacity={0}/></linearGradient></defs>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#E6EBF4" /><XAxis dataKey="date" tickFormatter={(v) => new Date(v).toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' })} tick={{ fontSize: 12, fill: '#7a8ca8' }} axisLine={false} tickLine={false}/><YAxis tickFormatter={compactMoney} tick={{ fontSize: 12, fill: '#7a8ca8' }} axisLine={false} tickLine={false}/><Tooltip contentStyle={tooltipStyle} formatter={(value) => money(Number(value))} labelFormatter={(label) => shortDate(String(label))} /><Legend iconType="circle" />
              <Area isAnimationActive={!reducedMotion} type="monotone" dataKey="revenue" name="Doanh thu" stroke="#2350F0" fill="url(#rev)" strokeWidth={2.5} animationDuration={1100}/><Area isAnimationActive={!reducedMotion} type="monotone" dataKey="expense" name="Chi phí" stroke="#D9452F" fill="transparent" strokeWidth={2} strokeDasharray="5 4" animationDuration={1100}/>
            </AreaChart></ResponsiveContainer>
          </div>
        </section>

        <section className="panel p-5 sm:p-6">
          <h2 className="font-bold text-ink">Cảnh báo cần chú ý</h2><p className="mt-1 text-sm text-slate-500">Ưu tiên theo mức độ ảnh hưởng</p>
          <div className="mt-5 space-y-3">
            {data.alerts.length === 0 ? <Empty title="Không có cảnh báo mới" /> : data.alerts.map((alert) => (
              <div key={alert.id} className="rounded-xl border border-line p-4 transition-colors hover:border-[#C9D6F5] hover:bg-[#F8FAFF]"><div className="mb-2 flex items-center justify-between gap-3"><p className="font-semibold text-slate-800">{alert.title}</p><StatusBadge value={alert.severity} /></div><p className="text-sm leading-6 text-slate-500">{alert.message}</p></div>
            ))}
          </div>
        </section>
      </div>

      <section className="panel mt-6 p-5 sm:p-6">
        <div className="mb-6"><h2 className="font-bold text-ink">Sản phẩm nổi bật</h2><p className="mt-1 text-sm text-slate-500">Theo doanh thu trong kỳ</p></div>
        <div className="h-[270px] w-full">
          <ResponsiveContainer width="100%" height="100%"><BarChart data={data.topProducts} layout="vertical" margin={{ left: 12, right: 20 }}><CartesianGrid strokeDasharray="3 3" horizontal={false} stroke="#E6EBF4"/><XAxis type="number" tickFormatter={compactMoney} tick={{ fontSize: 12 }} axisLine={false} tickLine={false}/><YAxis type="category" dataKey="name" width={150} tick={{ fontSize: 12, fill: '#536987' }} axisLine={false} tickLine={false}/><Tooltip contentStyle={tooltipStyle} cursor={{ fill: '#EAF0FF' }} formatter={(value) => money(Number(value))}/><Bar isAnimationActive={!reducedMotion} dataKey="revenue" name="Doanh thu" fill="#2350F0" radius={[0, 7, 7, 0]} animationDuration={900} barSize={22}/></BarChart></ResponsiveContainer>
        </div>
      </section>
    </>
  )
}
