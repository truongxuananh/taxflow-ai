import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { AlertTriangle, CheckCircle2, Clock3, HandCoins, RefreshCw } from 'lucide-react'
import { Empty, Loading, Modal, Notice, PageHeader, StatusBadge, Tabs } from '../components/UI'
import { api } from '../lib/api'
import { useAuth } from '../auth/AuthContext'
import { apiError, money, shortDate } from '../lib/format'
import type { Debt } from '../types'

export default function DebtsPage() {
  const { user } = useAuth()
  const canManage = user?.role === 'ADMIN' || user?.role === 'ACCOUNTANT'
  const [tab, setTab] = useState<'receivables' | 'payables'>('receivables')
  const [receivables, setReceivables] = useState<Debt[]>([])
  const [payables, setPayables] = useState<Debt[]>([])
  const [loading, setLoading] = useState(true)
  const [settling, setSettling] = useState<Debt | null>(null)
  const [amount, setAmount] = useState(0)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try { const [r, p] = await Promise.all([api.get<Debt[]>('/receivables'), api.get<Debt[]>('/payables')]); setReceivables(r.data); setPayables(p.data) }
    catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải công nợ.') }) }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])
  const list = tab === 'receivables' ? receivables : payables
  const stats = useMemo(() => ({ total: list.reduce((sum, item) => sum + item.remainingAmount, 0), overdue: list.filter((item) => item.overdueDays > 0).reduce((sum, item) => sum + item.remainingAmount, 0), dueCount: list.filter((item) => item.overdueDays > 0).length }), [list])

  const openSettle = (debt: Debt) => { setSettling(debt); setAmount(debt.remainingAmount); setMessage(null) }
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (!settling) return; setSaving(true); setMessage(null)
    try { await api.post(`/${tab}/${settling.id}/settle`, null, { params: { amount } }); setSettling(null); setMessage({ type: 'success', text: tab === 'receivables' ? 'Đã ghi nhận thu công nợ.' : 'Đã ghi nhận thanh toán công nợ.' }); await load() }
    catch (err) { setMessage({ type: 'error', text: apiError(err) }) }
    finally { setSaving(false) }
  }

  return (
    <>
      <PageHeader title="Công nợ" description="Theo dõi phải thu khách hàng, phải trả nhà cung cấp và các khoản quá hạn." actions={<button className="btn-secondary" onClick={load}><RefreshCw className="h-4 w-4" />Làm mới</button>} />
      {message && <div className="mb-5"><Notice type={message.type}>{message.text}</Notice></div>}
      <Tabs value={tab} onChange={setTab} items={[{ value: 'receivables', label: 'Phải thu' }, { value: 'payables', label: 'Phải trả' }]} />
      <div className="mb-6 grid gap-4 sm:grid-cols-3"><article className="panel p-5"><div className="flex items-center justify-between"><p className="text-sm font-semibold text-slate-500">Tổng còn lại</p><HandCoins className="h-5 w-5 text-emerald" /></div><p className="mt-4 text-2xl font-extrabold text-ink">{money(stats.total)}</p></article><article className="panel p-5"><div className="flex items-center justify-between"><p className="text-sm font-semibold text-slate-500">Đã quá hạn</p><AlertTriangle className="h-5 w-5 text-rose-500" /></div><p className="mt-4 text-2xl font-extrabold text-rose-600">{money(stats.overdue)}</p></article><article className="panel p-5"><div className="flex items-center justify-between"><p className="text-sm font-semibold text-slate-500">Khoản cần xử lý</p><Clock3 className="h-5 w-5 text-amber-500" /></div><p className="mt-4 text-2xl font-extrabold text-ink">{stats.dueCount}</p></article></div>
      {loading ? <Loading /> : list.length === 0 ? <div className="panel"><Empty title={`Không có công nợ ${tab === 'receivables' ? 'phải thu' : 'phải trả'}`} /></div> : <div className="table-shell"><table className="data-table"><thead><tr><th>{tab === 'receivables' ? 'Khách hàng' : 'Nhà cung cấp'}</th><th className="text-right">Số tiền ban đầu</th><th className="text-right">Còn lại</th><th>Ngày đến hạn</th><th>Quá hạn</th><th>Trạng thái</th><th /></tr></thead><tbody>{list.map((debt) => <tr key={debt.id}><td className="font-semibold text-slate-800">{debt.customer || debt.supplier}</td><td className="text-right">{money(debt.originalAmount)}</td><td className="text-right font-bold text-ink">{money(debt.remainingAmount)}</td><td>{shortDate(debt.dueDate)}</td><td>{debt.overdueDays > 0 ? <span className="font-semibold text-rose-600">{debt.overdueDays} ngày</span> : <span className="text-slate-400">Chưa đến hạn</span>}</td><td><StatusBadge value={debt.overdueDays > 0 ? 'HIGH' : debt.status} label={debt.overdueDays > 0 ? 'Quá hạn' : debt.status} /></td><td>{canManage && <button onClick={() => openSettle(debt)} className="inline-flex items-center gap-1 rounded-lg px-2 py-1.5 text-xs font-bold text-emerald hover:bg-mint"><CheckCircle2 className="h-3.5 w-3.5" />Thanh toán</button>}</td></tr>)}</tbody></table></div>}

      <Modal open={canManage && !!settling} title={tab === 'receivables' ? 'Thu công nợ' : 'Thanh toán công nợ'} onClose={() => setSettling(null)}>
        {settling && <form onSubmit={submit} className="space-y-5"><div className="rounded-xl bg-slate-50 p-4"><p className="text-sm text-slate-500">Đối tác</p><p className="mt-1 font-bold text-ink">{settling.customer || settling.supplier}</p><div className="mt-3 flex justify-between border-t border-line pt-3 text-sm"><span>Còn lại</span><strong>{money(settling.remainingAmount)}</strong></div></div><div><label className="label">Số tiền *</label><input autoFocus required type="number" min="1" max={settling.remainingAmount} className="field" value={amount} onChange={(e) => setAmount(Number(e.target.value))} /></div><Notice>Giao dịch sẽ đồng thời cập nhật công nợ và {tab === 'receivables' ? 'khoản thu' : 'khoản chi'}.</Notice>{message?.type === 'error' && <Notice type="error">{message.text}</Notice>}<div className="flex justify-end gap-2"><button type="button" className="btn-secondary" onClick={() => setSettling(null)}>Hủy</button><button className="btn-primary" disabled={saving}>{saving ? 'Đang xử lý...' : 'Xác nhận'}</button></div></form>}
      </Modal>
    </>
  )
}
