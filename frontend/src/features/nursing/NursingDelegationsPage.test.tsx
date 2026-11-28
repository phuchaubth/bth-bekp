import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { describe, expect, it } from 'vitest'
import type { NursingDelegateCandidate, NursingDelegation } from '../../api/nursingReports'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'
import { delegationPeriod } from './nursingReportUtils'

// Dữ liệu giả lập cho test (không phải dữ liệu thật). Ngày tính theo hôm nay để không phụ thuộc ngày chạy test.
const day = (offset: number) => dayjs().add(offset, 'day').format('YYYY-MM-DD')

const nurseSession = () =>
  session({
    id: 'u-dieuduong',
    userName: 'dieuduong.demo',
    fullName: 'Điều dưỡng Demo',
    roles: [{ id: 'r-nurse', code: 'NURSE', name: 'Điều dưỡng' }],
    permissions: [...doctorPermissions, Permissions.NursingReportsSubmit],
  })

const officeSession = () =>
  session({
    id: 'u-pdd',
    userName: 'phongdieuduong.demo',
    fullName: 'Phòng Điều dưỡng Demo',
    departmentId: 'd-pdd',
    departmentName: 'Phòng Điều dưỡng',
    roles: [{ id: 'r-office', code: 'NURSING_OFFICE', name: 'Phòng Điều dưỡng' }],
    permissions: [Permissions.NursingReportsRead, Permissions.NursingReportsManage],
  })

const delegation = (overrides: Partial<NursingDelegation> = {}): NursingDelegation => ({
  id: 'dl-1',
  departmentId: 'd-noi',
  departmentCode: 'NOI',
  departmentName: 'Khoa Nội tổng hợp',
  delegatorUserId: 'u-dieuduong',
  delegatorName: 'Điều dưỡng Demo',
  delegateUserId: 'u-ngoai',
  delegateName: 'Điều dưỡng Ngoại B (giả lập)',
  startDate: day(0),
  endDate: day(2),
  contents: ['report'],
  reason: 'Nghỉ phép',
  status: 'Active',
  createdAtUtc: '2026-11-20T01:00:00Z',
  revokedBy: null,
  revokedAtUtc: null,
  canRevoke: true,
  ...overrides,
})

/** Người ủy quyền cho điều dưỡng Demo làm báo cáo Khoa Nhi. */
const received = delegation({
  id: 'dl-2',
  departmentId: 'd-nhi',
  departmentCode: 'NHI',
  departmentName: 'Khoa Nhi (giả lập)',
  delegatorUserId: 'u-nhi',
  delegatorName: 'Điều dưỡng Nhi (giả lập)',
  delegateUserId: 'u-dieuduong',
  delegateName: 'Điều dưỡng Demo',
  startDate: day(-1),
  endDate: day(1),
  contents: ['report', 'hisEntry'],
  reason: null,
  canRevoke: false,
})

const candidates: NursingDelegateCandidate[] = [
  { userId: 'u-ngoai', fullName: 'Điều dưỡng Ngoại B (giả lập)', title: 'Điều dưỡng', departmentName: 'Khoa Ngoại B (giả lập)' },
  { userId: 'u-nhi', fullName: 'Điều dưỡng Nhi (giả lập)', title: null, departmentName: 'Khoa Nhi (giả lập)' },
]

const lastCall = (calls: ApiCall[], method: string, path: string) =>
  calls.filter((c) => c.method === method && c.path === path).at(-1)

const card = (title: string) => screen.getByText(title).closest('.ant-card') as HTMLElement

describe('Ủy quyền báo cáo điều dưỡng', () => {
  it('người lập báo cáo thấy ủy quyền mình đã tạo và được giao, mở được báo cáo của khoa được ủy quyền', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/delegations': [
        delegation(),
        received,
        // Ủy quyền đã hết hạn: không còn mở báo cáo của khoa.
        {
          ...received,
          id: 'dl-old',
          departmentCode: 'TMH',
          departmentName: 'Khoa Tai Mũi Họng (giả lập)',
          delegatorName: 'Điều dưỡng TMH (giả lập)',
          contents: ['report'],
          status: 'Expired',
          startDate: day(-6),
          endDate: day(-4),
        },
      ],
    })
    renderApp('/dieu-duong/uy-quyen')

    expect(await screen.findByText('Ủy quyền báo cáo điều dưỡng')).toBeInTheDocument()
    const given = card('Tôi đã ủy quyền')
    expect(await within(given).findByText('Điều dưỡng Ngoại B (giả lập)')).toBeInTheDocument()
    expect(within(given).getByText(delegationPeriod(delegation()))).toBeInTheDocument()
    expect(within(given).getByText('Báo cáo của khoa')).toBeInTheDocument()
    expect(within(given).getByText('Nghỉ phép')).toBeInTheDocument()
    expect(within(given).getByText('Đang hiệu lực')).toBeInTheDocument()
    expect(within(given).getByRole('button', { name: 'Hủy ủy quyền' })).toBeInTheDocument()
    expect(within(given).queryByRole('link', { name: 'Mở báo cáo của khoa' })).not.toBeInTheDocument()

    const got = card('Tôi được ủy quyền')
    expect(within(got).getByText('Điều dưỡng Nhi (giả lập)')).toBeInTheDocument()
    expect(within(got).getByText('Số liệu HIS khi HIS lỗi')).toBeInTheDocument()
    expect(within(got).getByText('Đã hết hạn')).toBeInTheDocument()
    expect(within(got).getByRole('link', { name: 'Mở báo cáo của khoa' })).toHaveAttribute('href', '/dieu-duong/bao-cao-khoa?khoa=d-nhi')
    // Người được ủy quyền không tự hủy được (máy chủ trả canRevoke = false).
    expect(within(got).queryByRole('button', { name: 'Hủy ủy quyền' })).not.toBeInTheDocument()

    // Không có quyền xem báo cáo của mọi khoa: không tải "Mọi ủy quyền".
    expect(screen.queryByText('Mọi ủy quyền')).not.toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/v1/nursing-reports/delegations').every((c) => c.search === '')).toBe(true)
  })

  it('ủy quyền mới: chọn người, thời gian, nội dung; gửi đúng dữ liệu rồi tải lại danh sách', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    let created: NursingDelegation[] = []
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/delegations': () => created,
      'GET /api/v1/nursing-reports/delegations/candidates': candidates,
      'POST /api/v1/nursing-reports/delegations': () => {
        created = [delegation({ contents: ['report', 'hisEntry'], reason: 'Đi học' })]
        return created[0]
      },
    })
    renderApp('/dieu-duong/uy-quyen')

    expect(await screen.findByText('Bạn chưa ủy quyền cho ai.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Ủy quyền mới/ }))
    const dialog = await screen.findByRole('dialog')
    // Mặc định: khoa chính, từ hôm nay đến ngày mai, ủy quyền báo cáo của khoa.
    expect(within(dialog).getByRole('combobox', { name: 'Khoa' }).closest('.ant-select')).toHaveTextContent('Khoa Nội tổng hợp')
    expect(within(dialog).getByRole('checkbox', { name: 'Báo cáo của khoa' })).toBeChecked()
    expect(within(dialog).getByRole('checkbox', { name: 'Số liệu HIS khi HIS lỗi' })).not.toBeChecked()

    // Chưa chọn người: không gửi.
    await user.click(within(dialog).getByRole('button', { name: 'Ủy quyền' }))
    expect(await within(dialog).findByText('Chọn người được ủy quyền.')).toBeInTheDocument()
    expect(lastCall(calls, 'POST', '/api/v1/nursing-reports/delegations')).toBeUndefined()

    await user.click(within(dialog).getByRole('combobox', { name: 'Người được ủy quyền' }))
    await user.click(await screen.findByTitle('Điều dưỡng Ngoại B (giả lập) – Khoa Ngoại B (giả lập)'))
    await user.click(within(dialog).getByRole('checkbox', { name: 'Số liệu HIS khi HIS lỗi' }))
    await user.type(within(dialog).getByRole('textbox', { name: 'Lý do (không bắt buộc)' }), '  Đi học ')
    await user.click(within(dialog).getByRole('button', { name: 'Ủy quyền' }))

    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/nursing-reports/delegations')?.body).toEqual({
        departmentId: 'd-noi',
        delegateUserId: 'u-ngoai',
        startDate: day(0),
        endDate: day(1),
        report: true,
        hisEntry: true,
        reason: 'Đi học',
      }),
    )
    expect(await screen.findByText('Đã ủy quyền.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await within(card('Tôi đã ủy quyền')).findByText('Đi học')).toBeInTheDocument()
  })

  it('lỗi của máy chủ hiện ở đúng ô: thời gian, người được ủy quyền, nội dung', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/delegations': [],
      'GET /api/v1/nursing-reports/delegations/candidates': candidates,
      'POST /api/v1/nursing-reports/delegations': () =>
        problem(400, 'Dữ liệu không hợp lệ.', {
          code: 'validation',
          errors: {
            endDate: ['Ủy quyền tối đa 366 ngày.'],
            delegateUserId: ['Người được ủy quyền phải là tài khoản đang dùng, thuộc một khoa, có quyền lập báo cáo điều dưỡng.'],
            contents: ['Khoa không có số liệu HIS phải nhập nên không ủy quyền nhập số liệu HIS được.'],
          },
        }),
    })
    renderApp('/dieu-duong/uy-quyen')

    await user.click(await screen.findByRole('button', { name: /Ủy quyền mới/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('combobox', { name: 'Người được ủy quyền' }))
    await user.click(await screen.findByTitle('Điều dưỡng Nhi (giả lập) – Khoa Nhi (giả lập)'))
    await user.click(within(dialog).getByRole('button', { name: 'Ủy quyền' }))

    expect(await within(dialog).findByText('Ủy quyền tối đa 366 ngày.')).toBeInTheDocument()
    expect(within(dialog).getByText(/Người được ủy quyền phải là tài khoản đang dùng/)).toBeInTheDocument()
    expect(within(dialog).getByText(/không ủy quyền nhập số liệu HIS được/)).toBeInTheDocument()
    // Hộp vẫn mở để sửa.
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('người ủy quyền hủy ủy quyền sau khi xác nhận', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    let revoked = false
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/delegations': () =>
        revoked
          ? [delegation({ status: 'Revoked', canRevoke: false, revokedBy: 'Điều dưỡng Demo', revokedAtUtc: '2026-11-20T02:00:00Z' })]
          : [delegation()],
      'POST /api/v1/nursing-reports/delegations/dl-1/revoke': () => {
        revoked = true
        return delegation({ status: 'Revoked', canRevoke: false })
      },
    })
    renderApp('/dieu-duong/uy-quyen')

    const given = await screen.findByText('Tôi đã ủy quyền').then((t) => t.closest('.ant-card') as HTMLElement)
    await user.click(await within(given).findByRole('button', { name: 'Hủy ủy quyền' }))
    expect(await screen.findByText('Hủy ủy quyền này?')).toBeInTheDocument()
    // Nút xác nhận trong hộp hỏi cũng tên "Hủy ủy quyền": bấm nút trong hộp hỏi.
    const confirm = screen.getByText('Hủy ủy quyền này?').closest('.ant-popover') as HTMLElement
    await user.click(within(confirm).getByRole('button', { name: 'Hủy ủy quyền' }))

    await waitFor(() => expect(lastCall(calls, 'POST', '/api/v1/nursing-reports/delegations/dl-1/revoke')).toBeDefined())
    expect(await screen.findByText('Đã hủy ủy quyền.')).toBeInTheDocument()
    expect(await within(given).findByText('Đã hủy')).toBeInTheDocument()
    expect(within(given).queryByRole('button', { name: 'Hủy ủy quyền' })).not.toBeInTheDocument()
  })

  it('Phòng Điều dưỡng xem mọi ủy quyền, không tạo ủy quyền', async () => {
    const { calls } = mockApi({
      ...baseRoutes(officeSession()),
      'GET /api/v1/nursing-reports/delegations': (call: ApiCall) =>
        call.search === '?all=true'
          ? [delegation({ canRevoke: true }), received, delegation({ id: 'dl-3', status: 'Expired', canRevoke: false, startDate: day(-5), endDate: day(-3) })]
          : problem(403, 'Bạn không có quyền.'),
    })
    renderApp('/dieu-duong/uy-quyen')

    const all = await screen.findByText('Mọi ủy quyền').then((t) => t.closest('.ant-card') as HTMLElement)
    expect(await within(all).findByText('Điều dưỡng Nhi (giả lập)')).toBeInTheDocument()
    expect(within(all).getAllByText('Điều dưỡng Demo').length).toBeGreaterThan(0)
    expect(within(all).getByText('Đã hết hạn')).toBeInTheDocument()
    expect(within(all).getAllByRole('button', { name: 'Hủy ủy quyền' })).toHaveLength(1)
    expect(screen.queryByRole('button', { name: /Ủy quyền mới/ })).not.toBeInTheDocument()
    expect(screen.queryByText('Tôi đã ủy quyền')).not.toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/v1/nursing-reports/delegations').map((c) => c.search)).toEqual(['?all=true'])
  })

  it('tài khoản không có quyền báo cáo điều dưỡng không mở được trang', async () => {
    mockApi({ ...baseRoutes(session()) })
    renderApp('/dieu-duong/uy-quyen')

    expect(await screen.findByText(/không có quyền/i)).toBeInTheDocument()
  })
})
