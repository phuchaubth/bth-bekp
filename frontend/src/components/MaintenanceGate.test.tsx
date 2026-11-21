import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { MaintenanceInput, MaintenanceSettings } from '../api/maintenance'
import { maintenanceStore } from '../api/maintenanceStore'
import { filterNavItems, navItems } from '../app/navigation'
import { Permissions } from '../auth/permissions'
import { adminSession, anonymous, baseRoutes, session } from '../test/fixtures'
import { json, mockApi, problem } from '../test/mockApi'
import { renderApp } from '../test/renderApp'

// Quyết định 53: chế độ bảo trì.
const REASON = 'Nâng cấp phần mềm'
const END = '2026-11-21T04:30:00Z'
const MAINTENANCE = { code: 'system.maintenance', reason: REASON, expectedEndAtUtc: END }
const blocked = () => problem(503, 'Hệ thống đang bảo trì. Vui lòng quay lại sau.', MAINTENANCE)
/** IIS trả khi ứng dụng đang dừng (đang cài gói): không có nội dung JSON. */
const serverStopped = () => new Response('<h1>Service Unavailable</h1>', { status: 503, headers: { 'Content-Type': 'text/html' } })
const status = (enabled: boolean) => ({ enabled, reason: enabled ? REASON : null, expectedEndAtUtc: enabled ? END : null })
const maintenanceScreen = () => screen.findByRole('dialog', { name: 'Hệ thống đang bảo trì' })

afterEach(() => {
  window.history.replaceState(null, '', '/')
  window.sessionStorage.clear()
})

describe('Màn hình "Hệ thống đang bảo trì"', () => {
  it('đang làm việc: máy chủ báo bảo trì thì màn hình bảo trì che trang, chữ đang gõ vẫn còn; hết bảo trì thì màn hình đóng, làm tiếp', async () => {
    let maintenance = false
    const { calls } = mockApi({
      ...baseRoutes(session()),
      'GET /api/v1/patients': () => (maintenance ? blocked() : { items: [], totalCount: 0, page: 1, pageSize: 20 }),
      'GET /api/v1/maintenance': () => status(maintenance),
    })
    renderApp('/nguoi-benh')
    const user = userEvent.setup()
    const search = await screen.findByPlaceholderText(/Tìm theo họ tên/)

    maintenance = true
    await user.type(search, 'Nguyen Van A{Enter}')

    const dialog = await maintenanceScreen()
    expect(within(dialog).getByText(REASON)).toBeInTheDocument()
    expect(within(dialog).getByText(`Dự kiến xong lúc ${dayjs(END).format('HH:mm [ngày] DD/MM/YYYY')}.`)).toBeInTheDocument()
    expect(dialog).toHaveTextContent('dữ liệu đang nhập vẫn được giữ')
    // Bác sĩ không có quyền bảo trì: chỉ có nút đăng xuất, không có đăng nhập quản trị.
    expect(within(dialog).getByRole('button', { name: /Đăng xuất \(Bác sĩ Demo\)/ })).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: /Đăng nhập bằng tài khoản quản trị/ })).not.toBeInTheDocument()
    // Trang vẫn nằm dưới màn hình bảo trì, không bị dựng lại.
    expect(search).toBeInTheDocument()
    expect(search).toHaveValue('Nguyen Van A')

    maintenance = false
    await user.click(within(dialog).getByRole('button', { name: /Kiểm tra lại/ }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Hệ thống đang bảo trì' })).not.toBeInTheDocument())
    expect(screen.getByPlaceholderText(/Tìm theo họ tên/)).toHaveValue('Nguyen Van A')
    // Dữ liệu đang hiện được tải lại; phiên đăng nhập không bị xóa (không làm mới phiên thêm lần nào).
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/patients' && c.search.includes('keyword')).length).toBe(2))
    expect(calls.filter((c) => c.path === '/api/v1/auth/refresh')).toHaveLength(1)
  })

  it('mở ứng dụng khi đang bảo trì: thấy màn hình bảo trì; quản trị chọn đăng nhập, tài khoản không có quyền bị từ chối, quản trị vào được và thấy dải báo', async () => {
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'GET /api/v1/maintenance': status(true),
      'POST /api/v1/auth/login': (call) =>
        (call.body as { userName: string }).userName === 'admin'
          ? adminSession()
          : problem(503, 'Hệ thống đang bảo trì. Trong lúc bảo trì chỉ tài khoản có quyền bảo trì hệ thống đăng nhập được.', MAINTENANCE),
    })
    renderApp('/')
    const user = userEvent.setup()

    const dialog = await maintenanceScreen()
    expect(within(dialog).getByText(REASON)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Đăng nhập' })).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: /Đăng nhập bằng tài khoản quản trị/ }))

    expect(await screen.findByRole('button', { name: 'Đăng nhập' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Hệ thống đang bảo trì' })).not.toBeInTheDocument()
    expect(screen.getByText('Hệ thống đang bảo trì.')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Tên đăng nhập'), 'bacsi.demo')
    await user.type(screen.getByLabelText('Mật khẩu'), 'Mat-Khau-Demo')
    await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))

    expect(
      await screen.findByText('Hệ thống đang bảo trì. Trong lúc bảo trì chỉ tài khoản có quyền bảo trì hệ thống đăng nhập được.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Hệ thống đang bảo trì' })).not.toBeInTheDocument()

    await user.clear(screen.getByLabelText('Tên đăng nhập'))
    await user.type(screen.getByLabelText('Tên đăng nhập'), 'admin')
    await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))

    expect(await screen.findByText(`Đang bật chế độ bảo trì: ${REASON}.`, { exact: false })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Hệ thống đang bảo trì' })).not.toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/v1/auth/login')).toHaveLength(2)
  })

  it('người bệnh ký qua mã QR: màn hình bảo trì mời chờ, không có nút của nhân viên', async () => {
    window.history.replaceState(null, '', '/ky-tu-xa#MA-KY-GIA-LAP')
    mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/remote-signing/claim': blocked,
      'GET /api/v1/maintenance': status(true),
    })
    renderApp('/ky-tu-xa')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Ngày sinh (ngày/tháng/năm)'), '05041988')
    await user.click(screen.getByRole('button', { name: /Mở phiếu/ }))

    const dialog = await maintenanceScreen()
    expect(dialog).toHaveTextContent('Mời ông/bà chờ trong giây lát')
    expect(within(dialog).getByText(REASON)).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: /Đăng nhập bằng tài khoản quản trị/ })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: /Đăng xuất/ })).not.toBeInTheDocument()
  })

  it('phiên hết hạn đúng lúc bảo trì: không bị đẩy ra trang đăng nhập; hết bảo trì thì làm mới phiên và làm tiếp', async () => {
    let maintenance = false
    let expired = false
    const { calls } = mockApi({
      ...baseRoutes(session()),
      'POST /api/v1/auth/refresh': () => {
        if (maintenance) {
          return blocked()
        }
        expired = false
        return json(session())
      },
      'GET /api/v1/patients': () =>
        expired ? problem(401, 'Phiên đăng nhập đã hết hạn.') : { items: [], totalCount: 0, page: 1, pageSize: 20 },
      'GET /api/v1/maintenance': () => status(maintenance),
    })
    const { router } = renderApp('/nguoi-benh')
    const user = userEvent.setup()
    const search = await screen.findByPlaceholderText(/Tìm theo họ tên/)

    maintenance = true
    expired = true
    await user.type(search, 'Tran Thi B{Enter}')

    const dialog = await maintenanceScreen()
    expect(router.state.location.pathname).toBe('/nguoi-benh')
    expect(search).toHaveValue('Tran Thi B')

    maintenance = false
    await user.click(within(dialog).getByRole('button', { name: /Kiểm tra lại/ }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Hệ thống đang bảo trì' })).not.toBeInTheDocument())
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/auth/refresh').length).toBeGreaterThanOrEqual(3))
    expect(router.state.location.pathname).toBe('/nguoi-benh')
    expect(screen.getByPlaceholderText(/Tìm theo họ tên/)).toHaveValue('Tran Thi B')
  })

  it('đang ở trang đăng nhập (đã biết không bảo trì) thì máy chủ dừng: bấm đăng nhập là thấy màn hình bảo trì', async () => {
    let stopped = false
    const reload = vi.spyOn(maintenanceStore, 'reloadPage').mockImplementation(() => {})
    mockApi({
      ...baseRoutes(anonymous),
      'GET /api/v1/maintenance': () => (stopped ? serverStopped() : json(status(false))),
      'POST /api/v1/auth/login': () => serverStopped(),
    })
    renderApp('/dang-nhap')
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('Tên đăng nhập'), 'bacsi.demo')
    await user.type(screen.getByLabelText('Mật khẩu'), 'Mat-Khau-Demo')
    stopped = true
    await user.click(screen.getByRole('button', { name: 'Đăng nhập' }))

    const dialog = await maintenanceScreen()
    expect(dialog).toHaveTextContent('Máy chủ đang tạm dừng')
    // Kết quả "không bảo trì" hỏi từ trước không được đóng màn hình bảo trì hay tải lại trang khi máy chủ vẫn dừng.
    await new Promise((resolve) => setTimeout(resolve, 300))
    expect(screen.getByRole('dialog', { name: 'Hệ thống đang bảo trì' })).toBeInTheDocument()
    expect(reload).not.toHaveBeenCalled()
  })

  it('đang làm việc thì máy chủ dừng hẳn (đang cài gói): màn hình "Máy chủ đang tạm dừng"; chạy lại thì tải lại trang (có thể là bản mới)', async () => {
    let stopped = false
    const reload = vi.spyOn(maintenanceStore, 'reloadPage').mockImplementation(() => {})
    mockApi({
      ...baseRoutes(session()),
      'GET /api/v1/patients': () => (stopped ? serverStopped() : json({ items: [], totalCount: 0, page: 1, pageSize: 20 })),
      'GET /api/v1/maintenance': () => (stopped ? serverStopped() : json(status(false))),
    })
    renderApp('/nguoi-benh')
    const user = userEvent.setup()
    const search = await screen.findByPlaceholderText(/Tìm theo họ tên/)

    stopped = true
    await user.type(search, 'Le Minh{Enter}')

    const dialog = await maintenanceScreen()
    expect(dialog).toHaveTextContent('Máy chủ đang tạm dừng')
    stopped = false
    await user.click(within(dialog).getByRole('button', { name: /Kiểm tra lại/ }))

    await waitFor(() => expect(reload).toHaveBeenCalledTimes(1))
  })

  it('máy chủ dừng hẳn (đang cài gói): màn hình bảo trì; máy chủ chạy lại thì tải lại trang', async () => {
    let stopped = true
    const reload = vi.spyOn(maintenanceStore, 'reloadPage').mockImplementation(() => {})
    mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/auth/refresh': () => (stopped ? serverStopped() : json(session())),
      'GET /api/v1/environment': () => (stopped ? serverStopped() : json({ label: null })),
      'GET /api/v1/maintenance': () => (stopped ? serverStopped() : json(status(false))),
    })
    renderApp('/')
    const user = userEvent.setup()

    const dialog = await maintenanceScreen()
    expect(dialog).toHaveTextContent('Máy chủ đang tạm dừng')
    await user.click(within(dialog).getByRole('button', { name: /Kiểm tra lại/ }))
    expect(reload).not.toHaveBeenCalled()

    stopped = false
    await user.click(within(dialog).getByRole('button', { name: /Kiểm tra lại/ }))

    await waitFor(() => expect(reload).toHaveBeenCalledTimes(1))
  })
})

describe('Trang Bảo trì hệ thống', () => {
  it('chỉ người có quyền bảo trì thấy mục menu', () => {
    const adminChildren = (granted: string[]) =>
      filterNavItems(navItems, (p) => granted.includes(p)).find((i) => i.label === 'Quản trị')?.children?.map((c) => c.label) ?? []

    expect(adminChildren([Permissions.SystemMaintenance])).toEqual(['Bảo trì hệ thống'])
    expect(adminChildren([Permissions.SettingsManage])).toEqual(['Cấu hình hệ thống'])
  })

  it('bật (bắt buộc lý do, hỏi lại), thấy dải báo; đang bật thì sửa lý do; tắt thì hết dải báo', async () => {
    const off: MaintenanceSettings = { enabled: false, reason: null, expectedEndAtUtc: null, startedAtUtc: null, startedBy: null }
    let settings = off
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/maintenance/settings': () => settings,
      'PUT /api/v1/maintenance/settings': (call) => {
        const input = call.body as MaintenanceInput
        settings = input.enabled
          ? {
              enabled: true,
              reason: input.reason ?? null,
              expectedEndAtUtc: input.expectedEndAtUtc ?? null,
              startedAtUtc: '2026-11-21T02:00:00Z',
              startedBy: 'Quản trị Demo (admin)',
            }
          : off
        return settings
      },
    })
    renderApp('/quan-tri/bao-tri')
    const user = userEvent.setup()

    expect(await screen.findByText('Hệ thống đang hoạt động bình thường.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Bật chế độ bảo trì' }))
    expect(await screen.findByText('Vui lòng nhập lý do bảo trì (hiện cho người dùng).')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Lý do (hiện cho người dùng)'), REASON)
    await user.click(screen.getByRole('button', { name: 'Bật chế độ bảo trì' }))
    const confirm = await screen.findByRole('dialog', { name: 'Bật chế độ bảo trì?' })
    expect(confirm).toHaveTextContent('Tài khoản quản trị vẫn làm việc như thường.')
    await user.click(within(confirm).getByRole('button', { name: 'Bật bảo trì' }))

    expect(await screen.findByText('Đang bảo trì')).toBeInTheDocument()
    expect(screen.getByText(/, người bật: Quản trị Demo \(admin\)\./)).toBeInTheDocument()
    expect(screen.getByText(`Đang bật chế độ bảo trì: ${REASON}.`, { exact: false })).toBeInTheDocument()
    const puts = () => calls.filter((c) => c.method === 'PUT').map((c) => c.body)
    expect(puts()).toEqual([{ enabled: true, reason: REASON, expectedEndAtUtc: null }])

    const reason = screen.getByLabelText('Lý do (hiện cho người dùng)')
    await user.clear(reason)
    await user.type(reason, `${REASON}, chậm hơn dự kiến`)
    await user.click(screen.getByRole('button', { name: 'Lưu lý do, giờ dự kiến' }))
    await waitFor(() => expect(puts()).toHaveLength(2))
    expect(puts()[1]).toEqual({ enabled: true, reason: `${REASON}, chậm hơn dự kiến`, expectedEndAtUtc: null })

    await user.click(screen.getByRole('button', { name: 'Tắt chế độ bảo trì' }))

    expect(await screen.findByText('Hệ thống đang hoạt động bình thường.')).toBeInTheDocument()
    expect(puts()[2]).toEqual({ enabled: false })
    expect(screen.queryByText('Đang bật chế độ bảo trì', { exact: false })).not.toBeInTheDocument()
  })
})
