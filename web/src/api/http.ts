import axios from 'axios'
import { ApiError } from './problems'
import type { ProblemDetails } from '../types/contracts'

let csrfToken = ''
let tenantId: string | null = null
export function setTenantContext(id: string | null) {
  tenantId = id
}

export const http = axios.create({ baseURL: '/api/v1', timeout: 15000, withCredentials: true })
http.interceptors.request.use((config) => {
  if (csrfToken && config.method !== 'get') {
    config.headers.set('X-CSRF-TOKEN', csrfToken)
  }
  if (tenantId && !config.url?.startsWith('/auth')) {
    config.headers.set('X-Tenant-Context', tenantId)
  }
  return config
})
http.interceptors.response.use(
  (response) => response,
  (error: unknown) => {
    if (axios.isCancel(error)) {
      return Promise.reject(error)
    }
    if (axios.isAxiosError<ProblemDetails>(error)) {
      const problem = error.response?.data ?? {
        detail: '连接中断，操作结果可能尚未确认，请使用相同数据重试。',
      }
      const wrapped = new ApiError(error.response?.status ?? 0, problem)
      if (wrapped.code === 'auth.unauthenticated' || wrapped.code === 'auth.session_expired') {
        window.dispatchEvent(new Event('qizhu:session-expired'))
      }
      return Promise.reject(wrapped)
    }
    return Promise.reject(error)
  },
)
export async function refreshCsrf() {
  csrfToken = (await http.get<{ token: string }>('/auth/csrf')).data.token
}
