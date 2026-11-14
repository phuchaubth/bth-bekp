import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions'
import {
  adminSession,
  baseRoutes,
  departments,
  permissionCatalog,
  roles,
  session,
  users,
} from '../../test/fixtures'
import { json, mockApi, noContent, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

describe('Khoa/phòng', () => {
  it('liệt kê khoa và tìm kiếm không phân biệt dấu', async () => {
    mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/departments': departments })
    renderApp('/quan-tri/khoa-phong')
    const user = userEvent.setup()

    expect(await screen.findByText('Khoa Khám bệnh')).toBeInTheDocument()
    expect(screen.getByText('Ngừng sử dụng')).toBeInTheDocument()

    await user.type(screen.getByPlaceholderText('Tìm theo mã hoặc tên khoa'), 'noi tong hop')

    expect(screen.getByText('Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(screen.queryByText('Khoa Khám bệnh')).not.toBeInTheDocument()
  })

  it('thêm khoa: chuẩn hóa mã thành chữ hoa và gửi đúng dữ liệu', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/departments': departments,
      'POST /api/v1/departments': () => json({ id: 'd-new', code: 'CDHA', name: 'Khoa Chẩn đoán hình ảnh', isActive: true }, 201),
    })
    renderApp('/quan-tri/khoa-phong')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Thêm khoa\/phòng/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Mã khoa'), 'cdha')
    await user.type(within(dialog).getByLabelText('Tên khoa/phòng'), 'Khoa Chẩn đoán hình ảnh')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/departments')).toBe(true))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/departments')!.body).toEqual({
      code: 'CDHA',
      name: 'Khoa Chẩn đoán hình ảnh',
      isActive: true,
    })
  })

  it('hiển thị lỗi từng trường do máy chủ trả về', async () => {
    mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/departments': departments,
      'POST /api/v1/departments': () =>
        problem(400, 'Dữ liệu không hợp lệ.', { code: 'validation', errors: { code: ['Mã khoa "KKB" đã tồn tại.'] } }),
    })
    renderApp('/quan-tri/khoa-phong')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Thêm khoa\/phòng/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Mã khoa'), 'KKB')
    await user.type(within(dialog).getByLabelText('Tên khoa/phòng'), 'Trùng')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    expect(await within(dialog).findByText('Mã khoa "KKB" đã tồn tại.')).toBeInTheDocument()
  })
})

describe('Người dùng', () => {
  const userRoutes = {
    'GET /api/v1/users': { items: users, page: 1, pageSize: 20, totalCount: 2, totalPages: 1 },
    'GET /api/v1/departments': departments,
  }

  it('liệt kê người dùng với vai trò và trạng thái', async () => {
    mockApi({ ...baseRoutes(adminSession()), ...userRoutes })
    renderApp('/quan-tri/nguoi-dung')

    expect(await screen.findByText('Tổng 2 người dùng')).toBeInTheDocument()
    const doctorRow = screen.getByText('bacsi.demo').closest('tr')!
    expect(within(doctorRow).getByText('Hoạt động')).toBeInTheDocument()
    expect(within(doctorRow).getByText('Khoa Nội tổng hợp')).toBeInTheDocument()
    const lockedRow = screen.getByText('tiepdon.demo').closest('tr')!
    expect(within(lockedRow).getByText('Đã khóa')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Thao tác khác cho tiepdon.demo' }))
    expect(await screen.findByRole('menuitem', { name: /Mở khóa/ })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: /Khóa tài khoản/ })).not.toBeInTheDocument()
  })

  it('chưa tích hợp SmartCA: ẩn ô mã SmartCA khi sửa người dùng nhưng vẫn giữ giá trị đã lưu', async () => {
    const doctor = {
      id: 'u-bacsi',
      userName: 'bacsi.demo',
      fullName: 'Bác sĩ Demo',
      email: null,
      phoneNumber: null,
      title: 'Bác sĩ',
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      smartCaUserId: 'SMARTCA0001',
      roles: [{ id: 'r-doctor', code: 'DOCTOR', name: 'Bác sĩ' }],
      isActive: true,
      isLockedOut: false,
      lockoutEndUtc: null,
      mustChangePassword: false,
      lastLoginAtUtc: null,
      createdAtUtc: '2026-11-01T00:00:00Z',
    }
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'GET /api/v1/roles': roles,
      'GET /api/v1/users/u-bacsi': doctor,
      'PUT /api/v1/users/u-bacsi': doctor,
    })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    const row = (await screen.findByText('bacsi.demo')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: /Sửa/ }))
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByLabelText('Họ tên')).toHaveValue('Bác sĩ Demo'))
    expect(within(dialog).queryByLabelText('Mã tài khoản SmartCA')).not.toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')!.body).toMatchObject({ fullName: 'Bác sĩ Demo', smartCaUserId: 'SMARTCA0001' })
  })

  it('khóa tài khoản sau khi xác nhận', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'POST /api/v1/users/u-bacsi/lock': () => noContent(),
    })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Thao tác khác cho bacsi.demo' }))
    await user.click(await screen.findByRole('menuitem', { name: /Khóa tài khoản/ }))
    expect(await screen.findByText(/sẽ bị đăng xuất khỏi mọi thiết bị/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Khóa' }))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/users/u-bacsi/lock')).toBe(true))
  })

  it('người chỉ có quyền xem không thấy nút thao tác', async () => {
    mockApi({
      ...baseRoutes(session({ permissions: [Permissions.UsersRead] })),
      ...userRoutes,
    })
    renderApp('/quan-tri/nguoi-dung')

    expect(await screen.findByText('bacsi.demo')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Thêm người dùng/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Sửa/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Thao tác khác/ })).not.toBeInTheDocument()
  })

  it('tìm kiếm gửi từ khóa lên máy chủ', async () => {
    const { calls } = mockApi({ ...baseRoutes(adminSession()), ...userRoutes })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    await user.type(await screen.findByPlaceholderText('Tìm theo họ tên, tên đăng nhập, chức danh'), 'dieu duong{Enter}')

    await waitFor(() =>
      expect(calls.some((c) => c.path === '/api/v1/users' && c.search.includes('keyword=dieu+duong'))).toBe(true),
    )
  })
})

describe('Vai trò và quyền', () => {
  const roleRoutes = { 'GET /api/v1/roles': roles, 'GET /api/v1/permissions': permissionCatalog }

  it('vai trò Quản trị hệ thống không sửa được quyền', async () => {
    mockApi({ ...baseRoutes(adminSession()), ...roleRoutes })
    renderApp('/quan-tri/vai-tro')

    expect(await screen.findByText('Vai trò Quản trị hệ thống luôn có toàn bộ quyền và không thể thay đổi.')).toBeInTheDocument()
    for (const checkbox of screen.getAllByRole('checkbox')) {
      expect(checkbox).toBeDisabled()
      expect(checkbox).toBeChecked()
    }
  })

  it('thay đổi quyền của vai trò và lưu', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...roleRoutes,
      'PUT /api/v1/roles/r-doctor/permissions': (call) => ({ ...roles[1], permissions: (call.body as { permissions: string[] }).permissions }),
    })
    renderApp('/quan-tri/vai-tro')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('menuitem', { name: /Bác sĩ/ }))
    const save = await screen.findByRole('button', { name: 'Lưu thay đổi' })
    expect(save).toBeDisabled()

    await user.click(screen.getByRole('checkbox', { name: /Lập phiếu/ }))
    await user.click(screen.getByRole('checkbox', { name: /Xem người dùng/ }))
    await user.click(save)

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    const body = calls.find((c) => c.method === 'PUT')!.body as { permissions: string[] }
    expect([...body.permissions].sort()).toEqual([Permissions.ConsentsRead, Permissions.UsersRead].sort())
  })
})
