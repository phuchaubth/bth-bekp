import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { adminSession, anonymous, baseRoutes } from '../../test/fixtures'
import { mockApi } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Logo giả lập (thông tin ảnh, không phải logo thật của bệnh viện).
const logo = { contentType: 'image/png', width: 64, height: 64, updatedAtUtc: '2026-11-27T03:00:00Z' }
const logoSrc = `/api/v1/branding/logo/image?v=${encodeURIComponent(logo.updatedAtUtc)}`

function favicon() {
  const link = document.createElement('link')
  link.rel = 'icon'
  link.type = 'image/svg+xml'
  link.href = '/favicon.svg'
  document.head.appendChild(link)
  return link
}

describe('Logo phần mềm trên web (quyết định 61)', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    document.head.querySelectorAll('link[rel="icon"]').forEach((l) => l.remove())
  })

  it('thanh trên và biểu tượng thẻ dùng logo bệnh viện tải lên ở Cấu hình hệ thống', async () => {
    const link = favicon()
    mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/branding/logo': logo })
    renderApp('/')

    await screen.findByRole('navigation', { name: 'Điều hướng chính' })
    await waitFor(() => expect(document.querySelector(`header img[src="${logoSrc}"]`)).not.toBeNull())
    await waitFor(() => expect(link.getAttribute('href')).toBe(logoSrc))
    expect(link.type).toBe('image/png')
  })

  it('trang đăng nhập có logo; chưa có logo thì dùng dấu nhận diện và biểu tượng mặc định', async () => {
    mockApi({ ...baseRoutes(anonymous), 'GET /api/v1/branding/logo': logo })
    const { unmount } = renderApp('/dang-nhap')
    await waitFor(() => expect(document.querySelector(`img[src="${logoSrc}"]`)).not.toBeNull())
    unmount()

    vi.restoreAllMocks()
    const link = favicon()
    mockApi(baseRoutes(anonymous))
    renderApp('/dang-nhap')
    expect(await screen.findByRole('button', { name: /Đăng nhập/ })).toBeInTheDocument()
    await waitFor(() => expect(link.getAttribute('href')).toBe('/favicon.svg'))
    expect(document.querySelector('img[src^="/api/v1/branding/logo"]')).toBeNull()
  })
})
