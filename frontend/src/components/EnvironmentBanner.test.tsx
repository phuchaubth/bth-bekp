import { screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { anonymous, baseRoutes } from '../test/fixtures'
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

  it('môi trường chạy thật: không có dải báo, tiêu đề tab giữ nguyên', async () => {
    const { calls } = mockApi({ ...baseRoutes(anonymous) })
    renderApp('/dang-nhap')

    expect(await screen.findByRole('button', { name: /Đăng nhập/ })).toBeInTheDocument()
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/environment')).toBe(true))
    expect(screen.queryByText(/môi trường kiểm thử/)).not.toBeInTheDocument()
    expect(document.title).toBe(title)
  })
})
