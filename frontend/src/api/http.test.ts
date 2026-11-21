import { describe, expect, it, vi } from 'vitest'
import { tokenStore } from '../auth/tokenStore'
import { json, mockApi, problem } from '../test/mockApi'
import { ApiError, apiBlob, apiRequest, getJson, setRefreshHandler } from './http'
import { maintenanceStore } from './maintenanceStore'

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

// Quyết định 53: mọi phản hồi 503 bật màn hình "Hệ thống đang bảo trì".
describe('503: màn hình bảo trì', () => {
  it('đang bảo trì (mã system.maintenance): ghi lý do, giờ dự kiến xong; hết bảo trì thì làm tiếp, không tải lại trang', async () => {
    mockApi({
      'GET /api/v1/patients': () =>
        problem(503, 'Hệ thống đang bảo trì. Vui lòng quay lại sau.', {
          code: 'system.maintenance',
          reason: 'Nâng cấp phần mềm',
          expectedEndAtUtc: '2026-11-21T04:30:00Z',
        }),
    })

    const error = await apiRequest('/api/v1/patients').catch((e: unknown) => e)

    expect(error).toMatchObject({ status: 503, code: 'system.maintenance' })
    expect(maintenanceStore.get().notice).toMatchObject({
      kind: 'maintenance',
      reason: 'Nâng cấp phần mềm',
      expectedEndAtUtc: '2026-11-21T04:30:00Z',
      reloadOnEnd: false,
    })
  })

  it('503 không có mã (IIS khi ứng dụng đang dừng): giữ lý do đang hiện, hết thì tải lại trang', async () => {
    mockApi({
      'GET /api/v1/patients': () => problem(503, 'Đang bảo trì', { code: 'system.maintenance', reason: 'Nâng cấp phần mềm' }),
      'GET /api/v1/consent-forms/f-1/pdf': () => new Response('Service Unavailable', { status: 503 }),
    })
    await apiRequest('/api/v1/patients').catch(() => undefined)
    const since = maintenanceStore.get().notice?.since

    await expect(apiBlob('/api/v1/consent-forms/f-1/pdf')).rejects.toMatchObject({ status: 503 })

    expect(maintenanceStore.get().notice).toMatchObject({ kind: 'unavailable', reason: 'Nâng cấp phần mềm', reloadOnEnd: true, since })
  })

  it('lỗi khác 503 không bật màn hình bảo trì', async () => {
    mockApi({ 'GET /api/v1/patients': () => problem(500, 'Đã xảy ra lỗi hệ thống') })

    await apiRequest('/api/v1/patients').catch(() => undefined)

    expect(maintenanceStore.get().notice).toBeNull()
  })

  it('đăng nhập được (kể cả khi đang bảo trì) thì đóng màn hình, trừ khi máy chủ vừa dừng hẳn (phải tải lại trang)', () => {
    maintenanceStore.report({ kind: 'maintenance', reason: 'Nâng cấp phần mềm' })
    maintenanceStore.startAdminLogin()
    maintenanceStore.sessionStarted()
    expect(maintenanceStore.get()).toEqual({ notice: null, adminLogin: false })

    maintenanceStore.report({ kind: 'unavailable' })
    maintenanceStore.startAdminLogin()
    maintenanceStore.sessionStarted()
    expect(maintenanceStore.get()).toMatchObject({ notice: { kind: 'unavailable', reloadOnEnd: true }, adminLogin: false })
  })

  it('báo lại cùng nội dung thì không vẽ lại', () => {
    const listener = vi.fn()
    const unsubscribe = maintenanceStore.subscribe(listener)

    maintenanceStore.report({ kind: 'maintenance', reason: 'A' })
    maintenanceStore.report({ kind: 'maintenance', reason: 'A' })
    maintenanceStore.report({ kind: 'maintenance', reason: 'B' })
    unsubscribe()

    expect(listener).toHaveBeenCalledTimes(2)
  })
})
