import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { describe, expect, it, vi } from 'vitest'
import type { NursingDashboard, NursingReport, NursingReportFormDefinition, NursingSummary } from '../../api/nursingReports'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật). Ngày tính từ hôm nay để không phụ thuộc ngày chạy test.
const TODAY = dayjs().format('YYYY-MM-DD')
const daysAgo = (n: number) => dayjs().subtract(n, 'day').format('YYYY-MM-DD')
const KEYS = [
  'previousDayPatients',
  'currentPatients',
  'admissions',
  'admissionsFromClinic',
  'admissionsByTransfer',
  'dischargesYesterday',
  'transfersOutYesterday',
  'careLevel1',
  'ventilated',
  'monitored',
  'oxygen',
  'noBed',
  'adr',
]
const values = (overrides: Record<string, number> = {}) =>
  Object.fromEntries(KEYS.map((key) => [key, overrides[key] ?? null])) as Record<string, number | null>

const dashboard: NursingDashboard = {
  from: daysAgo(2),
  to: TODAY,
  reportingCount: 3,
  days: [
    {
      date: daysAgo(2),
      submitted: 2,
      values: values({ currentPatients: 54, admissions: 4, dischargesYesterday: 2, transfersOutYesterday: 0, careLevel1: 4 }),
      patientChange: 2,
      beds: 60,
      bedPatients: 54,
      issueDepartments: 1,
      incidents: 1,
    },
    { date: daysAgo(1), submitted: 0, values: values(), patientChange: null, beds: null, bedPatients: null, issueDepartments: 0, incidents: 0 },
    {
      date: TODAY,
      submitted: 2,
      values: values({
        previousDayPatients: 54,
        currentPatients: 52,
        admissions: 3,
        admissionsFromClinic: 3,
        admissionsByTransfer: 0,
        dischargesYesterday: 4,
        transfersOutYesterday: 1,
        careLevel1: 2,
        ventilated: 2,
        monitored: 3,
        oxygen: 0,
        noBed: 1,
        adr: 1,
      }),
      patientChange: -2,
      beds: 60,
      bedPatients: 52,
      issueDepartments: 2,
      incidents: 1,
    },
  ],
  departments: [
    {
      departmentId: 'd-noi',
      code: 'NOI A',
      name: 'Khoa Nội A (giả lập)',
      isReporting: true,
      bedCount: 40,
      submittedDays: 2,
      submitted: true,
      values: values({ currentPatients: 30, admissions: 1, dischargesYesterday: 3, transfersOutYesterday: 0, careLevel1: 2, ventilated: 0 }),
      issues: ['drugAdministration'],
      hasIncident: false,
    },
    {
      departmentId: 'd-ngoai',
      code: 'NGOAI',
      name: 'Khoa Ngoại (giả lập)',
      isReporting: true,
      bedCount: 20,
      submittedDays: 1,
      submitted: true,
      values: values({ currentPatients: 22, admissions: 2, dischargesYesterday: 1, transfersOutYesterday: 1, careLevel1: 0, ventilated: 2 }),
      issues: ['professional', 'speakers'],
      hasIncident: true,
    },
    {
      departmentId: 'd-hstc',
      code: 'HSTC',
      name: 'Khoa Hồi sức (giả lập)',
      isReporting: true,
      bedCount: null,
      submittedDays: 0,
      submitted: false,
      values: {},
      issues: [],
      hasIncident: false,
    },
    {
      departmentId: 'd-khac',
      code: 'KHAC',
      name: 'Khoa Khác (giả lập)',
      isReporting: false,
      bedCount: null,
      submittedDays: 1,
      submitted: false,
      values: {},
      issues: [],
      hasIncident: false,
    },
  ],
  checks: [
    { key: 'professional', label: 'Vấn đề chuyên môn', issueCount: 2, lastDayDepartments: ['NGOAI'] },
    { key: 'security', label: 'An ninh trật tự', issueCount: 0, lastDayDepartments: [] },
    { key: 'drugAdministration', label: 'ĐD thực hiện thuốc', issueCount: 1, lastDayDepartments: ['NOI A'] },
    { key: 'speakers', label: 'Tình hình loa', issueCount: 1, lastDayDepartments: ['NGOAI'] },
  ],
}

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

const tile = (name: string) => screen.getByRole('group', { name })

/** Bảng tổng hợp tối thiểu (giả lập) của ngày `date`: thẻ Bảng tổng hợp mở sẵn của Dashboard tải bảng này (quyết định 58). */
const summaryOf = (date: string): NursingSummary => ({
  reportDate: date,
  columns: [],
  rows: [],
  totals: {},
  outpatient: [],
  procedures: [],
  imaging: [],
  situation: [],
  incidents: [],
  reporters: { outpatient: null, procedures: null, imaging: null, situation: null, incidents: null },
  statistics: { hisAvailable: false, hasValues: false, source: null, updatedAtUtc: null, updatedBy: null, entries: [], usesDepartmentEntries: false },
  submittedCount: 2,
  reportingCount: 3,
})

/** Phòng Điều dưỡng mở Dashboard: trang tải cả bảng tổng hợp của ngày đã chọn. */
const officeRoutes = () => ({
  ...baseRoutes(officeSession()),
  'GET /api/v1/nursing-reports/summary': (call: ApiCall) => summaryOf(new URLSearchParams(call.search).get('date') ?? TODAY),
})

/** Biểu đồ, công suất giường, "Không ổn" và bảng theo khoa nằm ở thẻ Biểu đồ (quyết định 58). */
const openTrends = async (user: ReturnType<typeof userEvent.setup>) => user.click(await screen.findByRole('tab', { name: 'Biểu đồ' }))

/** Mẫu báo cáo khoa thu gọn (giả lập) để xem báo cáo của khoa trong ngăn kéo. */
const definition: NursingReportFormDefinition = {
  title: 'BÁO CÁO CÔNG TÁC ĐIỀU DƯỠNG',
  category: 'Khoa Lâm sàng',
  sections: [
    {
      title: 'VI. SỐ LƯỢNG NB',
      valueKey: null,
      items: [{ key: 'currentPatients', label: 'Số NB hiện tại', carryOver: false }],
    },
  ],
  checks: [{ key: 'professional', label: 'Vấn đề chuyên môn', inSummary: true }],
  issueLabel: 'Có sự cố/ Không ổn',
  okLabel: 'Không có sự cố/ Ổn',
  notesLabel: 'Báo cáo các vấn đề bất thường (nếu có):',
  maxValue: 99999,
  notesMaxLength: 20000,
}

const report = (overrides: Partial<NursingReport> = {}): NursingReport => ({
  id: null,
  departmentId: 'd-ngoai',
  departmentName: 'Khoa Ngoại (giả lập)',
  reportDate: TODAY,
  status: 'New',
  values: {},
  checks: {},
  notes: null,
  canEdit: false,
  canReopen: false,
  updatedBy: null,
  updatedAtUtc: null,
  submittedBy: null,
  submittedAtUtc: null,
  reopenedBy: null,
  reopenedAtUtc: null,
  reopenReason: null,
  carriedOverKeys: [],
  isReportingDepartment: true,
  hisEntry: null,
  reportVisible: true,
  ...overrides,
})

const lastCall = (calls: ApiCall[], method: string, path: string) =>
  calls.filter((c) => c.method === method && c.path === path).at(-1)

/**
 * Ngăn kéo (hoặc hộp thoại) theo chữ tiêu đề. AntD trong môi trường test đặt cùng id cho chú thích của mã khoa và tiêu đề ngăn
 * kéo, nên mở ngăn kéo thứ hai thì tên đọc được của nó lấy nhầm chú thích cũ: tìm theo chữ thay vì theo tên.
 */
const dialogTitled = async (title: string) => (await screen.findByText(title)).closest<HTMLElement>('[role="dialog"]')!

describe('Dashboard điều dưỡng', () => {
  it('ô chỉ số ngày cuối kỳ, công suất giường theo khoa, "Không ổn" theo nội dung và bảng theo khoa', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({ ...officeRoutes(), 'GET /api/v1/nursing-reports/dashboard': dashboard })

    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByRole('heading', { name: 'Dashboard điều dưỡng' })).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/nursing-reports/dashboard')?.search).toBe(`?to=${TODAY}&days=30`)
    expect(await screen.findByRole('heading', { name: `Ngày ${dayjs().format('DD/MM/YYYY')}` })).toBeInTheDocument()
    // Quyết định 58: ô chỉ số ở trên hai thẻ; thẻ Bảng tổng hợp mở sẵn, biểu đồ ở thẻ Biểu đồ (chưa vẽ khi chưa mở thẻ).
    expect(screen.getByRole('tab', { name: 'Bảng tổng hợp' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByText(/^Công suất giường theo khoa/)).not.toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/nursing-reports/summary')?.search).toBe(`?date=${TODAY}`)

    expect(within(tile('Khoa đã gửi báo cáo')).getByText('2/3')).toBeInTheDocument()
    expect(within(tile('Khoa đã gửi báo cáo')).getByText('Chưa gửi: HSTC')).toBeInTheDocument()
    expect(within(tile('Số NB hiện tại')).getByText('52')).toBeInTheDocument()
    expect(tile('Số NB hiện tại')).toHaveTextContent('Giảm 2 so với ngày hôm trước')
    // 52/60 = 86,7% → 87%.
    expect(within(tile('Công suất giường')).getByText('87%')).toBeInTheDocument()
    expect(tile('Công suất giường')).toHaveTextContent('52 NB / 60 giường của khoa đã gửi · 1 NB chưa có giường')
    expect(tile('Công suất giường')).not.toHaveTextContent('Vượt số giường')
    expect(tile('NB chăm sóc cấp 1')).toHaveTextContent('2Thở máy 2 · Monitor 3 · Thở oxy 0')
    expect(tile('Nhập khoa (hôm trước)')).toHaveTextContent('3Từ PK 3 · Do CK 0')
    expect(tile('Xuất viện (hôm trước)')).toHaveTextContent('4')
    expect(tile('Chuyển khoa (hôm trước)')).toHaveTextContent('1')
    expect(tile('Khoa báo "Không ổn"')).toHaveTextContent('2ADR 1 · 1 báo cáo có nội dung bất thường')

    // Công suất theo khoa: cao nhất trước, vượt số giường có biểu tượng cảnh báo; khoa chưa cài số giường không có.
    await openTrends(user)
    expect(screen.getByText(`Từ ${dayjs(daysAgo(2)).format('DD/MM/YYYY')} đến ${dayjs().format('DD/MM/YYYY')}.`)).toBeInTheDocument()
    const occupancy = screen.getByText(/^Công suất giường theo khoa/).closest('.ant-card') as HTMLElement
    const occupancyRows = within(occupancy).getAllByRole('listitem')
    expect(occupancyRows.map((row) => row.textContent)).toEqual(['NGOAI22/20 · 110%', 'NOI A30/40 · 75%'])
    expect(within(occupancyRows[0]).getByLabelText('Vượt số giường')).toBeInTheDocument()

    // "Không ổn": nhiều lượt nhất trước, ghi khoa báo ngày cuối kỳ.
    const issues = screen.getByText(/^"Không ổn" theo nội dung – 3 ngày/).closest('.ant-card') as HTMLElement
    const issueRows = within(issues).getAllByRole('listitem')
    expect(issueRows.map((row) => row.textContent)).toEqual([
      `Vấn đề chuyên môn2Ngày ${dayjs().format('DD/MM')}: NGOAI`,
      `ĐD thực hiện thuốc1Ngày ${dayjs().format('DD/MM')}: NOI A`,
      `Tình hình loa1Ngày ${dayjs().format('DD/MM')}: NGOAI`,
      'An ninh trật tự0',
    ])

    // Bảng theo khoa (thứ tự bảng tổng hợp).
    const table = screen.getByText(/^Theo khoa – ngày/).closest('.ant-card') as HTMLElement
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(4)
    expect(within(rows[0]).getAllByRole('cell').map((c) => c.textContent)).toEqual([
      'NOI A',
      '40',
      '30',
      '75%',
      '1',
      '3',
      '0',
      '2',
      '0',
      'ĐD thực hiện thuốc',
      '2/3',
    ])
    expect(within(rows[1]).getAllByRole('cell')[3]).toHaveTextContent('110%')
    expect(within(rows[1]).getAllByRole('cell')[9]).toHaveTextContent('Vấn đề chuyên môn, Tình hình loa')
    expect(within(rows[2]).getAllByRole('cell').map((c) => c.textContent)).toEqual(['HSTCChưa gửi', '—', '', '', '', '', '', '', '', '', '0/3'])
    expect(within(rows[3]).getAllByRole('cell')[0]).toHaveTextContent('KHACChưa gửiKhông trong danh sách')
  })

  it('xem số từng ngày dạng bảng; đổi khoảng thời gian, đổi ngày thì tải lại, giữ số cũ trong lúc tải', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    let release: () => void = () => {}
    const { calls } = mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/dashboard': (call) =>
        call.search.includes('days=30')
          ? dashboard
          : new Promise((resolve) => {
              release = () => resolve({ ...dashboard, from: daysAgo(6) })
            }),
    })

    renderApp('/dieu-duong/dashboard')
    await openTrends(user)
    await user.click(await screen.findByRole('button', { name: 'Xem bảng: Số NB hiện tại' }))

    const card = screen.getByRole('button', { name: 'Xem biểu đồ: Số NB hiện tại' }).closest('.ant-card') as HTMLElement
    const rows = within(card).getAllByRole('row').slice(1)
    // Ngày mới nhất trước; ngày không khoa nào gửi để trống, không ghi 0.
    expect(rows.map((row) => within(row).getAllByRole('cell').map((c) => c.textContent))).toEqual([
      [dayjs().format('DD/MM/YYYY'), '52', '2/3 khoa đã gửi · Công suất giường 87%'],
      [dayjs(daysAgo(1)).format('DD/MM/YYYY'), '—', '0/3 khoa đã gửi'],
      [dayjs(daysAgo(2)).format('DD/MM/YYYY'), '54', '2/3 khoa đã gửi · Công suất giường 90%'],
    ])
    await user.click(screen.getByRole('button', { name: 'Xem biểu đồ: Số NB hiện tại' }))
    expect(screen.getByRole('group', { name: /^Số người bệnh hiện tại theo ngày\./ })).toBeInTheDocument()

    await user.click(screen.getByText('7 ngày'))
    await waitFor(() => expect(calls.some((c) => c.search === `?to=${TODAY}&days=7`)).toBe(true))
    // Đang tải: số cũ vẫn hiện, mờ đi.
    const body = screen.getByRole('heading', { name: `Ngày ${dayjs().format('DD/MM/YYYY')}` }).parentElement as HTMLElement
    expect(body).toHaveAttribute('aria-busy', 'true')
    expect(within(tile('Số NB hiện tại')).getByText('52')).toBeInTheDocument()
    release()
    await waitFor(() => expect(body).toHaveAttribute('aria-busy', 'false'))

    const earlier = dayjs().subtract(5, 'day')
    const picker = screen.getByLabelText('Ngày báo cáo')
    await user.click(picker)
    await user.clear(picker)
    await user.type(picker, `${earlier.format('DD/MM/YYYY')}{Enter}`)
    await waitFor(() => expect(calls.some((c) => c.search === `?to=${earlier.format('YYYY-MM-DD')}&days=7`)).toBe(true))
  })

  it(
    'bấm mã khoa mở báo cáo của khoa ngày cuối kỳ (như bảng tổng hợp): in PDF, trả lại khoa thì dashboard tải lại',
    { timeout: 30_000 },
    async () => {
      const user = userEvent.setup()
      URL.createObjectURL = vi.fn(() => 'blob:test/bao-cao')
      URL.revokeObjectURL = vi.fn()
      const earlier = dayjs().subtract(3, 'day')
      const end = earlier.format('YYYY-MM-DD')
      let reopened = false
      const submitted = (id: string, departmentId: string, departmentName: string, currentPatients: number, canReopen: boolean) =>
        report({
          id,
          departmentId,
          departmentName,
          reportDate: end,
          status: 'Submitted',
          values: { currentPatients },
          checks: { professional: 'ok' },
          canReopen,
          updatedAtUtc: '2026-11-20T00:40:00Z',
          submittedBy: 'Điều dưỡng Demo',
          submittedAtUtc: '2026-11-20T00:45:00Z',
        })
      const { calls } = mockApi({
        ...officeRoutes(),
        // Ngày cuối kỳ theo ngày đã chọn; sau khi trả lại, báo cáo Khoa Ngoại về dạng nháp nên không còn được tính.
        'GET /api/v1/nursing-reports/dashboard': (call: ApiCall) => ({
          ...dashboard,
          to: new URLSearchParams(call.search).get('to'),
          departments: dashboard.departments.map((d) => (reopened && d.departmentId === 'd-ngoai' ? { ...d, submitted: false, values: {} } : d)),
        }),
        'GET /api/v1/nursing-reports/form': definition,
        'GET /api/v1/nursing-reports/daily': (call: ApiCall) => {
          const departmentId = new URLSearchParams(call.search).get('departmentId')
          return departmentId === 'd-ngoai'
            ? submitted('r-ngoai', 'd-ngoai', 'Khoa Ngoại (giả lập)', 22, true)
            : departmentId === 'd-noi'
              ? // Máy chủ không cho trả lại (ví dụ người chỉ có quyền xem): không có nút "Trả lại khoa".
                submitted('r-noi', 'd-noi', 'Khoa Nội A (giả lập)', 30, false)
              : report({ departmentId: departmentId ?? '', departmentName: 'Khoa Hồi sức (giả lập)', reportDate: end })
        },
        'GET /api/v1/nursing-reports/r-ngoai/pdf': () => new Response('%PDF-1.7', { headers: { 'Content-Type': 'application/pdf' } }),
        'POST /api/v1/nursing-reports/r-ngoai/reopen': () => {
          reopened = true
          return report({
            id: 'r-ngoai',
            reportDate: end,
            status: 'Draft',
            values: { currentPatients: 22 },
            reopenReason: 'Sai số NB hiện tại',
            reopenedBy: 'Phòng Điều dưỡng Demo',
            reopenedAtUtc: '2026-11-20T01:00:00Z',
          })
        },
      })

      renderApp('/dieu-duong/dashboard')
      const picker = await screen.findByLabelText('Ngày báo cáo')
      await user.click(picker)
      await user.clear(picker)
      await user.type(picker, `${earlier.format('DD/MM/YYYY')}{Enter}`)
      await openTrends(user)
      const table = (await screen.findByText(`Theo khoa – ngày ${earlier.format('DD/MM/YYYY')}`)).closest('.ant-card') as HTMLElement
      const dashboardCalls = () => calls.filter((c) => c.path === '/api/v1/nursing-reports/dashboard' && c.search === `?to=${end}&days=30`).length
      expect(dashboardCalls()).toBe(1)

      // Khoa chưa gửi cũng bấm được: báo khoa chưa lập báo cáo, không có nút in hay trả lại.
      await user.click(within(table).getByRole('button', { name: 'HSTC – Khoa Hồi sức (giả lập)' }))
      let drawer = await dialogTitled(`Khoa Hồi sức (giả lập) – ngày ${earlier.format('DD/MM/YYYY')}`)
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')?.search).toBe(`?date=${end}&departmentId=d-hstc`)
      expect(await within(drawer).findByText('Khoa chưa lập báo cáo ngày này.')).toBeInTheDocument()
      expect(within(drawer).queryByRole('button', { name: /In PDF/ })).not.toBeInTheDocument()
      await user.click(within(drawer).getByRole('button', { name: 'Đóng' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

      // Máy chủ không cho trả lại: chỉ có nút In PDF.
      await user.click(within(table).getByRole('button', { name: 'NOI A – Khoa Nội A (giả lập)' }))
      drawer = await dialogTitled(`Khoa Nội A (giả lập) – ngày ${earlier.format('DD/MM/YYYY')}`)
      expect(await within(drawer).findByRole('spinbutton', { name: 'Số NB hiện tại' })).toHaveValue('30')
      expect(within(drawer).getByRole('button', { name: /In PDF/ })).toBeInTheDocument()
      expect(within(drawer).queryByRole('button', { name: /Trả lại khoa/ })).not.toBeInTheDocument()
      await user.click(within(drawer).getByRole('button', { name: 'Đóng' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

      await user.click(within(table).getByRole('button', { name: 'NGOAI – Khoa Ngoại (giả lập)' }))
      drawer = await dialogTitled(`Khoa Ngoại (giả lập) – ngày ${earlier.format('DD/MM/YYYY')}`)
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')?.search).toBe(`?date=${end}&departmentId=d-ngoai`)
      expect(await within(drawer).findByRole('spinbutton', { name: 'Số NB hiện tại' })).toBeDisabled()
      expect(within(drawer).getByRole('spinbutton', { name: 'Số NB hiện tại' })).toHaveValue('22')

      await user.click(within(drawer).getByRole('button', { name: /In PDF/ }))
      expect(await screen.findByTitle('Báo cáo điều dưỡng Khoa Ngoại (giả lập)')).toHaveAttribute('src', 'blob:test/bao-cao')
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/r-ngoai/pdf')).toBeDefined()
      const pdf = screen.getByTitle('Báo cáo điều dưỡng Khoa Ngoại (giả lập)').closest<HTMLElement>('[role="dialog"]')!
      await user.click(within(pdf).getByRole('button', { name: 'Đóng' }))

      await user.click(within(drawer).getByRole('button', { name: /Trả lại khoa/ }))
      const modal = await dialogTitled('Trả lại báo cáo cho khoa sửa')
      expect(within(modal).getByRole('button', { name: 'Trả lại khoa' })).toBeDisabled()
      await user.type(within(modal).getByRole('textbox', { name: 'Lý do trả lại' }), 'Sai số NB hiện tại')
      await user.click(within(modal).getByRole('button', { name: 'Trả lại khoa' }))

      await waitFor(() =>
        expect(lastCall(calls, 'POST', '/api/v1/nursing-reports/r-ngoai/reopen')?.body).toEqual({ reason: 'Sai số NB hiện tại' }),
      )
      expect(await screen.findByText('Đã trả lại báo cáo cho khoa. Khoa sửa và gửi lại.')).toBeInTheDocument()
      expect(await within(drawer).findByText(/^Đã trả lại khoa lúc .* bởi Phòng Điều dưỡng Demo$/)).toBeInTheDocument()
      expect(within(drawer).getByText('Lý do: Sai số NB hiện tại')).toBeInTheDocument()
      expect(within(drawer).queryByRole('button', { name: /Trả lại khoa/ })).not.toBeInTheDocument()
      // Dashboard tải lại: Khoa Ngoại thành "Chưa gửi".
      await waitFor(() => expect(dashboardCalls()).toBe(2))
      const ngoai = within(table).getByRole('button', { name: 'NGOAI – Khoa Ngoại (giả lập)' }).closest('tr')!
      await waitFor(() => expect(within(ngoai).getAllByRole('cell')[0]).toHaveTextContent('NGOAIChưa gửi'))
    },
  )

  it('chưa khoa nào gửi trong kỳ: ô chỉ số để trống, không vẽ biểu đồ', async () => {
    const user = userEvent.setup()
    mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/dashboard': {
        ...dashboard,
        days: dashboard.days.map((d) => ({ ...d, submitted: 0, values: values(), patientChange: null, beds: null, bedPatients: null, issueDepartments: 0, incidents: 0 })),
        departments: dashboard.departments.map((d) => ({ ...d, submitted: false, submittedDays: 0, values: {}, issues: [] })),
        checks: dashboard.checks.map((c) => ({ ...c, issueCount: 0, lastDayDepartments: [] })),
      },
    })

    renderApp('/dieu-duong/dashboard')

    await openTrends(user)
    expect(await screen.findByText('Chưa có khoa nào gửi báo cáo trong 3 ngày này.')).toBeInTheDocument()
    expect(within(tile('Khoa đã gửi báo cáo')).getByText('0/3')).toBeInTheDocument()
    expect(tile('Khoa đã gửi báo cáo')).toHaveTextContent('Chưa gửi: NOI A, NGOAI, HSTC')
    expect(tile('Số NB hiện tại')).toHaveTextContent('—Chưa khoa nào gửi báo cáo')
    expect(tile('Công suất giường')).toHaveTextContent('—Chưa khoa nào gửi báo cáo')
    expect(screen.queryByRole('button', { name: /^Xem bảng/ })).not.toBeInTheDocument()
  })

  it('đủ các khoa và vượt số giường thì ghi rõ bằng chữ kèm biểu tượng; lỗi máy chủ thì báo lỗi', async () => {
    const full: NursingDashboard = {
      ...dashboard,
      reportingCount: 2,
      days: dashboard.days.map((d) => (d.date === TODAY ? { ...d, beds: 40, bedPatients: 44, values: values({ currentPatients: 44 }) } : d)),
      departments: dashboard.departments.filter((d) => d.submitted),
    }
    mockApi({ ...officeRoutes(), 'GET /api/v1/nursing-reports/dashboard': full })
    const { unmount } = renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText('Đủ các khoa')).toBeInTheDocument()
    expect(within(tile('Công suất giường')).getByText('110%')).toBeInTheDocument()
    expect(tile('Công suất giường')).toHaveTextContent('Vượt số giường')
    unmount()

    mockApi({ ...officeRoutes(), 'GET /api/v1/nursing-reports/dashboard': () => problem(400, 'Chọn từ 1 đến 92 ngày.') })
    renderApp('/dieu-duong/dashboard')
    expect(await screen.findByText('Chọn từ 1 đến 92 ngày.')).toBeInTheDocument()
  })

  it('chỉ người xem được bảng tổng hợp mới có dashboard', async () => {
    mockApi({
      ...baseRoutes(
        session({ roles: [{ id: 'r-nurse', code: 'NURSE', name: 'Điều dưỡng' }], permissions: [...doctorPermissions, Permissions.NursingReportsSubmit] }),
      ),
    })

    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Dashboard' })).not.toBeInTheDocument()
  })
})
