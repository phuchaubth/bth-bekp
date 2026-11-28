import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { TestAssignment, TestAssignmentList, TestCandidate } from '../../api/testing'
import { Permissions } from '../../auth/permissions'
import { adminSession, baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật). Ngày tính theo hôm nay.
const day = (offset: number) => dayjs().add(offset, 'day').format('YYYY-MM-DD')
const VERSION = 'Gói 71 (giả lập)'

const assignment = (overrides: Partial<TestAssignment> = {}): TestAssignment => ({
  id: 't-1',
  version: VERSION,
  feature: 'Ủy quyền báo cáo điều dưỡng',
  instructions: 'Ủy quyền cho người khác rồi đăng nhập người được ủy quyền.',
  assigneeUserId: 'u-dieuduong',
  assigneeName: 'Điều dưỡng Demo',
  assigneeDepartment: 'Khoa Nội tổng hợp',
  startDate: day(0),
  dueDate: day(2),
  status: 'InProgress',
  result: null,
  findings: null,
  suggestions: null,
  reportedAtUtc: null,
  late: false,
  reviewNote: null,
  reviewedBy: null,
  reviewedAtUtc: null,
  createdBy: 'Quản trị Demo',
  createdAtUtc: '2026-11-21T01:00:00Z',
  cancelledBy: null,
  cancelledAtUtc: null,
  canReport: true,
  canEdit: false,
  ...overrides,
})

const candidates: TestCandidate[] = [
  { userId: 'u-bacsi', fullName: 'Bác sĩ Demo', title: 'Bác sĩ', departmentName: 'Khoa Nội tổng hợp' },
  { userId: 'u-dieuduong', fullName: 'Điều dưỡng Demo', title: null, departmentName: 'Khoa Nội tổng hợp' },
]

const nurseSession = () =>
  session({
    id: 'u-dieuduong',
    userName: 'dieuduong.demo',
    fullName: 'Điều dưỡng Demo',
    roles: [{ id: 'r-nurse', code: 'NURSE', name: 'Điều dưỡng' }],
    permissions: [...doctorPermissions, Permissions.NursingReportsSubmit],
  })

const lastCall = (calls: ApiCall[], method: string, path: string) =>
  calls.filter((c) => c.method === method && c.path === path).at(-1)

describe('Phân công kiểm thử (người quản lý)', () => {
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => 'blob:test/1')
    URL.revokeObjectURL = vi.fn()
  })

  const list: TestAssignmentList = {
    versions: [VERSION, 'Gói 70 (giả lập)'],
    items: [
      assignment({ canEdit: false, canReport: false, status: 'Reported', result: 'Failed', findings: 'Không thấy khoa Nhi.', reportedAtUtc: '2026-11-21T02:00:00Z', late: true }),
      assignment({ id: 't-2', assigneeUserId: 'u-bacsi', assigneeName: 'Bác sĩ Demo', status: 'Overdue', canEdit: true, canReport: false, dueDate: day(-1), startDate: day(-3) }),
      assignment({ id: 't-3', feature: 'Dashboard điều dưỡng', assigneeName: 'Bác sĩ Demo', status: 'Reported', result: 'Passed', canEdit: false, canReport: false }),
      assignment({ id: 't-4', feature: 'Dashboard điều dưỡng', status: 'Cancelled', canEdit: false, canReport: false }),
    ],
  }

  it('tổng hợp theo tính năng, lọc đợt, xuất Excel, hủy phân công chưa báo cáo', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/testing/assignments': list,
      'GET /api/v1/testing/assignments/xlsx': () => new Response(new Blob(['PK']), { status: 200 }),
      'POST /api/v1/testing/assignments/t-2/cancel': assignment({ id: 't-2', status: 'Cancelled' }),
    })
    renderApp('/quan-tri/kiem-thu')

    const summary = (await screen.findByText('Tổng hợp theo tính năng')).closest('.ant-card') as HTMLElement
    // Ủy quyền: 2 người (1 không đạt, 1 chưa báo cáo và quá hạn); Dashboard: 1 người đạt (phân công đã hủy không tính).
    const delegationRow = (await within(summary).findAllByText('Ủy quyền báo cáo điều dưỡng'))[0].closest('tr')!
    expect(within(delegationRow).getAllByRole('cell').map((c) => c.textContent)).toEqual([
      expect.stringContaining('Ủy quyền báo cáo điều dưỡng'),
      '2',
      '0',
      '0',
      '1',
      '1 (1 quá hạn)',
    ])
    const dashboardRow = within(summary).getByText('Dashboard điều dưỡng').closest('tr')!
    expect(within(dashboardRow).getAllByRole('cell').map((c) => c.textContent).slice(1)).toEqual(['1', '1', '0', '0', '0'])

    const table = screen.getByText('Phân công', { selector: '.ant-card-head-title' }).closest('.ant-card') as HTMLElement
    expect(within(table).getByText('Trễ hạn')).toBeInTheDocument()
    expect(within(table).getByText('Không đạt')).toBeInTheDocument()
    expect(within(table).getByText('Quá hạn')).toBeInTheDocument()
    expect(within(table).getAllByRole('button', { name: 'Hủy' })).toHaveLength(1)

    await user.click(screen.getByRole('combobox', { name: 'Đợt kiểm thử' }))
    await user.click(await screen.findByTitle(VERSION))
    await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/testing/assignments')!.search).toBe(`?version=${encodeURIComponent(VERSION)}`))

    await user.click(screen.getByRole('button', { name: /Xuất Excel/ }))
    await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/testing/assignments/xlsx')!.search).toBe(`?version=${encodeURIComponent(VERSION)}`))

    await user.click(within(table).getByRole('button', { name: 'Hủy' }))
    await user.click(await screen.findByRole('button', { name: 'Hủy phân công' }))
    await waitFor(() => expect(lastCall(calls, 'POST', '/api/v1/testing/assignments/t-2/cancel')).toBeDefined())
    expect(await screen.findByText('Đã hủy phân công.')).toBeInTheDocument()
  })

  it('phân công một tính năng cho nhiều người, mỗi người một thời gian; lỗi từng người hiện đúng dòng', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    let attempt = 0
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/testing/assignments': { versions: [VERSION], items: [] },
      'GET /api/v1/testing/assignments/candidates': candidates,
      'POST /api/v1/testing/assignments': () =>
        ++attempt === 1
          ? problem(400, 'Dữ liệu không hợp lệ.', { code: 'validation', errors: { 'assignees[1].dueDate': ['Hạn báo cáo từ hôm nay trở đi.'] } })
          : [assignment(), assignment({ id: 't-2', assigneeUserId: 'u-bacsi' })],
    })
    renderApp('/quan-tri/kiem-thu')

    await user.click(await screen.findByRole('button', { name: /Phân công mới/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('combobox', { name: /Đợt kiểm thử/ }), VERSION)
    await user.type(within(dialog).getByRole('textbox', { name: 'Tính năng cần kiểm thử' }), 'Ủy quyền báo cáo điều dưỡng')
    await user.type(within(dialog).getByRole('textbox', { name: /Nội dung, cách kiểm thử/ }), '  Thử ủy quyền rồi gửi báo cáo.  ')

    await user.click(within(dialog).getByRole('combobox', { name: 'Người kiểm thử 1' }))
    await user.click(await screen.findByTitle('Điều dưỡng Demo – Khoa Nội tổng hợp'))
    await user.click(within(dialog).getByRole('button', { name: /Thêm người kiểm thử/ }))
    await user.click(within(dialog).getByRole('combobox', { name: 'Người kiểm thử 2' }))
    // Ô chọn thứ hai mở danh sách riêng (danh sách của ô thứ nhất vẫn nằm trong trang): chọn trong danh sách mở sau cùng.
    await user.click((await screen.findAllByTitle('Bác sĩ Demo – Khoa Nội tổng hợp')).at(-1)!)
    await user.click(within(dialog).getByRole('button', { name: 'Phân công' }))

    expect(await within(dialog).findByText('Hạn báo cáo từ hôm nay trở đi.')).toBeInTheDocument()
    // Lỗi nằm dưới ô thời gian của người thứ hai, không phải dưới ô chọn người.
    const secondRange = within(dialog).getAllByPlaceholderText('Từ ngày')[1].closest('.ant-form-item') as HTMLElement
    expect(within(secondRange).getByText('Hạn báo cáo từ hôm nay trở đi.')).toBeInTheDocument()
    expect(lastCall(calls, 'POST', '/api/v1/testing/assignments')?.body).toEqual({
      version: VERSION,
      feature: 'Ủy quyền báo cáo điều dưỡng',
      instructions: 'Thử ủy quyền rồi gửi báo cáo.',
      assignees: [
        { userId: 'u-dieuduong', startDate: day(0), dueDate: day(2) },
        { userId: 'u-bacsi', startDate: day(0), dueDate: day(2) },
      ],
    })

    // Bỏ người thứ hai rồi phân công lại.
    await user.click(within(dialog).getByRole('button', { name: 'Bỏ người kiểm thử 2' }))
    await user.click(within(dialog).getByRole('button', { name: 'Phân công' }))
    expect(await screen.findByText('Đã phân công 2 người.')).toBeInTheDocument()
    expect((lastCall(calls, 'POST', '/api/v1/testing/assignments')!.body as { assignees: unknown[] }).assignees).toHaveLength(1)
  })

  it('xem báo cáo và ghi hướng xử lý', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/testing/assignments': list,
      'POST /api/v1/testing/assignments/t-1/review': assignment({ reviewNote: 'Sửa trong gói 72.' }),
    })
    renderApp('/quan-tri/kiem-thu')

    const row = (await screen.findAllByRole('button', { name: 'Xem báo cáo' }))[0]
    await user.click(row)
    const drawer = await screen.findByRole('dialog')
    expect(within(drawer).getByText('Không thấy khoa Nhi.')).toBeInTheDocument()
    await user.click(within(drawer).getByRole('button', { name: 'Lưu hướng xử lý' }))
    expect(await within(drawer).findByText('Ghi hướng xử lý.')).toBeInTheDocument()
    await user.type(within(drawer).getByRole('textbox', { name: 'Hướng xử lý' }), 'Sửa trong gói 72.')
    await user.click(within(drawer).getByRole('button', { name: 'Lưu hướng xử lý' }))
    await waitFor(() => expect(lastCall(calls, 'POST', '/api/v1/testing/assignments/t-1/review')?.body).toEqual({ note: 'Sửa trong gói 72.' }))
  })

  it('tài khoản không có quyền testing.manage không mở được trang phân công', async () => {
    mockApi({ ...baseRoutes(nurseSession()) })
    renderApp('/quan-tri/kiem-thu')

    expect(await screen.findByText(/không có quyền/i)).toBeInTheDocument()
  })
})

describe('Việc kiểm thử của tôi', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('gửi báo cáo bắt buộc: không đạt thì phải ghi lỗi; sửa được tới khi có hướng xử lý', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    let reported: TestAssignment | null = null
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/testing/assignments/mine': () => [
        reported ?? assignment(),
        assignment({ id: 't-up', feature: 'Dashboard điều dưỡng', status: 'Upcoming', startDate: day(2), dueDate: day(4), canReport: false }),
        assignment({ id: 't-done', feature: 'Môi trường UAT', status: 'Reported', result: 'Passed', reviewNote: 'Đã đưa lên chạy thật.', reviewedBy: 'Quản trị Demo', canReport: false }),
      ],
      'PUT /api/v1/testing/assignments/t-1/report': (call: ApiCall) => {
        reported = assignment({ status: 'Reported', result: 'Failed', findings: (call.body as { findings: string }).findings, reportedAtUtc: '2026-11-21T03:00:00Z' })
        return reported
      },
    })
    renderApp('/kiem-thu')

    const first = (await screen.findByText('Ủy quyền báo cáo điều dưỡng')).closest('.ant-card') as HTMLElement
    expect(within(first).getByText('Đang kiểm thử')).toBeInTheDocument()
    const upcoming = screen.getByText('Dashboard điều dưỡng').closest('.ant-card') as HTMLElement
    expect(within(upcoming).getByText('Gửi báo cáo được từ ngày bắt đầu kiểm thử.')).toBeInTheDocument()
    expect(within(upcoming).queryByRole('button', { name: /báo cáo/ })).not.toBeInTheDocument()
    const done = screen.getByText('Môi trường UAT').closest('.ant-card') as HTMLElement
    expect(within(done).getByText('Đã đưa lên chạy thật. (Quản trị Demo)')).toBeInTheDocument()
    expect(within(done).queryByRole('button', { name: /báo cáo/ })).not.toBeInTheDocument()

    await user.click(within(first).getByRole('button', { name: 'Gửi báo cáo' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('radio', { name: 'Không đạt' }))
    await user.click(within(dialog).getByRole('button', { name: 'Gửi báo cáo' }))
    expect(await within(dialog).findByText('Ghi lỗi hoặc vấn đề phát hiện khi kết quả không đạt hoặc đạt một phần.')).toBeInTheDocument()
    expect(lastCall(calls, 'PUT', '/api/v1/testing/assignments/t-1/report')).toBeUndefined()

    await user.type(within(dialog).getByRole('textbox', { name: 'Lỗi, vấn đề phát hiện' }), ' Không thấy khoa Nhi. ')
    await user.click(within(dialog).getByRole('button', { name: 'Gửi báo cáo' }))
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/testing/assignments/t-1/report')?.body).toEqual({
        result: 'Failed',
        findings: 'Không thấy khoa Nhi.',
        suggestions: null,
      }),
    )
    expect(await screen.findByText('Đã gửi báo cáo kiểm thử.')).toBeInTheDocument()
    expect(await within(first).findByRole('button', { name: 'Sửa báo cáo' })).toBeInTheDocument()
  })

  it('chưa được phân công: báo trống; trang Tổng quan nhắc việc chưa báo cáo và quá hạn', async () => {
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/dashboard': problem(403, 'Không có quyền.'),
      'GET /api/v1/testing/assignments/mine': [
        assignment(),
        assignment({ id: 't-2', status: 'Overdue' }),
        assignment({ id: 't-3', status: 'Upcoming', canReport: false }),
        assignment({ id: 't-4', status: 'Reported', result: 'Passed' }),
      ],
    })
    renderApp('/')

    expect(await screen.findByText('Bạn có 2 việc kiểm thử chưa báo cáo (1 quá hạn).')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Xem và báo cáo' })).toHaveAttribute('href', '/kiem-thu')
  })

  it('không có việc kiểm thử: trang trống, Tổng quan không nhắc', async () => {
    mockApi({ ...baseRoutes(nurseSession()), 'GET /api/v1/testing/assignments/mine': [] })
    renderApp('/kiem-thu')

    expect(await screen.findByText('Bạn chưa được phân công kiểm thử.')).toBeInTheDocument()
  })
})
