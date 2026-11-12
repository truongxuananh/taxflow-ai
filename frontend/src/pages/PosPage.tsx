import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Barcode, CheckCircle2, CreditCard, Minus, Plus, Printer, QrCode, Receipt, Search, Share2, ShoppingBag, Trash2, WalletCards } from 'lucide-react'
import { api } from '../lib/api'
import { apiError, money, number } from '../lib/format'
import type { Product } from '../types'
import { Empty, Loading, Modal, Notice, PageHeader } from '../components/UI'

interface Customer { id: string; code: string; name: string; taxCode?: string; address?: string; email?: string }
interface CartLine { product: Product; quantity: number }
interface OrderResult { id: string; number: string; subtotal: number; discount: number; vatAmount: number; total: number; paidAmount: number; balance: number; invoice?: { id: string; number: string; status: string; isLegallyIssued: boolean } }

const methods = [
  { value: 'CASH', label: 'Tiền mặt', icon: WalletCards },
  { value: 'TRANSFER', label: 'Chuyển khoản', icon: CreditCard },
  { value: 'QR', label: 'QR', icon: QrCode },
  { value: 'CARD', label: 'Thẻ', icon: CreditCard },
  { value: 'EWALLET', label: 'Ví điện tử', icon: WalletCards },
]

export default function PosPage() {
  const [products, setProducts] = useState<Product[]>([])
  const [customers, setCustomers] = useState<Customer[]>([])
  const [cart, setCart] = useState<CartLine[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [discount, setDiscount] = useState(0)
  const [paidAmount, setPaidAmount] = useState<number | ''>('')
  const [paymentMethod, setPaymentMethod] = useState('CASH')
  const [customerId, setCustomerId] = useState('')
  const [vatInvoice, setVatInvoice] = useState(false)
  const [result, setResult] = useState<OrderResult | null>(null)
  const searchRef = useRef<HTMLInputElement>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [productResponse, customerResponse] = await Promise.all([api.get<Product[]>('/products'), api.get<Customer[]>('/customers')])
      setProducts(productResponse.data); setCustomers(customerResponse.data)
    } catch (err) { setError(apiError(err, 'Không thể tải dữ liệu POS.')) }
    finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])

  const filtered = useMemo(() => {
    const value = search.trim().toLocaleLowerCase('vi')
    if (!value) return products
    return products.filter((p) => p.name.toLocaleLowerCase('vi').includes(value) || p.sku.toLowerCase().includes(value) || p.barcode?.includes(value))
  }, [products, search])
  const subtotal = cart.reduce((sum, line) => sum + line.product.sellingPrice * line.quantity, 0)
  const effectiveDiscount = Math.min(subtotal, Math.max(0, discount))
  let allocatedDiscount = 0
  const vat = cart.reduce((sum, line, index) => {
    const amount = line.product.sellingPrice * line.quantity
    const share = index === cart.length - 1 ? effectiveDiscount - allocatedDiscount : subtotal > 0 ? Math.round(effectiveDiscount * amount / subtotal * 100) / 100 : 0
    allocatedDiscount += share
    return sum + Math.round((amount - share) * line.product.vatRate) / 100
  }, 0)
  const total = Math.max(0, subtotal + vat - effectiveDiscount)
  const selectedCustomer = customers.find((customer) => customer.id === customerId)

  const add = (product: Product) => {
    if (product.stock <= 0) return
    setCart((current) => {
      const existing = current.find((line) => line.product.id === product.id)
      if (!existing) return [...current, { product, quantity: 1 }]
      return current.map((line) => line.product.id === product.id ? { ...line, quantity: Math.min(product.stock, line.quantity + 1) } : line)
    })
    setSearch(''); searchRef.current?.focus()
  }
  const updateQuantity = (id: string, next: number) => setCart((current) => current.map((line) => line.product.id === id ? { ...line, quantity: Math.max(1, Math.min(line.product.stock, next)) } : line))
  const remove = (id: string) => setCart((current) => current.filter((line) => line.product.id !== id))
  const handleSearchKey = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== 'Enter') return
    const exact = products.find((p) => p.barcode === search.trim() || p.sku.toLowerCase() === search.trim().toLowerCase())
    if (exact) add(exact)
  }

  const checkout = async () => {
    if (cart.length === 0) return
    setSubmitting(true); setError('')
    try {
      const amount = paidAmount === '' ? total : Number(paidAmount)
      const { data } = await api.post<OrderResult>('/orders', {
        customerId: customerId || null,
        items: cart.map((line) => ({ productId: line.product.id, quantity: line.quantity })),
        discount: effectiveDiscount, paidAmount: amount, paymentMethod, requestVatInvoice: vatInvoice,
        customerName: selectedCustomer?.name || null, customerTaxCode: selectedCustomer?.taxCode || null,
        customerAddress: selectedCustomer?.address || null, customerEmail: selectedCustomer?.email || null,
      })
      setResult(data); setCart([]); setDiscount(0); setPaidAmount(''); setVatInvoice(false); await load()
    } catch (err) { setError(apiError(err)) }
    finally { setSubmitting(false) }
  }

  const printReceipt = () => {
    if (!result) return
    const popup = window.open('', '_blank', 'width=480,height=700')
    if (!popup) { setError('Trình duyệt đang chặn cửa sổ in. Hãy cho phép pop-up rồi thử lại.'); return }
    popup.document.write(`<!doctype html><html lang="vi"><head><meta charset="utf-8"><title>${result.number}</title><style>body{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;max-width:380px;margin:32px auto;color:#17251f}h1{text-align:center;font-size:22px}.muted{text-align:center;color:#66756e;font-size:13px}.row{display:flex;justify-content:space-between;padding:9px 0;border-bottom:1px dashed #ccd7d1}.total{font-size:20px;font-weight:800;border-top:2px solid #17251f;margin-top:8px}.note{font-size:12px;color:#66756e;line-height:1.5;margin-top:20px}@media print{button{display:none}}</style></head><body><h1>HÓA ĐƠN BÁN HÀNG</h1><p class="muted">TaxFlow AI · ${result.number}</p><div class="row"><span>Tiền hàng</span><strong>${money(result.subtotal)}</strong></div><div class="row"><span>Giảm giá</span><strong>−${money(result.discount)}</strong></div><div class="row"><span>VAT</span><strong>${money(result.vatAmount)}</strong></div><div class="row total"><span>Tổng cộng</span><strong>${money(result.total)}</strong></div><div class="row"><span>Đã thanh toán</span><strong>${money(result.paidAmount)}</strong></div><p class="note">Đây là hóa đơn bán hàng/phiếu thu từ hệ thống quản trị. Không thay thế hóa đơn điện tử hợp pháp.</p><button onclick="window.print()">In</button></body></html>`)
    popup.document.close(); popup.focus(); setTimeout(() => popup.print(), 250)
  }
  const shareReceipt = async () => {
    if (!result) return
    const text = `Đơn hàng ${result.number}\nTổng cộng: ${money(result.total)}\nĐã thanh toán: ${money(result.paidAmount)}`
    try {
      if (navigator.share) await navigator.share({ title: `Đơn hàng ${result.number}`, text })
      else { await navigator.clipboard.writeText(text); setError('Đã sao chép thông tin đơn hàng để gửi cho khách.') }
    } catch { /* Người dùng hủy bảng chia sẻ. */ }
  }

  return (
    <>
      <PageHeader title="Bán hàng POS" description="Tìm nhanh bằng tên, SKU hoặc quét barcode. Tồn kho và dòng tiền được cập nhật khi hoàn tất đơn." />
      {error && <div className="mb-4"><Notice type="error">{error}</Notice></div>}
      <div className="grid gap-5 xl:grid-cols-[1.35fr_.85fr]">
        <section className="panel min-h-[680px] p-4 sm:p-5">
          <div className="relative mb-5"><Search className="absolute left-4 top-3.5 h-5 w-5 text-slate-400" /><input ref={searchRef} autoFocus className="field !pl-12 !pr-12" placeholder="Tên sản phẩm, SKU hoặc barcode..." value={search} onChange={(e) => setSearch(e.target.value)} onKeyDown={handleSearchKey}/><Barcode className="absolute right-4 top-3.5 h-5 w-5 text-emerald" /></div>
          {loading ? <Loading /> : filtered.length === 0 ? <Empty title="Không tìm thấy sản phẩm" /> : (
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 2xl:grid-cols-4">
              {filtered.map((product) => (
                <button key={product.id} onClick={() => add(product)} disabled={product.stock <= 0} className="group min-h-36 rounded-2xl border border-line bg-white p-4 text-left transition hover:-translate-y-0.5 hover:border-emerald hover:shadow-panel disabled:cursor-not-allowed disabled:opacity-45">
                  <div className="mb-4 flex items-center justify-between"><span className="rounded-lg bg-mint px-2 py-1 text-xs font-bold text-emerald">{product.sku}</span><ShoppingBag className="h-5 w-5 text-slate-300 group-hover:text-emerald" /></div>
                  <p className="line-clamp-2 min-h-10 text-sm font-bold leading-5 text-ink">{product.name}</p>
                  <p className="mt-3 text-base font-extrabold text-emerald">{money(product.sellingPrice)}</p>
                  <p className={`mt-1 text-xs ${product.stock <= product.minimumStock ? 'font-semibold text-rose-600' : 'text-slate-400'}`}>Còn {number(product.stock)} {product.unit}</p>
                </button>
              ))}
            </div>
          )}
        </section>

        <aside className="panel self-start xl:sticky xl:top-24">
          <div className="flex items-center justify-between border-b border-line px-5 py-4"><div><h2 className="font-bold text-ink">Giỏ hàng</h2><p className="mt-0.5 text-xs text-slate-400">{cart.length} mặt hàng</p></div><Receipt className="h-5 w-5 text-emerald" /></div>
          <div className="max-h-[330px] overflow-y-auto px-5">
            {cart.length === 0 ? <Empty title="Chưa có sản phẩm" description="Chọn sản phẩm bên trái để thêm vào đơn." /> : cart.map((line) => (
              <div key={line.product.id} className="border-b border-slate-100 py-4 last:border-0">
                <div className="flex items-start justify-between gap-3"><div><p className="font-semibold text-slate-800">{line.product.name}</p><p className="mt-1 text-xs text-slate-400">{money(line.product.sellingPrice)} · VAT {number(line.product.vatRate)}%</p></div><button onClick={() => remove(line.product.id)} className="rounded-lg p-1.5 text-slate-400 hover:bg-rose-50 hover:text-rose-600" aria-label={`Xóa ${line.product.name}`}><Trash2 className="h-4 w-4" /></button></div>
                <div className="mt-3 flex items-center justify-between"><div className="flex items-center rounded-xl border border-line"><button onClick={() => updateQuantity(line.product.id, line.quantity - 1)} className="p-2 text-slate-500"><Minus className="h-4 w-4" /></button><span className="w-10 text-center text-sm font-bold">{line.quantity}</span><button onClick={() => updateQuantity(line.product.id, line.quantity + 1)} className="p-2 text-slate-500"><Plus className="h-4 w-4" /></button></div><p className="font-bold text-ink">{money(line.product.sellingPrice * line.quantity)}</p></div>
              </div>
            ))}
          </div>
          <div className="space-y-4 border-t border-line p-5">
            <div><label className="label" htmlFor="customer">Khách hàng</label><select id="customer" className="field" value={customerId} onChange={(e) => setCustomerId(e.target.value)}><option value="">Khách lẻ</option>{customers.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></div>
            <div className="grid grid-cols-2 gap-3"><div><label className="label" htmlFor="discount">Giảm giá</label><input id="discount" type="number" min="0" className="field" value={discount} onChange={(e) => setDiscount(Number(e.target.value))} /></div><div><label className="label" htmlFor="paid">Khách trả</label><input id="paid" type="number" min="0" className="field" placeholder={String(total)} value={paidAmount} onChange={(e) => setPaidAmount(e.target.value === '' ? '' : Number(e.target.value))} /></div></div>
            <div><span className="label">Phương thức ghi nhận thanh toán</span><p className="mb-2 text-xs text-slate-400">Ghi nhận thủ công; QR/thẻ/ví chưa tự trừ tiền hay xác nhận từ ngân hàng.</p><div className="grid grid-cols-3 gap-2">{methods.slice(0, 5).map(({ value, label, icon: Icon }) => <button key={value} onClick={() => setPaymentMethod(value)} className={`flex min-h-14 flex-col items-center justify-center rounded-xl border px-1 py-2 text-[11px] font-semibold transition ${paymentMethod === value ? 'border-emerald bg-mint text-forest' : 'border-line text-slate-500 hover:border-emerald/50'}`}><Icon className="mb-1 h-4 w-4" />{label}</button>)}</div></div>
            <label className="flex cursor-pointer items-start gap-3 rounded-xl bg-slate-50 p-3"><input type="checkbox" className="mt-1 h-4 w-4 accent-emerald" checked={vatInvoice} onChange={(e) => setVatInvoice(e.target.checked)} /><span><span className="block text-sm font-semibold text-slate-700">Khách yêu cầu hóa đơn VAT</span><span className="mt-0.5 block text-xs leading-5 text-slate-400">Tạo bản nháp để kiểm tra trước khi gửi nhà cung cấp.</span></span></label>
            <div className="space-y-2 border-t border-dashed border-line pt-4 text-sm"><div className="flex justify-between text-slate-500"><span>Tiền hàng</span><span>{money(subtotal)}</span></div><div className="flex justify-between text-slate-500"><span>VAT</span><span>{money(vat)}</span></div><div className="flex justify-between text-slate-500"><span>Giảm giá</span><span>-{money(discount)}</span></div><div className="flex items-end justify-between pt-2"><span className="font-bold text-ink">Tổng thanh toán</span><span className="text-2xl font-extrabold text-emerald">{money(total)}</span></div></div>
            <button className="btn-primary w-full" disabled={cart.length === 0 || submitting} onClick={checkout}>{submitting ? 'Đang xử lý...' : 'Hoàn tất đơn hàng'}<CheckCircle2 className="h-5 w-5" /></button>
          </div>
        </aside>
      </div>

      <Modal open={!!result} title="Đơn hàng đã hoàn tất" onClose={() => setResult(null)}>
        {result && <div className="space-y-5"><Notice type="success">Đơn <strong>{result.number}</strong> đã được lưu, tồn kho và dòng tiền đã cập nhật.</Notice><div className="rounded-2xl bg-slate-50 p-5"><div className="space-y-3 text-sm"><div className="flex justify-between"><span>Tiền hàng</span><strong>{money(result.subtotal)}</strong></div><div className="flex justify-between"><span>VAT</span><strong>{money(result.vatAmount)}</strong></div><div className="flex justify-between"><span>Đã thu</span><strong>{money(result.paidAmount)}</strong></div><div className="flex justify-between border-t border-line pt-3 text-base"><span>Tổng cộng</span><strong className="text-emerald">{money(result.total)}</strong></div>{result.balance > 0 && <div className="flex justify-between text-rose-600"><span>Còn phải thu</span><strong>{money(result.balance)}</strong></div>}</div></div>{result.invoice && <Notice>Hóa đơn <strong>{result.invoice.number}</strong> đang ở trạng thái {result.invoice.status}. Chưa được phát hành hợp pháp.</Notice>}<div className="grid grid-cols-2 gap-2"><button className="btn-secondary" onClick={printReceipt}><Printer className="h-4 w-4" />In phiếu</button><button className="btn-secondary" onClick={shareReceipt}><Share2 className="h-4 w-4" />Gửi khách</button></div><button className="btn-primary w-full" onClick={() => setResult(null)}>Bán đơn tiếp theo</button></div>}
      </Modal>
    </>
  )
}
