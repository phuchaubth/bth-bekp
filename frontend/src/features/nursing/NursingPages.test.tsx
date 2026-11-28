import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  NursingDashboard,
  NursingDelegation,
  NursingReport,
  NursingReportDepartment,
  NursingReportFormDefinition,
  NursingSummary,
} from '../../api/nursingReports'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { json, mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật). Mẫu thu gọn: đủ các kiểu dòng của bản giấy.
// Trang mở ở ngày hôm nay: báo cáo giả lập cũng là của hôm nay (không phụ thuộc ngày chạy test).
const TODAY = dayjs().format('YYYY-MM-DD')

const definition: NursingReportFormDefinition = {
  title: 'BÁO CÁO CÔNG TÁC ĐIỀU DƯỠNG',
  category: 'Khoa Lâm sàng',
  sections: [
    {
      title: 'I. NHÂN SỰ',
      valueKey: null,
      items: [
        { key: 'nurses', label: 'Điều dưỡng', carryOver: true },
        { key: 'orderlies', label: 'HL - YC', carryOver: true },
      ],
    },
    { title: 'IV. PHẢN ỨNG CÓ HẠI THUỐC (ADR)', valueKey: 'adr', items: [] },
    {
      title: 'VI. SỐ LƯỢNG NB',
      valueKey: null,
      items: [
        { key: 'previousDayPatients', label: 'Số NB ngày hôm trước', carryOver: false },
        {
          key: 'currentPatients',
          label: 'Số NB hiện tại',
          carryOver: false,
          formula: { base: 'previousDayPatients', add: ['admissionsFromClinic'], subtract: ['dischargesYesterday'] },
        },
        { key: 'admissionsFromClinic', label: 'Số ca nhập khoa từ PK ngày hôm trước', carryOver: false },
        { key: 'dischargesYesterday', label: 'Số ca xuất viện ngày hôm trước', carryOver: false },
      ],
    },
  ],
  checks: [
    { key: 'professional', label: 'Vấn đề chuyên môn', inSummary: true },
    { key: 'security', label: 'An ninh trật tự', inSummary: true },
  ],
  issueLabel: 'Có sự cố/ Không ổn',
  okLabel: 'Không có sự cố/ Ổn',
  notesLabel: 'Báo cáo các vấn đề bất thường (nếu có):',
  maxValue: 99999,
  notesMaxLength: 20000,
}

const report = (overrides: Partial<NursingReport> = {}): NursingReport => ({
  id: null,
  departmentId: 'd-noi',
  departmentName: 'Khoa Nội tổng hợp',
  reportDate: TODAY,
  status: 'New',
  values: { nurses: 18 },
  checks: {},
  notes: null,
  canEdit: true,
  canReopen: false,
  updatedBy: null,
  updatedAtUtc: null,
  submittedBy: null,
  submittedAtUtc: null,
  reopenedBy: null,
  reopenedAtUtc: null,
  reopenReason: null,
  carriedOverKeys: ['nurses'],
  isReportingDepartment: true,
  hisEntry: null,
  reportVisible: true,
  ...overrides,
})

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

const summary: NursingSummary = {
  reportDate: TODAY,
  columns: [
    { key: 'beds', header: 'Số giường thực kê (Không tính băng ca)' },
    { key: 'currentPatients', header: 'Số NB hiện tại' },
    { key: 'waitingAdmission', header: 'NB PK chờ nhập viện' },
  ],
  rows: [
    {
      departmentId: 'd-ngoai',
      departmentCode: 'NGOAIB',
      departmentName: 'Khoa Ngoại B (giả lập)',
      isReporting: true,
      reportId: null,
      status: 'New',
      submittedBy: null,
      submittedAtUtc: null,
      cells: { beds: 25, currentPatients: null, waitingAdmission: 1 },
    },
    {
      departmentId: 'd-noi',
      departmentCode: 'NOI',
      departmentName: 'Khoa Nội tổng hợp',
      isReporting: true,
      reportId: 'r-noi',
      status: 'Submitted',
      submittedBy: 'Điều dưỡng Demo',
      submittedAtUtc: '2026-11-16T00:45:00Z',
      cells: { beds: 40, currentPatients: 47, waitingAdmission: 2 },
    },
  ],
  totals: { beds: 65, currentPatients: 47, waitingAdmission: 3 },
  outpatient: [{ key: 'outpatientVisits', label: 'TSNB đến khám', value: 686 }],
  procedures: [{ key: 'boneMarrow', label: 'Tủy xương', value: 2 }],
  imaging: [{ key: 'xray', label: 'TS X-quang', value: 65 }],
  situation: [
    { key: 'professional', label: 'Vấn đề chuyên môn', text: 'Ổn', issueDepartments: [] },
    { key: 'drugSupply', label: 'Tình hình cung cấp thuốc', text: 'Không ổn: NOI', issueDepartments: ['NOI'] },
  ],
  incidents: [
    { departmentId: 'd-noi', departmentCode: 'NOI', departmentName: 'Khoa Nội tổng hợp', text: 'Người bệnh A (giả lập) sốt về đêm\nĐã báo bác sĩ trực' },
  ],
  reporters: {
    outpatient: 'Phòng Điều dưỡng Demo (lấy từ HIS)',
    procedures: 'Điều dưỡng Demo (NOI)',
    imaging: null,
    situation: 'Điều dưỡng Demo (NOI)',
    incidents: 'Điều dưỡng Demo (NOI)',
  },
  statistics: {
    hisAvailable: true,
    hasValues: true,
    source: 'His',
    updatedAtUtc: '2026-11-16T00:50:00Z',
    updatedBy: 'Phòng Điều dưỡng Demo',
    entries: [],
    usesDepartmentEntries: false,
  },
  submittedCount: 1,
  reportingCount: 2,
}

const departments: NursingReportDepartment[] = [
  { departmentId: 'd-kkb', code: 'KKB', name: 'Khoa Khám bệnh', shortName: null, isActive: true, isReporting: false, bedCount: null, hisIndicators: ['outpatientVisits'] },
  { departmentId: 'd-ngoai', code: 'NGOAIB', name: 'Khoa Ngoại B (giả lập)', shortName: null, isActive: true, isReporting: true, bedCount: 25, hisIndicators: [] },
  { departmentId: 'd-noi', code: 'NOI', name: 'Khoa Nội tổng hợp', shortName: null, isActive: true, isReporting: true, bedCount: 40, hisIndicators: [] },
]

/** Số liệu HIS khoa nhập khi HIS lỗi (quyết định 35). */
const hisEntry = (overrides: Partial<NonNullable<NursingReport['hisEntry']>> = {}): NonNullable<NursingReport['hisEntry']> => ({
  lines: [{ key: 'waitingAdmission', label: 'NB PK chờ nhập viện', value: null, hisValue: null }],
  canEdit: true,
  hisHasValues: false,
  hisUpdatedAtUtc: null,
  updatedBy: null,
  updatedAtUtc: null,
  ...overrides,
})

const lastCall = (calls: ApiCall[], method: string, path: string) =>
  calls.filter((c) => c.method === method && c.path === path).at(-1)

/** Dashboard tối thiểu (giả lập) của ngày `to`: một ngày, một khoa đã gửi. */
const dashboardOf = (to: string): NursingDashboard => ({
  from: to,
  to,
  reportingCount: 2,
  days: [{ date: to, submitted: 1, values: { currentPatients: 47 }, patientChange: null, beds: 40, bedPatients: 47, issueDepartments: 1, incidents: 1 }],
  departments: [],
  checks: [],
})

/**
 * Phòng Điều dưỡng mở Dashboard điều dưỡng: bảng tổng hợp nằm ở thẻ "Bảng tổng hợp" (mở sẵn) của Dashboard (quyết định 58), nên
 * trang tải cả dashboard của ngày đã chọn.
 */
const officeRoutes = (routes: Parameters<typeof mockApi>[0] = {}): Parameters<typeof mockApi>[0] => ({
  ...baseRoutes(officeSession()),
  'GET /api/v1/nursing-reports/dashboard': (call: ApiCall) => dashboardOf(new URLSearchParams(call.search).get('to') ?? TODAY),
  ...routes,
})

const day = (offset: number) => dayjs().add(offset, 'day')

/** Điều dưỡng Demo được ủy quyền làm báo cáo Khoa Nhi từ hôm qua đến ngày mai (quyết định 48). */
const nhiDelegation = (overrides: Partial<NursingDelegation> = {}): NursingDelegation => ({
  id: 'dl-nhi',
  departmentId: 'd-nhi',
  departmentCode: 'NHI',
  departmentName: 'Khoa Nhi (giả lập)',
  delegatorUserId: 'u-nhi',
  delegatorName: 'Điều dưỡng Nhi (giả lập)',
  delegateUserId: 'u-dieuduong',
  delegateName: 'Điều dưỡng Demo',
  startDate: day(-1).format('YYYY-MM-DD'),
  endDate: day(1).format('YYYY-MM-DD'),
  contents: ['report'],
  reason: 'Nghỉ phép',
  status: 'Active',
  createdAtUtc: '2026-11-19T01:00:00Z',
  revokedBy: null,
  revokedAtUtc: null,
  canRevoke: false,
  ...overrides,
})

const nhi = { departmentId: 'd-nhi', departmentName: 'Khoa Nhi (giả lập)' }

describe('Báo cáo điều dưỡng của khoa', () => {
  it('điều dưỡng lưu nháp với số điền sẵn, phải chọn đủ phần tick mới gửi được, gửi xong thì khóa', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const saved: NursingReport[] = []
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      // Như máy chủ: sau khi lưu, tải lại trả báo cáo vừa lưu. Báo cáo mới điền sẵn số NB ngày hôm trước (quyết định 46).
      'GET /api/v1/nursing-reports/daily': () =>
        saved.at(-1) ?? report({ values: { nurses: 18, previousDayPatients: 45 }, carriedOverKeys: ['nurses', 'previousDayPatients'] }),
      'PUT /api/v1/nursing-reports/daily': (call: ApiCall) => {
        const body = call.body as { values: Record<string, number | null>; checks: Record<string, string | null>; notes: string | null; submit: boolean }
        const values = Object.fromEntries(Object.entries(body.values).filter(([, v]) => v !== null)) as Record<string, number>
        const next = report({
          id: 'r-noi',
          status: body.submit ? 'Submitted' : 'Draft',
          // Máy chủ tính Số NB hiện tại khi lưu.
          values: { ...values, currentPatients: values.previousDayPatients + (values.admissionsFromClinic ?? 0) - (values.dischargesYesterday ?? 0) },
          checks: Object.fromEntries(Object.entries(body.checks).filter(([, v]) => v !== null)) as NursingReport['checks'],
          notes: body.notes,
          canEdit: !body.submit,
          carriedOverKeys: [],
          updatedAtUtc: '2026-11-16T00:40:00Z',
          updatedBy: 'Điều dưỡng Demo',
          submittedAtUtc: body.submit ? '2026-11-16T00:45:00Z' : null,
          submittedBy: body.submit ? 'Điều dưỡng Demo' : null,
        })
        saved.push(next)
        return next
      },
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(
      await screen.findByText(
        'Đã điền sẵn nhân sự và chỉ tiêu theo báo cáo đã gửi gần nhất của khoa; số NB ngày hôm trước theo báo cáo đã gửi hôm trước. Kiểm tra lại trước khi gửi.',
      ),
    ).toBeInTheDocument()
    // Tài khoản một khoa: không có ô chọn khoa.
    expect(screen.queryByRole('combobox', { name: 'Khoa' })).not.toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Điều dưỡng' })).toHaveValue('18')
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toMatch(/^\?date=\d{4}-\d{2}-\d{2}$/)

    // Số NB hiện tại không nhập được, tự tính = số NB ngày hôm trước + nhập khoa − xuất viện (quyết định 46).
    const current = screen.getByRole('spinbutton', { name: 'Số NB hiện tại' })
    expect(current).toBeDisabled()
    expect(current).toHaveValue('45')
    expect(screen.getByText('Tự tính: Số NB ngày hôm trước + Số ca nhập khoa từ PK ngày hôm trước − Số ca xuất viện ngày hôm trước')).toBeInTheDocument()
    await user.type(screen.getByRole('spinbutton', { name: 'Số ca nhập khoa từ PK ngày hôm trước' }), '3')
    const discharges = screen.getByRole('spinbutton', { name: 'Số ca xuất viện ngày hôm trước' })
    // Nhập nhầm làm số âm: ô có viền đỏ.
    await user.type(discharges, '99')
    expect(current).toHaveValue('-51')
    expect(current.closest('.ant-input-number')).toHaveClass('ant-input-number-status-error')
    await user.clear(discharges)
    await user.type(discharges, '1')
    expect(current).toHaveValue('47')
    expect(current.closest('.ant-input-number')).not.toHaveClass('ant-input-number-status-error')
    await user.type(screen.getByRole('spinbutton', { name: 'Số ca' }), '0')
    await user.type(screen.getByRole('textbox', { name: /Báo cáo các vấn đề bất thường/ }), 'Người bệnh A (giả lập) sốt')
    await user.click(screen.getByRole('button', { name: /Lưu nháp/ }))

    await waitFor(() => expect(saved).toHaveLength(1))
    const draft = lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')!.body as Record<string, unknown>
    expect(draft).toMatchObject({
      values: { nurses: 18, orderlies: null, adr: 0, previousDayPatients: 45, admissionsFromClinic: 3, dischargesYesterday: 1 },
      checks: { professional: null, security: null },
      notes: 'Người bệnh A (giả lập) sốt',
      submit: false,
    })
    // Không gửi dòng tính tự động: máy chủ tự tính.
    expect(draft.values).not.toHaveProperty('currentPatients')
    expect(await screen.findByText('Đã lưu nháp.')).toBeInTheDocument()

    // Gửi khi chưa chọn đủ phần tick: không gọi máy chủ.
    await user.click(screen.getByRole('button', { name: /Gửi báo cáo/ }))
    await user.click(await screen.findByRole('button', { name: 'Gửi' }))
    expect(await screen.findByText(/cho mọi dòng của phần tick trước khi gửi/)).toBeInTheDocument()
    expect(saved).toHaveLength(1)

    for (const label of ['Vấn đề chuyên môn', 'An ninh trật tự']) {
      await user.click(within(screen.getByRole('radiogroup', { name: label })).getByRole('radio', { name: 'Không có sự cố/ Ổn' }))
    }
    // Thiếu số NB ngày hôm trước thì chưa tính được số NB hiện tại: báo ngay, không gọi máy chủ.
    await user.clear(screen.getByRole('spinbutton', { name: 'Số NB ngày hôm trước' }))
    expect(screen.getByRole('spinbutton', { name: 'Số NB hiện tại' })).toHaveValue('')
    await user.click(screen.getByRole('button', { name: /Gửi báo cáo/ }))
    await user.click(await screen.findByRole('button', { name: 'Gửi' }))
    expect(await screen.findAllByText('Nhập Số NB ngày hôm trước để tính Số NB hiện tại.')).not.toHaveLength(0)
    expect(saved).toHaveLength(1)
    await user.type(screen.getByRole('spinbutton', { name: 'Số NB ngày hôm trước' }), '45')
    await user.click(screen.getByRole('button', { name: /Gửi báo cáo/ }))
    await user.click(await screen.findByRole('button', { name: 'Gửi' }))

    await waitFor(() => expect(saved).toHaveLength(2))
    expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')!.body).toMatchObject({
      checks: { professional: 'ok', security: 'ok' },
      submit: true,
    })
    expect(await screen.findByText('Báo cáo đã gửi Phòng Điều dưỡng.')).toBeInTheDocument()
    // Báo cáo đã gửi hiện số máy chủ đã tính.
    expect(screen.getByRole('spinbutton', { name: 'Số NB hiện tại' })).toBeDisabled()
    expect(screen.getByRole('spinbutton', { name: 'Số NB hiện tại' })).toHaveValue('47')
    expect(screen.queryByRole('button', { name: /Lưu nháp/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /In PDF/ })).toBeInTheDocument()
  })

  // Quyết định 41: tài khoản thuộc nhiều khoa chọn khoa để lập báo cáo; mặc định khoa chính.
  it('điều dưỡng thuộc nhiều khoa: chọn khoa ở đầu trang, tải và lưu đúng báo cáo của khoa đã chọn', async () => {
    const user = userEvent.setup()
    const surgery = { departmentId: 'd-ngoai', departmentName: 'Khoa Ngoại B (giả lập)' }
    let surgeryLoads = 0
    const { calls } = mockApi({
      ...baseRoutes(
        session({
          ...nurseSession().user,
          departments: [
            { id: 'd-noi', name: 'Khoa Nội tổng hợp' },
            { id: 'd-ngoai', name: 'Khoa Ngoại B (giả lập)' },
          ],
        }),
      ),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/daily': (call: ApiCall) => {
        if (!call.search.includes('departmentId=d-ngoai')) return report()
        // Lần tải lại sau khi lưu chưa trả về (mạng chậm): trang phải hiện ngay báo cáo vừa lưu của đúng khoa đó.
        surgeryLoads += 1
        return surgeryLoads === 1 ? report(surgery) : new Promise(() => {})
      },
      'PUT /api/v1/nursing-reports/daily': (call: ApiCall) =>
        report({
          ...((call.body as { departmentId: string }).departmentId === 'd-ngoai' ? surgery : {}),
          id: 'r-1',
          status: 'Draft',
          updatedAtUtc: '2026-11-17T01:00:00Z',
          updatedBy: 'Điều dưỡng Demo',
        }),
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText(`${definition.title} – Khoa Nội tổng hợp`)).toBeInTheDocument()
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toMatch(/^\?date=\d{4}-\d{2}-\d{2}$/)
    expect(screen.getByRole('combobox', { name: 'Khoa' }).closest('.ant-select')).toHaveTextContent('Khoa Nội tổng hợp')

    await user.click(screen.getByRole('combobox', { name: 'Khoa' }))
    await user.click(await screen.findByTitle('Khoa Ngoại B (giả lập)'))

    expect(await screen.findByText(`${definition.title} – Khoa Ngoại B (giả lập)`)).toBeInTheDocument()
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toContain('departmentId=d-ngoai')
    await user.click(screen.getByRole('button', { name: /Lưu nháp/ }))
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')?.body).toMatchObject({ departmentId: 'd-ngoai', submit: false }),
    )
    expect(await screen.findByText('Đã lưu nháp.')).toBeInTheDocument()
    expect(screen.getByText(`${definition.title} – Khoa Ngoại B (giả lập)`)).toBeInTheDocument()
    expect(await screen.findByText('Nháp')).toBeInTheDocument()
    expect(screen.queryByText('Chưa lập')).not.toBeInTheDocument()
  })

  it('khoa chưa có trong danh sách báo cáo thì chỉ xem, không lưu được', async () => {
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/daily': report({ isReportingDepartment: false, canEdit: false, carriedOverKeys: [] }),
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText('Khoa chưa có trong danh sách khoa báo cáo điều dưỡng.')).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Điều dưỡng' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Lưu nháp/ })).not.toBeInTheDocument()
  })

  it('Phòng Điều dưỡng trả lại khoa: khoa thấy ai trả lại và lý do, sửa được để gửi lại', async () => {
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/daily': report({
        id: 'r-noi',
        status: 'Draft',
        carriedOverKeys: [],
        updatedAtUtc: '2026-11-16T01:00:00Z',
        reopenedBy: 'Phòng Điều dưỡng Demo',
        reopenedAtUtc: '2026-11-16T01:00:00Z',
        reopenReason: 'Sai số NB hiện tại',
      }),
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText('Phòng Điều dưỡng đã trả lại báo cáo cho khoa (Phòng Điều dưỡng Demo).')).toBeInTheDocument()
    expect(screen.getByText('Lý do: Sai số NB hiện tại. Sửa rồi bấm "Gửi báo cáo" lại.')).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Điều dưỡng' })).toBeEnabled()
  })

  it('HIS lỗi: khoa nhập "NB PK chờ nhập viện" của khoa ở thẻ riêng, lưu riêng với báo cáo', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/daily': report({ hisEntry: hisEntry() }),
      'PUT /api/v1/nursing-reports/his-entry': report({
        hisEntry: hisEntry({
          lines: [{ key: 'waitingAdmission', label: 'NB PK chờ nhập viện', value: 3, hisValue: null }],
          updatedBy: 'Điều dưỡng Demo',
          updatedAtUtc: '2026-11-16T02:00:00Z',
        }),
      }),
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText('Số liệu HIS khoa nhập khi HIS lỗi')).toBeInTheDocument()
    expect(screen.getByText('Khoa chưa nhập số liệu HIS ngày này.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Lưu nháp/ })).toBeInTheDocument()
    await user.type(screen.getByRole('spinbutton', { name: 'NB PK chờ nhập viện' }), '3')
    await user.click(screen.getByRole('button', { name: /Lưu số liệu HIS/ }))

    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/his-entry')?.body).toEqual({
        departmentId: 'd-noi',
        reportDate: TODAY,
        values: { waitingAdmission: 3 },
      }),
    )
    expect(await screen.findByText(/Đã nhập lúc .* bởi Điều dưỡng Demo/)).toBeInTheDocument()
    expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')).toBeUndefined()
  })

  it('khoa không phải báo cáo nhưng được giao chỉ số: chỉ thấy thẻ số liệu HIS, thấy số HIS nếu đã có', async () => {
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/daily': report({
        departmentName: 'Khoa Khám bệnh',
        isReportingDepartment: false,
        canEdit: false,
        carriedOverKeys: [],
        hisEntry: hisEntry({
          lines: [{ key: 'outpatientVisits', label: 'TSNB đến khám', value: 412, hisValue: 686 }],
          hisHasValues: true,
          hisUpdatedAtUtc: '2026-11-16T01:00:00Z',
        }),
      }),
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText('Khoa Khám bệnh không phải gửi báo cáo công tác điều dưỡng.')).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'TSNB đến khám' })).toHaveValue('412')
    expect(screen.getByText('Số lấy từ HIS: 686')).toBeInTheDocument()
    expect(screen.getByText(/Ngày này đã có số liệu HIS/)).toBeInTheDocument()
    expect(screen.queryByText('Khoa chưa có trong danh sách khoa báo cáo điều dưỡng.')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Lưu nháp/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('spinbutton', { name: 'Điều dưỡng' })).not.toBeInTheDocument()
  })

  // Quyết định 48: người được ủy quyền chọn khoa được ủy quyền, chỉ các ngày trong thời gian ủy quyền.
  it('người được ủy quyền chọn khoa được ủy quyền: chuyển về ngày trong thời gian ủy quyền, thấy đang làm thay ai', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/delegations': [
        nhiDelegation(),
        // Ủy quyền đã hết hạn hoặc mình là người ủy quyền: không thêm khoa vào danh sách chọn.
        nhiDelegation({ id: 'dl-old', departmentId: 'd-tmh', departmentName: 'Khoa Tai Mũi Họng (giả lập)', status: 'Expired' }),
        nhiDelegation({ id: 'dl-given', departmentId: 'd-noi', delegatorUserId: 'u-dieuduong', delegateUserId: 'u-nhi' }),
      ],
      'GET /api/v1/nursing-reports/daily': (call: ApiCall) =>
        call.search.includes('departmentId=d-nhi') ? report({ ...nhi, reportDate: new URLSearchParams(call.search).get('date')! }) : report(),
      'PUT /api/v1/nursing-reports/daily': (call: ApiCall) =>
        report({ ...nhi, id: 'r-nhi', status: 'Draft', updatedAtUtc: '2026-11-20T01:00:00Z', updatedBy: 'Điều dưỡng Demo', reportDate: (call.body as { reportDate: string }).reportDate }),
    })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText(`${definition.title} – Khoa Nội tổng hợp`)).toBeInTheDocument()
    // Danh sách ủy quyền đã tải (ô khoa hiện ra): khoa của mình không ghi "làm thay", kể cả khoa mình đã ủy quyền cho người khác.
    await screen.findByRole('combobox', { name: 'Khoa' })
    expect(screen.queryByText(/^Bạn làm thay theo ủy quyền/)).not.toBeInTheDocument()
    // Khoa của mình: chọn ngày 3 hôm trước được (trong 7 ngày gần nhất).
    const dateInput = screen.getByRole('textbox', { name: 'Ngày báo cáo' })
    await user.clear(dateInput)
    await user.type(dateInput, `${day(-3).format('DD/MM/YYYY')}{Enter}`)
    await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toContain(`date=${day(-3).format('YYYY-MM-DD')}`))

    await user.click(screen.getByRole('combobox', { name: 'Khoa' }))
    expect(screen.queryByTitle('Khoa Tai Mũi Họng (giả lập) (được ủy quyền)')).not.toBeInTheDocument()
    await user.click(await screen.findByTitle('Khoa Nhi (giả lập) (được ủy quyền)'))

    // Ngày 3 hôm trước ở ngoài thời gian ủy quyền: chuyển về ngày bắt đầu ủy quyền.
    expect(await screen.findByText(`${definition.title} – Khoa Nhi (giả lập)`)).toBeInTheDocument()
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toBe(`?date=${day(-1).format('YYYY-MM-DD')}&departmentId=d-nhi`)
    expect(screen.getByText(/^Bạn làm thay theo ủy quyền của Điều dưỡng Nhi \(giả lập\) \(Khoa Nhi \(giả lập\)\)/)).toBeInTheDocument()
    expect(screen.getByText('Nội dung được ủy quyền: Báo cáo của khoa. Bảng tổng hợp ghi "được Điều dưỡng Nhi (giả lập) ủy quyền".')).toBeInTheDocument()

    // Ngày trước thời gian ủy quyền không nhận.
    await user.clear(dateInput)
    await user.type(dateInput, `${day(-2).format('DD/MM/YYYY')}{Enter}`)
    expect(calls.some((c) => c.path === '/api/v1/nursing-reports/daily' && c.search.includes(`date=${day(-2).format('YYYY-MM-DD')}&departmentId=d-nhi`))).toBe(false)

    await user.click(screen.getByRole('button', { name: /Lưu nháp/ }))
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')?.body).toMatchObject({ departmentId: 'd-nhi', reportDate: day(-1).format('YYYY-MM-DD') }),
    )
  })

  it('mở từ trang Ủy quyền (?khoa=) thì tải ngay báo cáo của khoa được ủy quyền', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/delegations': [nhiDelegation()],
      'GET /api/v1/nursing-reports/daily': (call: ApiCall) => (call.search.includes('departmentId=d-nhi') ? report(nhi) : report()),
    })
    renderApp('/dieu-duong/bao-cao-khoa?khoa=d-nhi')

    expect(await screen.findByText(`${definition.title} – Khoa Nhi (giả lập)`)).toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/v1/nursing-reports/daily').every((c) => c.search.includes('departmentId=d-nhi'))).toBe(true)
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Khoa' }).closest('.ant-select')).toHaveTextContent('Khoa Nhi (giả lập) (được ủy quyền)'))
    expect(await screen.findByText(/^Bạn làm thay theo ủy quyền của Điều dưỡng Nhi \(giả lập\)/)).toBeInTheDocument()
  })

  it('tài khoản nhiều khoa mở ?khoa= khi danh sách ủy quyền chưa tải xong: ô khoa không hiện mã khoa thô', async () => {
    mockApi({
      ...baseRoutes(
        session({
          ...nurseSession().user,
          departments: [
            { id: 'd-noi', name: 'Khoa Nội tổng hợp' },
            { id: 'd-ngoai', name: 'Khoa Ngoại B (giả lập)' },
          ],
        }),
      ),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/delegations': () => new Promise(() => {}),
      'GET /api/v1/nursing-reports/daily': report(nhi),
    })
    renderApp('/dieu-duong/bao-cao-khoa?khoa=d-nhi')

    expect(await screen.findByText(`${definition.title} – Khoa Nhi (giả lập)`)).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Khoa' }).closest('.ant-select')).not.toHaveTextContent('d-nhi')
  })

  it('chỉ được ủy quyền nhập số liệu HIS: không thấy báo cáo của khoa, chỉ thẻ số liệu HIS', async () => {
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/delegations': [nhiDelegation({ contents: ['hisEntry'] })],
      'GET /api/v1/nursing-reports/daily': report({
        ...nhi,
        reportVisible: false,
        values: {},
        canEdit: false,
        carriedOverKeys: [],
        hisEntry: hisEntry(),
      }),
    })
    renderApp('/dieu-duong/bao-cao-khoa?khoa=d-nhi')

    expect(await screen.findByText('Bạn chỉ được ủy quyền nhập số liệu HIS của Khoa Nhi (giả lập).')).toBeInTheDocument()
    expect(screen.getByText('Số liệu HIS khoa nhập khi HIS lỗi')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Lưu số liệu HIS/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Lưu nháp/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('spinbutton', { name: 'Điều dưỡng' })).not.toBeInTheDocument()
    expect(await screen.findByText('Nội dung được ủy quyền: Số liệu HIS khi HIS lỗi. Bảng tổng hợp ghi "được Điều dưỡng Nhi (giả lập) ủy quyền".')).toBeInTheDocument()
  })

  it('Phòng Điều dưỡng không có quyền lập báo cáo khoa', async () => {
    mockApi({ ...baseRoutes(officeSession()), 'GET /api/v1/nursing-reports/summary': summary })
    renderApp('/dieu-duong/bao-cao-khoa')

    expect(await screen.findByText(/không có quyền/i)).toBeInTheDocument()
  })
})

describe('Tổng hợp báo cáo điều dưỡng', () => {
  const objectUrls: string[] = []
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => {
      const url = `blob:test/${objectUrls.length}`
      objectUrls.push(url)
      return url
    })
    URL.revokeObjectURL = vi.fn()
  })
  afterEach(() => {
    objectUrls.length = 0
  })

  it('hiện mục A–F như bản giấy, xem báo cáo một khoa và trả lại khoa kèm lý do', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/form': definition,
      'GET /api/v1/nursing-reports/summary': summary,
      'GET /api/v1/nursing-reports/daily': report({
        id: 'r-noi',
        status: 'Submitted',
        values: { nurses: 18, currentPatients: 47 },
        checks: { professional: 'ok', security: 'ok' },
        notes: 'Người bệnh A (giả lập) sốt về đêm',
        canEdit: false,
        canReopen: true,
        carriedOverKeys: [],
        submittedBy: 'Điều dưỡng Demo',
        submittedAtUtc: '2026-11-16T00:45:00Z',
      }),
      'POST /api/v1/nursing-reports/r-noi/reopen': report({
        id: 'r-noi',
        status: 'Draft',
        canEdit: false,
        reopenReason: 'Sai số NB hiện tại',
        reopenedBy: 'Phòng Điều dưỡng Demo',
        reopenedAtUtc: '2026-11-16T01:00:00Z',
        carriedOverKeys: [],
      }),
    })
    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText('A. Số liệu người bệnh nội trú')).toBeInTheDocument()
    // Quyết định 58: bảng tổng hợp là thẻ mở sẵn của Dashboard; số khoa đã gửi ghi ở ô chỉ số đầu trang, không nhắc lại ở ô HIS.
    expect(screen.getByRole('heading', { name: 'Dashboard điều dưỡng' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Bảng tổng hợp' })).toHaveAttribute('aria-selected', 'true')
    expect(within(await screen.findByRole('group', { name: 'Khoa đã gửi báo cáo' })).getByText('1/2')).toBeInTheDocument()
    expect(screen.queryByText(/Khoa đã gửi:/)).not.toBeInTheDocument()
    expect(screen.getByText(/Số liệu HIS lấy lúc/)).toBeInTheDocument()
    const table = screen.getAllByRole('table')[0]
    // Quyết định 44: cột khoa phòng ghi mã khoa (tên đầy đủ khi rê chuột, đọc được qua nhãn nút); cột cuối là người báo cáo.
    expect(within(table).getByRole('button', { name: 'NGOAIB – Khoa Ngoại B (giả lập)' })).toHaveTextContent('NGOAIB')
    expect(within(table).queryByText('Khoa Ngoại B (giả lập)')).not.toBeInTheDocument()
    expect(within(table).getAllByRole('columnheader').at(-1)).toHaveTextContent('Người báo cáo')
    const noiRow = within(table).getByRole('button', { name: 'NOI – Khoa Nội tổng hợp' }).closest('tr')!
    expect(within(noiRow).getAllByRole('cell').at(-1)).toHaveTextContent('Điều dưỡng Demo')
    expect(within(table).getByText('Tổng')).toBeInTheDocument()
    // Dòng tổng có đủ ô tới cột người báo cáo (kẻ viền thẳng hàng với tiêu đề).
    const totalRow = within(table).getByText('Tổng').closest('tr')!
    expect(totalRow.querySelectorAll('td')).toHaveLength(within(table).getAllByRole('columnheader').length)
    expect(within(table).getByText('65')).toBeInTheDocument()
    expect(screen.getByText('686')).toBeInTheDocument()
    expect(screen.getByText('Không ổn: NOI')).toBeInTheDocument()
    expect(screen.getByText(/Người bệnh A \(giả lập\) sốt về đêm/)).toBeInTheDocument()
    // Mục F: cột khoa ghi mã khoa.
    const incidentRow = screen.getByText(/Người bệnh A \(giả lập\) sốt về đêm/).closest('tr')!
    expect(within(incidentRow).getAllByRole('cell')[0]).toHaveTextContent(/^NOI$/)
    // Mỗi mục B–F có dòng người báo cáo; mục chưa có số liệu ghi "chưa có".
    expect(screen.getAllByText(/^Người báo cáo: /).map((p) => p.textContent)).toEqual([
      'Người báo cáo: Phòng Điều dưỡng Demo (lấy từ HIS)',
      'Người báo cáo: Điều dưỡng Demo (NOI)',
      'Người báo cáo: chưa có',
      'Người báo cáo: Điều dưỡng Demo (NOI)',
      'Người báo cáo: Điều dưỡng Demo (NOI)',
    ])

    await user.click(within(table).getByRole('button', { name: 'NOI – Khoa Nội tổng hợp' }))
    const drawer = await screen.findByRole('dialog', { name: /Khoa Nội tổng hợp/ })
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toContain('departmentId=d-noi')
    expect(await within(drawer).findByRole('spinbutton', { name: 'Số NB hiện tại' })).toBeDisabled()
    // Xem báo cáo đã lưu: hiện số đã lưu (báo cáo cũ khoa nhập tay, chưa có số NB ngày hôm trước), không tính lại.
    expect(within(drawer).getByRole('spinbutton', { name: 'Số NB hiện tại' })).toHaveValue('47')

    await user.click(within(drawer).getByRole('button', { name: /Trả lại khoa/ }))
    // Hộp thoại mở trên ngăn kéo: AntD trong môi trường test đặt cùng id tiêu đề, nên tìm theo chữ thay vì theo tên.
    const modal = (await screen.findByText('Trả lại báo cáo cho khoa sửa')).closest<HTMLElement>('[role="dialog"]')!
    expect(within(modal).getByRole('button', { name: 'Trả lại khoa' })).toBeDisabled()
    await user.type(within(modal).getByRole('textbox', { name: 'Lý do trả lại' }), 'Sai số NB hiện tại')
    await user.click(within(modal).getByRole('button', { name: 'Trả lại khoa' }))

    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/nursing-reports/r-noi/reopen')?.body).toEqual({ reason: 'Sai số NB hiện tại' }),
    )
    expect(await screen.findByText('Đã trả lại báo cáo cho khoa. Khoa sửa và gửi lại.')).toBeInTheDocument()
    expect(await within(drawer).findByText(/^Đã trả lại khoa lúc .* bởi Phòng Điều dưỡng Demo$/)).toBeInTheDocument()
    expect(within(drawer).getByText('Lý do: Sai số NB hiện tại')).toBeInTheDocument()

    // Rê chuột vào mã khoa thấy tên khoa đầy đủ. Kiểm tra sau cùng: AntD trong môi trường test đặt cùng id cho chú thích và
    // tiêu đề ngăn kéo, nên chú thích mở trước sẽ làm sai tên ngăn kéo. Chú thích của mã NOI (đã bấm) vẫn còn trong trang.
    await user.hover(within(table).getByRole('button', { name: 'NGOAIB – Khoa Ngoại B (giả lập)' }))
    await waitFor(() => expect(screen.getAllByRole('tooltip').map((t) => t.textContent)).toContain('Khoa Ngoại B (giả lập)'))
  })

  it('xuất Excel, lấy số liệu HIS (báo mã khoa không khớp), lỗi HIS thì báo các khoa nhập tay', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    let refreshes = 0
    const { calls } = mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/summary': summary,
      'GET /api/v1/nursing-reports/summary/xlsx': () => new Response(new Blob(['PK']), { status: 200 }),
      'POST /api/v1/nursing-reports/statistics/refresh': () =>
        ++refreshes === 1
          ? json({ summary, unmatchedDepartmentCodes: ['KHONGCO'] })
          : problem(422, 'Không lấy được số liệu từ HIS (HIS lỗi hoặc chưa có câu truy vấn số liệu). Các khoa nhập tay số liệu của khoa.', {
              code: 'nursing_reports.his_unavailable',
            }),
    })
    renderApp('/dieu-duong/dashboard')

    await user.click(await screen.findByRole('button', { name: /Xuất Excel/ }))
    await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary/xlsx')).toBeDefined())
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary/xlsx')!.search).toMatch(/^\?date=/)

    await user.click(screen.getByRole('button', { name: /Lấy số liệu HIS/ }))
    expect(await screen.findByText(/Mã khoa không khớp khoa nào: KHONGCO/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Lấy số liệu HIS/ }))
    expect(await screen.findByText(/Các khoa nhập tay số liệu của khoa/)).toBeInTheDocument()
  })

  it('cài khoa báo cáo, số giường và khoa nhập từng chỉ số khi HIS lỗi; Phòng Điều dưỡng không nhập tay số liệu HIS', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/summary': summary,
      'GET /api/v1/nursing-reports/departments': departments,
      'PUT /api/v1/nursing-reports/departments': departments,
    })
    renderApp('/dieu-duong/dashboard')

    await user.click(await screen.findByRole('button', { name: /Khoa báo cáo và số giường/ }))
    expect(screen.queryByRole('button', { name: /Nhập số liệu HIS/ })).not.toBeInTheDocument()
    const modal = await screen.findByRole('dialog', { name: 'Khoa báo cáo và số giường thực kê' })
    await user.click(await within(modal).findByRole('checkbox', { name: 'Khoa Khám bệnh phải báo cáo' }))
    await user.type(within(modal).getByRole('spinbutton', { name: 'Số giường Khoa Khám bệnh' }), '10')
    await user.click(within(modal).getByRole('checkbox', { name: 'Khoa Ngoại B (giả lập) phải báo cáo' }))

    // Đang giao Khoa Khám bệnh nhập TSNB đến khám; giao thêm Khoa Nội tổng hợp nhập X-quang.
    expect(within(modal).getByRole('combobox', { name: 'Khoa nhập TSNB đến khám' }).closest('.ant-select')).toHaveTextContent('Khoa Khám bệnh')
    await user.click(within(modal).getByRole('combobox', { name: 'Khoa nhập TS X-quang' }))
    await user.click(await screen.findByTitle('Khoa Nội tổng hợp'))
    await user.click(within(modal).getByRole('button', { name: 'Lưu' }))

    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/departments')?.body).toEqual({
        // Khoa mới chọn xếp sau các khoa đang báo cáo (quyết định 45).
        items: [
          { departmentId: 'd-noi', bedCount: 40 },
          { departmentId: 'd-kkb', bedCount: 10 },
        ],
        hisEntry: { outpatientVisits: 'd-kkb', xray: 'd-noi' },
        shortNames: {},
      }),
    )
  })

  it('khoa ngừng sử dụng (không còn trong danh mục HIS) có nhãn, không chọn được, bỏ khi lưu; lỗi máy chủ ghi rõ khoa', { timeout: 20_000 }, async () => {
    // Bệnh viện báo (20/11): chọn khoa HIS mới rồi Lưu thì báo "Dữ liệu không hợp lệ", vì khoa cũ đã ngừng sử dụng vẫn được gửi lên.
    const user = userEvent.setup()
    const stoppedIcu: NursingReportDepartment = {
      departmentId: 'd-hstc', code: 'HSTC', name: 'Khoa Hồi sức tích cực (giả lập)', shortName: null, isActive: false, isReporting: true, bedCount: 20, hisIndicators: ['xray'],
    }
    const hisIcu: NursingReportDepartment = {
      departmentId: 'd-hhtccd', code: 'HHTCCD', name: 'Hồi Sức Tích Cực - Chống Độc (giả lập)', shortName: null, isActive: true, isReporting: false, bedCount: null, hisIndicators: [],
    }
    let saves = 0
    const { calls } = mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/summary': summary,
      'GET /api/v1/nursing-reports/departments': [hisIcu, stoppedIcu, ...departments.slice(1)],
      'PUT /api/v1/nursing-reports/departments': () =>
        ++saves === 1
          ? problem(400, 'Dữ liệu không hợp lệ.', {
              errors: { 'items[0].departmentId': ['Khoa X (giả lập) (KX) đã ngừng sử dụng (không còn trong danh mục khoa/phòng), bỏ chọn khoa này.'] },
            })
          : json([{ ...hisIcu, isReporting: true, bedCount: 22 }, ...departments.slice(1)]),
    })
    renderApp('/dieu-duong/dashboard')

    await user.click(await screen.findByRole('button', { name: /Khoa báo cáo và số giường/ }))
    const modal = await screen.findByRole('dialog', { name: 'Khoa báo cáo và số giường thực kê' })
    expect(await within(modal).findByText('Có khoa đã ngừng sử dụng')).toBeInTheDocument()
    expect(within(modal).getByText(/Khoa Hồi sức tích cực \(giả lập\) \(HSTC\): không còn trong danh mục khoa\/phòng/)).toBeInTheDocument()
    // Khoa ngừng sử dụng xuống cuối bảng, có nhãn, không chọn được.
    const rows = within(modal).getAllByRole('row').slice(1)
    expect(rows.at(-1)).toHaveTextContent('Khoa Hồi sức tích cực (giả lập) (HSTC)Ngừng sử dụng')
    const stoppedBox = within(modal).getByRole('checkbox', { name: 'Khoa Hồi sức tích cực (giả lập) phải báo cáo' })
    expect(stoppedBox).toBeDisabled()
    expect(stoppedBox).not.toBeChecked()
    // Không xếp thứ tự, không đặt tên viết tắt cho khoa ngừng sử dụng.
    expect(within(modal).queryByRole('textbox', { name: 'Thứ tự Khoa Hồi sức tích cực (giả lập)' })).not.toBeInTheDocument()
    expect(within(modal).getByRole('textbox', { name: 'Tên viết tắt Khoa Hồi sức tích cực (giả lập)' })).toBeDisabled()
    // Chỉ số đang giao cho khoa ngừng sử dụng: ô chọn để trống, có nhắc; danh sách chọn không có khoa ngừng sử dụng.
    expect(within(modal).getByText(/Đang giao Khoa Hồi sức tích cực \(giả lập\) \(HSTC\), khoa này đã ngừng sử dụng/)).toBeInTheDocument()
    expect(within(modal).getByRole('combobox', { name: 'Khoa nhập TS X-quang' }).closest('.ant-select')).not.toHaveTextContent('Khoa Hồi sức')
    await user.click(within(modal).getByRole('combobox', { name: 'Khoa nhập TS X-quang' }))
    expect(await screen.findByTitle('Hồi Sức Tích Cực - Chống Độc (giả lập)')).toBeInTheDocument()
    expect(screen.queryByTitle('Khoa Hồi sức tích cực (giả lập)')).not.toBeInTheDocument()
    // Đóng danh sách chọn (Esc sẽ đóng cả hộp thoại).
    await user.click(within(modal).getByRole('combobox', { name: 'Khoa nhập TS X-quang' }))

    await user.click(within(modal).getByRole('checkbox', { name: 'Hồi Sức Tích Cực - Chống Độc (giả lập) phải báo cáo' }))
    await user.type(within(modal).getByRole('spinbutton', { name: 'Số giường Hồi Sức Tích Cực - Chống Độc (giả lập)' }), '22')
    await user.click(within(modal).getByRole('button', { name: 'Lưu' }))

    // Không gửi khoa ngừng sử dụng, bỏ giao chỉ số của khoa đó.
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/departments')?.body).toEqual({
        items: [
          { departmentId: 'd-ngoai', bedCount: 25 },
          { departmentId: 'd-noi', bedCount: 40 },
          { departmentId: 'd-hhtccd', bedCount: 22 },
        ],
        hisEntry: { outpatientVisits: null, xray: null },
        shortNames: {},
      }),
    )
    // Máy chủ vẫn từ chối (ví dụ khoa vừa ngừng sử dụng): thông báo ghi rõ khoa, không chỉ "Dữ liệu không hợp lệ".
    expect(
      await screen.findByText(
        'Dữ liệu không hợp lệ. Khoa X (giả lập) (KX) đã ngừng sử dụng (không còn trong danh mục khoa/phòng), bỏ chọn khoa này.',
      ),
    ).toBeInTheDocument()
    // Báo lỗi xong thì nút Lưu dùng được ngay (không chờ thông báo đóng).
    await user.click(within(modal).getByRole('button', { name: 'Lưu' }))
    expect(await screen.findByText('Đã lưu danh sách khoa báo cáo.')).toBeInTheDocument()
  })

  it('Phòng Điều dưỡng xếp thứ tự các khoa ở mục A và đặt tên viết tắt in trên bảng tổng hợp (quyết định 45)', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/summary': summary,
      // Máy chủ trả khoa phải báo cáo trước, theo thứ tự đã lưu, rồi các khoa khác.
      'GET /api/v1/nursing-reports/departments': [departments[1], { ...departments[2], shortName: 'Nội' }, departments[0]],
      'PUT /api/v1/nursing-reports/departments': departments,
    })
    renderApp('/dieu-duong/dashboard')

    await user.click(await screen.findByRole('button', { name: /Khoa báo cáo và số giường/ }))
    const modal = await screen.findByRole('dialog', { name: 'Khoa báo cáo và số giường thực kê' })
    await within(modal).findByRole('checkbox', { name: 'Khoa Khám bệnh phải báo cáo' })
    const order = () => within(modal).getAllByRole('row').slice(1).map((row) => row.textContent)
    const position = (name: string) => within(modal).queryByRole('textbox', { name: `Thứ tự ${name}` })
    const moveTo = async (name: string, typed: string) => {
      await user.clear(position(name)!)
      await user.type(position(name)!, typed)
    }
    expect(order()).toEqual(['Khoa Ngoại B (giả lập) (NGOAIB)', 'Khoa Nội tổng hợp (NOI)', 'Khoa Khám bệnh (KKB)'])
    expect(position('Khoa Ngoại B (giả lập)')).toHaveValue('1')
    expect(position('Khoa Nội tổng hợp')).toHaveValue('2')
    expect(position('Khoa Khám bệnh')).not.toBeInTheDocument()
    expect(within(modal).getByRole('button', { name: 'Đưa Khoa Ngoại B (giả lập) lên' })).toBeDisabled()
    expect(within(modal).getByRole('button', { name: 'Đưa Khoa Nội tổng hợp xuống' })).toBeDisabled()

    // Đổi chỗ bằng mũi tên; khoa mới chọn xếp cuối; gõ số thứ tự rồi Enter hoặc rời ô (số ngoài khoảng thì về đầu, cuối).
    await user.click(within(modal).getByRole('button', { name: 'Đưa Khoa Nội tổng hợp lên' }))
    expect(order()).toEqual(['Khoa Nội tổng hợp (NOI)', 'Khoa Ngoại B (giả lập) (NGOAIB)', 'Khoa Khám bệnh (KKB)'])
    await user.click(within(modal).getByRole('checkbox', { name: 'Khoa Khám bệnh phải báo cáo' }))
    expect(position('Khoa Khám bệnh')).toHaveValue('3')
    await user.type(within(modal).getByRole('spinbutton', { name: 'Số giường Khoa Khám bệnh' }), '12')
    await moveTo('Khoa Khám bệnh', '1{Enter}')
    expect(order()).toEqual(['Khoa Khám bệnh (KKB)', 'Khoa Nội tổng hợp (NOI)', 'Khoa Ngoại B (giả lập) (NGOAIB)'])
    expect(position('Khoa Ngoại B (giả lập)')).toHaveValue('3')
    await moveTo('Khoa Khám bệnh', '99{Enter}')
    expect(order()).toEqual(['Khoa Nội tổng hợp (NOI)', 'Khoa Ngoại B (giả lập) (NGOAIB)', 'Khoa Khám bệnh (KKB)'])
    await moveTo('Khoa Ngoại B (giả lập)', 'x')
    expect(position('Khoa Ngoại B (giả lập)')).toHaveValue('')
    await user.click(within(modal).getByText(/Các khoa được chọn đứng đầu danh sách/))
    expect(position('Khoa Ngoại B (giả lập)')).toHaveValue('2')
    await moveTo('Khoa Ngoại B (giả lập)', '0')
    await user.click(within(modal).getByText(/Các khoa được chọn đứng đầu danh sách/))
    expect(order()).toEqual(['Khoa Ngoại B (giả lập) (NGOAIB)', 'Khoa Nội tổng hợp (NOI)', 'Khoa Khám bệnh (KKB)'])
    // Bỏ chọn thì ra khỏi thứ tự, chọn lại thì xếp cuối.
    await user.click(within(modal).getByRole('checkbox', { name: 'Khoa Nội tổng hợp phải báo cáo' }))
    expect(position('Khoa Nội tổng hợp')).not.toBeInTheDocument()
    expect(position('Khoa Khám bệnh')).toHaveValue('2')
    await user.click(within(modal).getByRole('checkbox', { name: 'Khoa Nội tổng hợp phải báo cáo' }))
    expect(order()).toEqual(['Khoa Ngoại B (giả lập) (NGOAIB)', 'Khoa Khám bệnh (KKB)', 'Khoa Nội tổng hợp (NOI)'])

    // Tên viết tắt: để trống thì in mã khoa; trùng tên viết tắt hoặc mã của khoa khác (không phân biệt hoa thường) thì báo ngay.
    const shortName = (name: string) => within(modal).getByRole('textbox', { name: `Tên viết tắt ${name}` })
    expect(shortName('Khoa Nội tổng hợp')).toHaveValue('Nội')
    expect(shortName('Khoa Khám bệnh')).toHaveAttribute('placeholder', 'KKB')
    await user.type(shortName('Khoa Khám bệnh'), 'nội')
    expect(within(modal).getByText('Trùng với Khoa Nội tổng hợp (NOI)')).toBeInTheDocument()
    expect(within(modal).getByText('Trùng với Khoa Khám bệnh (KKB)')).toBeInTheDocument()
    expect(shortName('Khoa Khám bệnh')).toHaveClass('ant-input-status-error')
    expect(shortName('Khoa Nội tổng hợp')).toHaveClass('ant-input-status-error')
    expect(shortName('Khoa Ngoại B (giả lập)')).not.toHaveClass('ant-input-status-error')
    // Gõ kiểu tổ hợp (một số bộ gõ tiếng Việt) vẫn là trùng.
    await user.clear(shortName('Khoa Khám bệnh'))
    await user.type(shortName('Khoa Khám bệnh'), 'NO\u0302\u0323I')
    expect(within(modal).getByText('Trùng với Khoa Nội tổng hợp (NOI)')).toBeInTheDocument()
    await user.clear(shortName('Khoa Khám bệnh'))
    await user.type(shortName('Khoa Khám bệnh'), 'ngoaib')
    expect(within(modal).getByText('Trùng với Khoa Ngoại B (giả lập) (NGOAIB)')).toBeInTheDocument()
    await user.clear(shortName('Khoa Khám bệnh'))
    await user.type(shortName('Khoa Khám bệnh'), '  KB ')
    expect(within(modal).queryByText(/Trùng với/)).not.toBeInTheDocument()
    expect(shortName('Khoa Khám bệnh')).not.toHaveClass('ant-input-status-error')
    // Gõ rồi xóa hết (vẫn như cũ) thì không gửi; xóa tên viết tắt đang có thì gửi null.
    await user.type(shortName('Khoa Ngoại B (giả lập)'), 'x')
    await user.clear(shortName('Khoa Ngoại B (giả lập)'))
    await user.clear(shortName('Khoa Nội tổng hợp'))
    await user.click(within(modal).getByRole('button', { name: 'Lưu' }))

    // Gửi khoa theo thứ tự đã xếp và chỉ các tên viết tắt đã đổi (bỏ khoảng trắng hai đầu; xóa tên viết tắt đang có là null).
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/departments')?.body).toEqual({
        items: [
          { departmentId: 'd-ngoai', bedCount: 25 },
          { departmentId: 'd-kkb', bedCount: 12 },
          { departmentId: 'd-noi', bedCount: 40 },
        ],
        hisEntry: { outpatientVisits: 'd-kkb', xray: null },
        shortNames: { 'd-kkb': 'KB', 'd-noi': null },
      }),
    )
    expect(await screen.findByText('Đã lưu danh sách khoa báo cáo.')).toBeInTheDocument()
  })

  it('HIS lỗi: số do khoa nhập có ghi tên khoa, và cho biết khoa nào chưa nhập', async () => {
    mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/summary': {
        ...summary,
        outpatient: [{ key: 'outpatientVisits', label: 'TSNB đến khám', value: 412, enteredBy: 'Khoa Khám bệnh' }],
        imaging: [{ key: 'xray', label: 'TS X-quang', value: null }],
        statistics: {
          hisAvailable: true,
          hasValues: false,
          source: null,
          updatedAtUtc: null,
          updatedBy: null,
          usesDepartmentEntries: true,
          entries: [
            { departmentId: 'd-kkb', departmentName: 'Khoa Khám bệnh', keys: ['outpatientVisits', 'xray'], enteredAtUtc: '2026-11-16T02:10:00Z', enteredBy: 'Điều dưỡng KKB' },
            { departmentId: 'd-ngoai', departmentName: 'Khoa Ngoại B (giả lập)', keys: ['waitingAdmission'], enteredAtUtc: null, enteredBy: null },
            { departmentId: 'd-noi', departmentName: 'Khoa Nội tổng hợp', keys: ['waitingAdmission'], enteredAtUtc: '2026-11-16T02:00:00Z', enteredBy: 'Điều dưỡng Demo' },
          ],
        },
      },
    })
    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText(/HIS lỗi thì các khoa nhập tay số liệu của khoa/)).toBeInTheDocument()
    expect(screen.getByText(/Khoa Khám bệnh \(TSNB đến khám, TS X-quang\): đã nhập lúc 16\/11\/2026 \d\d:\d\d \(Điều dưỡng KKB\)/)).toBeInTheDocument()
    expect(screen.getByText(/NB PK chờ nhập viện: 1\/2 khoa đã nhập \(chưa nhập: Khoa Ngoại B \(giả lập\)\)/)).toBeInTheDocument()
    expect(screen.getByText('Khoa Khám bệnh nhập tay')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Nhập số liệu HIS/ })).not.toBeInTheDocument()
  })

  // Quyết định 48: bảng tổng hợp ghi người được ủy quyền làm và ghi rõ được ai ủy quyền.
  it('ủy quyền: liệt kê ủy quyền trong ngày, người báo cáo và người nhập số liệu HIS ghi "được … ủy quyền"', async () => {
    mockApi({
      ...officeRoutes(),
      'GET /api/v1/nursing-reports/summary': {
        ...summary,
        rows: summary.rows.map((r) =>
          r.departmentId === 'd-noi' ? { ...r, submittedBy: 'Điều dưỡng Ngoại B (giả lập) (được Điều dưỡng Demo ủy quyền)' } : r,
        ),
        statistics: {
          ...summary.statistics,
          hasValues: false,
          usesDepartmentEntries: true,
          entries: [
            {
              departmentId: 'd-kkb',
              departmentName: 'Khoa Khám bệnh',
              keys: ['outpatientVisits'],
              enteredAtUtc: '2026-11-16T02:10:00Z',
              enteredBy: 'Điều dưỡng Ngoại B (giả lập)',
              enteredByDelegator: 'Điều dưỡng KKB (giả lập)',
            },
          ],
        },
        delegations: [
          {
            departmentId: 'd-noi',
            departmentCode: 'NOI',
            departmentName: 'Khoa Nội tổng hợp',
            delegateName: 'Điều dưỡng Ngoại B (giả lập)',
            delegatorName: 'Điều dưỡng Demo',
            startDate: '2026-11-16',
            endDate: '2026-11-18',
            contents: ['report', 'hisEntry'],
          },
        ],
      },
    })
    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText('Ủy quyền báo cáo trong ngày (1)')).toBeInTheDocument()
    const item = screen.getByText('NOI', { selector: 'b' }).closest('li')!
    expect(item).toHaveTextContent(
      'NOI: Điều dưỡng Ngoại B (giả lập) được Điều dưỡng Demo ủy quyền, 16/11 – 18/11/2026 (Báo cáo của khoa, Số liệu HIS khi HIS lỗi)',
    )
    expect(screen.getByRole('cell', { name: 'Điều dưỡng Ngoại B (giả lập) (được Điều dưỡng Demo ủy quyền)' })).toBeInTheDocument()
    expect(
      screen.getByText(/Khoa Khám bệnh \(TSNB đến khám\): đã nhập lúc 16\/11\/2026 \d\d:\d\d \(Điều dưỡng Ngoại B \(giả lập\), được Điều dưỡng KKB \(giả lập\) ủy quyền\)/),
    ).toBeInTheDocument()
  })

  it('không có ủy quyền trong ngày thì không hiện mục ủy quyền', async () => {
    mockApi({ ...officeRoutes(), 'GET /api/v1/nursing-reports/summary': { ...summary, delegations: [] } })
    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByText('A. Số liệu người bệnh nội trú')).toBeInTheDocument()
    expect(screen.queryByText(/Ủy quyền báo cáo trong ngày/)).not.toBeInTheDocument()
  })

  // Quyết định 58: bỏ trang Tổng hợp hằng ngày; đường dẫn cũ (đã lưu trên trình duyệt) mở Dashboard, vẫn kiểm tra quyền.
  it('đường dẫn cũ của trang Tổng hợp hằng ngày mở Dashboard ở thẻ Bảng tổng hợp; không có quyền thì vẫn bị chặn', async () => {
    mockApi(officeRoutes({ 'GET /api/v1/nursing-reports/summary': summary }))
    const { router, unmount } = renderApp('/dieu-duong/tong-hop')

    expect(await screen.findByText('A. Số liệu người bệnh nội trú')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dieu-duong/dashboard')
    expect(screen.getByRole('heading', { name: 'Dashboard điều dưỡng' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Bảng tổng hợp' })).toHaveAttribute('aria-selected', 'true')
    unmount()

    const { calls } = mockApi({ ...baseRoutes(nurseSession()) })
    renderApp('/dieu-duong/tong-hop')
    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
    expect(calls.some((c) => c.path.startsWith('/api/v1/nursing-reports/summary'))).toBe(false)
  })

  it(
    'một ô ngày cho cả Dashboard: bảng tổng hợp, ô chỉ số, PDF, báo cáo khoa và thẻ Biểu đồ cùng ngày; đổi khoảng ngày không tải lại bảng tổng hợp',
    { timeout: 30_000 },
    async () => {
      const user = userEvent.setup()
      const { calls } = mockApi(
        officeRoutes({
          'GET /api/v1/nursing-reports/form': definition,
          'GET /api/v1/nursing-reports/summary': (call: ApiCall) => ({ ...summary, reportDate: new URLSearchParams(call.search).get('date') }),
          'GET /api/v1/nursing-reports/summary/pdf': () => new Response('%PDF-1.7', { headers: { 'Content-Type': 'application/pdf' } }),
          'GET /api/v1/nursing-reports/daily': (call: ApiCall) =>
            report({ reportDate: new URLSearchParams(call.search).get('date') ?? TODAY, canEdit: false, carriedOverKeys: [] }),
        }),
      )
      renderApp('/dieu-duong/dashboard')

      await screen.findByText('A. Số liệu người bệnh nội trú')
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary')!.search).toBe(`?date=${TODAY}`)
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/dashboard')!.search).toBe(`?to=${TODAY}&days=30`)

      const earlier = dayjs().subtract(3, 'day')
      const end = earlier.format('YYYY-MM-DD')
      const picker = screen.getByLabelText('Ngày báo cáo')
      await user.click(picker)
      await user.clear(picker)
      await user.type(picker, `${earlier.format('DD/MM/YYYY')}{Enter}`)
      await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary')!.search).toBe(`?date=${end}`))
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/dashboard')!.search).toBe(`?to=${end}&days=30`)
      expect(await screen.findByRole('heading', { name: `Ngày ${earlier.format('DD/MM/YYYY')}` })).toBeInTheDocument()

      // In PDF và báo cáo của khoa theo ngày đã chọn.
      await user.click(await screen.findByRole('button', { name: /In PDF/ }))
      expect(await screen.findByTitle(`Tổng hợp báo cáo điều dưỡng ngày ${earlier.format('DD/MM/YYYY')}`)).toBeInTheDocument()
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary/pdf')!.search).toBe(`?date=${end}`)
      const pdf = screen.getByTitle(`Tổng hợp báo cáo điều dưỡng ngày ${earlier.format('DD/MM/YYYY')}`).closest<HTMLElement>('[role="dialog"]')!
      await user.click(within(pdf).getByRole('button', { name: 'Đóng' }))
      await user.click(within(screen.getAllByRole('table')[0]).getByRole('button', { name: 'NOI – Khoa Nội tổng hợp' }))
      expect(await screen.findByText(`Khoa Nội tổng hợp – ngày ${earlier.format('DD/MM/YYYY')}`)).toBeInTheDocument()
      expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toBe(`?date=${end}&departmentId=d-noi`)
      const drawer = (await screen.findByText(`Khoa Nội tổng hợp – ngày ${earlier.format('DD/MM/YYYY')}`)).closest<HTMLElement>('[role="dialog"]')!
      await user.click(within(drawer).getByRole('button', { name: 'Đóng' }))
      // Hộp PDF đã đóng vẫn nằm trong trang (ẩn); ngăn kéo thì được bỏ hẳn.
      await waitFor(() => expect(screen.queryByText(`Khoa Nội tổng hợp – ngày ${earlier.format('DD/MM/YYYY')}`)).not.toBeInTheDocument())

      // Thẻ Biểu đồ: cùng ngày cuối kỳ; đổi khoảng ngày chỉ tải lại dashboard.
      const summaryCalls = calls.filter((c) => c.path === '/api/v1/nursing-reports/summary').length
      await user.click(screen.getByRole('tab', { name: 'Biểu đồ' }))
      expect(await screen.findByText(`Từ ${earlier.format('DD/MM/YYYY')} đến ${earlier.format('DD/MM/YYYY')}.`)).toBeInTheDocument()
      await user.click(screen.getByText('7 ngày'))
      await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/dashboard')!.search).toBe(`?to=${end}&days=7`))
      await user.click(screen.getByRole('tab', { name: 'Bảng tổng hợp' }))
      expect(screen.getByText('A. Số liệu người bệnh nội trú')).toBeVisible()
      expect(calls.filter((c) => c.path === '/api/v1/nursing-reports/summary')).toHaveLength(summaryCalls)
    },
  )

  it('tài khoản chỉ có quyền xem: có In PDF, Xuất Excel; không có Lấy số liệu HIS, Khoa báo cáo và số giường', async () => {
    mockApi({
      ...baseRoutes(session({ ...officeSession().user, permissions: [Permissions.NursingReportsRead] })),
      'GET /api/v1/nursing-reports/dashboard': dashboardOf(TODAY),
      'GET /api/v1/nursing-reports/summary': summary,
    })
    renderApp('/dieu-duong/dashboard')

    expect(await screen.findByRole('button', { name: /Xuất Excel/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /In PDF/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Lấy số liệu HIS/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Khoa báo cáo và số giường/ })).not.toBeInTheDocument()
  })
})
