import { useReducedMotion } from '../hooks/useReducedMotion'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { AlertTriangle, Bot, CalendarRange, RefreshCw, Send, ShieldAlert, Sparkles, UserRound } from 'lucide-react'
import { Area, AreaChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Empty, Loading, Notice, PageHeader, StatusBadge, Tabs } from '../components/UI'
import { api } from '../lib/api'
import { apiError, money, shortDateTime } from '../lib/format'

interface AssistantResponse { answer: string; value?: number; source?: string; calculatedAt: string; mode?: string }
interface ChatMessage { role: 'user' | 'assistant'; text: string; source?: string; time: string; mode?: string }
interface Forecast { quality?: { holdoutDays: number; inflowMae: number; outflowMae: number; baselineInflowMae: number }; status: string; method?: string; historicalActiveDays?: number; requiredActiveDays?: number; availableActiveDays?: number; horizon: number; disclaimer?: string; series: Array<{ date: string; projectedInflow: number; projectedOutflow: number; projectedNet: number }> }
interface AIAlert { id: string; type: string; severity: string; title: string; message: string; entityType?: string; entityId?: string; isRead: boolean; createdAt: string }
interface ScanResult { status?: string; scannedAt: string; method: string; invoiceRecords: number; newAlerts: number; duplicateAlerts: number; mlAlerts: number }
interface AssistantStatus { mode: string; keyConfigured: boolean; model: string; connectionVerified: boolean }
const suggestions = ['Tóm tắt tình hình tài chính tháng này', 'So sánh doanh thu tháng này với tháng trước', 'Ai đang thiếu tiền bên mình nhiều nhất?', 'Có hóa đơn hay giao dịch nào đáng chú ý?', 'Sản phẩm nào sắp hết hàng?']

export default function AssistantPage() {
  const reducedMotion = useReducedMotion()
  const [tab, setTab] = useState<'chat' | 'forecast' | 'anomaly'>('chat')
  const [question, setQuestion] = useState('')
  const [messages, setMessages] = useState<ChatMessage[]>([{ role: 'assistant', text: 'Tôi sẵn sàng truy vấn số liệu doanh nghiệp. Mọi con số đều lấy từ dữ liệu đã lưu.', time: new Date().toISOString() }])
  const [assistantStatus, setAssistantStatus] = useState<AssistantStatus | null>(null)
  const [asking, setAsking] = useState(false)
  const [horizon, setHorizon] = useState(30)
  const [forecast, setForecast] = useState<Forecast | null>(null)
  const [alerts, setAlerts] = useState<AIAlert[]>([])
  const [scan, setScan] = useState<ScanResult | null>(null)
  const [loadingForecast, setLoadingForecast] = useState(false)
  const [loadingAlerts, setLoadingAlerts] = useState(false)
  const [error, setError] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => { api.get<AssistantStatus>('/ai/assistant-status').then(r => { if (typeof r.data.keyConfigured === 'boolean') setAssistantStatus(r.data) }).catch(() => {}) }, [])
  useEffect(() => { api.get<ChatMessage[]>('/ai/chat-history').then(r=>{if(r.data.length)setMessages(r.data)}).catch(()=>{}) }, [])
  // Chỉ cuộn khung hội thoại, không kéo cả trang xuống.
  useEffect(() => { const box = bottomRef.current?.parentElement; box?.scrollTo?.({ top: box.scrollHeight, behavior: reducedMotion ? 'auto' : 'smooth' }) }, [messages, asking, reducedMotion])
  useEffect(() => {
    if (tab !== 'forecast') return
    const load = async () => { setLoadingForecast(true); setError(''); try { const { data } = await api.get<Forecast>('/ai/forecast', { params: { horizon } }); setForecast(data) } catch (err) { setError(apiError(err)) } finally { setLoadingForecast(false) } }
    load()
  }, [tab, horizon])
  useEffect(() => { if (tab === 'anomaly') loadAlerts() }, [tab])

  const loadAlerts = async () => { setLoadingAlerts(true); setError(''); try { const { data } = await api.get<AIAlert[]>('/ai/alerts'); setAlerts(data) } catch (err) { setError(apiError(err)) } finally { setLoadingAlerts(false) } }
  const scanAnomalies = async () => { setLoadingAlerts(true); setError(''); try { const { data } = await api.post<ScanResult>('/ai/scan-anomalies'); setScan(data); await loadAlerts() } catch (err) { setError(apiError(err)) } finally { setLoadingAlerts(false) } }

  const ask = async (event?: FormEvent, suggested?: string) => {
    event?.preventDefault(); const text = (suggested || question).trim(); if (!text || asking) return
    setMessages((current) => [...current, { role: 'user', text, time: new Date().toISOString() }]); setQuestion(''); setAsking(true); setError('')
    try { const { data } = await api.post<AssistantResponse>('/ai/assistant', { question: text }); setMessages((current) => [...current, { role: 'assistant', text: data.answer, source: data.source, time: data.calculatedAt, mode: data.mode }]) }
    catch (err) { setMessages((current) => [...current, { role: 'assistant', text: apiError(err, 'Không thể truy vấn dữ liệu.'), time: new Date().toISOString() }]) }
    finally { setAsking(false) }
  }
  const compact = (value: number) => new Intl.NumberFormat('vi-VN', { notation: 'compact', maximumFractionDigits: 1 }).format(value)

  return <>
    <PageHeader title="Trợ lý & AI tài chính" description="Hỏi dữ liệu, dự báo dòng tiền bằng ML và phát hiện hóa đơn bất thường bằng Isolation Forest." actions={<button className="btn-secondary" onClick={async()=>{if(window.confirm('Xóa lịch sử hội thoại của bạn tại doanh nghiệp này?')){await api.delete('/ai/chat-history');setMessages([])}}}>Xóa lịch sử của tôi</button>} />
    <Tabs value={tab} onChange={setTab} items={[
      { value: 'chat', label: <><Bot className="h-4 w-4" />Hỏi dữ liệu</> },
      { value: 'forecast', label: <><CalendarRange className="h-4 w-4" />Dự báo ML</> },
      { value: 'anomaly', label: <><ShieldAlert className="h-4 w-4" />AI Anomaly</> },
    ]} />

    {tab === 'chat' && <div className="grid gap-5 xl:grid-cols-[1fr_300px]">
      <section className="panel flex min-h-[650px] flex-col overflow-hidden"><div className="border-b border-line bg-slate-50 px-5 py-4"><div className="flex items-center gap-3"><span className="flex h-10 w-10 items-center justify-center rounded-xl bg-forest text-white"><Sparkles className="h-5 w-5" /></span><div><p className="font-bold text-ink">TaxFlow Data Assistant</p><p className="text-xs text-slate-500">Gemini hiểu câu hỏi · backend truy vấn và tính số liệu</p></div></div></div>
        {assistantStatus && <div className="border-b border-line px-5 py-2 text-xs text-slate-500">{assistantStatus.mode === 'legacy' ? 'Chế độ dự phòng đang được chọn.' : assistantStatus.keyConfigured ? `Đã cấu hình Gemini (${assistantStatus.model}); kết nối sẽ được kiểm tra khi gửi câu hỏi.` : 'Chưa cấu hình Gemini; đang dùng truy vấn dự phòng.'}</div>}
        <div className="scrollbar-thin flex-1 space-y-5 overflow-y-auto p-5 sm:p-6">{messages.map((message, index) => <div key={`${message.time}-${index}`} className={`bubble-in flex gap-3 ${message.role === 'user' ? 'flex-row-reverse' : ''}`}><span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl ${message.role === 'assistant' ? 'bg-mint text-emerald' : 'bg-slate-100 text-slate-600'}`}>{message.role === 'assistant' ? <Bot className="h-5 w-5" /> : <UserRound className="h-5 w-5" />}</span><div className={`max-w-[82%] rounded-2xl px-4 py-3 ${message.role === 'assistant' ? 'rounded-tl-sm bg-slate-100 text-slate-700' : 'rounded-tr-sm bg-forest text-white'}`}><p className="whitespace-pre-wrap text-sm leading-6">{message.text}</p>{message.mode && <p className="mt-2 text-xs font-semibold text-blue-700">{message.mode === 'gemini-functions+database' ? 'Gemini hiểu câu hỏi · dữ liệu từ hệ thống' : 'Truy vấn dự phòng'}</p>}{message.source && <p className="mt-2 border-t border-slate-200 pt-2 text-xs text-slate-400">Nguồn: {message.source} · {shortDateTime(message.time)}</p>}</div></div>)}{asking && <div role="status" className="bubble-in flex items-center gap-3 text-sm text-slate-500"><span className="flex h-9 w-9 items-center justify-center rounded-xl bg-mint text-emerald"><Bot className="h-5 w-5" /></span><span className="flex items-center gap-1 rounded-2xl rounded-tl-sm bg-slate-100 px-4 py-3" aria-label="Đang truy vấn dữ liệu"><span className="typing-dot" /><span className="typing-dot" /><span className="typing-dot" /></span></div>}<div ref={bottomRef} /></div>
        <form onSubmit={(e) => ask(e)} className="border-t border-line p-4"><div className="flex gap-2"><input className="field flex-1" placeholder="Hỏi tự nhiên, ví dụ: Họ đã trả được bao nhiêu rồi?" value={question} onChange={(e) => setQuestion(e.target.value)} /><button className="btn-primary !px-4" disabled={!question.trim() || asking}><Send className="h-5 w-5" /></button></div></form></section>
      <aside className="panel self-start p-5"><h2 className="font-bold text-ink">Câu hỏi gợi ý</h2><div className="mt-4 space-y-2">{suggestions.map((s) => <button key={s} onClick={() => ask(undefined, s)} className="w-full rounded-xl border border-line px-3 py-3 text-left text-sm font-semibold text-slate-600 hover:bg-mint">{s}</button>)}</div><div className="mt-5"><Notice>Bạn có thể hỏi bằng cách diễn đạt của mình và hỏi tiếp về đối tượng vừa nhắc. Nếu thiếu thông tin hoặc ngoài dữ liệu hỗ trợ, trợ lý sẽ hỏi lại. Mỗi câu trả lời cho biết nguồn và chế độ xử lý.</Notice></div></aside>
    </div>}

    {tab === 'forecast' && <section className="space-y-5">
      <div className="panel flex flex-col gap-4 p-5 sm:flex-row sm:items-center sm:justify-between"><div><h2 className="font-bold text-ink">Cash Flow ML Forecast</h2><p className="mt-1 text-sm text-slate-500">Huber Regression + day-of-week seasonality; tự fallback khi AI service lỗi.</p></div><div className="inline-flex rounded-xl bg-slate-100 p-1">{[7,30,90].map((v) => <button key={v} onClick={() => setHorizon(v)} className={`min-h-10 rounded-lg px-4 text-sm font-bold ${horizon === v ? 'bg-white text-forest shadow-sm' : 'text-slate-500'}`}>{v} ngày</button>)}</div></div>
      {error && <Notice type="error">{error}</Notice>}{loadingForecast ? <Loading /> : forecast?.status === 'Insufficient historical data' ? <div className="panel"><Empty title="Chưa đủ dữ liệu" description={`Cần ít nhất ${forecast.requiredActiveDays} ngày có giao dịch; hiện có ${forecast.availableActiveDays}.`} /></div> : forecast ? <><div className="grid gap-4 sm:grid-cols-3"><article className="panel p-5"><p className="text-sm text-slate-500">Tiền vào dự kiến</p><p className="mt-3 text-2xl font-extrabold text-emerald">{money(forecast.series.reduce((s,x)=>s+x.projectedInflow,0))}</p></article><article className="panel p-5"><p className="text-sm text-slate-500">Tiền ra dự kiến</p><p className="mt-3 text-2xl font-extrabold text-rose-600">{money(forecast.series.reduce((s,x)=>s+x.projectedOutflow,0))}</p></article><article className="panel p-5"><p className="text-sm text-slate-500">Dòng tiền ròng</p><p className="mt-3 text-2xl font-extrabold text-ink">{money(forecast.series.reduce((s,x)=>s+x.projectedNet,0))}</p></article></div><section className="panel p-5"><p className="mb-4 text-sm font-semibold text-slate-500">Model: {forecast.method}</p><div className="h-[360px]"><ResponsiveContainer width="100%" height="100%"><AreaChart data={forecast.series}><CartesianGrid strokeDasharray="3 3" vertical={false}/><XAxis dataKey="date" tickFormatter={(v)=>new Date(v).toLocaleDateString('vi-VN',{day:'2-digit',month:'2-digit'})}/><YAxis tickFormatter={compact}/><Tooltip formatter={(v)=>money(Number(v))}/><Legend/><Area isAnimationActive={!reducedMotion} type="monotone" dataKey="projectedInflow" name="Tiền vào" stroke="#2350F0" fill="#eaf2ff"/><Area isAnimationActive={!reducedMotion} type="monotone" dataKey="projectedOutflow" name="Tiền ra" stroke="#D9452F" fill="#ffe9e9"/></AreaChart></ResponsiveContainer></div></section><Notice>{forecast.disclaimer}{forecast.quality && <p className="mt-2">Kiểm tra trên {forecast.quality.holdoutDays} ngày giữ lại: MAE tiền vào {money(forecast.quality.inflowMae)}, tiền ra {money(forecast.quality.outflowMae)}; phương pháp trung bình đối chứng {money(forecast.quality.baselineInflowMae)}. MAE càng thấp càng tốt.</p>}</Notice></> : <Empty />}
    </section>}

    {tab === 'anomaly' && <section className="space-y-5">
      <div className="panel flex flex-col gap-4 p-5 sm:flex-row sm:items-center sm:justify-between"><div><h2 className="font-bold text-ink">Invoice Anomaly Detection</h2><p className="mt-1 text-sm text-slate-500">Isolation Forest phân tích amount, VAT, lịch sử nhà cung cấp và tần suất hóa đơn.</p></div><button className="btn-primary" onClick={scanAnomalies} disabled={loadingAlerts}><RefreshCw className={`h-4 w-4 ${loadingAlerts ? 'animate-spin' : ''}`}/>Quét bất thường</button></div>
      {scan && <Notice>{scan.status === 'insufficient_data' ? 'Chưa đủ hóa đơn để chạy Isolation Forest. ' : ''}Model: {scan.method} · {scan.invoiceRecords} hóa đơn · phát hiện mới {scan.newAlerts} cảnh báo ({scan.mlAlerts} ML, {scan.duplicateAlerts} duplicate).</Notice>}
      {error && <Notice type="error">{error}</Notice>}
      {loadingAlerts ? <Loading /> : alerts.length === 0 ? <div className="panel"><Empty title="Chưa có cảnh báo AI" description="Bấm Quét bất thường để phân tích các hóa đơn đầu vào." /></div> : <div className="grid gap-4 lg:grid-cols-2">{alerts.map(a => <article key={a.id} className="panel p-5"><div className="flex items-start justify-between gap-3"><span className="rounded-xl bg-amber-50 p-2 text-amber-700"><AlertTriangle className="h-5 w-5"/></span><StatusBadge value={a.severity}/></div><h3 className="mt-4 font-bold text-ink">{a.title}</h3><p className="mt-2 text-sm leading-6 text-slate-500">{a.message}</p><p className="mt-3 text-xs text-slate-400">{a.type} · {shortDateTime(a.createdAt)}</p></article>)}</div>}
    </section>}
  </>
}
