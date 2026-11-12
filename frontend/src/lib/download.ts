import { api } from './api'

export async function downloadExport(dataset: string, format: 'csv' | 'xlsx' | 'pdf', filters?: { from?: string; to?: string; search?: string }) {
  const response = await api.get<Blob>(`/exports/${dataset}`, { params: { format, ...filters }, responseType: 'blob' })
  const disposition = response.headers['content-disposition'] as string | undefined
  const encoded = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  const plain = disposition?.match(/filename="?([^";]+)"?/i)?.[1]
  const filename = encoded ? decodeURIComponent(encoded) : plain || `taxflow-${dataset}.${format}`
  const url = URL.createObjectURL(response.data)
  const anchor = document.createElement('a')
  anchor.href = url; anchor.download = filename; document.body.appendChild(anchor); anchor.click(); anchor.remove()
  URL.revokeObjectURL(url)
}
