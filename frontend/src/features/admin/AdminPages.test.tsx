import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { Role } from '../../api/admin'
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

  it('tài khoản kiosk đang tạm khóa tra cứu: hiện trạng thái và mở khóa được ngay', async () => {
    const kiosk: (typeof users)[number] = {
      id: 'u-kiosk',
      userName: 'kiosk.kkb',
      fullName: 'Kiosk Khoa Khám bệnh',
      title: null,
      departmentId: 'd-kkb',
      departmentName: 'Khoa Khám bệnh',
      roleNames: ['Kiosk người bệnh'],
      isActive: true,
      isLockedOut: false,
      lastLoginAtUtc: '2026-11-16T01:00:00Z',
      isKioskLookupLocked: true,
    }
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'GET /api/v1/users': { items: [kiosk], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 },
      'POST /api/v1/users/u-kiosk/unlock': () => noContent(),
    })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    const row = (await screen.findByText('kiosk.kkb')).closest('tr')!
    expect(within(row).getByText('Tạm khóa tra cứu')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Thao tác khác cho kiosk.kkb' }))
    await user.click(await screen.findByRole('menuitem', { name: /Mở khóa/ }))
    expect(await screen.findByText(/Máy kiosk tra cứu lại được ngay/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Mở khóa' }))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/users/u-kiosk/unlock')).toBe(true))
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

  // Quyết định 40: quản trị thêm, đổi tên, xóa vai trò.
  const customRole: Role = {
    id: 'r-duocsi',
    code: 'VT_DUOC_SI',
    name: 'Dược sĩ',
    description: null,
    isSystem: false,
    isAdministrator: false,
    permissions: [Permissions.ConsentsRead],
  }

  it('thêm vai trò, lấy sẵn quyền từ vai trò có sẵn; lưu xong chọn ngay vai trò mới', async () => {
    let current: Role[] = [...roles]
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/roles': () => current,
      'GET /api/v1/permissions': permissionCatalog,
      'POST /api/v1/roles': (call) => {
        const body = call.body as { name: string; description: string | null; permissions: string[] }
        const created: Role = { ...customRole, id: 'r-moi', code: 'VT_DUOC_SI_LAM_SANG', ...body }
        current = [...current, created]
        return json(created, 201)
      },
    })
    renderApp('/quan-tri/vai-tro')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Thêm vai trò/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Thêm vai trò' })
    // Vai trò Quản trị hệ thống (quyền cố định) không dùng làm mẫu quyền.
    await user.click(within(dialog).getByRole('combobox', { name: 'Lấy sẵn quyền từ vai trò' }))
    const doctorOption = await screen.findByTitle('Bác sĩ (2 quyền)')
    const dropdown = doctorOption.closest<HTMLElement>('.ant-select-dropdown')!
    expect(within(dropdown).queryByTitle(/Quản trị hệ thống/)).not.toBeInTheDocument()
    await user.click(doctorOption)
    await user.type(within(dialog).getByLabelText('Tên vai trò'), 'Dược sĩ lâm sàng')
    await user.type(within(dialog).getByLabelText('Mô tả'), 'Khoa Dược')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/roles')).toBe(true))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/roles')!.body).toEqual({
      name: 'Dược sĩ lâm sàng',
      description: 'Khoa Dược',
      permissions: [Permissions.ConsentsRead, Permissions.ConsentsCreate],
    })
    expect(await screen.findByText(/Đã thêm vai trò "Dược sĩ lâm sàng" \(mã VT_DUOC_SI_LAM_SANG\)/)).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Thêm vai trò' })).not.toBeInTheDocument())
    expect(screen.getByRole('menuitem', { name: /Dược sĩ lâm sàng/ })).toHaveClass('ant-menu-item-selected')
    expect(screen.getByText('Tự tạo')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /Lập phiếu/ })).toBeChecked()
  })

  it('đổi tên vai trò có sẵn; tên trùng thì báo ngay ở ô tên', async () => {
    let attempts = 0
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/roles': roles,
      'GET /api/v1/permissions': permissionCatalog,
      'PUT /api/v1/roles/r-doctor': (call) => {
        attempts += 1
        return attempts === 1
          ? problem(400, 'Dữ liệu không hợp lệ.', { code: 'validation', errors: { name: ['Đã có vai trò tên "Quản trị hệ thống".'] } })
          : { ...roles[1], ...(call.body as object) }
      },
    })
    renderApp('/quan-tri/vai-tro')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('menuitem', { name: /Bác sĩ/ }))
    expect(screen.getByText('Có sẵn')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Xóa vai trò/ })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Sửa tên, mô tả/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Sửa tên, mô tả vai trò' })
    expect(within(dialog).getByText('DOCTOR')).toBeInTheDocument()
    const name = within(dialog).getByLabelText('Tên vai trò')
    await user.clear(name)
    await user.type(name, 'Quản trị hệ thống')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    expect(await within(dialog).findByText('Đã có vai trò tên "Quản trị hệ thống".')).toBeInTheDocument()

    await user.clear(name)
    await user.type(name, 'Bác sĩ điều trị')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    expect(await screen.findByText('Đã cập nhật vai trò "Bác sĩ điều trị".')).toBeInTheDocument()
    expect(calls.filter((c) => c.method === 'PUT').at(-1)!.body).toEqual({ name: 'Bác sĩ điều trị', description: 'Lập và ký phiếu' })
  })

  it('xóa vai trò tự tạo: máy chủ từ chối thì báo lý do; xóa được thì về vai trò đầu danh sách', async () => {
    let current: Role[] = [...roles, customRole]
    let attempts = 0
    mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/roles': () => current,
      'GET /api/v1/permissions': permissionCatalog,
      'DELETE /api/v1/roles/r-duocsi': () => {
        attempts += 1
        if (attempts === 1) {
          return problem(422, 'Vai trò đang gán cho 2 tài khoản. Bỏ vai trò này khỏi các tài khoản đó (Quản trị → Người dùng) rồi xóa.', {
            code: 'roles.in_use',
          })
        }
        current = current.filter((r) => r.id !== 'r-duocsi')
        return noContent()
      },
    })
    renderApp('/quan-tri/vai-tro')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('menuitem', { name: /Dược sĩ/ }))
    await user.click(screen.getByRole('button', { name: /Xóa vai trò/ }))
    await user.click(await screen.findByRole('button', { name: 'Xóa' }))
    expect(await screen.findByText(/Vai trò đang gán cho 2 tài khoản/)).toBeInTheDocument()
    // Nút còn hiệu ứng đang xử lý (không bấm được) đến khi yêu cầu xóa kết thúc.
    await waitFor(() => expect(screen.getByRole('button', { name: /Xóa vai trò/ })).not.toHaveClass('ant-btn-loading'))

    await user.click(screen.getByRole('button', { name: /Xóa vai trò/ }))
    await user.click((await screen.findAllByRole('button', { name: 'Xóa' })).at(-1)!)

    expect(await screen.findByText('Đã xóa vai trò "Dược sĩ".')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('menuitem', { name: /Dược sĩ/ })).not.toBeInTheDocument())
    expect(screen.getByRole('menuitem', { name: /Quản trị hệ thống/ })).toHaveClass('ant-menu-item-selected')
  })

  it('quyền dùng kiosk không đánh dấu được cho vai trò khác vai trò Kiosk', async () => {
    mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/roles': roles,
      'GET /api/v1/permissions': [
        ...permissionCatalog,
        { code: Permissions.KioskUse, name: 'Dùng kiosk người bệnh tự làm phiếu', group: 'Phiếu đồng thuận' },
      ],
    })
    renderApp('/quan-tri/vai-tro')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('menuitem', { name: /Bác sĩ/ }))

    const kiosk = screen.getByRole('checkbox', { name: /Dùng kiosk người bệnh tự làm phiếu.*chỉ dành cho vai trò Kiosk/ })
    expect(kiosk).toBeDisabled()
    expect(kiosk).not.toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Lập phiếu/ })).toBeEnabled()
  })
})
