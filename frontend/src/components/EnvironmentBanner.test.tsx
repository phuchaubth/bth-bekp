import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { anonymous, baseRoutes, session } from '../test/fixtures'
import { mockApi } from '../test/mockApi'
import { renderApp } from '../test/renderApp'

// Quyết định 50: UAT hiện dải báo môi trường kiểm thử, kể cả trang đăng nhập; môi trường chạy thật không có.
describe('Dải báo môi trường kiểm thử', () => {
  const original = document.title
  const title = 'Quản lý Phiếu đồng thuận'
  beforeEach(() => {
    document.title = title
  })
  afterEach(() => {
    document.title = original
  })

  it('UAT: trang đăng nhập có dải báo và tiêu đề tab ghi nhãn môi trường', async () => {
    mockApi({ ...baseRoutes(anonymous), 'GET /api/v1/environment': { label: 'UAT' } })
    renderApp('/dang-nhap')

    expect(await screen.findByText(/^UAT: môi trường kiểm thử\./)).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('không dùng cho người bệnh thật')
    await waitFor(() => expect(document.title).toBe(`[UAT] ${title}`))
  })

  it('UAT: nhãn về sau lúc khôi phục phiên thất bại vẫn hiện dải báo', async () => {
    // Máy chủ trả lời làm mới phiên (401) trước, nhãn môi trường sau: xóa dữ liệu phiên không được hủy yêu cầu lấy nhãn.
    let answer: () => void = () => {}
    const later = new Promise<void>((resolve) => {
      answer = resolve
    })
    mockApi({
      ...baseRoutes(anonymous),
      'GET /api/v1/environment': async () => {
        await later
        return { label: 'UAT' }
      },
    })
    renderApp('/dang-nhap')

    // Nút Đăng nhập chỉ hiện khi đã biết chưa có phiên (dữ liệu phiên đã xóa).
    expect(await screen.findByRole('button', { name: /Đăng nhập/ })).toBeInTheDocument()
    answer()

    expect(await screen.findByText(/^UAT: môi trường kiểm thử\./)).toBeInTheDocument()
  })

  it('UAT: đăng xuất xóa dữ liệu của phiên nhưng giữ dải báo, không tải lại nhãn', async () => {
    const { calls } = mockApi({ ...baseRoutes(session()), 'GET /api/v1/environment': { label: 'UAT' } })
    const { queryClient } = renderApp('/')
    const user = userEvent.setup()

    expect(await screen.findByText('Xin chào Bác sĩ Demo – Khoa Nội tổng hợp.')).toBeInTheDocument()
    expect(screen.getByText(/^UAT: môi trường kiểm thử\./)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Tài khoản' }))
    await user.click(await screen.findByText('Đăng xuất'))

    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument()
    expect(screen.getByText(/^UAT: môi trường kiểm thử\./)).toBeInTheDocument()
    // Chỉ còn dữ liệu không thuộc phiên nào: nhãn môi trường, và trạng thái bảo trì trang đăng nhập vừa hỏi (quyết định 53).
    const keys = queryClient.getQueryCache().getAll().map((query) => query.queryKey.join('/'))
    expect(keys.sort()).toEqual(['environment', 'maintenance/status'])
    expect(calls.filter((call) => call.path === '/api/v1/environment')).toHaveLength(1)
  })

  it('môi trường chạy thật: không có dải báo, tiêu đề tab giữ nguyên', async () => {
    const { calls } = mockApi({ ...baseRoutes(anonymous) })
    renderApp('/dang-nhap')

    expect(await screen.findByRole('button', { name: /Đăng nhập/ })).toBeInTheDocument()
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/environment')).toBe(true))
    expect(screen.queryByText(/môi trường kiểm thử/)).not.toBeInTheDocument()
    expect(document.title).toBe(title)
  })
})
