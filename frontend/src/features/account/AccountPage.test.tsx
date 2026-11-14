import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { baseRoutes, session } from '../../test/fixtures'
import { mockApi, noContent } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

/** jsdom không vẽ được canvas: giả lập phần vẽ và ảnh xuất ra của khung ký. */
function mockCanvas() {
  const context = { setTransform: vi.fn(), fillRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,Q0hVX0tZX01BVQ==')
  vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
    { left: 0, top: 0, right: 600, bottom: 200, width: 600, height: 200, x: 0, y: 0, toJSON: () => ({}) } as DOMRect,
  )
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
    const canvas = within(dialog).getByRole('img', { name: /Khung ký tên/ })
    fireEvent.pointerDown(canvas, { clientX: 40, clientY: 120, pointerId: 1, pointerType: 'mouse' })
    fireEvent.pointerMove(canvas, { clientX: 160, clientY: 80, pointerId: 1, pointerType: 'mouse' })
    fireEvent.pointerMove(canvas, { clientX: 260, clientY: 140, pointerId: 1, pointerType: 'mouse' })
    fireEvent.pointerUp(canvas, { clientX: 260, clientY: 140, pointerId: 1, pointerType: 'mouse' })
    await user.click(within(dialog).getByRole('button', { name: 'Lưu chữ ký mẫu' }))

    expect(await screen.findByRole('img', { name: 'Chữ ký mẫu của bạn' })).toHaveAttribute('src', 'blob:chu-ky-mau')
    expect(calls.find((c) => c.method === 'PUT')!.body).toEqual({ image: 'Q0hVX0tZX01BVQ==' })

    await user.click(screen.getByRole('button', { name: /Xóa/ }))
    await user.click(await screen.findByRole('button', { name: 'Xóa' }))
    await waitFor(() => expect(screen.getByText('Chưa có chữ ký mẫu')).toBeInTheDocument())
  })
})
