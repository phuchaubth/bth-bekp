import { describe, expect, it, vi } from 'vitest'
import { mockFetchJson } from '../test/renderApp'
import { ApiError, getJson } from './http'

describe('getJson', () => {
  it('trả về dữ liệu JSON khi thành công', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(mockFetchJson({ status: 'Healthy' }))

    await expect(getJson('/health')).resolves.toEqual({ status: 'Healthy' })
  })

  it('ném ApiError với tiêu đề tiếng Việt từ ProblemDetails', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      mockFetchJson({ title: 'Đã xảy ra lỗi hệ thống', traceId: 'abc-123' }, 500),
    )

    const error = await getJson('/api/v1/x').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ message: 'Đã xảy ra lỗi hệ thống', status: 500, traceId: 'abc-123' })
  })

  it('ném ApiError dễ hiểu khi không kết nối được máy chủ', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))

    await expect(getJson('/health')).rejects.toMatchObject({
      status: 0,
      message: 'Không kết nối được tới máy chủ. Vui lòng kiểm tra mạng.',
    })
  })

  it('dùng thông báo mặc định khi phản hồi lỗi không phải JSON', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('Bad gateway', { status: 502 }))

    await expect(getJson('/health')).rejects.toMatchObject({
      status: 502,
      message: 'Máy chủ trả về lỗi (502).',
    })
  })
})
