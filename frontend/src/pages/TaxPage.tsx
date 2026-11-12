import { useCallback, useEffect, useState } from 'react'
import { Calculator, CheckCircle2, FileClock, RefreshCw, ShieldAlert } from 'lucide-react'
import { Empty, Loading, Notice, PageHeader, StatusBadge } from '../components/UI'
import { api } from '../lib/api'
import { useAuth } from '../auth/AuthContext'
import { apiError, money, shortDate } from '../lib/format'

interface TaxRecord { id: string; taxType: string; periodStart: string; periodEnd: string; inputAmount: number; outputAmount: number; difference: number; status: string; reviewedAt?: string }
interface TaxSummary { period: { year: number; month: number; from: string; to: string }; vat: { input: number; output: number; difference: number }; extensions: string[]; records: TaxRecord[]; status: string; disclaimer: string }

export default function TaxPage() {
  const { user } = useAuth()
  const canManage = user?.role === 'ADMIN' || user?.role === 'ACCOUNTANT'
  const now = new Date()
  const [month, setMonth] = useState(now.getMonth() + 1)
  const [year, setYear] = useState(now.getFullYear())
  const [data, setData] = useState<TaxSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [working, setWorking] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try { const response = await api.get<TaxSummary>('/tax/summary', { params: { year, month } }); setData(response.data) }
    catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải dữ liệu thuế.') }) }
    finally { setLoading(false) }
  }, [year, month])
  useEffect(() => { load() }, [load])
  const createDraft = async () => { setWorking(true); try { await api.post('/tax/draft-report', null, { params: { year, month } }); setMessage({ type: 'success', text: 'Đã tạo báo cáo thuế dự thảo. Kế toán cần rà soát trước khi sử dụng.' }); await load() } catch (err) { setMessage({ type: 'error', text: apiError(err) }) } finally { setWorking(false) } }
  const review = async (id: string) => { setWorking(true); try { await api.post(`/tax/${id}/review`); setMessage({ type: 'success', text: 'Kế toán đã đánh dấu rà soát nội bộ. Đây vẫn chưa phải tờ khai chính thức.' }); await load() } catch (err) { setMessage({ type: 'error', text: apiError(err) }) } finally { setWorking(false) } }

  return (
    <>
      <PageHeader title="Quản lý thuế" description="Tổng hợp VAT đầu vào, đầu ra và tạo báo cáo dự thảo có bước kế toán rà soát."
        actions={<><select className="field !w-auto" value={month} onChange={(e) => setMonth(Number(e.target.value))}>{Array.from({ length: 12 }, (_, i) => <option key={i + 1} value={i + 1}>Tháng {i + 1}</option>)}</select><input className="field !w-28" type="number" min="2000" max="2200" value={year} onChange={(e) => setYear(Number(e.target.value))}/><button className="btn-secondary" onClick={load}><RefreshCw className="h-4 w-4" /></button></>} />
      <div className="mb-5"><Notice><strong>Không phải tờ khai chính thức:</strong> dữ liệu AI và số liệu tổng hợp chỉ phục vụ quản trị. Cần đối chiếu chứng từ, quy định hiện hành và được kế toán chịu trách nhiệm rà soát.</Notice></div>
      {message && <div className="mb-5"><Notice type={message.type}>{message.text}</Notice></div>}
      {loading ? <Loading /> : data ? <><div className="grid gap-4 sm:grid-cols-3"><article className="panel p-5"><div className="flex items-center justify-between"><p className="text-sm font-semibold text-slate-500">VAT đầu ra</p><Calculator className="h-5 w-5 text-emerald" /></div><p className="mt-4 text-2xl font-extrabold text-ink">{money(data.vat.output)}</p></article><article className="panel p-5"><div className="flex items-center justify-between"><p className="text-sm font-semibold text-slate-500">VAT đầu vào</p><FileClock className="h-5 w-5 text-blue-600" /></div><p className="mt-4 text-2xl font-extrabold text-ink">{money(data.vat.input)}</p></article><article className="panel p-5"><div className="flex items-center justify-between"><p className="text-sm font-semibold text-slate-500">VAT chênh lệch</p><ShieldAlert className="h-5 w-5 text-amber-600" /></div><p className={`mt-4 text-2xl font-extrabold ${data.vat.difference >= 0 ? 'text-rose-600' : 'text-emerald'}`}>{money(data.vat.difference)}</p></article></div><section className="panel mt-6 p-5 sm:p-6"><div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between"><div><h2 className="font-bold text-ink">Báo cáo thuế dự thảo</h2><p className="mt-1 text-sm text-slate-500">Kỳ {month}/{year} · Quy trình: Draft Tax Report → Accountant Review</p></div>{canManage && <button className="btn-primary" disabled={working} onClick={createDraft}><FileClock className="h-4 w-4" />{working ? 'Đang xử lý...' : 'Tạo bản dự thảo'}</button>}</div><div className="mt-5">{data.records.length === 0 ? <Empty title="Chưa có bản dự thảo cho kỳ này" /> : <div className="table-shell !shadow-none"><table className="data-table"><thead><tr><th>Loại</th><th>Kỳ</th><th className="text-right">Đầu vào</th><th className="text-right">Đầu ra</th><th className="text-right">Chênh lệch</th><th>Trạng thái</th><th /></tr></thead><tbody>{data.records.map((record) => <tr key={record.id}><td className="font-bold">{record.taxType}</td><td>{shortDate(record.periodStart)} – {shortDate(record.periodEnd)}</td><td className="text-right">{money(record.inputAmount)}</td><td className="text-right">{money(record.outputAmount)}</td><td className="text-right font-bold">{money(record.difference)}</td><td><StatusBadge value={record.status} /></td><td>{canManage && record.status !== 'ACCOUNTANT_REVIEWED' && <button disabled={working} onClick={() => review(record.id)} className="inline-flex items-center gap-1 rounded-lg px-2 py-1.5 text-xs font-bold text-emerald hover:bg-mint"><CheckCircle2 className="h-3.5 w-3.5" />Kế toán rà soát</button>}</td></tr>)}</tbody></table></div>}</div></section><section className="mt-6 grid gap-3 sm:grid-cols-4">{data.extensions.map((type, i) => <div key={type} className={`rounded-2xl border p-4 ${i === 0 ? 'border-emerald/30 bg-mint' : 'border-dashed border-line bg-white'}`}><p className="font-bold text-ink">{type}</p><p className="mt-1 text-xs text-slate-500">{i === 0 ? 'Đang hỗ trợ tổng hợp' : 'Điểm mở rộng kiến trúc'}</p></div>)}</section></> : <Empty />}
    </>
  )
}
