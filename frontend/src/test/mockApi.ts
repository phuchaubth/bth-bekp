import { vi } from 'vitest'

export interface ApiCall {
  method: string
  path: string
  search: string
  body: unknown
  headers: Record<string, string>
}

type Handler = (call: ApiCall) => unknown

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

export const problem = (status: number, title: string, extra: Record<string, unknown> = {}) =>
  new Response(JSON.stringify({ status, title, ...extra }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })

export const noContent = () => new Response(null, { status: 204 })

/**
 * Giả lập API theo bảng "PHƯƠNG_THỨC /đường-dẫn" → dữ liệu trả về (object → JSON 200) hoặc hàm trả về Response.
 * Yêu cầu không khớp trả 404. Mọi yêu cầu được ghi lại trong `calls` để kiểm tra.
 */
export function mockApi(routes: Record<string, Handler | object>) {
  const calls: ApiCall[] = []
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost')
    const method = (init?.method ?? 'GET').toUpperCase()
    const call: ApiCall = {
      method,
      path: url.pathname,
      search: url.search,
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
      headers: { ...(init?.headers as Record<string, string> | undefined) },
    }
    calls.push(call)

    const route = routes[`${method} ${url.pathname}`]
    if (route === undefined) {
      return problem(404, 'Không tìm thấy tài nguyên.')
    }
    const result = typeof route === 'function' ? await (route as Handler)(call) : route
    return result instanceof Response ? result : json(result)
  })
  return { calls }
}
