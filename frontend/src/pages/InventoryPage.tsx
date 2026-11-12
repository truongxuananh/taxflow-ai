import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { ArrowDownToLine, ArrowUpFromLine, RefreshCcw, SlidersHorizontal } from 'lucide-react'
import { Empty, Loading, Modal, Notice, PageHeader, StatusBadge } from '../components/UI'
import { api } from '../lib/api'
import { useAuth } from '../auth/AuthContext'
import { apiError, money, number, shortDateTime } from '../lib/format'
import type { Product } from '../types'

interface StockTx { id: string; type: string; quantity: number; unitCost: number; reference?: string; note?: string; createdAt: string; product: { id: string; sku: string; name: string; unit: string }; warehouse: string }
interface LowStock { id: string; sku: string; name: string; stock: number; minimumStock: number; unit: string }

export default function InventoryPage() {
  const { user } = useAuth()
  const canManage = user?.role === 'ADMIN' || user?.role === 'ACCOUNTANT'
  const [transactions, setTransactions] = useState<StockTx[]>([])
  const [products, setProducts] = useState<Product[]>([])
  const [lowStock, setLowStock] = useState<LowStock[]>([])
  const [loading, setLoading] = useState(true)
  const [modal, setModal] = useState(false)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [form, setForm] = useState({ productId: '', type: 'IN', quantity: 1, unitCost: 0, reference: '', note: '' })

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [t, p, l] = await Promise.all([api.get<StockTx[]>('/inventory/transactions'), api.get<Product[]>('/products'), api.get<LowStock[]>('/inventory/low-stock')])
      setTransactions(t.data); setProducts(p.data); setLowStock(l.data)
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải dữ liệu kho.') }) }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setSaving(true); setMessage(null)
    try { await api.post('/inventory/transactions', { ...form, warehouseId: null }); setModal(false); setMessage({ type: 'success', text: 'Đã ghi nhận giao dịch kho.' }); await load() }
    catch (err) { setMessage({ type: 'error', text: apiError(err) }) }
    finally { setSaving(false) }
  }
  const open = (type: string) => { setForm({ productId: products[0]?.id || '', type, quantity: 1, unitCost: products[0]?.purchasePrice || 0, reference: '', note: '' }); setModal(true) }

  return (
    <>
      <PageHeader title="Kho hàng" description="Nhập, xuất, điều chỉnh và truy vết toàn bộ biến động tồn kho."
        actions={canManage ? <><button className="btn-secondary" onClick={() => open('OUT')}><ArrowUpFromLine className="h-4 w-4" />Xuất kho</button><button className="btn-primary" onClick={() => open('IN')}><ArrowDownToLine className="h-4 w-4" />Nhập kho</button></> : undefined} />
      {message && <div className="mb-5"><Notice type={message.type}>{message.text}</Notice></div>}
      <div className="mb-6 grid gap-4 sm:grid-cols-3"><div className="panel p-5"><p className="text-sm font-semibold text-slate-500">Sản phẩm quản lý</p><p className="mt-3 text-3xl font-extrabold text-ink">{products.length}</p></div><div className="panel p-5"><p className="text-sm font-semibold text-slate-500">Sắp hết hàng</p><p className="mt-3 text-3xl font-extrabold text-rose-600">{lowStock.length}</p></div><div className="panel p-5"><p className="text-sm font-semibold text-slate-500">Giao dịch gần đây</p><p className="mt-3 text-3xl font-extrabold text-ink">{transactions.length}</p></div></div>
      {lowStock.length > 0 && <section className="panel mb-6 p-5"><h2 className="font-bold text-ink">Cảnh báo tồn kho</h2><div className="mt-4 flex flex-wrap gap-2">{lowStock.map((item) => <span key={item.id} className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700"><strong>{item.name}</strong> · {number(item.stock)}/{number(item.minimumStock)} {item.unit}</span>)}</div></section>}
      <section><div className="mb-3 flex items-center justify-between"><h2 className="font-bold text-ink">Lịch sử giao dịch</h2><button onClick={load} className="rounded-lg p-2 text-slate-500 hover:bg-white" aria-label="Làm mới"><RefreshCcw className="h-4 w-4" /></button></div>{loading ? <Loading /> : transactions.length === 0 ? <div className="panel"><Empty /></div> : <div className="table-shell"><table className="data-table"><thead><tr><th>Thời gian</th><th>Loại</th><th>Sản phẩm</th><th>Kho</th><th className="text-right">Số lượng</th><th className="text-right">Đơn giá</th><th>Tham chiếu</th><th>Ghi chú</th></tr></thead><tbody>{transactions.map((tx) => <tr key={tx.id}><td>{shortDateTime(tx.createdAt)}</td><td><StatusBadge value={tx.type === 'IN' ? 'OK' : tx.type === 'OUT' ? 'INFO' : 'MEDIUM'} label={tx.type === 'IN' ? 'Nhập' : tx.type === 'OUT' ? 'Xuất' : 'Điều chỉnh'} /></td><td><p className="font-semibold text-slate-800">{tx.product.name}</p><p className="text-xs text-slate-400">{tx.product.sku}</p></td><td>{tx.warehouse}</td><td className="text-right font-bold">{number(tx.quantity)} {tx.product.unit}</td><td className="text-right">{money(tx.unitCost)}</td><td>{tx.reference || '—'}</td><td className="max-w-56 truncate">{tx.note || '—'}</td></tr>)}</tbody></table></div>}</section>

      <Modal open={canManage && modal} title={form.type === 'IN' ? 'Nhập kho' : form.type === 'OUT' ? 'Xuất kho' : 'Điều chỉnh kho'} onClose={() => setModal(false)}>
        <form onSubmit={submit} className="space-y-4"><div><label className="label">Loại giao dịch</label><div className="grid grid-cols-3 gap-2">{[['IN','Nhập'],['OUT','Xuất'],['ADJUSTMENT','Điều chỉnh']].map(([value,label]) => <button type="button" key={value} onClick={() => setForm({ ...form, type: value })} className={form.type === value ? 'btn-primary' : 'btn-secondary'}>{value === 'IN' ? <ArrowDownToLine className="h-4 w-4" /> : value === 'OUT' ? <ArrowUpFromLine className="h-4 w-4" /> : <SlidersHorizontal className="h-4 w-4" />}{label}</button>)}</div></div><div><label className="label">Sản phẩm</label><select required className="field" value={form.productId} onChange={(e) => { const p = products.find((item) => item.id === e.target.value); setForm({ ...form, productId: e.target.value, unitCost: p?.purchasePrice || 0 }) }}><option value="">Chọn sản phẩm</option>{products.map((p) => <option key={p.id} value={p.id}>{p.sku} — {p.name} (tồn {number(p.stock)})</option>)}</select></div><div className="grid grid-cols-2 gap-3"><div><label className="label">Số lượng {form.type === 'ADJUSTMENT' && '(có thể âm)'}</label><input required type="number" step="0.01" className="field" value={form.quantity} onChange={(e) => setForm({ ...form, quantity: Number(e.target.value) })} /></div><div><label className="label">Đơn giá</label><input required type="number" min="0" className="field" value={form.unitCost} onChange={(e) => setForm({ ...form, unitCost: Number(e.target.value) })} /></div></div><div><label className="label">Tham chiếu</label><input className="field" placeholder="Phiếu nhập, đơn hàng..." value={form.reference} onChange={(e) => setForm({ ...form, reference: e.target.value })} /></div><div><label className="label">Ghi chú</label><textarea className="field min-h-24" value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} /></div>{message?.type === 'error' && <Notice type="error">{message.text}</Notice>}<div className="flex justify-end gap-2"><button type="button" className="btn-secondary" onClick={() => setModal(false)}>Hủy</button><button className="btn-primary" disabled={saving}>{saving ? 'Đang lưu...' : 'Ghi nhận'}</button></div></form>
      </Modal>
    </>
  )
}
