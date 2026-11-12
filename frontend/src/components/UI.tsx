import { useEffect, useId, useRef, type ReactNode } from 'react'
import { AlertCircle, CheckCircle2, Inbox } from 'lucide-react'
import { motion } from 'motion/react'

export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <div className="page-header mb-7 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <motion.h1 initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .4, ease: [.2, .7, .2, 1] }} className="text-[26px] font-bold leading-tight tracking-[-0.02em] text-ink sm:text-[30px]">{title}</motion.h1>
        {description && <motion.p initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: .4, delay: .08 }} className="mt-2 max-w-3xl text-sm leading-6 text-slate-500">{description}</motion.p>}
      </div>
      {actions && <motion.div initial={{ opacity: 0, y: 6 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .35, delay: .12 }} className="flex flex-wrap gap-2">{actions}</motion.div>}
    </div>
  )
}

export function Loading({ label = 'Đang tải dữ liệu...' }: { label?: string }) {
  return (
    <div role="status" className="flex min-h-48 flex-col items-center justify-center gap-4 text-sm font-medium text-slate-500">
      <span aria-hidden="true" className="flex items-end gap-1.5">
        {[0, 1, 2, 3].map(i => <motion.span key={i} className="block w-1.5 rounded-full bg-emerald" initial={{ height: 6, opacity: .4 }} animate={{ height: [6, 22, 6], opacity: [.4, 1, .4] }} transition={{ duration: 1, repeat: Infinity, delay: i * .12, ease: 'easeInOut' }} />)}
      </span>
      {label}
    </div>
  )
}

export function PageSkeleton() {
  return (
    <div aria-busy="true" aria-label="Đang tải trang" className="space-y-6">
      <div className="space-y-3"><div className="skeleton h-8 w-72 max-w-full" /><div className="skeleton h-4 w-[28rem] max-w-full" /></div>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{[0, 1, 2, 3].map(i => <div key={i} className="skeleton h-32 rounded-2xl" />)}</div>
      <div className="skeleton h-72 rounded-2xl" />
    </div>
  )
}

export function Empty({ title = 'Chưa có dữ liệu', description }: { title?: string; description?: string }) {
  return (
    <motion.div initial={{ opacity: 0, scale: .98 }} animate={{ opacity: 1, scale: 1 }} transition={{ duration: .35 }} className="flex min-h-48 flex-col items-center justify-center px-6 text-center">
      <span className="mb-4 flex h-14 w-14 items-center justify-center rounded-2xl border border-dashed border-slate-300 bg-canvas text-slate-400"><Inbox className="h-6 w-6" /></span>
      <p className="font-semibold text-slate-700">{title}</p>
      {description && <p className="mt-1 max-w-md text-sm leading-6 text-slate-500">{description}</p>}
    </motion.div>
  )
}

/** Thanh tab có ô đánh dấu trượt sang tab được chọn. */
export function Tabs<T extends string>({ value, onChange, items, className = '' }: { value: T; onChange: (value: T) => void; items: Array<{ value: T; label: ReactNode }>; className?: string }) {
  const group = useId()
  return (
    <div role="tablist" className={`mb-5 inline-flex max-w-full gap-1 overflow-x-auto rounded-xl border border-line bg-white p-1 shadow-panel ${className}`}>
      {items.map(item => {
        const active = item.value === value
        return (
          <button key={item.value} role="tab" aria-selected={active} onClick={() => onChange(item.value)} className={`relative min-h-10 whitespace-nowrap rounded-lg px-4 text-sm font-semibold transition-colors sm:px-5 ${active ? 'text-white' : 'text-slate-500 hover:text-ink'}`}>
            {active && <motion.span layoutId={`tab-${group}`} className="absolute inset-0 rounded-lg bg-ink shadow-[0_6px_16px_-6px_rgba(14,27,61,.6)]" transition={{ type: 'spring', stiffness: 420, damping: 34 }} />}
            <span className="relative z-10 flex items-center gap-2">{item.label}</span>
          </button>
        )
      })}
    </div>
  )
}

const statusStyles: Record<string, string> = {
  OK: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  COMPLETED: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  PAID: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Issued: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  ACCOUNTANT_REVIEWED: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20',
  Draft: 'bg-slate-100 text-slate-700 ring-slate-500/20',
  DRAFT: 'bg-slate-100 text-slate-700 ring-slate-500/20',
  Pending: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  PARTIAL: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  OPEN: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  LOW: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  HIGH: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  Cancelled: 'bg-rose-50 text-rose-700 ring-rose-600/20',
  MEDIUM: 'bg-amber-50 text-amber-700 ring-amber-600/20',
  INFO: 'bg-blue-50 text-blue-700 ring-blue-600/20',
}

export function StatusBadge({ value, label }: { value: string; label?: string }) {
  return <span className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs font-bold ring-1 ring-inset ${statusStyles[value] || 'bg-slate-100 text-slate-700 ring-slate-500/20'}`}>{label || value}</span>
}

export function Notice({ type = 'info', children }: { type?: 'info' | 'success' | 'error'; children: ReactNode }) {
  const success = type === 'success'
  const error = type === 'error'
  return (
    <div className={`modal-backdrop flex gap-3 rounded-xl border px-4 py-3 text-sm leading-6 ${success ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : error ? 'border-rose-200 bg-rose-50 text-rose-800' : 'border-blue-200 bg-blue-50 text-blue-800'}`}>
      {success ? <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0" /> : <AlertCircle className="mt-0.5 h-5 w-5 shrink-0" />}
      <div>{children}</div>
    </div>
  )
}

export function Modal({ open, title, onClose, children, width = 'max-w-xl', guardChanges = true }: { open: boolean; title: string; onClose: () => void; children: ReactNode; width?: string; guardChanges?: boolean }) {
  const panel = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const dirtyRef = useRef(false)
  const requestClose = () => { if (!guardChanges || !dirtyRef.current || window.confirm('Biểu mẫu có thay đổi chưa lưu. Bạn có muốn đóng?')) onClose() }
  const closeRef = useRef(requestClose)
  closeRef.current = requestClose
  useEffect(() => {
    if (!open) return
    dirtyRef.current = false
    const previousFocus = document.activeElement as HTMLElement | null
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const focusable = () => panel.current?.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]')
    focusable()?.[0]?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') closeRef.current()
      if (event.key !== 'Tab') return
      const nodes = focusable()
      if (!nodes?.length) return
      const first = nodes[0], last = nodes[nodes.length - 1]
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => { document.body.style.overflow = previousOverflow; document.removeEventListener('keydown', onKeyDown); previousFocus?.focus() }
  }, [open])
  if (!open) return null
  return (
    <div className="modal-backdrop fixed inset-0 z-50 flex items-center justify-center bg-navy/55 p-4 backdrop-blur-[3px]" role="dialog" aria-modal="true" aria-labelledby={titleId} onMouseDown={(e) => e.target === e.currentTarget && requestClose()}>
      <div ref={panel} onChangeCapture={() => { dirtyRef.current = true }} className={`modal-panel max-h-[92vh] w-full overflow-y-auto rounded-2xl bg-white shadow-[0_40px_80px_-24px_rgba(11,22,52,.45)] ${width}`}>
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-line bg-white px-5 py-4">
          <h2 id={titleId} className="text-lg font-bold text-ink">{title}</h2>
          <button onClick={requestClose} className="rounded-lg px-3 py-1.5 text-sm font-semibold text-slate-500 hover:bg-slate-100" aria-label="Đóng">Đóng</button>
        </div>
        <div className="p-5">{children}</div>
      </div>
    </div>
  )
}
