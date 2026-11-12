import { useState, type FormEvent } from 'react'
import { motion } from 'motion/react'
import { ArrowRight, Check, Eye, EyeOff, LoaderCircle } from 'lucide-react'
import { useAuth } from '../auth/AuthContext'
import { BrandMark } from '../components/AppLayout'
import { apiError } from '../lib/format'

const accounts = [
  { label: 'Quản trị', email: 'admin@example.com', password: 'Admin@123' },
  { label: 'Kế toán', email: 'accountant@example.com', password: 'Accountant@123' },
  { label: 'Nhân viên', email: 'employee@example.com', password: 'Employee@123' },
  { label: 'Giám đốc', email: 'director@example.com', password: 'Director@123' },
]

const ease = [.2, .7, .2, 1] as const

/** Phiếu hóa đơn minh họa (không phải dữ liệu doanh nghiệp) được đóng dấu "Đã đối soát". */
function StampedReceipt() {
  const rows: Array<[string, string]> = [['Giấy in A4 × 40', '2.600.000'], ['Mực máy in × 6', '1.740.000'], ['Bìa hồ sơ × 120', '960.000']]
  return (
    <div aria-hidden="true" className="relative mx-auto w-full max-w-[380px]">
      <motion.div initial={{ opacity: 0, y: 40, rotate: -2 }} animate={{ opacity: 1, y: 0, rotate: -3 }} transition={{ duration: .9, ease, delay: .15 }}
        className="receipt-edge relative rounded-t-xl bg-[#FBFCFF] px-7 pb-10 pt-6 text-ink shadow-[0_40px_80px_-20px_rgba(0,0,0,.55)]">
        <div className="flex items-start justify-between border-b border-dashed border-slate-300 pb-4">
          <div><p className="text-[13px] font-bold">Hóa đơn giá trị gia tăng</p><p className="mt-1 text-[11px] text-slate-400">Ký hiệu 1C26TAA · Số 0001284</p></div>
          <BrandMark className="h-8 w-8" />
        </div>
        <div className="space-y-2.5 py-4 text-[12.5px]">
          {rows.map(([name, value], i) => (
            <motion.div key={name} className="flex justify-between gap-6" initial={{ opacity: 0, x: -8 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: .55 + i * .12, duration: .4, ease }}>
              <span className="text-slate-500">{name}</span><span className="tabular-nums font-medium">{value}</span>
            </motion.div>
          ))}
        </div>
        <div className="space-y-1.5 border-t border-dashed border-slate-300 pt-3 text-[12.5px]">
          <div className="flex justify-between text-slate-500"><span>Thuế GTGT 10%</span><span className="tabular-nums">530.000</span></div>
          <div className="flex items-baseline justify-between"><span className="font-semibold">Tổng thanh toán</span><span className="tabular-nums text-xl font-bold tracking-tight">5.830.000 ₫</span></div>
        </div>

      </motion.div>
      {/* Con dấu đỏ */}
      <motion.div
        initial={{ opacity: 0, scale: 2.2, rotate: -28 }}
        animate={{ opacity: [0, 1, .92], scale: [2.2, .94, 1], rotate: -14 }}
        transition={{ delay: 1.55, duration: .5, times: [0, .7, 1], ease: 'easeOut' }}
        className="absolute right-[18%] top-[92px] flex h-[104px] w-[104px] items-center justify-center rounded-full border-[3px] border-seal text-seal mix-blend-multiply"
        style={{ boxShadow: 'inset 0 0 0 5px #FBFCFF, inset 0 0 0 6.5px #D9452F' }}
      >
        <div className="text-center leading-tight">
          <p className="text-[9px] font-bold tracking-[.12em]">TAXFLOW</p>
          <p className="my-0.5 text-[15px] font-extrabold">ĐÃ</p>
          <p className="text-[11px] font-extrabold tracking-wide">ĐỐI SOÁT</p>
        </div>
      </motion.div>
      {/* Gợn chấn khi dấu chạm giấy */}
      <motion.span className="pointer-events-none absolute right-[18%] top-[92px] h-[104px] w-[104px] rounded-full border-2 border-seal/40"
        initial={{ opacity: 0, scale: 1 }} animate={{ opacity: [0, .6, 0], scale: [1, 1, 1.5] }} transition={{ delay: 1.88, duration: .6 }} />
    </div>
  )
}

export default function LoginPage() {
  const { login } = useAuth()
  const [email, setEmail] = useState('admin@example.com')
  const [password, setPassword] = useState('Admin@123')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault(); setError(''); setSubmitting(true)
    try { await login(email, password) }
    catch (err) { setError(apiError(err, 'Email hoặc mật khẩu không đúng.')) }
    finally { setSubmitting(false) }
  }

  return (
    <div className="min-h-screen lg:grid lg:grid-cols-[1.1fr_.9fr]">
      <section className="hero-surface relative hidden overflow-hidden px-12 py-10 text-white lg:flex lg:flex-col">
        <div className="hero-glow pointer-events-none absolute -bottom-40 -left-24 h-[28rem] w-[28rem] rounded-full bg-[#2350F0]/30 blur-3xl" />
        <div className="relative flex items-center gap-3">
          <BrandMark className="h-11 w-11" />
          <div className="leading-tight"><p className="text-lg font-bold tracking-[-0.01em]">TaxFlow AI</p><p className="text-xs text-white/50">Sổ sách & dòng tiền doanh nghiệp</p></div>
        </div>

        <div className="relative mt-14 grid flex-1 grid-rows-[auto_1fr] gap-12 xl:mt-16">
          <motion.div initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .7, ease }} className="max-w-2xl">
            <h1 className="text-[44px] font-bold leading-[1.06] tracking-[-0.03em] xl:text-[54px]">Từ tờ hóa đơn<br />đến bức tranh dòng tiền.</h1>
            <p className="mt-5 max-w-md text-[15px] leading-7 text-white/60">Đọc chứng từ, đối soát ngân hàng, theo dõi công nợ và VAT trong cùng một quy trình có kiểm soát.</p>
          </motion.div>
          <div className="flex items-center justify-center pb-6"><StampedReceipt /></div>
        </div>

        <p className="relative text-xs text-white/35">Minh họa giao diện — không phải dữ liệu doanh nghiệp thật.</p>
      </section>

      <section className="flex min-h-screen items-center justify-center px-4 py-10 sm:px-8">
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: .6, ease, delay: .1 }} className="w-full max-w-[420px]">
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <BrandMark className="h-11 w-11" />
            <p className="text-xl font-bold tracking-[-0.01em] text-ink">TaxFlow AI</p>
          </div>
          <h2 className="text-[28px] font-bold leading-tight tracking-[-0.02em] text-ink">Đăng nhập hệ thống</h2>
          <p className="mt-2 text-sm leading-6 text-slate-500">Chọn một vai trò demo bên dưới hoặc nhập tài khoản của bạn.</p>

          <form onSubmit={submit} className="mt-8 space-y-4">
            <div><label className="label" htmlFor="email">Email</label><input id="email" type="email" className="field" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="username" required /></div>
            <div>
              <label className="label" htmlFor="password">Mật khẩu</label>
              <div className="relative">
                <input id="password" type={showPassword ? 'text' : 'password'} className="field !pr-12" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
                <button type="button" onClick={() => setShowPassword(v => !v)} className="absolute right-1.5 top-1.5 rounded-lg p-2 text-slate-400 transition hover:bg-mint hover:text-emerald" aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}>{showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}</button>
              </div>
            </div>
            {error && <motion.div role="alert" initial={{ opacity: 0, x: 0 }} animate={{ opacity: 1, x: [0, -6, 6, -3, 0] }} transition={{ duration: .4 }} className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">{error}</motion.div>}
            <button type="submit" disabled={submitting} className="btn-primary w-full !min-h-12">
              {submitting ? <><LoaderCircle className="h-4 w-4 animate-spin" />Đang đăng nhập...</> : <>Đăng nhập<ArrowRight className="h-4 w-4" /></>}
            </button>
          </form>

          <p className="mb-3 mt-8 text-sm font-semibold text-slate-600">Tài khoản demo</p>
          <div className="grid grid-cols-2 gap-2">
            {accounts.map((account) => {
              const selected = email === account.email
              return (
                <button key={account.email} type="button" onClick={() => { setEmail(account.email); setPassword(account.password); setError('') }}
                  className={`relative flex min-h-12 items-center justify-between overflow-hidden rounded-xl border px-3.5 py-2 text-left text-sm font-semibold transition-colors ${selected ? 'border-emerald text-forest' : 'border-line bg-white text-slate-600 hover:border-emerald/40'}`}>
                  {selected && <motion.span layoutId="demo-account" className="absolute inset-0 bg-mint" transition={{ type: 'spring', stiffness: 420, damping: 34 }} />}
                  <span className="relative">{account.label}</span>
                  {selected && <motion.span className="relative" initial={{ scale: 0 }} animate={{ scale: 1 }} transition={{ type: 'spring', stiffness: 500, damping: 22 }}><Check className="h-4 w-4" /></motion.span>}
                </button>
              )
            })}
          </div>
          <p className="mt-6 text-xs leading-5 text-slate-400">Tài khoản mặc định chỉ dành cho môi trường demo. Hãy đổi mật khẩu trước khi sử dụng thực tế.</p>
        </motion.div>
      </section>
    </div>
  )
}
