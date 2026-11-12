import axios from 'axios'

export const api = axios.create({ baseURL: import.meta.env.VITE_API_URL || '/api', timeout: 60_000 })
// Retain a command key after a network failure so a user retry cannot create a second payment.
const commandKeys = new Map<string, { key: string; expires: number }>()
api.interceptors.request.use((config) => {
  const token = localStorage.getItem('taxflow-token')
  const companyId = localStorage.getItem('taxflow-company')
  if (token) config.headers.Authorization = `Bearer ${token}`
  if (companyId) config.headers['X-Company-Id'] = companyId
  if (['post', 'put', 'delete'].includes(config.method || '') && !(config.data instanceof FormData)) {
    const fingerprint = JSON.stringify([token, companyId, config.method, config.url, config.params, config.data])
    const now = Date.now()
    for (const [name, value] of commandKeys) if (value.expires < now) commandKeys.delete(name)
    const existing = commandKeys.get(fingerprint)
    const key = existing?.key || crypto.randomUUID()
    commandKeys.set(fingerprint, { key, expires: now + 15 * 60_000 })
    config.headers['Idempotency-Key'] ||= key
    ;(config as unknown as { commandFingerprint: string }).commandFingerprint = fingerprint
  }
  return config
})
const clearKey = (config: unknown) => {
  const key = (config as { commandFingerprint?: string } | undefined)?.commandFingerprint
  if (key) commandKeys.delete(key)
}
api.interceptors.response.use(
  response => { clearKey(response.config); return response },
  error => {
    // The server's 4xx responses did not commit a command; a corrected request can be retried.
    if (error.response?.status >= 400 && error.response?.status < 500) clearKey(error.config)
    if (error.response?.status === 401) {
      localStorage.removeItem('taxflow-token')
      window.dispatchEvent(new Event('taxflow-unauthorized'))
    }
    return Promise.reject(error)
  },
)
