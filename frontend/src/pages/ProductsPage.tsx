import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { Download, Pencil, Plus, Search, Trash2 } from 'lucide-react'
import { useAuth } from '../auth/AuthContext'
import { Empty, Loading, Modal, Notice, PageHeader, StatusBadge } from '../components/UI'
import { api } from '../lib/api'
import { apiError, money, number } from '../lib/format'
import { downloadExport } from '../lib/download'
import type { Product } from '../types'

interface Category { id: string; name: string }
const blank = { sku: '', barcode: '', name: '', categoryId: '', categoryName: '', unit: 'Cái', purchasePrice: 0, sellingPrice: 0, vatRate: 10, stock: 0, minimumStock: 10 }

export default function ProductsPage() {
  const { user } = useAuth()
  const canEdit = user?.role === 'ADMIN' || user?.role === 'ACCOUNTANT'
  const [products, setProducts] = useState<Product[]>([])
  const [categories, setCategories] = useState<Category[]>([])
  const [search, setSearch] = useState('')
  const [onlyLow, setOnlyLow] = useState(false)
  const [loading, setLoading] = useState(true)
  const [modal, setModal] = useState(false)
  const [editing, setEditing] = useState<Product | null>(null)
  const [form, setForm] = useState(blank)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [p, c] = await Promise.all([api.get<Product[]>('/products'), api.get<Category[]>('/categories')])
      setProducts(p.data); setCategories(c.data)
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải danh mục sản phẩm.') }) }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])

  const filtered = useMemo(() => products.filter((p) => {
    const term = search.toLocaleLowerCase('vi').trim()
    const matches = !term || p.name.toLocaleLowerCase('vi').includes(term) || p.sku.toLowerCase().includes(term) || p.barcode?.includes(term)
    return matches && (!onlyLow || p.stockStatus === 'LOW')
  }), [products, search, onlyLow])

  const openCreate = () => { setEditing(null); setForm(blank); setModal(true); setMessage(null) }
  const openEdit = (product: Product) => {
    setEditing(product)
    setForm({ sku: product.sku, barcode: product.barcode || '', name: product.name, categoryId: product.category.id, categoryName: product.category.name, unit: product.unit, purchasePrice: product.purchasePrice, sellingPrice: product.sellingPrice, vatRate: product.vatRate, stock: product.stock, minimumStock: product.minimumStock })
    setModal(true); setMessage(null)
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setSaving(true); setMessage(null)
    try {
      if (editing) await api.put(`/products/${editing.id}`, form)
      else await api.post('/products', form)
      setModal(false); setMessage({ type: 'success', text: editing ? 'Đã cập nhật sản phẩm.' : 'Đã thêm sản phẩm.' }); await load()
    } catch (err) { setMessage({ type: 'error', text: apiError(err) }) }
    finally { setSaving(false) }
  }
  const deactivate = async (product: Product) => {
    if (!window.confirm(`Ngừng kinh doanh “${product.name}”?`)) return
    try { await api.delete(`/products/${product.id}`); setMessage({ type: 'success', text: 'Đã ngừng kinh doanh sản phẩm.' }); await load() }
    catch (err) { setMessage({ type: 'error', text: apiError(err) }) }
  }
  const exportFile = async () => {
    try { await downloadExport('inventory', 'xlsx') }
    catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải tệp Excel.') }) }
  }

  return (
    <>
      <PageHeader title="Sản phẩm" description={`${products.length} sản phẩm đang kinh doanh · Giá bán, VAT và ngưỡng tồn kho được dùng trực tiếp tại POS.`}
        actions={<><button className="btn-secondary" onClick={exportFile}><Download className="h-4 w-4" />Excel</button>{canEdit && <button className="btn-primary" onClick={openCreate}><Plus className="h-4 w-4" />Thêm sản phẩm</button>}</>} />
      {message && <div className="mb-4"><Notice type={message.type}>{message.text}</Notice></div>}
      <div className="panel mb-5 flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
        <div className="relative flex-1"><Search className="absolute left-3.5 top-3 h-5 w-5 text-slate-400"/><input className="field !pl-11" placeholder="Tìm tên, SKU, barcode..." value={search} onChange={(e) => setSearch(e.target.value)} /></div>
        <label className="flex min-h-11 cursor-pointer items-center gap-2 rounded-xl border border-line px-4 text-sm font-semibold text-slate-600"><input type="checkbox" checked={onlyLow} onChange={(e) => setOnlyLow(e.target.checked)} className="accent-emerald" />Chỉ hàng sắp hết</label>
      </div>
      {loading ? <Loading /> : filtered.length === 0 ? <div className="panel"><Empty title="Không có sản phẩm phù hợp" /></div> : (
        <div className="table-shell"><table className="data-table"><thead><tr><th>SKU / Barcode</th><th>Sản phẩm</th><th>Nhóm</th><th className="text-right">Giá vốn</th><th className="text-right">Giá bán</th><th>VAT</th><th className="text-right">Tồn kho</th><th>Trạng thái</th><th /></tr></thead><tbody>
          {filtered.map((product) => <tr key={product.id}><td><p className="font-bold text-ink">{product.sku}</p><p className="mt-1 text-xs text-slate-400">{product.barcode || '—'}</p></td><td><p className="font-semibold text-slate-800">{product.name}</p><p className="mt-1 text-xs text-slate-400">ĐVT: {product.unit}</p></td><td>{product.category.name}</td><td className="text-right">{money(product.purchasePrice)}</td><td className="text-right font-semibold text-ink">{money(product.sellingPrice)}</td><td>{number(product.vatRate)}%</td><td className="text-right"><strong>{number(product.stock)}</strong><span className="ml-1 text-slate-400">{product.unit}</span><p className="mt-1 text-xs text-slate-400">Tối thiểu {number(product.minimumStock)}</p></td><td><StatusBadge value={product.stockStatus} label={product.stockStatus === 'LOW' ? 'Sắp hết' : 'Đủ hàng'} /></td><td><div className="flex justify-end gap-1">{canEdit && <button onClick={() => openEdit(product)} className="rounded-lg p-2 text-slate-400 hover:bg-mint hover:text-emerald" aria-label="Sửa"><Pencil className="h-4 w-4" /></button>}{user?.role === 'ADMIN' && <button onClick={() => deactivate(product)} className="rounded-lg p-2 text-slate-400 hover:bg-rose-50 hover:text-rose-600" aria-label="Ngừng kinh doanh"><Trash2 className="h-4 w-4" /></button>}</div></td></tr>)}
        </tbody></table></div>
      )}

      <Modal open={modal} title={editing ? 'Cập nhật sản phẩm' : 'Thêm sản phẩm'} onClose={() => setModal(false)}>
        <form onSubmit={submit} className="space-y-4">
          <div className="grid grid-cols-2 gap-3"><div><label className="label">SKU *</label><input className="field" required value={form.sku} onChange={(e) => setForm({ ...form, sku: e.target.value })} /></div><div><label className="label">Barcode</label><input className="field" value={form.barcode} onChange={(e) => setForm({ ...form, barcode: e.target.value })} /></div></div>
          <div><label className="label">Tên sản phẩm *</label><input className="field" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></div>
          <div className="grid grid-cols-2 gap-3"><div><label className="label">Nhóm hàng</label><select className="field" value={form.categoryId} onChange={(e) => { const category = categories.find((c) => c.id === e.target.value); setForm({ ...form, categoryId: e.target.value, categoryName: category?.name || 'Khác' }) }}><option value="">Khác</option>{categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}</select></div><div><label className="label">Đơn vị tính</label><input className="field" required value={form.unit} onChange={(e) => setForm({ ...form, unit: e.target.value })} /></div></div>
          <div className="grid grid-cols-2 gap-3"><div><label className="label">Giá vốn</label><input type="number" min="0" className="field" value={form.purchasePrice} onChange={(e) => setForm({ ...form, purchasePrice: Number(e.target.value) })} /></div><div><label className="label">Giá bán</label><input type="number" min="0" className="field" value={form.sellingPrice} onChange={(e) => setForm({ ...form, sellingPrice: Number(e.target.value) })} /></div></div>
          <div className="grid grid-cols-3 gap-3"><div><label className="label">VAT %</label><input type="number" min="0" max="100" className="field" value={form.vatRate} onChange={(e) => setForm({ ...form, vatRate: Number(e.target.value) })} /></div><div><label className="label">{editing ? 'Tồn hiện tại' : 'Tồn ban đầu'}</label><input type="number" min="0" className="field" disabled={!!editing} value={form.stock} onChange={(e) => setForm({ ...form, stock: Number(e.target.value) })} /></div><div><label className="label">Tồn tối thiểu</label><input type="number" min="0" className="field" value={form.minimumStock} onChange={(e) => setForm({ ...form, minimumStock: Number(e.target.value) })} /></div></div>
          {message?.type === 'error' && <Notice type="error">{message.text}</Notice>}
          <div className="flex justify-end gap-2 pt-2"><button type="button" className="btn-secondary" onClick={() => setModal(false)}>Hủy</button><button className="btn-primary" disabled={saving}>{saving ? 'Đang lưu...' : 'Lưu sản phẩm'}</button></div>
        </form>
      </Modal>
    </>
  )
}
