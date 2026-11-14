import { describe, expect, it, vi } from 'vitest'
import { tokenStore } from '../auth/tokenStore'
import { json, mockApi, problem } from '../test/mockApi'
import { ApiError, apiRequest, getJson, setRefreshHandler } from './http'

describe('apiRequest', () => {
  it('trả về JSON và gửi access token cùng header chống CSRF', async () => {
    tokenStore.set('token-1')
    const { calls } = mockApi({ 'GET /api/v1/roles': [{ id: 'r1' }] })

    await expect(apiRequest('/api/v1/roles')).resolves.toEqual([{ id: 'r1' }])
    expect(calls[0].headers.Authorization).toBe('Bearer token-1')
    expect(calls[0].headers['X-Requested-With']).toBe('consent-web')
  })

  it('không gửi access token khi auth = false', async () => {
    tokenStore.set('token-1')
    const { calls } = mockApi({ 'GET /health': { status: 'Healthy' } })

    await getJson('/health')

    expect(calls[0].headers.Authorization).toBeUndefined()
  })

  it('đọc tiêu đề, mã lỗi và lỗi từng trường từ ProblemDetails', async () => {
    mockApi({
      'POST /api/v1/users': () =>
        problem(400, 'Dữ liệu không hợp lệ.', { code: 'validation', traceId: 't-1', errors: { email: ['Email không hợp lệ.'] } }),
    })

    const error = await apiRequest('/api/v1/users', { method: 'POST', body: {} }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({
      message: 'Dữ liệu không hợp lệ.',
      status: 400,
      code: 'validation',
      traceId: 't-1',
      fieldErrors: { email: ['Email không hợp lệ.'] },
    })
  })

  it('khi access token hết hạn: làm mới phiên một lần rồi gửi lại với token mới', async () => {
    tokenStore.set('token-cu')
    let attempts = 0
    const { calls } = mockApi({
      'GET /api/v1/auth/me': () => (++attempts === 1 ? problem(401, 'Hết hạn') : json({ id: 'u1' })),
    })
    const refresh = vi.fn(async () => {
      tokenStore.set('token-moi')
      return 'token-moi'
    })
    setRefreshHandler(refresh)

    await expect(apiRequest('/api/v1/auth/me')).resolves.toEqual({ id: 'u1' })
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(calls.map((c) => c.headers.Authorization)).toEqual(['Bearer token-cu', 'Bearer token-moi'])
  })

  it('không làm mới được phiên thì báo lỗi 401', async () => {
    mockApi({ 'GET /api/v1/auth/me': () => problem(401, 'Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.') })
    setRefreshHandler(async () => null)

    await expect(apiRequest('/api/v1/auth/me')).rejects.toMatchObject({ status: 401 })
  })

  it('báo lỗi dễ hiểu khi không kết nối được máy chủ', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))

    await expect(getJson('/health')).rejects.toMatchObject({
      status: 0,
      message: 'Không kết nối được tới máy chủ. Vui lòng kiểm tra mạng.',
    })
  })

  it('dùng thông báo mặc định khi phản hồi lỗi không phải JSON', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('Bad gateway', { status: 502 }))

    await expect(getJson('/health')).rejects.toMatchObject({ status: 502, message: 'Máy chủ trả về lỗi (502).' })
  })

  it('trả về undefined với phản hồi 204', async () => {
    mockApi({ 'POST /api/v1/auth/logout': () => new Response(null, { status: 204 }) })

    await expect(apiRequest('/api/v1/auth/logout', { method: 'POST', auth: false })).resolves.toBeUndefined()
  })
})
