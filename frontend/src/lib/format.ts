export const money = (value: number | null | undefined) =>
  new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 }).format(value ?? 0)

export const number = (value: number | null | undefined) =>
  new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value ?? 0)

export const shortDate = (value: string | Date) =>
  new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(value))

export const shortDateTime = (value: string | Date) =>
  new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(value))

export const apiError = (error: unknown, fallback = 'Không thể xử lý yêu cầu.') => {
  if (typeof error === 'object' && error && 'response' in error) {
    const response = (error as { response?: { data?: { detail?: string; message?: string; title?: string } } }).response
    return response?.data?.detail || response?.data?.message || response?.data?.title || fallback
  }
  return fallback
}

/** Ngày dài kiểu Việt Nam, chỉ viết hoa chữ cái đầu: "Thứ Năm, 12 tháng 11, 2026". */
export const longDate = (date: Date = new Date(), withYear = true) => {
  return date.toLocaleDateString('vi-VN', { weekday: 'long', day: 'numeric', month: 'long', ...(withYear ? { year: 'numeric' } : {}) })
    .replace(/Tháng/g, 'tháng').replace(/^thứ/, 'Thứ').replace(/^chủ nhật/i, 'Chủ Nhật')
}
