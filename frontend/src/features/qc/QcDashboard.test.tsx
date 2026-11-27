import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { QcDashboard, QcDashboardFigures, QcDashboardMeter } from '../../api/qc'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, session } from '../../test/fixtures'
import { mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật): dashboard nội kiểm tháng này (quyết định 59).
const month = dayjs().format('YYYY-MM')
const previous = dayjs().subtract(1, 'month').format('YYYY-MM')
const day = (n: number, of = month) => `${of}-${String(n).padStart(2, '0')}`

const figures = (overrides: Partial<QcDashboardFigures> = {}): QcDashboardFigures => ({
  requiredDays: 40,
  recordedDays: 37,
  missingDays: 3,
  lateDays: 2,
  failedDays: 4,
  repeatPassedDays: 1,
  repeatFailedDays: 1,
  notRepeatedDays: 2,
  reviewedDays: 20,
  daysToReview: 9,
  ...overrides,
})

const meterA: QcDashboardMeter = {
  meterId: 'm-1',
  meterLabel: 'Máy đo A (giả lập) GL-0001',
  meterActive: true,
  bookId: 'b-1',
  status: 'Open',
  returned: false,
  canOpen: true,
  figures: figures({ requiredDays: 38, recordedDays: 37, missingDays: 1 }),
  missingDates: [day(5)],
  latestFailure: { date: day(6), meterLabel: 'Máy đo A (giả lập) GL-0001', reason: null },
}

const meterB: QcDashboardMeter = {
  meterId: 'm-2',
  meterLabel: 'Máy đo B (giả lập) GL-0002',
  meterActive: true,
  bookId: null,
  status: null,
  returned: false,
  canOpen: false,
  figures: figures({ requiredDays: 2, recordedDays: 0, missingDays: 2, lateDays: 0, failedDays: 0, repeatPassedDays: 0, repeatFailedDays: 0, notRepeatedDays: 0, reviewedDays: 0, daysToReview: 0 }),
  missingDates: [day(9), day(10)],
  latestFailure: null,
}

const dashboard = (overrides: Partial<QcDashboard> = {}): QcDashboard => ({
  month,
  today: dayjs().format('YYYY-MM-DD'),
  totals: { departments: 1, meters: 2, figures: figures(), books: { open: 1, returned: 0, submitted: 0, confirmed: 0, notOpened: 1 } },
  departments: [
    {
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      figures: figures(),
      books: { open: 1, returned: 0, submitted: 0, confirmed: 0, notOpened: 1 },
      latestFailure: { date: day(6), meterLabel: 'Máy đo A (giả lập) GL-0001', reason: null },
      meters: [meterA, meterB],
    },
  ],
  trend: [5, 4, 3, 2, 1, 0].map((back, index) => ({
    month: dayjs().subtract(back, 'month').format('YYYY-MM'),
    requiredDays: back === 0 ? 40 : 60,
    recordedDays: back === 0 ? 37 : 54 + index,
    failedDays: back === 0 ? 4 : 3,
    lateDays: 1,
  })),
  alerts: {
    currentMonth: month,
    lots: [
      {
        lotId: 's-9',
        kind: 'TestStrip',
        productName: 'Que thử C (giả lập)',
        lotNumber: 'QT-C1',
        level: null,
        expiryDate: dayjs().subtract(3, 'day').format('YYYY-MM-DD'),
        daysLeft: -3,
        usedBy: [],
      },
      {
        lotId: 'c-9',
        kind: 'ControlSolution',
        productName: 'Dung dịch chứng B (giả lập)',
        lotNumber: 'DC-B1',
        level: 1,
        expiryDate: dayjs().add(9, 'day').format('YYYY-MM-DD'),
        daysLeft: 9,
        usedBy: ['Khoa Nội tổng hợp'],
      },
    ],
    controlLevels: [1],
    stripLots: 2,
    metersWithoutBook: [{ meterId: 'm-2', meterLabel: 'Máy đo B (giả lập) GL-0002', departmentId: 'd-noi', departmentName: 'Khoa Nội tổng hợp' }],
    pendingBooks: [
      {
        bookId: 'b-0',
        month: previous,
        departmentId: 'd-noi',
        departmentName: 'Khoa Nội tổng hợp',
        meterLabel: 'Máy đo A (giả lập) GL-0001',
        status: 'Open',
        returned: true,
        canOpen: true,
      },
    ],
  },
  ...overrides,
})

const labSession = () =>
  session({
    id: 'u-hsh',
    userName: 'hsh.demo',
    fullName: 'Khoa HSH Demo',
    departmentId: 'd-hsh',
    departmentName: 'Khoa Huyết học – Sinh hóa',
    roles: [{ id: 'r-lab', code: 'LAB_QC', name: 'Khoa HSH (nội kiểm)' }],
    permissions: [Permissions.QcReview, Permissions.QcManage],
  })

/** Lãnh đạo, Phòng Điều dưỡng, QLCL: chỉ có quyền xem dashboard nội kiểm. */
const monitorSession = () =>
  session({
    id: 'u-qlcl',
    userName: 'kiemtra.demo',
    fullName: 'Kiểm tra Demo',
    departmentId: 'd-khth',
    departmentName: 'Phòng Kế hoạch tổng hợp',
    roles: [{ id: 'r-auditor', code: 'AUDITOR', name: 'Kiểm tra' }],
    permissions: [Permissions.QcMonitor],
  })

describe('Dashboard nội kiểm đường huyết', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('Khoa HSH: ô chỉ số, cảnh báo cần xử lý, xu hướng 6 tháng, bảng theo khoa và từng máy; đổi tháng thì tải tháng đó', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(labSession()),
      'GET /api/v1/qc/dashboard': (call) =>
        call.search === `?month=${previous}`
          ? dashboard({ month: previous, totals: { departments: 1, meters: 1, figures: figures({ requiredDays: 30, recordedDays: 30, missingDays: 0 }), books: { open: 0, returned: 0, submitted: 1, confirmed: 0, notOpened: 0 } } })
          : dashboard(),
    })
    renderApp('/noi-kiem/dashboard')

    // Ô chỉ số của tháng này.
    const compliance = await screen.findByRole('group', { name: 'Tỷ lệ thực hiện' })
    expect(within(compliance).getByText('92,5%')).toBeInTheDocument()
    expect(within(compliance).getByText('37/40 ngày phải thực hiện có kết quả')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Ngày chưa ghi' })).getByText('3')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Không đạt' })).getByText(/10,8% số ngày đã ghi · chạy lại đạt 1, vẫn không đạt 1/)).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Không đạt, chưa chạy lại' })).getByText(/Cần nhắc khoa chạy lại QC/)).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Chờ Khoa HSH xem xét' })).getByText('9')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Sổ đã xác nhận' })).getByText('0/2')).toBeInTheDocument()
    expect(screen.getByText(`Tháng ${dayjs().format('MM/YYYY')}`)).toBeInTheDocument()

    // Cảnh báo tính đến hôm nay: lô hết hạn, sắp hết hạn (khoa đang dùng), thiếu mức, máy chưa mở sổ, sổ tháng trước bị trả lại.
    expect(screen.getByText(/^Lô que thử QT-C1 \(Que thử C \(giả lập\)\): đã hết hạn ngày .+ nhưng chưa ngưng sử dụng\.$/)).toBeInTheDocument()
    expect(
      screen.getByText(/^Lô dung dịch chứng DC-B1 \(Dung dịch chứng B \(giả lập\), Level 1\): hết hạn ngày .+, còn 9 ngày\. Đang dùng ở: Khoa Nội tổng hợp\.$/),
    ).toBeInTheDocument()
    expect(screen.getByText(/Danh mục chỉ còn dung dịch chứng Level 1 còn hạn: khoa không ghi được kết quả hai mức/)).toBeInTheDocument()
    expect(screen.getByText('Khoa Nội tổng hợp – Máy đo B (giả lập) GL-0002')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'mở sổ ở trang Sổ nội kiểm' })).toHaveAttribute('href', `/noi-kiem/so?thang=${month}`)
    const pending = screen.getByText(/bị trả lại, khoa chưa gửi lại/).closest('li') as HTMLElement
    expect(within(pending).getByRole('link', { name: 'Xem sổ' })).toHaveAttribute('href', '/noi-kiem/so/b-0')

    // Xu hướng 6 tháng: bảng số của biểu đồ (tháng này ở cuối).
    expect(screen.getByRole('group', { name: /^Tỷ lệ thực hiện nội kiểm theo tháng\. .*từng tháng\.$/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Xem bảng: Tỷ lệ thực hiện 6 tháng' }))
    expect(await screen.findByText('92,5')).toBeInTheDocument()
    expect(screen.getByText('37/40 ngày phải thực hiện')).toBeInTheDocument()
    const trendCard = screen.getByText('Tỷ lệ thực hiện 6 tháng').closest('.ant-card') as HTMLElement
    expect(within(trendCard).getAllByRole('columnheader', { name: 'Tháng' }).length).toBeGreaterThan(0)
    expect(within(trendCard).queryByRole('columnheader', { name: 'Ngày' })).not.toBeInTheDocument()
    expect(within(trendCard).getByText(dayjs().format('MM/YYYY'), { selector: 'td' })).toBeInTheDocument()

    // Bảng theo khoa: một khoa thì mở sẵn từng máy; máy đã mở sổ có "Xem sổ", máy chưa mở sổ ghi "Chưa mở sổ".
    const rowA = screen.getByText('Máy đo A (giả lập) GL-0001', { selector: 'span' }).closest('tr') as HTMLElement
    expect(within(rowA).getByText('ngày 5')).toBeInTheDocument()
    expect(within(rowA).getByText('06/' + dayjs().format('MM') + ': khoa chưa ghi nguyên nhân')).toBeInTheDocument()
    expect(within(rowA).getByRole('link', { name: 'Xem sổ' })).toHaveAttribute('href', '/noi-kiem/so/b-1')
    const rowB = screen.getByText('Máy đo B (giả lập) GL-0002', { selector: 'span' }).closest('tr') as HTMLElement
    expect(within(rowB).getByText('Chưa mở sổ')).toBeInTheDocument()
    expect(within(rowB).getByText('ngày 9, 10')).toBeInTheDocument()
    expect(within(rowB).queryByRole('link')).not.toBeInTheDocument()
    const department = screen.getByText('Khoa Nội tổng hợp', { selector: 'strong' }).closest('tr') as HTMLElement
    expect(within(department).getByText('0/2 đã xác nhận')).toBeInTheDocument()
    expect(within(department).getByText('1 chưa mở sổ')).toBeInTheDocument()

    // Đổi sang tháng trước: tải dashboard tháng đó, tháng đã qua tính cả tháng.
    const picker = screen.getByLabelText('Tháng')
    await user.click(picker)
    await user.clear(picker)
    await user.type(picker, `${dayjs(`${previous}-01`).format('MM/YYYY')}{Enter}`)
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/qc/dashboard' && c.search === `?month=${previous}`)).toBe(true))
    expect(await screen.findByText(`Tính mọi ngày trong tháng (${dayjs(`${previous}-01`).daysInMonth()} ngày).`, { exact: false })).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Tỷ lệ thực hiện' })).getByText('100%')).toBeInTheDocument()
  })

  it('quyền chỉ xem dashboard: xem được dashboard, không có liên kết mở sổ, không vào được Sổ nội kiểm', { timeout: 30_000 }, async () => {
    const readOnly = dashboard({
      departments: dashboard().departments.map((d) => ({ ...d, meters: d.meters.map((m) => ({ ...m, canOpen: false })) })),
      alerts: { ...dashboard().alerts, pendingBooks: dashboard().alerts.pendingBooks.map((b) => ({ ...b, canOpen: false })) },
    })
    mockApi({ ...baseRoutes(monitorSession()), 'GET /api/v1/qc/dashboard': readOnly })
    const { unmount } = renderApp('/noi-kiem/dashboard')

    expect(await screen.findByRole('group', { name: 'Tỷ lệ thực hiện' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Xem sổ' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'mở sổ ở trang Sổ nội kiểm' })).not.toBeInTheDocument()
    expect(screen.getByText(/1 máy chưa mở sổ tháng/)).toBeInTheDocument()
    expect(screen.getByText(/bị trả lại, khoa chưa gửi lại$/)).toBeInTheDocument()
    unmount()

    renderApp('/noi-kiem/so')
    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
  })

  it('không có cảnh báo, chưa có máy đo: báo rõ; lỗi máy chủ thì báo lỗi', { timeout: 30_000 }, async () => {
    const empty = dashboard({
      totals: { departments: 0, meters: 0, figures: figures({ requiredDays: 0, recordedDays: 0, missingDays: 0, lateDays: 0, failedDays: 0, repeatPassedDays: 0, repeatFailedDays: 0, notRepeatedDays: 0, reviewedDays: 0, daysToReview: 0 }), books: { open: 0, returned: 0, submitted: 0, confirmed: 0, notOpened: 0 } },
      departments: [],
      alerts: { currentMonth: month, lots: [], controlLevels: [1, 2], stripLots: 1, metersWithoutBook: [], pendingBooks: [] },
    })
    mockApi({ ...baseRoutes(labSession()), 'GET /api/v1/qc/dashboard': empty })
    const { unmount } = renderApp('/noi-kiem/dashboard')

    const compliance = await screen.findByRole('group', { name: 'Tỷ lệ thực hiện' })
    expect(within(compliance).getByText('—')).toBeInTheDocument()
    expect(within(compliance).getByText('Chưa có ngày phải thực hiện')).toBeInTheDocument()
    expect(screen.getByText(/Không có cảnh báo: lô đang dùng còn hạn trên 30 ngày/)).toBeInTheDocument()
    expect(screen.getByText(/Tháng này chưa có máy đo nào/)).toBeInTheDocument()
    unmount()

    vi.restoreAllMocks()
    mockApi({ ...baseRoutes(labSession()), 'GET /api/v1/qc/dashboard': () => problem(400, 'Chỉ xem được 24 tháng gần đây.') })
    renderApp('/noi-kiem/dashboard?thang=2020-01')
    expect(await screen.findByText('Chỉ xem được 24 tháng gần đây.')).toBeInTheDocument()
  })
})
