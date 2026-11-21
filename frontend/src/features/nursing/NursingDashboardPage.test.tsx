import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { describe, expect, it } from 'vitest'
import type { NursingDashboard } from '../../api/nursingReports'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { mockApi, problem } from '../../test/mockApi'
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

describe('Dashboard điều dưỡng', () => {
  it('ô chỉ số ngày cuối kỳ, công suất giường theo khoa, "Không ổn" theo nội dung và bảng theo khoa', { timeout: 20_000 }, async () => {
    const { calls } = mockApi({ ...baseRoutes(officeSession()), 'GET /api/v1/nursing-reports/dashboard': dashboard })

    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByRole('heading', { name: 'Dashboard điều dưỡng' })).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/nursing-reports/dashboard')?.search).toBe(`?to=${TODAY}&days=30`)
    expect(await screen.findByRole('heading', { name: `Ngày ${dayjs().format('DD/MM/YYYY')}` })).toBeInTheDocument()

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
      ...baseRoutes(officeSession()),
      'GET /api/v1/nursing-reports/dashboard': (call) =>
        call.search.includes('days=30')
          ? dashboard
          : new Promise((resolve) => {
              release = () => resolve({ ...dashboard, from: daysAgo(6) })
            }),
    })

    renderApp('/dieu-duong/dashboard')
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
    const picker = screen.getByLabelText('Đến ngày')
    await user.click(picker)
    await user.clear(picker)
    await user.type(picker, `${earlier.format('DD/MM/YYYY')}{Enter}`)
    await waitFor(() => expect(calls.some((c) => c.search === `?to=${earlier.format('YYYY-MM-DD')}&days=7`)).toBe(true))
  })

  it('chưa khoa nào gửi trong kỳ: ô chỉ số để trống, không vẽ biểu đồ', async () => {
    mockApi({
      ...baseRoutes(officeSession()),
      'GET /api/v1/nursing-reports/dashboard': {
        ...dashboard,
        days: dashboard.days.map((d) => ({ ...d, submitted: 0, values: values(), patientChange: null, beds: null, bedPatients: null, issueDepartments: 0, incidents: 0 })),
        departments: dashboard.departments.map((d) => ({ ...d, submitted: false, submittedDays: 0, values: {}, issues: [] })),
        checks: dashboard.checks.map((c) => ({ ...c, issueCount: 0, lastDayDepartments: [] })),
      },
    })

    renderApp('/dieu-duong/dashboard')

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
    mockApi({ ...baseRoutes(officeSession()), 'GET /api/v1/nursing-reports/dashboard': full })
    const { unmount } = renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText('Đủ các khoa')).toBeInTheDocument()
    expect(within(tile('Công suất giường')).getByText('110%')).toBeInTheDocument()
    expect(tile('Công suất giường')).toHaveTextContent('Vượt số giường')
    unmount()

    mockApi({ ...baseRoutes(officeSession()), 'GET /api/v1/nursing-reports/dashboard': () => problem(400, 'Chọn từ 1 đến 92 ngày.') })
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
