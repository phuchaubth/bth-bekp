/** Lỗi trả về từ API, đọc từ ProblemDetails (RFC 9457) nếu có. */
export class ApiError extends Error {
  readonly status: number
  readonly traceId?: string

  constructor(message: string, status: number, traceId?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.traceId = traceId
  }
}

interface ProblemDetails {
  title?: string
  detail?: string
  traceId?: string
}

const baseUrl: string = import.meta.env.VITE_API_BASE_URL ?? ''

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${baseUrl}${path}`, {
      headers: { Accept: 'application/json' },
      credentials: 'include',
      signal,
    })
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error
    }
    throw new ApiError('Không kết nối được tới máy chủ. Vui lòng kiểm tra mạng.', 0)
  }

  if (!response.ok) {
    const problem = await readProblem(response)
    throw new ApiError(
      problem?.title ?? `Máy chủ trả về lỗi (${response.status}).`,
      response.status,
      problem?.traceId,
    )
  }

  return (await response.json()) as T
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}
