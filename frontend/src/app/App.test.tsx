import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { mockFetchJson, renderApp } from '../test/renderApp'

describe('App', () => {
  it('hiển thị menu điều hướng tiếng Việt và trang tổng quan', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(mockFetchJson({ status: 'Healthy', checks: {} }))

    renderApp('/')

    const nav = screen.getByRole('navigation')
    for (const label of ['Tổng quan', 'Phiếu đồng thuận', 'Người bệnh', 'Mẫu phiếu', 'Xuất dữ liệu', 'Quản trị']) {
      expect(within(nav).getByText(label)).toBeInTheDocument()
    }
    expect(screen.getByRole('heading', { name: 'Tổng quan' })).toBeInTheDocument()
    expect(await screen.findByText('Máy chủ đang hoạt động')).toBeInTheDocument()
  })

  it('báo lỗi khi không kết nối được máy chủ', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))

    renderApp('/')

    expect(
      await screen.findByText('Không kết nối được tới máy chủ. Vui lòng kiểm tra mạng.'),
    ).toBeInTheDocument()
  })

  it('điều hướng tới trang chức năng khi chọn menu', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(mockFetchJson({ status: 'Healthy', checks: {} }))
    const user = userEvent.setup()
    const { router } = renderApp('/')

    await user.click(within(screen.getByRole('navigation')).getByText('Người bệnh'))

    expect(router.state.location.pathname).toBe('/nguoi-benh')
    expect(screen.getByRole('heading', { name: 'Người bệnh' })).toBeInTheDocument()
    expect(screen.getByText('Dự kiến hoàn thành trong Sprint 2.')).toBeInTheDocument()
  })

  it('hiển thị trang 404 tiếng Việt cho đường dẫn không tồn tại', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(mockFetchJson({ status: 'Healthy', checks: {} }))

    renderApp('/khong-ton-tai')

    expect(screen.getByText('Không tìm thấy trang')).toBeInTheDocument()
  })
})
