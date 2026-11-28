import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { adminSession, anonymous, baseRoutes, departments, session } from '../test/fixtures'
import { mockApi, problem } from '../test/mockApi'
import { renderApp } from '../test/renderApp'

async function fillLogin(userName: string, password: string) {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Tên đăng nhập'), userName)
  await user.type(screen.getByLabelText('Mật khẩu'), password)
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))
}

describe('Đăng nhập và phiên làm việc', () => {
  it('chuyển tới trang đăng nhập khi chưa có phiên', async () => {
    mockApi(baseRoutes(anonymous))

    const { router } = renderApp('/')

    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dang-nhap')
    expect(router.state.location.search).toBe('?redirect=%2F')
  })

  it('đăng nhập thành công: hiển thị tổng quan và chỉ hiện menu theo quyền', async () => {
    const { calls } = mockApi({ ...baseRoutes(anonymous), 'POST /api/v1/auth/login': session() })
    renderApp('/')

    await fillLogin('bacsi.demo', 'Mat-Khau-2026!')

    expect(await screen.findByText('Xin chào Bác sĩ Demo – Khoa Nội tổng hợp.')).toBeInTheDocument()
    const nav = screen.getByRole('navigation')
    expect(within(nav).queryByText('Quản trị')).not.toBeInTheDocument()
    // Quyết định 62: các chức năng phiếu đồng thuận nằm chung menu "Phiếu đồng thuận" (mục con trong khung thả xuống).
    expect(within(nav).getByText('Phiếu đồng thuận')).toBeInTheDocument()
    expect(within(nav).queryByText('Người bệnh')).not.toBeInTheDocument()

    const login = calls.find((c) => c.path === '/api/v1/auth/login')!
    expect(login.body).toEqual({ userName: 'bacsi.demo', password: 'Mat-Khau-2026!' })
    expect(login.headers.Authorization).toBeUndefined()
  })

  it('hiển thị thông báo lỗi tiếng Việt từ máy chủ', async () => {
    mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/auth/login': () =>
        problem(401, 'Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.', {
          code: 'auth.locked_out',
        }),
    })
    renderApp('/dang-nhap')

    await fillLogin('tiepdon.demo', 'sai')

    expect(await screen.findByRole('alert')).toHaveTextContent('Tài khoản tạm khóa do đăng nhập sai nhiều lần')
  })

  it('kiểm tra bắt buộc nhập trước khi gửi', async () => {
    const { calls } = mockApi(baseRoutes(anonymous))
    renderApp('/dang-nhap')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Đăng nhập' }))

    expect(await screen.findByText('Vui lòng nhập tên đăng nhập.')).toBeInTheDocument()
    expect(screen.getByText('Vui lòng nhập mật khẩu.')).toBeInTheDocument()
    expect(calls.some((c) => c.path === '/api/v1/auth/login')).toBe(false)
  })

  it('quay lại trang đang mở sau khi đăng nhập', async () => {
    mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/auth/login': adminSession(),
      'GET /api/v1/departments': departments,
    })
    const { router } = renderApp('/quan-tri/khoa-phong')

    await fillLogin('admin', 'Mat-Khau-2026!')

    expect(await screen.findByRole('heading', { name: 'Khoa/phòng' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/quan-tri/khoa-phong')
  })

  it('không chuyển hướng ra trang web bên ngoài sau khi đăng nhập', async () => {
    mockApi({ ...baseRoutes(anonymous), 'POST /api/v1/auth/login': session() })
    const { router } = renderApp('/dang-nhap?redirect=//trang-gia-mao.example')

    await fillLogin('bacsi.demo', 'Mat-Khau-2026!')

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
  })

  it('khôi phục phiên khi tải lại trang bằng refresh token', async () => {
    const { calls } = mockApi(baseRoutes(session()))

    renderApp('/')

    expect(await screen.findByText('Xin chào Bác sĩ Demo – Khoa Nội tổng hợp.')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/auth/refresh')!.headers['X-Requested-With']).toBe('consent-web')
  })

  it('bắt buộc đổi mật khẩu trước khi sử dụng', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'POST /api/v1/auth/refresh': () =>
        new Response(JSON.stringify(session({ mustChangePassword: true, permissions: [] })), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      'POST /api/v1/auth/change-password': session(),
    })
    const { router } = renderApp('/phieu-dong-thuan')
    const user = userEvent.setup()

    expect(await screen.findByText(/Bạn cần đổi mật khẩu trước khi sử dụng hệ thống/)).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/doi-mat-khau')

    await user.type(screen.getByLabelText('Mật khẩu hiện tại'), 'Mat-Khau-Tam-2026')
    await user.type(screen.getByLabelText('Mật khẩu mới'), 'Mat-Khau-Moi-2026!')
    await user.type(screen.getByLabelText('Nhập lại mật khẩu mới'), 'Mat-Khau-Moi-2026!')
    await user.click(screen.getByRole('button', { name: 'Đổi mật khẩu' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
    expect(calls.find((c) => c.path === '/api/v1/auth/change-password')!.body).toEqual({
      currentPassword: 'Mat-Khau-Tam-2026',
      newPassword: 'Mat-Khau-Moi-2026!',
    })
  })

  it('kiểm tra mật khẩu mới ngay trên giao diện', async () => {
    mockApi(baseRoutes(session({ mustChangePassword: true, permissions: [] })))
    renderApp('/doi-mat-khau')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Mật khẩu mới'), 'yeu')
    await user.type(screen.getByLabelText('Nhập lại mật khẩu mới'), 'khac')
    await user.click(screen.getByRole('button', { name: 'Đổi mật khẩu' }))

    expect(await screen.findByText('Mật khẩu phải có ít nhất 10 ký tự.')).toBeInTheDocument()
    expect(screen.getByText('Mật khẩu nhập lại không khớp.')).toBeInTheDocument()
  })

  it('hiển thị trang 403 khi thiếu quyền', async () => {
    mockApi(baseRoutes(session()))

    renderApp('/quan-tri/nguoi-dung')

    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
  })

  it('đăng xuất: gọi API, xóa phiên và về trang đăng nhập', async () => {
    const { calls } = mockApi(baseRoutes(session()))
    renderApp('/')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Tài khoản' }))
    await user.click(await screen.findByText('Đăng xuất'))

    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/auth/logout')).toBe(true)
  })

  it('sau khi chủ động đăng xuất, người đăng nhập tiếp theo không bị đưa về trang của người trước', async () => {
    mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/departments': departments,
      'POST /api/v1/auth/login': session(),
    })
    const { router } = renderApp('/quan-tri/khoa-phong')
    const user = userEvent.setup()

    await screen.findByRole('heading', { name: 'Khoa/phòng' })
    await user.click(screen.getByRole('button', { name: 'Tài khoản' }))
    await user.click(await screen.findByText('Đăng xuất'))
    await screen.findByRole('button', { name: 'Đăng nhập' })
    expect(router.state.location.search).toBe('')

    await fillLogin('bacsi.demo', 'Mat-Khau-2026!')

    expect(await screen.findByText('Xin chào Bác sĩ Demo – Khoa Nội tổng hợp.')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/')
  })

  it('hiển thị trang 404 tiếng Việt cho đường dẫn không tồn tại', async () => {
    mockApi(baseRoutes(session()))

    renderApp('/khong-ton-tai')

    expect(await screen.findByText('Không tìm thấy trang')).toBeInTheDocument()
  })

  it('điều hướng tới trang chức năng khi chọn menu (menu điện thoại: mở nhóm Phiếu đồng thuận, chọn Mẫu phiếu)', async () => {
    // Khung thả xuống của menu ngang trên máy tính không mở được trong jsdom; kiểm tra bằng trình duyệt thật. Menu trên điện
    // thoại dùng cùng danh sách mục, mở nhóm ngay trong ngăn kéo.
    const original = window.matchMedia
    window.matchMedia = ((query: string) => ({ ...original(query), matches: false })) as typeof window.matchMedia
    try {
      mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/consent-templates': [] })
      const user = userEvent.setup()
      const { router } = renderApp('/')

      await user.click(await screen.findByRole('button', { name: 'Mở menu' }))
      const nav = await screen.findByRole('navigation', { name: 'Điều hướng chính' })
      expect(within(nav).queryByText('Mẫu phiếu')).not.toBeInTheDocument()
      await user.click(within(nav).getByText('Phiếu đồng thuận'))
      await user.click(await within(nav).findByText('Mẫu phiếu'))

      expect(router.state.location.pathname).toBe('/mau-phieu')
      expect(await screen.findByRole('heading', { name: 'Mẫu phiếu' })).toBeInTheDocument()
      // Mở lại menu: nhóm đang mở, mục Mẫu phiếu được đánh dấu.
      await user.click(screen.getByRole('button', { name: 'Mở menu' }))
      const item = (await within(screen.getByRole('navigation', { name: 'Điều hướng chính' })).findByText('Mẫu phiếu')).closest('li')!
      expect(item).toHaveClass('ant-menu-item-selected')
    } finally {
      window.matchMedia = original
    }
  })

  it('menu điện thoại mở sẵn nhóm chứa trang đang xem (đang ở Mẫu phiếu thì thấy ngay các mục Phiếu đồng thuận)', async () => {
    const original = window.matchMedia
    window.matchMedia = ((query: string) => ({ ...original(query), matches: false })) as typeof window.matchMedia
    try {
      mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/consent-templates': [] })
      const user = userEvent.setup()
      renderApp('/mau-phieu/m-1')

      await user.click(await screen.findByRole('button', { name: 'Mở menu' }))
      const nav = await screen.findByRole('navigation', { name: 'Điều hướng chính' })
      // Không bấm nhóm: các mục con đã hiện, Mẫu phiếu được đánh dấu; nhóm khác vẫn đóng.
      expect((await within(nav).findByText('Mẫu phiếu')).closest('li')).toHaveClass('ant-menu-item-selected')
      expect(within(nav).getByText('Danh sách phiếu')).toBeInTheDocument()
      expect(within(nav).queryByText('Người dùng')).not.toBeInTheDocument()
    } finally {
      window.matchMedia = original
    }
  })

  it('trang con của mục trong nhóm: nhóm Phiếu đồng thuận được đánh dấu đang mở, liên kết cũ vẫn mở đúng trang', async () => {
    mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/consent-templates': [] })

    renderApp('/mau-phieu')

    expect(await screen.findByRole('heading', { name: 'Mẫu phiếu' })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Điều hướng chính' })
    const group = within(nav).getByText('Phiếu đồng thuận').closest('li')!
    expect(group).toHaveClass('ant-menu-submenu-selected')
    expect(within(nav).getByText('Quản trị').closest('li')).not.toHaveClass('ant-menu-submenu-selected')
  })
})
