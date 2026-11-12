import { useCallback, useEffect, useState } from 'react'
import { Download, FileBarChart, FileSpreadsheet, FileText, Plus, RefreshCw } from 'lucide-react'
import { Empty, Loading, Notice, PageHeader } from '../components/UI'
import { api } from '../lib/api'
import { downloadExport } from '../lib/download'
import { apiError, money, shortDate } from '../lib/format'

interface Summary { period: { type: string; from: string; to: string }; revenue: number; expense: number; estimatedProfit: number; vat: { input: number; output: number; difference: number }; cashFlow: { inflow: number; outflow: number; net: number }; debt: { receivable: number; payable: number }; inventoryValue: number; disclaimer: string }
interface SavedReport { id: string; periodType: string; periodStart: string; periodEnd: string; status: string; createdAt: string }
const datasets = [
  ['sales', 'Bán hàng'], ['invoices', 'Hóa đơn'], ['incomes', 'Khoản thu'], ['expenses', 'Khoản chi'],
  ['receivables', 'Công nợ phải thu'], ['payables', 'Công nợ phải trả'], ['tax', 'Thuế'], ['inventory', 'Tồn kho'],
]

export default function ReportsPage() {
  const [period, setPeriod] = useState('monthly')
  const [summary, setSummary] = useState<Summary | null>(null)
  const [reports, setReports] = useState<SavedReport[]>([])
  const [loading, setLoading] = useState(true)
  const [working, setWorking] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try { const [s, r] = await Promise.all([api.get<Summary>('/reports/summary', { params: { period } }), api.get<SavedReport[]>('/reports')]); setSummary(s.data); setReports(r.data) }
    catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải báo cáo.') }) }
    finally { setLoading(false) }
  }, [period])
  useEffect(() => { load() }, [load])
  const generate = async () => { setWorking(true); try { await api.post('/reports/generate', null, { params: { period } }); setMessage({ type: 'success', text: 'Đã tạo báo cáo quản trị dự thảo.' }); await load() } catch (err) { setMessage({ type: 'error', text: apiError(err) }) } finally { setWorking(false) } }
  const download = async (dataset: string, format: 'csv' | 'xlsx' | 'pdf') => { setWorking(true); setMessage(null); try { await downloadExport(dataset, format, summary ? { from: summary.period.from.slice(0,10), to: summary.period.to.slice(0,10) } : undefined) } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải tệp xuất.') }) } finally { setWorking(false) } }

  return (
    <>
      <PageHeader title="Báo cáo" description="Tổng hợp theo ngày, tuần hoặc tháng; xuất dữ liệu nghiệp vụ ra PDF, Excel và CSV."
        actions={<><select className="field !w-auto" value={period} onChange={(e) => setPeriod(e.target.value)}><option value="daily">Hôm nay</option><option value="weekly">7 ngày</option><option value="monthly">Tháng này</option></select><button className="btn-secondary" onClick={load}><RefreshCw className="h-4 w-4" /></button><button className="btn-primary" onClick={generate} disabled={working}><Plus className="h-4 w-4" />Tạo báo cáo</button></>} />
      {message && <div className="mb-5"><Notice type={message.type}>{message.text}</Notice></div>}
      {loading ? <Loading /> : summary ? <><div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">{[
        ['Doanh thu', summary.revenue], ['Chi phí', summary.expense], ['Lợi nhuận ước tính', summary.estimatedProfit], ['Tồn kho theo giá vốn', summary.inventoryValue],
        ['VAT đầu ra', summary.vat.output], ['VAT đầu vào', summary.vat.input], ['Công nợ phải thu', summary.debt.receivable], ['Công nợ phải trả', summary.debt.payable],
      ].map(([label,value]) => <article key={String(label)} className="panel p-5"><p className="text-sm font-semibold text-slate-500">{String(label)}</p><p className="mt-3 text-xl font-extrabold text-ink">{money(Number(value))}</p></article>)}</div><Notice>{summary.disclaimer}</Notice></> : <Empty />}

      <section className="mt-7"><div className="mb-4"><h2 className="text-lg font-bold text-ink">Xuất dữ liệu</h2><p className="mt-1 text-sm text-slate-500">Tệp được tạo trực tiếp từ dữ liệu doanh nghiệp đang chọn.</p></div><div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{datasets.map(([key,label]) => <article key={key} className="panel p-4"><div className="mb-4 flex items-center gap-3"><span className="rounded-xl bg-mint p-2.5 text-emerald"><FileBarChart className="h-5 w-5" /></span><p className="font-bold text-ink">{label}</p></div><div className="grid grid-cols-3 gap-2"><button disabled={working} onClick={() => download(key, 'pdf')} className="rounded-lg border border-line px-2 py-2 text-xs font-bold text-slate-600 hover:border-emerald hover:text-emerald"><FileText className="mx-auto mb-1 h-4 w-4"/>PDF</button><button disabled={working} onClick={() => download(key, 'xlsx')} className="rounded-lg border border-line px-2 py-2 text-xs font-bold text-slate-600 hover:border-emerald hover:text-emerald"><FileSpreadsheet className="mx-auto mb-1 h-4 w-4"/>Excel</button><button disabled={working} onClick={() => download(key, 'csv')} className="rounded-lg border border-line px-2 py-2 text-xs font-bold text-slate-600 hover:border-emerald hover:text-emerald"><Download className="mx-auto mb-1 h-4 w-4"/>CSV</button></div></article>)}</div></section>

      <section className="mt-7"><h2 className="mb-4 text-lg font-bold text-ink">Lịch sử báo cáo</h2>{reports.length === 0 ? <div className="panel"><Empty /></div> : <div className="table-shell"><table className="data-table"><thead><tr><th>Loại kỳ</th><th>Từ ngày</th><th>Đến ngày</th><th>Trạng thái</th><th>Ngày tạo</th></tr></thead><tbody>{reports.map((report) => <tr key={report.id}><td className="font-bold">{report.periodType}</td><td>{shortDate(report.periodStart)}</td><td>{shortDate(report.periodEnd)}</td><td>{report.status}</td><td>{shortDate(report.createdAt)}</td></tr>)}</tbody></table></div>}</section>
    </>
  )
}
