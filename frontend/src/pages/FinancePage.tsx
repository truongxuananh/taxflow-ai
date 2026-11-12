import { useReducedMotion } from '../hooks/useReducedMotion'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ArrowDownCircle, ArrowUpCircle, Plus, RefreshCw, Wallet } from 'lucide-react'
import { Area, AreaChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Empty, Loading, Modal, Notice, PageHeader, StatusBadge, Tabs } from '../components/UI'
import { api } from '../lib/api'
import { useAuth } from '../auth/AuthContext'
import { apiError, money, shortDate } from '../lib/format'
import type { CashFlow } from '../types'

interface Transaction { id: string; category: string; description: string; amount: number; vatAmount?: number; transactionDate: string; paymentMethod: string; reference?: string }
const expenseCategories = ['Nhập hàng', 'Lương', 'Marketing', 'Vận hành', 'Thuế', 'Điện', 'Nước', 'Thuê mặt bằng', 'Chi khác']
const incomeCategories = ['Bán hàng', 'Khách thanh toán', 'Thu khác']

export default function FinancePage() {
  const reducedMotion = useReducedMotion()
  const { user } = useAuth()
  const canManage = user?.role === 'ADMIN' || user?.role === 'ACCOUNTANT'
  const [tab, setTab] = useState<'cashflow' | 'income' | 'expense'>('cashflow')
  const [cashflow, setCashflow] = useState<CashFlow | null>(null)
  const [incomes, setIncomes] = useState<Transaction[]>([])
  const [expenses, setExpenses] = useState<Transaction[]>([])
  const [loading, setLoading] = useState(true)
  const [modal, setModal] = useState<'income' | 'expense' | null>(null)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [form, setForm] = useState({ category: 'Thu khác', description: '', amount: 0, vatAmount: 0, transactionDate: new Date().toISOString().slice(0, 10), paymentMethod: 'TRANSFER', reference: '' })

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [c, i, e] = await Promise.all([api.get<CashFlow>('/cash-flow', { params: { days: 30 } }), api.get<Transaction[]>('/incomes'), api.get<Transaction[]>('/expenses')])
      setCashflow(c.data); setIncomes(i.data); setExpenses(e.data)
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải dữ liệu tài chính.') }) }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])

  const open = (type: 'income' | 'expense') => {
    setForm({ category: type === 'income' ? 'Thu khác' : 'Chi khác', description: '', amount: 0, vatAmount: 0, transactionDate: new Date().toISOString().slice(0, 10), paymentMethod: 'TRANSFER', reference: '' }); setModal(type); setMessage(null)
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (!modal) return; setSaving(true); setMessage(null)
    try { await api.post(modal === 'income' ? '/incomes' : '/expenses', { ...form, transactionDate: new Date(`${form.transactionDate}T12:00:00Z`).toISOString() }); setModal(null); setMessage({ type: 'success', text: `Đã ghi nhận khoản ${modal === 'income' ? 'thu' : 'chi'}.` }); await load() }
    catch (err) { setMessage({ type: 'error', text: apiError(err) }) }
    finally { setSaving(false) }
  }
  const compact = (value: number) => new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value)
  const list = tab === 'income' ? incomes : expenses

  return (
    <>
      <PageHeader title="Thu chi & dòng tiền" description="Opening Balance + Cash Inflow − Cash Outflow = Closing Balance"
        actions={canManage ? <><button className="btn-secondary" onClick={() => open('expense')}><ArrowDownCircle className="h-4 w-4 text-rose-600" />Ghi chi</button><button className="btn-primary" onClick={() => open('income')}><ArrowUpCircle className="h-4 w-4" />Ghi thu</button></> : undefined} />
      {message && <div className="mb-5"><Notice type={message.type}>{message.text}</Notice></div>}
      <Tabs value={tab} onChange={setTab} items={[{ value: 'cashflow', label: 'Dòng tiền' }, { value: 'income', label: 'Khoản thu' }, { value: 'expense', label: 'Khoản chi' }]} />
      {loading ? <Loading /> : tab === 'cashflow' && cashflow ? <>
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">{[
          ['Số dư đầu kỳ', cashflow.openingBalance, 'neutral'], ['Tiền vào', cashflow.cashInflow, 'in'], ['Tiền ra', cashflow.cashOutflow, 'out'], ['Dòng tiền ròng', cashflow.netCashFlow, cashflow.netCashFlow >= 0 ? 'in' : 'out'], ['Số dư cuối kỳ', cashflow.closingBalance, 'neutral'],
        ].map(([label,value,tone]) => <article key={String(label)} className="panel p-5"><p className="text-sm font-semibold text-slate-500">{String(label)}</p><p className={`mt-3 text-xl font-extrabold ${tone === 'in' ? 'text-emerald' : tone === 'out' ? 'text-rose-600' : 'text-ink'}`}>{money(Number(value))}</p></article>)}</div>
        <section className="panel mt-6 p-5 sm:p-6"><div className="mb-6 flex items-center justify-between"><div><h2 className="font-bold text-ink">Biến động dòng tiền</h2><p className="mt-1 text-sm text-slate-500">30 ngày gần nhất</p></div><Wallet className="h-5 w-5 text-emerald" /></div><div className="h-[330px]"><ResponsiveContainer width="100%" height="100%"><AreaChart data={cashflow.daily} margin={{ left: -10, right: 10 }}><CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#e8eef8"/><XAxis dataKey="date" tickFormatter={(v) => new Date(v).toLocaleDateString('vi-VN',{day:'2-digit',month:'2-digit'})} tick={{fontSize:12}} axisLine={false} tickLine={false}/><YAxis tickFormatter={compact} tick={{fontSize:12}} axisLine={false} tickLine={false}/><Tooltip formatter={(value) => money(Number(value))} labelFormatter={(label) => shortDate(String(label))}/><Legend iconType="circle"/><Area isAnimationActive={!reducedMotion} type="monotone" dataKey="inflow" name="Tiền vào" stroke="#2350F0" fill="#eaf2ff" strokeWidth={2}/><Area isAnimationActive={!reducedMotion} type="monotone" dataKey="outflow" name="Tiền ra" stroke="#D9452F" fill="#ffe9e9" strokeWidth={2}/></AreaChart></ResponsiveContainer></div></section>
      </> : tab !== 'cashflow' ? <section><div className="mb-3 flex items-center justify-between"><h2 className="font-bold text-ink">{tab === 'income' ? 'Danh sách khoản thu' : 'Danh sách khoản chi'}</h2><button onClick={load} className="rounded-lg p-2 text-slate-500 hover:bg-white"><RefreshCw className="h-4 w-4" /></button></div>{list.length === 0 ? <div className="panel"><Empty /></div> : <div className="table-shell"><table className="data-table"><thead><tr><th>Ngày</th><th>Nhóm</th><th>Nội dung</th><th className="text-right">Số tiền</th>{tab === 'expense' && <th className="text-right">VAT</th>}<th>Thanh toán</th><th>Tham chiếu</th></tr></thead><tbody>{list.map((item) => <tr key={item.id}><td>{shortDate(item.transactionDate)}</td><td><StatusBadge value={tab === 'income' ? 'OK' : 'MEDIUM'} label={item.category} /></td><td className="max-w-80 truncate font-semibold">{item.description}</td><td className={`text-right font-bold ${tab === 'income' ? 'text-emerald' : 'text-rose-600'}`}>{tab === 'income' ? '+' : '−'}{money(item.amount)}</td>{tab === 'expense' && <td className="text-right">{money(item.vatAmount)}</td>}<td>{item.paymentMethod}</td><td>{item.reference || '—'}</td></tr>)}</tbody></table></div>}</section> : <div className="panel"><Empty /></div>}

      <Modal open={canManage && !!modal} title={modal === 'income' ? 'Ghi nhận khoản thu' : 'Ghi nhận khoản chi'} onClose={() => setModal(null)}>
        <form onSubmit={submit} className="space-y-4"><div><label className="label">Nhóm</label><select className="field" value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}>{(modal === 'income' ? incomeCategories : expenseCategories).map((item) => <option key={item}>{item}</option>)}</select></div><div><label className="label">Nội dung *</label><input required className="field" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></div><div className="grid grid-cols-2 gap-3"><div><label className="label">Số tiền *</label><input required min="1" type="number" className="field" value={form.amount} onChange={(e) => setForm({ ...form, amount: Number(e.target.value) })} /></div>{modal === 'expense' ? <div><label className="label">VAT</label><input min="0" type="number" className="field" value={form.vatAmount} onChange={(e) => setForm({ ...form, vatAmount: Number(e.target.value) })} /></div> : <div><label className="label">Ngày</label><input required type="date" className="field" value={form.transactionDate} onChange={(e) => setForm({ ...form, transactionDate: e.target.value })} /></div>}</div>{modal === 'expense' && <div><label className="label">Ngày</label><input required type="date" className="field" value={form.transactionDate} onChange={(e) => setForm({ ...form, transactionDate: e.target.value })} /></div>}<div className="grid grid-cols-2 gap-3"><div><label className="label">Phương thức</label><select className="field" value={form.paymentMethod} onChange={(e) => setForm({ ...form, paymentMethod: e.target.value })}><option value="CASH">Tiền mặt</option><option value="TRANSFER">Chuyển khoản</option><option value="QR">QR</option><option value="CARD">Thẻ</option><option value="EWALLET">Ví điện tử</option></select></div><div><label className="label">Tham chiếu</label><input className="field" value={form.reference} onChange={(e) => setForm({ ...form, reference: e.target.value })} /></div></div>{message?.type === 'error' && <Notice type="error">{message.text}</Notice>}<div className="flex justify-end gap-2"><button type="button" className="btn-secondary" onClick={() => setModal(null)}>Hủy</button><button className="btn-primary" disabled={saving}><Plus className="h-4 w-4" />{saving ? 'Đang lưu...' : 'Ghi nhận'}</button></div></form>
      </Modal>
    </>
  )
}
