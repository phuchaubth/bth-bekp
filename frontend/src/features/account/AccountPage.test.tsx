import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { baseRoutes, session } from '../../test/fixtures'
import { mockApi, noContent } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'
import { SIGNATURE_INK, strokeWidth } from '../signing/signatureStyle'

/** jsdom không vẽ được canvas: giả lập phần vẽ và ảnh xuất ra của khung ký. */
function mockCanvas() {
  const context = { setTransform: vi.fn(), fillRect: vi.fn(), clearRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,Q0hVX0tZX01BVQ==')
  vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
    { left: 0, top: 0, right: 600, bottom: 200, width: 600, height: 200, x: 0, y: 0, toJSON: () => ({}) } as DOMRect,
  )
  return context as typeof context & { strokeStyle?: string; lineWidth?: number }
}

function drawStroke(canvas: HTMLElement) {
  fireEvent.pointerDown(canvas, { clientX: 40, clientY: 120, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerMove(canvas, { clientX: 160, clientY: 80, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerMove(canvas, { clientX: 260, clientY: 140, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerUp(canvas, { clientX: 260, clientY: 140, pointerId: 1, pointerType: 'mouse' })
}

describe('Tài khoản của tôi: chữ ký mẫu', () => {
  beforeEach(() => {
    mockCanvas()
    URL.createObjectURL = vi.fn(() => 'blob:chu-ky-mau')
    URL.revokeObjectURL = vi.fn()
  })
  afterEach(() => vi.restoreAllMocks())

  it('ký chữ ký mẫu trong khung rồi lưu; xóa sau khi xác nhận', async () => {
    let saved = false
    const { calls } = mockApi({
      ...baseRoutes(session()),
      'GET /api/v1/account/signature-specimen': () =>
        saved ? new Response('PNG-gia-lap', { headers: { 'Content-Type': 'image/png' } }) : new Response(null, { status: 404 }),
      'PUT /api/v1/account/signature-specimen': () => {
        saved = true
        return { updatedAtUtc: '2026-11-14T08:00:00Z' }
      },
      'DELETE /api/v1/account/signature-specimen': () => {
        saved = false
        return noContent()
      },
    })
    renderApp('/tai-khoan')
    const user = userEvent.setup()

    expect(await screen.findByText('Chưa có chữ ký mẫu')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Ký chữ ký mẫu/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Ký chữ ký mẫu' })
    expect(within(dialog).getByRole('button', { name: 'Lưu chữ ký mẫu' })).toBeDisabled()
    drawStroke(within(dialog).getByRole('img', { name: /Khung ký tên/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Lưu chữ ký mẫu' }))

    expect(await screen.findByRole('img', { name: 'Chữ ký mẫu của bạn' })).toHaveAttribute('src', 'blob:chu-ky-mau')
    expect(calls.find((c) => c.method === 'PUT')!.body).toEqual({ image: 'Q0hVX0tZX01BVQ==' })

    await user.click(screen.getByRole('button', { name: /Xóa/ }))
    await user.click(await screen.findByRole('button', { name: 'Xóa' }))
    await waitFor(() => expect(screen.getByText('Chưa có chữ ký mẫu')).toBeInTheDocument())
  })

  it('có khung chữ ký của bệnh viện: chữ ký mẫu và khung vẽ nằm trong khung; nét mực xanh, dày gấp đôi', async () => {
    const context = mockCanvas()
    mockApi({
      ...baseRoutes(session()),
      // Khung giả lập (kích thước ảnh khung thật của bệnh viện: ngang 4:3).
      'GET /api/v1/branding/signature-frame': { contentType: 'image/png', width: 1440, height: 1080, updatedAtUtc: '2026-11-15T09:00:00Z' },
      'GET /api/v1/account/signature-specimen': () => new Response('PNG-gia-lap', { headers: { 'Content-Type': 'image/png' } }),
    })
    renderApp('/tai-khoan')
    const user = userEvent.setup()
    const frameUrl = '/api/v1/branding/signature-frame/image?v=2026-11-15T09%3A00%3A00Z'

    const specimen = await screen.findByRole('img', { name: 'Chữ ký mẫu của bạn' })
    const box = await screen.findByTestId('signature-frame')
    expect(box).toContainElement(specimen)
    expect(box.style.backgroundImage).toContain(frameUrl)

    await user.click(screen.getByRole('button', { name: /Ký lại chữ ký mẫu/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Ký chữ ký mẫu' })
    const canvas = within(dialog).getByRole('img', { name: /Khung ký tên/ })
    expect(canvas.style.backgroundImage).toContain(frameUrl)
    expect(within(dialog).getByText(/Ký vào khoảng trống trong khung/)).toBeInTheDocument()
    drawStroke(canvas)
    expect(context.strokeStyle).toBe(SIGNATURE_INK)
    // Khung giả lập rộng 600 điểm ảnh, devicePixelRatio = 1: nét 5 (gấp đôi nét cũ 2,5).
    expect(context.lineWidth).toBe(5)
  })

  it('nét ký to dần theo bề rộng khung để cân đối: điện thoại 5, kiosk 32 inch khoảng 9', () => {
    expect(strokeWidth(343)).toBe(5)
    expect(strokeWidth(600)).toBe(5)
    expect(strokeWidth(1080)).toBe(9)
  })
})
