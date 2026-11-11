import { tokenStore } from '../auth/tokenStore'

/** Lỗi trả về từ API, đọc từ ProblemDetails (RFC 9457) nếu có. */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly traceId?: string
  readonly fieldErrors?: Record<string, string[]>

  constructor(
    message: string,
    status: number,
    options: { code?: string; traceId?: string; fieldErrors?: Record<string, string[]> } = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = options.code
    this.traceId = options.traceId
    this.fieldErrors = options.fieldErrors
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  signal?: AbortSignal
  /** Gửi kèm access token (mặc định: có). */
  auth?: boolean
}

/** Được AuthProvider đăng ký: làm mới phiên và trả về access token mới (hoặc null nếu phải đăng nhập lại). */
type RefreshHandler = () => Promise<string | null>

let refreshHandler: RefreshHandler | null = null

export function setRefreshHandler(handler: RefreshHandler | null): void {
  refreshHandler = handler
}

const baseUrl: string = import.meta.env.VITE_API_BASE_URL ?? ''

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const useAuth = options.auth !== false
  let response = await send(path, options, useAuth)

  // Access token hết hạn: làm mới phiên một lần rồi gửi lại yêu cầu.
  if (response.status === 401 && useAuth && refreshHandler) {
    const newToken = await refreshHandler()
    if (newToken) {
      response = await send(path, options, useAuth)
    }
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/** GET không cần đăng nhập (ví dụ health check). */
export function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  return apiRequest<T>(path, { signal, auth: false })
}

async function send(path: string, options: RequestOptions, useAuth: boolean): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    // Header bắt buộc cho endpoint dùng cookie (chống CSRF); trình duyệt không cho trang khác tự gửi header này.
    'X-Requested-With': 'consent-web',
  }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  const token = useAuth ? tokenStore.get() : null
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }

  try {
    return await fetch(`${baseUrl}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      credentials: 'include',
      signal: options.signal,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    throw new ApiError('Không kết nối được tới máy chủ. Vui lòng kiểm tra mạng.', 0)
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | undefined
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    problem = undefined
  }

  return new ApiError(problem?.title ?? `Máy chủ trả về lỗi (${response.status}).`, response.status, {
    code: problem?.code,
    traceId: problem?.traceId,
    fieldErrors: problem?.errors,
  })
}
