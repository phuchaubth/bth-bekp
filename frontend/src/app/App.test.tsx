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
    expect(within(nav).getByText('Phiếu đồng thuận')).toBeInTheDocument()
    expect(within(nav).getByText('Người bệnh')).toBeInTheDocument()
    expect(within(nav).queryByText('Quản trị')).not.toBeInTheDocument()
    expect(within(nav).queryByText('Xuất dữ liệu')).not.toBeInTheDocument()

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

  it('điều hướng tới trang chức năng khi chọn menu', async () => {
    mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/departments': departments })
    const user = userEvent.setup()
    const { router } = renderApp('/')

    await user.click(within(await screen.findByRole('navigation')).getByText('Quản trị'))
    await user.click(await within(screen.getByRole('navigation')).findByText('Khoa/phòng'))

    expect(router.state.location.pathname).toBe('/quan-tri/khoa-phong')
    expect(await screen.findByRole('heading', { name: 'Khoa/phòng' })).toBeInTheDocument()
  })
})
