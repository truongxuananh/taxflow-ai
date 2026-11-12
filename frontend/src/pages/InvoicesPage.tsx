import { useCallback, useEffect, useState, type FormEvent, type PointerEvent } from 'react'
import { CheckCircle2, CloudUpload, Crop, Download, FileCheck2, FileSearch, RefreshCw, Send, ShieldAlert, X } from 'lucide-react'
import { Empty, Loading, Modal, Notice, PageHeader, StatusBadge, Tabs } from '../components/UI'
import { api } from '../lib/api'
import { apiError, money, number, shortDate } from '../lib/format'
import type { Invoice } from '../types'

interface ValidationIssue { field: string; code: string; message: string; severity: 'error' | 'warning' }
interface Extraction {
  provider: string; supplierName: string; supplierTaxCode?: string; invoiceNumber: string; serial?: string;
  invoiceDate?: string; subtotal: number; vatAmount: number; total: number; confidenceScore: number;
  items: Array<{ description: string; unit: string; quantity: number; unitPrice: number; vatRate: number; vatAmount: number; total: number }>
  providerWarnings: string[]
  pageCount: number
  evidence?: Array<{ field: string; value?: string; page: number; x: number; y: number; width: number; height: number }>
}
interface OCRResult { document: { id: string; originalFileName: string; status: string }; extraction: Extraction; validation: ValidationIssue[]; workflow: string[] }

interface Selection { x: number; y: number; width: number; height: number }
interface RegionResult { field: string; value: string; confidence: number; page: number; boundingBox: Selection; source: string }

export default function InvoicesPage() {
  const [tab, setTab] = useState<'OUTGOING' | 'INCOMING'>('OUTGOING')
  const [documents, setDocuments] = useState<Array<{ id: string; originalFileName: string; status: string; analysis?: { resultJson: string; validationJson: string } }>>([])
  const [reviewItems, setReviewItems] = useState<Extraction['items']>([])
  const [dirty, setDirty] = useState(false)
  const [invoices, setInvoices] = useState<Invoice[]>([])
  const [loading, setLoading] = useState(true)
  const [uploadOpen, setUploadOpen] = useState(false)
  const [file, setFile] = useState<File | null>(null)
  const [uploading, setUploading] = useState(false)
  const [ocrSeconds, setOcrSeconds] = useState(0)
  const [ocrStage, setOcrStage] = useState('Đang chuẩn bị tệp...')
  const [ocr, setOcr] = useState<OCRResult | null>(null)
  const [originalUrl, setOriginalUrl] = useState<string | null>(null)
  const [previewPage, setPreviewPage] = useState(1)
  const [previewPageCount, setPreviewPageCount] = useState(1)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [review, setReview] = useState({ supplierName: '', supplierTaxCode: '', invoiceNumber: '', serial: '', invoiceDate: '', subtotal: 0, vatAmount: 0, total: 0 })
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null)
  const [selectedField, setSelectedField] = useState('supplierName')
  const [selection, setSelection] = useState<Selection | null>(null)
  const [dragStart, setDragStart] = useState<{ x: number; y: number } | null>(null)
  const [regionBusy, setRegionBusy] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try { const [response, docs] = await Promise.all([api.get<Invoice[]>('/invoices', { params: { direction: tab } }), api.get('/documents')]); setInvoices(response.data); setDocuments(docs.data.filter((d: { status: string }) => d.status !== 'CONFIRMED')) }
    catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể tải hóa đơn.') }) }
    finally { setLoading(false) }
  }, [tab])
  useEffect(() => { load() }, [load])

  useEffect(() => {
    if (!uploading) return
    const startedAt = Date.now()
    const timer = window.setInterval(() => setOcrSeconds(Math.floor((Date.now() - startedAt) / 1000)), 250)
    /* The backend exposes no per-field progress; only elapsed time is shown.
    const stages = [
      window.setTimeout(() => setOcrStage('AI đang đọc nội dung hóa đơn...'), 700),
      window.setTimeout(() => setOcrStage('Đang nhận diện số hóa đơn, MST và tổng tiền...'), 2500),
      window.setTimeout(() => setOcrStage('Đang kiểm tra dữ liệu trước khi hiển thị...'), 7000),
    ]
    */
    return () => { window.clearInterval(timer) }
  }, [uploading])

  useEffect(() => {
    if (!ocr) { setOriginalUrl(null); setPreviewPage(1); setPreviewPageCount(1); return }
    setPreviewPage(1)
  }, [ocr])

  useEffect(() => {
    if (!ocr) return
    let objectUrl: string | null = null
    setPreviewLoading(true)
    api.get(`/documents/${ocr.document.id}/preview`, { params: { page: previewPage }, responseType: 'blob' })
      .then(({ data, headers }) => { const count = Number(headers['x-page-count']); if (count > 0) setPreviewPageCount(count); objectUrl = URL.createObjectURL(data); setOriginalUrl(objectUrl) })
      .catch(() => setOriginalUrl(null))
      .finally(() => setPreviewLoading(false))
    return () => { if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [ocr, previewPage])

  useEffect(() => {
    if (!ocr) return
    setPreviewPageCount(Math.max(1, ocr.extraction.pageCount || 1))
  }, [ocr])

  const upload = async (event: FormEvent) => {
    event.preventDefault(); if (!file) return
    setUploading(true); setOcrSeconds(0); setOcrStage('Đang tải lên và xử lý OCR. Vui lòng chờ...'); setMessage(null)
    try {
      const form = new FormData(); form.append('file', file)
      const { data } = await api.post<OCRResult>('/documents/upload', form, { timeout: 120_000 })
      setOcr(data); setReviewItems(data.extraction.items || []); setDirty(true); setReview({ supplierName: data.extraction.supplierName, supplierTaxCode: data.extraction.supplierTaxCode || '', invoiceNumber: data.extraction.invoiceNumber, serial: data.extraction.serial || '', invoiceDate: data.extraction.invoiceDate ? data.extraction.invoiceDate.slice(0, 10) : '', subtotal: data.extraction.subtotal, vatAmount: data.extraction.vatAmount, total: data.extraction.total }); setUploadOpen(false)
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể OCR hóa đơn.') }) }
    finally { setUploading(false) }
  }
  const confirm = async (event: FormEvent) => {
    event.preventDefault(); if (!ocr) return
    setSaving(true); setMessage(null)
    try {
      await api.post(`/documents/${ocr.document.id}/confirm`, { ...review, invoiceDate: new Date(`${review.invoiceDate}T00:00:00Z`).toISOString(), items: reviewItems })
      setDirty(false); setOcr(null); setTab('INCOMING'); setMessage({ type: 'success', text: 'Đã xác nhận và lưu hóa đơn đầu vào.' }); if (tab === 'INCOMING') await load()
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Dữ liệu chưa hợp lệ.') }) }
    finally { setSaving(false) }
  }
  const issue = async (invoice: Invoice) => {
    if (!window.confirm(`Gửi yêu cầu phát hành hóa đơn ${invoice.number}?`)) return
    try { const { data } = await api.post<{ message: string }>(`/invoices/${invoice.id}/issue`); setMessage({ type: 'success', text: data.message }); await load() }
    catch (err) { setMessage({ type: 'error', text: apiError(err) }) }
  }


  const pointFromEvent = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)), y: Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height)) }
  }
  const startSelection = (event: PointerEvent<HTMLDivElement>) => {
    if (!ocr || !originalUrl) return
    const point = pointFromEvent(event); setDragStart(point); setSelection({ ...point, width: 0, height: 0 })
    event.currentTarget.setPointerCapture(event.pointerId)
  }
  const moveSelection = (event: PointerEvent<HTMLDivElement>) => {
    if (!dragStart) return
    const point = pointFromEvent(event)
    setSelection({ x: Math.min(dragStart.x, point.x), y: Math.min(dragStart.y, point.y), width: Math.abs(point.x - dragStart.x), height: Math.abs(point.y - dragStart.y) })
  }
  const endSelection = () => setDragStart(null)
  const runRegionOcr = async () => {
    if (!ocr || !selection || selection.width < 0.005 || selection.height < 0.005) return
    setRegionBusy(true); setMessage(null)
    try {
      const { data } = await api.post<RegionResult>(`/documents/${ocr.document.id}/ocr-region`, { field: selectedField, page: previewPage, ...selection })
      const value = data.value.trim()
      if (!value) { setMessage({ type: 'error', text: 'OCR vùng không đọc được nội dung. Hãy chọn vùng sát chữ hơn.' }); return }
      setReview((current) => {
        const next = { ...current }
        if (selectedField === 'supplierName') next.supplierName = value
        else if (selectedField === 'supplierTaxCode') next.supplierTaxCode = value.replace(/\D/g, '')
        else if (selectedField === 'invoiceNumber') next.invoiceNumber = value.replace(/\s+/g, '')
        else if (selectedField === 'serial') next.serial = value.replace(/\s+/g, '')
        else if (selectedField === 'invoiceDate') {
          const match = value.match(/(\d{1,2})[\/-](\d{1,2})[\/-](\d{4})/); if (match) next.invoiceDate = `${match[3]}-${match[2].padStart(2, '0')}-${match[1].padStart(2, '0')}`
        } else if (['subtotal', 'vatAmount', 'total'].includes(selectedField)) {
          const amount = Number(value.replace(/[^0-9]/g, ''))
          if (Number.isFinite(amount)) (next as unknown as Record<string, string | number>)[selectedField] = amount
        }
        return next
      })
      setOcr((current) => current ? { ...current, extraction: { ...current.extraction, evidence: [...(current.extraction.evidence || []).filter((b) => b.field !== selectedField), { field: selectedField, value, page: previewPage, ...data.boundingBox }] } } : current)
      setMessage({ type: 'success', text: `Đã OCR lại vùng ${selectedField} (${Math.round(data.confidence * 100)}%). Hãy kiểm tra trước khi xác nhận.` })
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể OCR vùng đã chọn.') }) }
    finally { setRegionBusy(false) }
  }

  const downloadTrainingDataset = async () => {
    try {
      const { data } = await api.get('/ai/training-dataset', { responseType: 'blob' })
      const url = URL.createObjectURL(data); const anchor = document.createElement('a'); anchor.href = url; anchor.download = `taxflow-ocr-training-${new Date().toISOString().slice(0, 10)}.jsonl`; anchor.click(); URL.revokeObjectURL(url)
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể xuất training dataset.') }) }
  }

  const closeReview = () => { if (!dirty || window.confirm('Thông tin chưa được xác nhận. Bạn vẫn có thể mở lại chứng từ gốc trong danh sách chờ. Đóng màn hình chỉnh sửa?')) { setOcr(null); setDirty(false) } }
  const openDocument = async (document: typeof documents[number], retry = false) => {
    try {
      const data: OCRResult = retry ? (await api.post(`/documents/${document.id}/retry`, null, { timeout: 180_000 })).data : {
        document: { id: document.id, originalFileName: document.originalFileName, status: document.status },
        extraction: JSON.parse(document.analysis?.resultJson || '{}', (_, value) => value && typeof value === 'object' && !Array.isArray(value) ? Object.fromEntries(Object.entries(value).map(([key,v]) => [key[0].toLowerCase()+key.slice(1),v])) : value), validation: JSON.parse(document.analysis?.validationJson || '[]', (_, value) => value && typeof value === 'object' && !Array.isArray(value) ? Object.fromEntries(Object.entries(value).map(([key,v]) => [key[0].toLowerCase()+key.slice(1),v])) : value), workflow: ['AI', 'Validation', 'User Review', 'Confirm', 'Database']
      }
      const x = data.extraction
      setOcr(data); setReviewItems(x.items || []); setDirty(true)
      setReview({ supplierName: x.supplierName || '', supplierTaxCode: x.supplierTaxCode || '', invoiceNumber: x.invoiceNumber || '', serial: x.serial || '', invoiceDate: x.invoiceDate?.slice(0,10) || '', subtotal: x.subtotal || 0, vatAmount: x.vatAmount || 0, total: x.total || 0 })
    } catch (err) { setMessage({ type: 'error', text: apiError(err, 'Không thể mở chứng từ.') }) }
  }
  const validationFor = (field: string) => ocr?.validation.find((issue) => issue.field === field)
  const needsReview = (field: string, value: string | number) => {
    const issue = validationFor(field)
    return Boolean(issue) || value === '' || value === 0
  }
  const fieldClass = (field: string, value: string | number) =>
    needsReview(field, value) ? 'field border-amber-400 bg-amber-50 focus:border-amber-500 focus:ring-amber-200' : 'field'
  const reviewHint = (field: string, value: string | number) => {
    if (!needsReview(field, value)) return null
    const issue = validationFor(field)
    return <p className="mt-1 text-xs font-semibold text-amber-700">{issue?.message || 'OCR chưa đọc rõ, vui lòng kiểm tra.'}</p>
  }

  return (
    <>
      {ocr && (() => {
        const unread = [
          ['Nhà cung cấp', ocr.extraction.supplierName, 'supplierName'],
          ['Mã số thuế', ocr.extraction.supplierTaxCode, 'supplierTaxCode'],
          ['Số hóa đơn', ocr.extraction.invoiceNumber, 'invoiceNumber'],
          ['Ngày hóa đơn', ocr.extraction.invoiceDate, 'invoiceDate'],
          ['Tiền hàng', ocr.extraction.subtotal, 'subtotal'],
          ['VAT', ocr.extraction.vatAmount, 'vatAmount'],
          ['Tổng tiền', ocr.extraction.total, 'total']
        ].filter(([, value, field]) => !value || value === 0 || ocr.validation.some((issue) => issue.field === field))
        return unread.length > 0 ? <div className="mx-auto mb-4 max-w-7xl rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900"><p className="font-bold">OCR chưa đọc rõ, cần kiểm tra:</p><div className="mt-2 flex flex-wrap gap-2">{unread.map(([label, , field]) => <span key={field} className="rounded-full border border-amber-300 bg-white px-3 py-1 font-semibold">{label}</span>)}</div></div> : null
      })()}

      <PageHeader title="Hóa đơn & OCR" description="Hóa đơn đầu ra, đầu vào và quy trình AI có kiểm duyệt. Bản demo không thay thế nhà cung cấp hóa đơn điện tử."
        actions={<><button className="btn-secondary" onClick={downloadTrainingDataset}><Download className="h-4 w-4" />Dataset OCR</button><button className="btn-secondary" onClick={load}><RefreshCw className="h-4 w-4" />Làm mới</button><button className="btn-primary" onClick={() => { setUploadOpen(true); setFile(null); setMessage(null) }}><CloudUpload className="h-4 w-4" />Tải hóa đơn đầu vào</button></>} />
      {message && <div className="mb-5"><Notice type={message.type}>{message.text}</Notice></div>}
      <Tabs value={tab} onChange={setTab} items={[{ value: 'OUTGOING', label: 'Hóa đơn đầu ra' }, { value: 'INCOMING', label: 'Hóa đơn đầu vào' }]} />
      <div className="mb-5"><Notice><strong>Lưu ý pháp lý:</strong> “Issued” chỉ được coi là phát hành hợp pháp khi một nhà cung cấp hóa đơn điện tử phù hợp xác nhận. `DemoInvoiceProvider` luôn giữ trạng thái Pending.</Notice></div>
      {documents.length > 0 && <section className="panel mb-5 p-4"><h2 className="mb-3 font-bold">Chứng từ chờ xử lý ({documents.length})</h2><div className="max-h-52 space-y-2 overflow-y-auto">{documents.map(d => <div key={d.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-line p-3"><div><p className="text-sm font-semibold">{d.originalFileName}</p><p className="text-xs text-slate-500">{d.status}</p></div><div className="flex gap-2">{d.status === 'PENDING_REVIEW' && <button className="btn-secondary" onClick={() => openDocument(d)}>Tiếp tục kiểm tra</button>}<button className="btn-secondary" onClick={() => openDocument(d,true)}>OCR lại</button></div></div>)}</div></section>}
      {loading ? <Loading /> : invoices.length === 0 ? <div className="panel"><Empty title="Chưa có hóa đơn" /></div> : (
        <div className="table-shell"><table className="data-table"><thead><tr><th>Số / Ký hiệu</th><th>Ngày</th><th>Đối tác</th><th>MST</th><th className="text-right">Tiền hàng</th><th className="text-right">VAT</th><th className="text-right">Tổng tiền</th><th>Trạng thái</th><th>Provider</th><th /></tr></thead><tbody>{invoices.map((invoice) => <tr key={invoice.id}><td><p className="font-bold text-ink">{invoice.number}</p><p className="mt-1 text-xs text-slate-400">{invoice.serial || '—'}</p></td><td>{shortDate(invoice.invoiceDate)}</td><td className="max-w-56 truncate font-semibold">{invoice.partyName}</td><td>{invoice.partyTaxCode || '—'}</td><td className="text-right">{money(invoice.subtotal)}</td><td className="text-right">{money(invoice.vatAmount)}</td><td className="text-right font-bold text-ink">{money(invoice.total)}</td><td><div className="flex items-center gap-2"><StatusBadge value={invoice.status} />{invoice.isLegallyIssued ? <FileCheck2 className="h-4 w-4 text-emerald" aria-label="Đã phát hành hợp pháp" /> : <ShieldAlert className="h-4 w-4 text-amber-500" aria-label="Chưa phát hành hợp pháp" />}</div></td><td><p>{invoice.provider}</p>{invoice.confidenceScore != null && <p className="mt-1 text-xs text-slate-400">Tin cậy {number(invoice.confidenceScore * 100)}%</p>}</td><td>{tab === 'OUTGOING' && !invoice.isLegallyIssued && invoice.status !== 'Cancelled' && <button onClick={() => issue(invoice)} className="inline-flex items-center gap-1 rounded-lg px-2 py-1.5 text-xs font-bold text-emerald hover:bg-mint"><Send className="h-3.5 w-3.5"/>Gửi phát hành</button>}</td></tr>)}</tbody></table></div>
      )}

      <Modal open={uploadOpen} title="AI đọc hóa đơn đầu vào" onClose={() => !uploading && setUploadOpen(false)}>
        <form onSubmit={upload} className="space-y-5"><div className="rounded-2xl border-2 border-dashed border-line bg-slate-50 p-8 text-center"><CloudUpload className="mx-auto h-10 w-10 text-emerald" /><p className="mt-3 font-bold text-ink">Chọn PDF, JPG hoặc PNG</p><p className="mt-1 text-sm text-slate-500">Dung lượng tối đa 10 MB</p><input disabled={uploading} className="mt-5 block w-full text-sm text-slate-500 file:mr-3 file:rounded-lg file:border-0 file:bg-mint file:px-4 file:py-2 file:font-semibold file:text-emerald" type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={(e) => setFile(e.target.files?.[0] || null)} required /></div>{uploading ? <div className="rounded-xl border border-emerald/20 bg-mint p-4"><div className="flex items-center justify-between gap-3"><p className="font-bold text-forest">{ocrStage}</p><span className="text-sm font-semibold text-emerald">{ocrSeconds}s</span></div><div className="mt-3 h-2 overflow-hidden rounded-full bg-white"><div className="h-full w-2/3 animate-pulse rounded-full bg-emerald" /></div><p className="mt-2 text-xs text-slate-500">Kết quả sẽ hiện ngay khi AI đọc xong.</p></div> : <Notice>Luồng realtime: AI → Validation → Người dùng kiểm tra → Xác nhận → Database.</Notice>}<button className="btn-primary w-full" disabled={!file || uploading}>{uploading ? 'AI đang đọc hóa đơn...' : 'Tải lên và phân tích'}<FileSearch className="h-4 w-4" /></button></form>
      </Modal>

      <Modal open={!!ocr} title="OCR Review Studio" onClose={closeReview} guardChanges={false} width="max-w-7xl">
        {ocr && <form onSubmit={confirm} className="space-y-5"><div className="grid gap-5 lg:grid-cols-[1.15fr_1fr]"><section className="rounded-2xl border border-line bg-slate-50 p-3"><div className="mb-3 flex flex-wrap items-center justify-between gap-2"><div><p className="font-bold text-ink">Chứng từ gốc</p><p className="text-xs text-slate-500">Kéo chuột để chọn vùng rồi OCR lại trường đang chọn.</p></div><div className="flex gap-2"><button type="button" className="btn-secondary" onClick={() => setSelection(null)}><X className="h-4 w-4"/>Bỏ vùng</button><button type="button" className="btn-primary" disabled={!selection || regionBusy || previewLoading} onClick={runRegionOcr}><Crop className="h-4 w-4"/>{regionBusy ? 'Đang OCR...' : 'OCR vùng'}</button></div></div><div className="mb-3 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-line bg-white p-2">
  <div className="flex items-center gap-2">
    <button type="button" className="btn-secondary" disabled={previewPage <= 1 || previewLoading} onClick={() => { setSelection(null); setPreviewPage((p) => Math.max(1, p - 1)) }}>Trang trước</button>
    <span className="rounded-lg bg-slate-100 px-3 py-2 text-xs font-bold text-slate-600">Trang {previewPage}/{previewPageCount}</span>
    <button type="button" className="btn-secondary" disabled={previewPage >= previewPageCount || previewLoading} onClick={() => { setSelection(null); setPreviewPage((p) => Math.min(previewPageCount, p + 1)) }}>Trang sau</button>
  </div>
  <div className="flex items-center gap-2">
    <span className="text-xs text-slate-500">PDF nhiều trang được chuyển thành ảnh PNG để xem và khoanh vùng OCR.</span>
    {originalUrl && <button type="button" className="btn-secondary" onClick={() => { const a = document.createElement('a'); a.href = originalUrl; a.download = `${ocr.document.originalFileName.replace(/\.[^.]+$/, '')}-page-${previewPage}.png`; a.click() }}>Tải ảnh trang</button>}
  </div>
</div>
{originalUrl ? <div className="overflow-auto rounded-xl bg-white p-2">
  <div className="flex min-h-[420px] items-start justify-center">
    <div className="relative inline-block max-w-full cursor-crosshair select-none" onPointerDown={startSelection} onPointerMove={moveSelection} onPointerUp={endSelection} onPointerCancel={endSelection}>
      <img src={originalUrl} draggable={false} alt={`Ảnh hóa đơn trang ${previewPage}`} className="block max-h-[680px] max-w-full object-contain"/>
      {ocr.extraction.evidence?.filter((box) => box.page === previewPage).map((box, index) => <span key={`${box.field}-${index}`} title={`${box.field}: ${box.value || 'đã nhận diện'}`} className={`pointer-events-none absolute border-2 ${box.field === selectedField ? 'border-emerald-500 bg-emerald-300/20' : 'border-red-500 bg-red-300/10'}`} style={{ left: `${box.x * 100}%`, top: `${box.y * 100}%`, width: `${box.width * 100}%`, height: `${box.height * 100}%` }}/>) }
      {selection && <span className="pointer-events-none absolute border-2 border-blue-600 bg-blue-300/20" style={{ left: `${selection.x * 100}%`, top: `${selection.y * 100}%`, width: `${selection.width * 100}%`, height: `${selection.height * 100}%` }}/>}
    </div>
  </div>
</div> : <Loading/>}
<p className="mt-2 text-xs text-slate-500">Ảnh hiển thị cạnh thông tin OCR. Bounding box cho biết vùng hệ thống đã đọc; bạn có thể kéo chuột để chọn vùng trên từng trang và OCR lại.</p></section><section className="space-y-4"><div className="grid gap-3 sm:grid-cols-5">{ocr.workflow.map((step, i) => <div key={step} className={`rounded-xl border p-3 text-center text-xs font-bold ${i < 3 ? 'border-emerald/30 bg-mint text-forest' : 'border-line bg-slate-50 text-slate-400'}`}>{i + 1}. {step}</div>)}</div><div className="flex flex-col gap-3 rounded-xl bg-slate-50 p-4 sm:flex-row sm:items-center sm:justify-between"><div><p className="font-bold text-ink">{ocr.document.originalFileName}</p><p className="mt-1 text-sm text-slate-500">Provider: {ocr.extraction.provider}</p></div><div className="text-right"><p className="text-xs font-semibold text-slate-400">ĐỘ TIN CẬY</p><p className="text-xl font-extrabold text-emerald">{number(ocr.extraction.confidenceScore * 100)}%</p></div></div>{(ocr.validation.length > 0 || ocr.extraction.providerWarnings?.length > 0) && <div className="space-y-2">{ocr.extraction.providerWarnings?.map((warning) => <Notice key={warning}>{warning}</Notice>)}{ocr.validation.map((issue) => <Notice key={`${issue.field}-${issue.code}`} type={issue.severity === 'error' ? 'error' : 'info'}><strong>{issue.field}:</strong> {issue.message}</Notice>)}</div>}<div className="grid gap-4 sm:grid-cols-2"><div><label className="label">Nhà cung cấp *</label><input required className="field" onFocus={() => setSelectedField('supplierName')} value={review.supplierName} onChange={(e) => setReview({ ...review, supplierName: e.target.value })} /></div><div><label className="label">Mã số thuế *</label><input required pattern="[0-9]{10}|[0-9]{13}" className="field" onFocus={() => setSelectedField('supplierTaxCode')} value={review.supplierTaxCode} onChange={(e) => setReview({ ...review, supplierTaxCode: e.target.value })} /></div><div><label className="label">Số hóa đơn *</label><input required className="field" onFocus={() => setSelectedField('invoiceNumber')} value={review.invoiceNumber} onChange={(e) => setReview({ ...review, invoiceNumber: e.target.value })} /></div><div><label className="label">Ký hiệu</label><input className="field" onFocus={() => setSelectedField('serial')} value={review.serial} onChange={(e) => setReview({ ...review, serial: e.target.value })} /></div><div><label className="label">Ngày hóa đơn *</label><input required type="date" className="field" onFocus={() => setSelectedField('invoiceDate')} value={review.invoiceDate} onChange={(e) => setReview({ ...review, invoiceDate: e.target.value })} /></div><div><label className="label">Tiền hàng *</label><input required min="0" type="number" className="field" onFocus={() => setSelectedField('subtotal')} value={review.subtotal} onChange={(e) => setReview({ ...review, subtotal: Number(e.target.value), total: Number(e.target.value) + review.vatAmount })} /></div><div><label className="label">VAT *</label><input required min="0" type="number" className="field" onFocus={() => setSelectedField('vatAmount')} value={review.vatAmount} onChange={(e) => setReview({ ...review, vatAmount: Number(e.target.value), total: review.subtotal + Number(e.target.value) })} /></div><div><label className="label">Tổng tiền *</label><input required min="1" type="number" className="field" onFocus={() => setSelectedField('total')} value={review.total} onChange={(e) => setReview({ ...review, total: Number(e.target.value) })} /></div></div></section></div><section className="rounded-xl border border-line p-4"><div className="mb-3 flex items-center justify-between"><h3 className="font-bold">Chi tiết hàng hóa ({reviewItems.length})</h3><button type="button" className="btn-secondary" onClick={() => setReviewItems([...reviewItems, { description:'',unit:'Cái',quantity:1,unitPrice:0,vatRate:0,vatAmount:0,total:0 }])}>Thêm dòng</button></div><p className="mb-3 text-xs text-slate-500">Sửa số liệu theo chứng từ gốc. Thành tiền phải bằng số lượng × đơn giá + tiền VAT; tổng các dòng phải khớp tổng hóa đơn.</p><div className="overflow-x-auto"><table className="data-table"><thead><tr>{['Mô tả','ĐVT','Số lượng','Đơn giá','VAT %','Tiền VAT','Thành tiền',''].map((h,i)=><th key={i}>{h}</th>)}</tr></thead><tbody>{reviewItems.map((item,index)=><tr key={index}>{(['description','unit','quantity','unitPrice','vatRate','vatAmount','total'] as const).map(field=><td key={field}><input aria-label={`${field} dòng ${index+1}`} className={`field ${field==='description'?'min-w-56':'min-w-24'}`} type={field==='description'||field==='unit'?'text':'number'} step="0.01" value={item[field]} onChange={e=>{setDirty(true);setReviewItems(current=>current.map((row,i)=>i===index?{...row,[field]:field==='description'||field==='unit'?e.target.value:Number(e.target.value)}:row))}}/></td>)}<td><button type="button" className="text-rose-600" onClick={()=>setReviewItems(current=>current.filter((_,i)=>i!==index))}>Xóa</button></td></tr>)}</tbody></table></div><p className="mt-3 text-sm font-semibold">Tổng dòng: {money(reviewItems.reduce((sum,row)=>sum+row.total,0))} / Tổng hóa đơn: {money(review.total)}</p></section>{message?.type === 'error' && <Notice type="error">{message.text}</Notice>}<div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end"><button type="button" className="btn-secondary" onClick={closeReview}>Để sau</button><button className="btn-primary" disabled={saving}>{saving ? 'Đang lưu...' : 'Tôi đã kiểm tra — Xác nhận'}<CheckCircle2 className="h-4 w-4" /></button></div></form>}
      </Modal>
    </>
  )
}
