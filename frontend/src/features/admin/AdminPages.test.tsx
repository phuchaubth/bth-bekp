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
import { json, mockApi, noContent, problem, type ApiCall } from '../../test/mockApi'
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

  it('chưa kết nối API thông tin người bệnh: thêm, sửa, xóa trong phần mềm, không có nút lấy từ HIS', async () => {
    mockApi({ ...baseRoutes(adminSession()), 'GET /api/v1/departments': departments })
    renderApp('/quan-tri/khoa-phong')

    expect(await screen.findByText('Khoa Khám bệnh')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Thêm khoa\/phòng/ })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Sửa/ })).toHaveLength(departments.length)
    expect(screen.queryByRole('button', { name: /Lấy danh mục từ HIS/ })).not.toBeInTheDocument()
  })

  it('danh mục lấy từ HIS: không thêm, sửa, xóa; bấm Lấy danh mục từ HIS thì báo kết quả và tải lại danh sách', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/features': { his: false, smartCa: false, patientSource: true, departmentsFromHis: true },
      'GET /api/v1/departments': departments,
      'POST /api/v1/departments/sync-his': () =>
        json({ total: 4, added: 1, updated: 2, unchanged: 1, deactivated: ['Phòng Kế hoạch tổng hợp'], skipped: ['MA-KHOA-DAI-HON-HAI-MUOI'] }),
    })
    renderApp('/quan-tri/khoa-phong')
    const user = userEvent.setup()

    expect(await screen.findByText('Danh mục khoa/phòng lấy từ HIS: không thêm, sửa, xóa ở đây.')).toBeInTheDocument()
    expect(await screen.findByText('Khoa Khám bệnh')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Thêm khoa\/phòng/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Sửa/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Xóa/ })).not.toBeInTheDocument()
    const listCalls = () => calls.filter((c) => c.method === 'GET' && c.path === '/api/v1/departments').length
    const before = listCalls()

    await user.click(screen.getByRole('button', { name: /Lấy danh mục từ HIS/ }))

    expect(
      (await screen.findAllByText('Đã lấy 4 khoa/phòng từ HIS: thêm 1, cập nhật 2, giữ nguyên 1.')).length,
    ).toBeGreaterThan(0)
    expect(screen.getByText(/Chuyển sang ngừng sử dụng vì không còn trên HIS: Phòng Kế hoạch tổng hợp\./)).toBeInTheDocument()
    expect(screen.getByText(/Bỏ qua 1 dòng có mã rỗng hoặc dài quá 20 ký tự: MA-KHOA-DAI-HON-HAI-MUOI/)).toBeInTheDocument()
    expect(calls.filter((c) => c.method === 'POST' && c.path === '/api/v1/departments/sync-his')).toHaveLength(1)
    await waitFor(() => expect(listCalls()).toBeGreaterThan(before))
  })

  it('lấy danh mục từ HIS không được thì báo lỗi của máy chủ, danh sách giữ nguyên', async () => {
    mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/features': { his: false, smartCa: false, patientSource: true, departmentsFromHis: true },
      'GET /api/v1/departments': departments,
      'POST /api/v1/departments/sync-his': () =>
        problem(422, 'API thông tin người bệnh chưa có câu truy vấn danh mục khoa/phòng.', {
          code: 'departments.his_not_configured',
        }),
    })
    renderApp('/quan-tri/khoa-phong')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Lấy danh mục từ HIS/ }))

    expect(await screen.findByText('API thông tin người bệnh chưa có câu truy vấn danh mục khoa/phòng.')).toBeInTheDocument()
    expect(screen.queryByText(/Đã lấy/)).not.toBeInTheDocument()
    expect(screen.getByText('Khoa Khám bệnh')).toBeInTheDocument()
  })
})

describe('Người dùng', () => {
  /** Như máy chủ: lọc theo isActive (thẻ Đang sử dụng / Ngưng sử dụng, quyết định 51). */
  const usersPage = (items: typeof users) => (call: ApiCall) => {
    const active = new URLSearchParams(call.search).get('isActive')
    const page = active === null ? items : items.filter((u) => String(u.isActive) === active)
    return { items: page, page: 1, pageSize: 20, totalCount: page.length, totalPages: 1 }
  }
  const userRoutes = {
    'GET /api/v1/users': usersPage(users),
    'GET /api/v1/departments': departments,
  }

  it('hai thẻ Đang sử dụng và Ngưng sử dụng, có số tài khoản; mỗi thẻ có thao tác riêng', async () => {
    const { calls } = mockApi({ ...baseRoutes(adminSession()), ...userRoutes })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    expect(await screen.findByRole('tab', { name: 'Đang sử dụng (1)' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByRole('tab', { name: 'Ngưng sử dụng (1)' })).toBeInTheDocument()
    const doctorRow = (await screen.findByText('bacsi.demo')).closest('tr')!
    expect(within(doctorRow).getByText('Hoạt động')).toBeInTheDocument()
    expect(within(doctorRow).getByText('Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(screen.queryByText('tiepdon.demo')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Trạng thái' })).not.toBeInTheDocument()
    expect(calls.some((c) => c.path === '/api/v1/users' && c.search.includes('isActive=true'))).toBe(true)
    expect(calls.some((c) => c.path === '/api/v1/users' && c.search.includes('isActive=false'))).toBe(true)

    await user.click(within(doctorRow).getByRole('button', { name: 'Thao tác khác cho bacsi.demo' }))
    expect(await screen.findByRole('menuitem', { name: /Ngưng sử dụng/ })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: /Mở khóa/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: /Xóa/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Ngưng sử dụng (1)' }))
    const stoppedRow = (await screen.findByText('tiepdon.demo')).closest('tr')!
    expect(screen.queryByText('bacsi.demo')).not.toBeInTheDocument()
    expect(screen.getByText(/Tài khoản ngưng sử dụng không đăng nhập được/)).toBeInTheDocument()
    expect(within(stoppedRow).getByRole('button', { name: /Sử dụng lại/ })).toBeInTheDocument()
    expect(within(stoppedRow).getByRole('button', { name: /Xóa/ })).toBeInTheDocument()
    expect(within(stoppedRow).getByRole('button', { name: /Sửa/ })).toBeInTheDocument()
    expect(within(stoppedRow).queryByText('Hoạt động')).not.toBeInTheDocument()
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

  // Quyết định 41: một tài khoản thuộc nhiều khoa.
  it('danh sách hiện khoa chính và các khoa khác của tài khoản', async () => {
    mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'GET /api/v1/users': usersPage([
        { ...users[0], additionalDepartmentNames: ['Khoa Khám bệnh', 'Khoa Ngoại (giả lập)'] },
        { ...users[1], isActive: true },
      ]),
    })
    renderApp('/quan-tri/nguoi-dung')

    const row = (await screen.findByText('bacsi.demo')).closest('tr')!
    expect(within(row).getByText('Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(within(row).getByText('Khoa khác: Khoa Khám bệnh, Khoa Ngoại (giả lập)')).toBeInTheDocument()
    expect(within(screen.getByText('tiepdon.demo').closest('tr')!).queryByText(/Khoa khác/)).not.toBeInTheDocument()
  })

  it('sửa người dùng: thêm khoa/phòng khác; danh sách không có khoa chính; khoa ngừng sử dụng đang gán hiện đúng tên và bỏ ra được', async () => {
    const doctor = {
      id: 'u-bacsi',
      userName: 'bacsi.demo',
      fullName: 'Bác sĩ Demo',
      email: null,
      phoneNumber: null,
      title: 'Bác sĩ',
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      additionalDepartments: [{ id: 'd-cu', name: 'Khoa Cũ' }],
      smartCaUserId: null,
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
    // Khoa đã ngừng sử dụng vẫn hiện đúng tên khi tài khoản đang thuộc khoa đó.
    expect(within(dialog).getByText('Khoa Cũ (ngừng sử dụng)')).toBeInTheDocument()

    // Khoa ngừng sử dụng (máy chủ không cho lưu) bỏ ra được.
    const oldTag = within(dialog).getByTitle('Khoa Cũ (ngừng sử dụng)')
    await user.click(within(oldTag).getByRole('img', { name: 'close', hidden: true }))
    expect(within(dialog).queryByTitle('Khoa Cũ (ngừng sử dụng)')).not.toBeInTheDocument()

    // Danh sách khoa khác không có khoa chính.
    await user.click(within(dialog).getByRole('combobox', { name: 'Khoa/phòng khác' }))
    const kkbOption = await screen.findByTitle('Khoa Khám bệnh')
    const dropdown = kkbOption.closest<HTMLElement>('.ant-select-dropdown')!
    expect(within(dropdown).queryByTitle('Khoa Nội tổng hợp')).not.toBeInTheDocument()
    await user.click(kkbOption)

    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.filter((c) => c.method === 'PUT').at(-1)!.body).toMatchObject({
      departmentId: 'd-noi',
      additionalDepartmentIds: ['d-kkb'],
    })
  })

  it('đổi khoa chính sang một khoa đang ở danh sách khoa khác thì khoa đó rời danh sách khoa khác', async () => {
    const doctor = {
      id: 'u-bacsi',
      userName: 'bacsi.demo',
      fullName: 'Bác sĩ Demo',
      email: null,
      phoneNumber: null,
      title: 'Bác sĩ',
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      additionalDepartments: [{ id: 'd-kkb', name: 'Khoa Khám bệnh' }],
      smartCaUserId: null,
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
    const lastPut = () => calls.filter((c) => c.method === 'PUT').at(-1)?.body

    const row = (await screen.findByText('bacsi.demo')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: /Sửa/ }))
    const dialog = await screen.findByRole('dialog')
    await waitFor(() => expect(within(dialog).getByLabelText('Họ tên')).toHaveValue('Bác sĩ Demo'))

    await user.click(within(dialog).getByRole('combobox', { name: 'Khoa/phòng chính' }))
    const options = await screen.findAllByTitle('Khoa Khám bệnh')
    await user.click(options.find((o) => o.classList.contains('ant-select-item-option'))!)
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))
    await waitFor(() => expect(lastPut()).toMatchObject({ departmentId: 'd-kkb', additionalDepartmentIds: [] }))
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
      'GET /api/v1/users': usersPage([kiosk]),
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

  it('ngưng sử dụng tài khoản sau khi xác nhận', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'POST /api/v1/users/u-bacsi/lock': () => noContent(),
    })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Thao tác khác cho bacsi.demo' }))
    await user.click(await screen.findByRole('menuitem', { name: /Ngưng sử dụng/ }))
    expect(await screen.findByText(/sẽ bị đăng xuất khỏi mọi thiết bị/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Ngưng sử dụng' }))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/users/u-bacsi/lock')).toBe(true))
    expect(await screen.findByText('Đã ngưng sử dụng tài khoản.')).toBeInTheDocument()
  })

  it('thẻ Ngưng sử dụng: sử dụng lại tài khoản sau khi xác nhận', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'POST /api/v1/users/u-tiepdon/unlock': () => noContent(),
    })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: /Ngưng sử dụng/ }))
    const row = (await screen.findByText('tiepdon.demo')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: /Sử dụng lại/ }))
    expect(await screen.findByText('Người dùng đăng nhập lại được ngay với mật khẩu hiện tại.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sử dụng lại' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/users/u-tiepdon/unlock')).toBe(true))
    expect(await screen.findByText('Đã sử dụng lại tài khoản.')).toBeInTheDocument()
  })

  it('thẻ Ngưng sử dụng: xóa tài khoản sau khi xác nhận; máy chủ từ chối thì báo lý do', { timeout: 20_000 }, async () => {
    let refuse = true
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      ...userRoutes,
      'DELETE /api/v1/users/u-tiepdon': () =>
        refuse
          ? problem(422, 'Chỉ xóa được tài khoản đã ngưng sử dụng. Bấm Ngưng sử dụng trước, rồi xóa ở thẻ Ngưng sử dụng.', {
              code: 'users.delete_active',
            })
          : noContent(),
    })
    renderApp('/quan-tri/nguoi-dung')
    const user = userEvent.setup()
    const deleteCalls = () => calls.filter((c) => c.method === 'DELETE' && c.path === '/api/v1/users/u-tiepdon').length

    await user.click(await screen.findByRole('tab', { name: /Ngưng sử dụng/ }))
    const row = (await screen.findByText('tiepdon.demo')).closest('tr')!
    await user.click(within(row).getByRole('button', { name: /Xóa/ }))
    expect(await screen.findByText(/tên đăng nhập này không tạo lại được/)).toBeInTheDocument()
    expect(screen.getByText(/Phiếu, báo cáo, nhật ký đã có vẫn giữ tên người này. Không hoàn tác được./)).toBeInTheDocument()
    const listCalls = () => calls.filter((c) => c.method === 'GET' && c.path === '/api/v1/users').length
    const listedBefore = listCalls()
    await user.click(screen.getByRole('button', { name: 'Xóa' }))
    expect(await screen.findByText(/Chỉ xóa được tài khoản đã ngưng sử dụng/)).toBeInTheDocument()
    expect(deleteCalls()).toBe(1)
    // Danh sách tải lại (trạng thái có thể vừa đổi). Hộp xác nhận đóng: đã kiểm trên Chromium (jsdom không chạy hiệu ứng đóng).
    await waitFor(() => expect(listCalls()).toBeGreaterThan(listedBefore))

    // Hộp xác nhận đã đóng (lỗi báo ở thông báo); xóa lại, lần này máy chủ đồng ý.
    refuse = false
    await user.click(within(row).getByRole('button', { name: /Xóa/ }))
    await user.click((await screen.findAllByRole('button', { name: 'Xóa' })).at(-1)!)

    expect(await screen.findByText('Đã xóa tài khoản.')).toBeInTheDocument()
    expect(deleteCalls()).toBe(2)
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

    await userEvent.setup().click(screen.getByRole('tab', { name: /Ngưng sử dụng/ }))
    expect(await screen.findByText('tiepdon.demo')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Sử dụng lại/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Xóa/ })).not.toBeInTheDocument()
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
