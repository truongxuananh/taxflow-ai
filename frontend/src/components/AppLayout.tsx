import { Suspense, useEffect, useRef, useState } from 'react'
import { NavLink, useLocation, useOutlet } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { PageSkeleton } from './UI'
import { useReducedMotion } from '../hooks/useReducedMotion'
import {
  Bell, Bot, Boxes, Building2, ChevronDown, CircleDollarSign, ArrowUpRight,
  FileBarChart, FileText, LayoutDashboard, LogOut, Menu, PackageSearch, ReceiptText,
  Scale, ScanLine, ShieldCheck, Search, UserRound, ShoppingCart, Sparkles, X,
} from 'lucide-react'
import { useAuth } from '../auth/AuthContext'
import { longDate } from '../lib/format'
import type { Role } from '../types'

const nav: Array<{ to: string; label: string; icon: typeof LayoutDashboard; roles: Role[] }> = [
  { to: '/records', label: 'Tra cứu nghiệp vụ', icon: Search, roles: ['ADMIN','ACCOUNTANT','EMPLOYEE','DIRECTOR'] },
  { to: '/account', label: 'Tài khoản & lời mời', icon: UserRound, roles: ['ADMIN','ACCOUNTANT','EMPLOYEE','DIRECTOR'] },
  { to: '/', label: 'Tổng quan', icon: LayoutDashboard, roles: ['ADMIN', 'ACCOUNTANT', 'EMPLOYEE', 'DIRECTOR'] },
  { to: '/pos', label: 'Bán hàng POS', icon: ShoppingCart, roles: ['ADMIN', 'ACCOUNTANT', 'EMPLOYEE'] },
  { to: '/products', label: 'Sản phẩm', icon: PackageSearch, roles: ['ADMIN', 'ACCOUNTANT', 'EMPLOYEE', 'DIRECTOR'] },
  { to: '/inventory', label: 'Kho hàng', icon: Boxes, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/invoices', label: 'Hóa đơn & OCR', icon: ReceiptText, roles: ['ADMIN', 'ACCOUNTANT'] },
  { to: '/finance', label: 'Thu chi & dòng tiền', icon: CircleDollarSign, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/debts', label: 'Công nợ', icon: Scale, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/reconciliation', label: 'Đối soát', icon: ScanLine, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/tax', label: 'Thuế', icon: FileText, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/reports', label: 'Báo cáo', icon: FileBarChart, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/assistant', label: 'Trợ lý AI', icon: Bot, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/alerts', label: 'Cảnh báo & nhật ký', icon: ShieldCheck, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
  { to: '/company', label: 'Doanh nghiệp', icon: Building2, roles: ['ADMIN', 'ACCOUNTANT', 'DIRECTOR'] },
]

const roleLabels: Record<Role, string> = { ADMIN: 'Quản trị viên', ACCOUNTANT: 'Kế toán', EMPLOYEE: 'Nhân viên', DIRECTOR: 'Giám đốc' }
const groups = [
  { label: 'Làm việc hằng ngày', paths: ['/', '/pos', '/products', '/inventory', '/records'] },
  { label: 'Tài chính & kế toán', paths: ['/invoices', '/finance', '/debts', '/reconciliation', '/tax', '/reports'] },
  { label: 'Công cụ & quản trị', paths: ['/assistant', '/alerts', '/company', '/account'] },
]

/** Logo: ba dòng sổ cái lệch nhau như dòng tiền chảy vào một ô vuông. */
export function BrandMark({ className = 'h-10 w-10' }: { className?: string }) {
  return (
    <span className={`relative flex shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-[#3B67FF] to-[#1A3FC4] shadow-[0_8px_20px_-6px_rgba(35,80,240,.7),inset_0_1px_0_rgba(255,255,255,.25)] ${className}`}>
      <svg viewBox="0 0 24 24" className="h-[55%] w-[55%]" fill="none" aria-hidden="true">
        <path d="M4 7h11" stroke="white" strokeWidth="2.4" strokeLinecap="round" />
        <path d="M4 12h16" stroke="white" strokeWidth="2.4" strokeLinecap="round" opacity=".85" />
        <path d="M4 17h7" stroke="white" strokeWidth="2.4" strokeLinecap="round" opacity=".6" />
        <circle cx="18.5" cy="7" r="2" fill="#FF8A73" />
      </svg>
    </span>
  )
}

/** Giữ nội dung trang cũ trong lúc chạy hiệu ứng rời đi, rồi đưa trang mới vào. */
function AnimatedOutlet({ routeKey }: { routeKey: string }) {
  const outlet = useOutlet()
  const reduced = useReducedMotion()
  return (
    <AnimatePresence mode="wait" initial={false}>
      <motion.div
        key={routeKey}
        initial={reduced ? { opacity: 0 } : { opacity: 0, y: 18, scale: .995 }}
        animate={reduced ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1, transition: { duration: .42, ease: [.2, .7, .2, 1] } }}
        exit={reduced ? { opacity: 0, transition: { duration: .05 } } : { opacity: 0, y: -8, transition: { duration: .16, ease: [.4, 0, 1, 1] } }}
      >
        <Suspense fallback={<PageSkeleton />}>{outlet}</Suspense>
      </motion.div>
    </AnimatePresence>
  )
}

export default function AppLayout() {
  const [mobileOpen, setMobileOpen] = useState(false)
  const { user, companies, selectedCompanyId, selectCompany, logout } = useAuth()
  const location = useLocation()
  const menuTrigger = useRef<HTMLButtonElement>(null)
  const drawer = useRef<HTMLElement>(null)
  const allowedNav = nav.filter((item) => !!user && item.roles.includes(user.role))
  const activeItem = allowedNav.find((item) => item.to === location.pathname)
  const active = activeItem?.label || 'TaxFlow AI'
  const activeGroup = groups.find(group => group.paths.includes(location.pathname))?.label
  const canUseAI = user?.role !== 'EMPLOYEE'
  const today = longDate(new Date(), false)
  useEffect(() => {
    if (!mobileOpen) return
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    drawer.current?.querySelector<HTMLButtonElement>('button')?.focus()
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setMobileOpen(false)
      if (event.key !== 'Tab') return
      const nodes = drawer.current?.querySelectorAll<HTMLElement>('a[href], button, select')
      if (!nodes?.length) return
      const first = nodes[0], last = nodes[nodes.length - 1]
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', handleKey)
    return () => { document.body.style.overflow = previousOverflow; document.removeEventListener('keydown', handleKey); menuTrigger.current?.focus() }
  }, [mobileOpen])
  useEffect(() => { window.scrollTo?.({ top: 0 }) }, [location.pathname])

  const companySelect = (mobile = false) => <label className={mobile ? 'relative block px-4 pb-4' : 'relative hidden sm:block'}>
    <span className="sr-only">Chọn doanh nghiệp</span>
    {!mobile && <Building2 className="pointer-events-none absolute left-3 top-3.5 h-4 w-4 text-slate-400" />}
    <select value={selectedCompanyId || ''} onChange={(e) => { selectCompany(e.target.value); setMobileOpen(false) }} className={mobile ? 'min-h-11 w-full appearance-none rounded-xl border border-white/10 bg-white/[.06] px-3.5 text-sm font-semibold text-white focus:border-white/30 focus:outline-none [&>option]:text-ink' : 'field !w-auto max-w-60 appearance-none !pl-9 !pr-9 text-sm font-semibold'}>
      {companies.map((company) => <option key={company.id} value={company.id}>{company.name}</option>)}
    </select>
    <ChevronDown className={`pointer-events-none absolute h-4 w-4 ${mobile ? 'right-7 top-3.5 text-white/50' : 'right-3 top-3.5 text-slate-400'}`} />
  </label>

  const sidebar = (variant: 'desktop' | 'mobile') => <div className="sidebar-surface flex h-full flex-col text-white">
    <div className="flex h-20 shrink-0 items-center gap-3 px-5">
      <BrandMark />
      <div className="leading-tight"><div className="text-[17px] font-bold tracking-[-0.01em]">TaxFlow AI</div><p className="text-[11px] font-medium text-white/45">Sổ sách & dòng tiền</p></div>
    </div>
    <div className="lg:hidden">{companySelect(true)}</div>
    <nav className="scrollbar-thin flex-1 overflow-y-auto px-3 pb-4" aria-label="Điều hướng chính">
      {groups.map(group => {
        const items = allowedNav.filter(item => group.paths.includes(item.to))
        return items.length > 0 && <div key={group.label} className="mb-4"><p className="mb-1.5 mt-3 px-3 text-[11px] font-semibold text-white/35">{group.label}</p><div className="space-y-0.5">{items.map(item => {
          const Icon = item.icon
          return <NavLink key={item.to} to={item.to} end={item.to === '/'} onClick={() => setMobileOpen(false)} className={({ isActive }) => `nav-item group flex min-h-10 items-center gap-3 rounded-lg px-3 py-2 text-[13.5px] font-medium ${isActive ? 'is-active text-white' : 'text-white/60 hover:text-white'}`}>
            {({ isActive }) => <>
              {isActive && <motion.span layoutId={`nav-pill-${variant}`} className="absolute inset-0 rounded-lg bg-white/[.09] ring-1 ring-inset ring-white/10" transition={{ type: 'spring', stiffness: 380, damping: 32 }}><span className="absolute left-0 top-2 bottom-2 w-[3px] rounded-r-full bg-[#5B82FF] shadow-[0_0_12px_rgba(91,130,255,.9)]" /></motion.span>}
              {!isActive && <span className="absolute inset-0 rounded-lg bg-white/0 transition-colors group-hover:bg-white/[.04]" />}
              <Icon className={`relative h-[18px] w-[18px] shrink-0 ${isActive ? 'text-[#8EA8FF]' : ''}`} /><span className="relative flex-1">{item.label}</span>{item.to === '/assistant' && <span className="relative rounded-md bg-[#2350F0] px-1.5 py-0.5 text-[10px] font-bold text-white">AI</span>}
            </>}
          </NavLink>
        })}</div></div>
      })}
    </nav>
    {canUseAI && <NavLink to="/assistant" onClick={() => setMobileOpen(false)} className="group mx-3 mb-3 block overflow-hidden [@media(max-height:780px)]:hidden rounded-2xl border border-white/10 bg-gradient-to-br from-[#2350F0]/40 to-white/[.03] p-4 transition hover:border-white/20">
      <div className="flex items-center justify-between"><Sparkles className="h-5 w-5 text-[#8EA8FF]" /><ArrowUpRight className="h-4 w-4 text-white/40 transition-transform duration-300 group-hover:-translate-y-0.5 group-hover:translate-x-0.5 group-hover:text-white" /></div>
      <p className="mt-3 text-sm font-semibold">Hỏi số liệu bằng lời</p><p className="mt-1 text-xs leading-5 text-white/50">“Ai đang nợ nhiều nhất?” — trợ lý trả lời từ sổ sách của bạn.</p>
    </NavLink>}
    <div className="flex shrink-0 items-center gap-3 border-t border-white/10 p-4"><div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-white/10 text-sm font-bold ring-1 ring-white/15">{user?.fullName?.charAt(0) || 'U'}</div><div className="min-w-0 flex-1"><p className="truncate text-[13px] font-semibold">{user?.fullName}</p><p className="mt-0.5 text-[11px] text-white/45">{user && roleLabels[user.role]}</p></div><button onClick={logout} className="rounded-lg p-2 text-white/45 transition hover:bg-white/10 hover:text-[#FF8A73]" aria-label="Đăng xuất"><LogOut className="h-4 w-4"/></button></div>
  </div>

  return <div className="min-h-screen">
    <aside className="fixed inset-y-0 left-0 z-30 hidden w-[264px] lg:block">{sidebar('desktop')}</aside>
    <AnimatePresence>
      {mobileOpen && <motion.div className="fixed inset-0 z-40 lg:hidden" initial={{ opacity: 1 }} exit={{ opacity: 1, transition: { duration: .25 } }}>
        <motion.button initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className="absolute inset-0 bg-navy/50 backdrop-blur-[2px]" onClick={() => setMobileOpen(false)} aria-label="Đóng menu"/>
        <motion.aside ref={drawer} role="dialog" aria-modal="true" aria-label="Menu điều hướng" initial={{ x: '-100%' }} animate={{ x: 0 }} exit={{ x: '-100%' }} transition={{ type: 'spring', stiffness: 380, damping: 38 }} className="relative h-full w-[86%] max-w-[288px] shadow-2xl">
          <button onClick={() => setMobileOpen(false)} className="absolute right-3 top-6 z-10 rounded-lg p-1.5 text-white/60 hover:bg-white/10 hover:text-white" aria-label="Đóng menu điều hướng"><X className="h-4 w-4"/></button>
          {sidebar('mobile')}
        </motion.aside>
      </motion.div>}
    </AnimatePresence>
    <div className="lg:pl-[264px]">
      <header className="sticky top-0 z-20 flex h-[68px] items-center gap-3 border-b border-line/80 bg-white/75 px-4 backdrop-blur-xl backdrop-saturate-150 sm:px-6 lg:px-8">
        <button ref={menuTrigger} onClick={() => setMobileOpen(true)} aria-expanded={mobileOpen} className="rounded-xl p-2 text-slate-600 hover:bg-mint lg:hidden" aria-label="Mở menu"><Menu className="h-5 w-5"/></button>
        <div className="min-w-0 flex-1">
          <AnimatePresence mode="popLayout" initial={false}>
            <motion.div key={location.pathname} initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: -10 }} transition={{ duration: .25, ease: [.2, .7, .2, 1] }} className="flex min-w-0 flex-col">
              <span className="hidden truncate text-[11px] font-medium text-slate-400 md:block">{activeGroup || 'TaxFlow AI'} · {today}</span>
              <span className="truncate text-[15px] font-semibold text-ink">{active}</span>
            </motion.div>
          </AnimatePresence>
        </div>
        {companySelect()}
        {canUseAI && <NavLink to="/alerts" className="relative rounded-xl border border-line bg-white p-2.5 text-slate-500 transition hover:border-emerald/40 hover:text-emerald" aria-label="Cảnh báo và thông báo"><Bell className="h-5 w-5"/></NavLink>}
        {/* Vạch mực chạy ngang khi đổi trang */}
        <motion.span key={`bar-${location.pathname}`} aria-hidden="true" className="pointer-events-none absolute inset-x-0 -bottom-px h-[2px] origin-left bg-gradient-to-r from-[#2350F0] via-[#5B82FF] to-[#2350F0]" initial={{ scaleX: 0, opacity: 1 }} animate={{ scaleX: 1, opacity: [1, 1, 0] }} transition={{ scaleX: { duration: .55, ease: [.2, .7, .2, 1] }, opacity: { duration: .9, times: [0, .6, 1] } }} />
      </header>
      <main className="mx-auto max-w-[1600px] p-4 sm:p-6 lg:p-8"><AnimatedOutlet routeKey={`${location.pathname}-${selectedCompanyId}`} /></main>
      <footer className="mx-auto flex max-w-[1600px] flex-wrap justify-between gap-2 px-4 pb-6 pt-2 text-[11px] text-slate-400 sm:px-6 lg:px-8"><span>TaxFlow AI — không gian quản trị doanh nghiệp</span><span>Số liệu quản trị cần được kế toán rà soát.</span></footer>
    </div>
  </div>
}
