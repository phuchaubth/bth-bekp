import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { NursingReport, NursingReportDepartment, NursingReportFormDefinition, NursingSummary } from '../../api/nursingReports'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { json, mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật). Mẫu thu gọn: đủ các kiểu dòng của bản giấy.
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
    { title: 'VI. SỐ LƯỢNG NB', valueKey: null, items: [{ key: 'currentPatients', label: 'Số NB hiện tại', carryOver: false }] },
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
  reportDate: '2026-11-16',
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
  reportDate: '2026-11-16',
  columns: [
    { key: 'beds', header: 'Số giường thực kê (Không tính băng ca)' },
    { key: 'currentPatients', header: 'Số NB hiện tại' },
    { key: 'waitingAdmission', header: 'NB PK chờ nhập viện' },
  ],
  rows: [
    {
      departmentId: 'd-ngoai',
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
    { key: 'drugSupply', label: 'Tình hình cung cấp thuốc', text: 'Không ổn: Khoa Nội tổng hợp', issueDepartments: ['Khoa Nội tổng hợp'] },
  ],
  incidents: [{ departmentId: 'd-noi', departmentName: 'Khoa Nội tổng hợp', text: 'Người bệnh A (giả lập) sốt về đêm\nĐã báo bác sĩ trực' }],
  statistics: { hisAvailable: true, hasValues: true, source: 'His', updatedAtUtc: '2026-11-16T00:50:00Z', updatedBy: 'Phòng Điều dưỡng Demo' },
  submittedCount: 1,
  reportingCount: 2,
}

const departments: NursingReportDepartment[] = [
  { departmentId: 'd-kkb', code: 'KKB', name: 'Khoa Khám bệnh', isReporting: false, bedCount: null },
  { departmentId: 'd-ngoai', code: 'NGOAIB', name: 'Khoa Ngoại B (giả lập)', isReporting: true, bedCount: 25 },
  { departmentId: 'd-noi', code: 'NOI', name: 'Khoa Nội tổng hợp', isReporting: true, bedCount: 40 },
]

const lastCall = (calls: ApiCall[], method: string, path: string) =>
  calls.filter((c) => c.method === method && c.path === path).at(-1)

describe('Báo cáo điều dưỡng của khoa', () => {
  it('điều dưỡng lưu nháp với số điền sẵn, phải chọn đủ phần tick mới gửi được, gửi xong thì khóa', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const saved: NursingReport[] = []
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/nursing-reports/form': definition,
      // Như máy chủ: sau khi lưu, tải lại trả báo cáo vừa lưu.
      'GET /api/v1/nursing-reports/daily': () => saved.at(-1) ?? report(),
      'PUT /api/v1/nursing-reports/daily': (call: ApiCall) => {
        const body = call.body as { values: Record<string, number | null>; checks: Record<string, string | null>; notes: string | null; submit: boolean }
        const next = report({
          id: 'r-noi',
          status: body.submit ? 'Submitted' : 'Draft',
          values: Object.fromEntries(Object.entries(body.values).filter(([, v]) => v !== null)) as Record<string, number>,
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

    expect(await screen.findByText(/Đã điền sẵn nhân sự và chỉ tiêu/)).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Điều dưỡng' })).toHaveValue('18')
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toMatch(/^\?date=\d{4}-\d{2}-\d{2}$/)

    await user.type(screen.getByRole('spinbutton', { name: 'Số NB hiện tại' }), '47')
    await user.type(screen.getByRole('spinbutton', { name: 'Số ca' }), '0')
    await user.type(screen.getByRole('textbox', { name: /Báo cáo các vấn đề bất thường/ }), 'Người bệnh A (giả lập) sốt')
    await user.click(screen.getByRole('button', { name: /Lưu nháp/ }))

    await waitFor(() => expect(saved).toHaveLength(1))
    const draft = lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')!.body as Record<string, unknown>
    expect(draft).toMatchObject({
      values: { nurses: 18, orderlies: null, adr: 0, currentPatients: 47 },
      checks: { professional: null, security: null },
      notes: 'Người bệnh A (giả lập) sốt',
      submit: false,
    })
    expect(await screen.findByText('Đã lưu nháp.')).toBeInTheDocument()

    // Gửi khi chưa chọn đủ phần tick: không gọi máy chủ.
    await user.click(screen.getByRole('button', { name: /Gửi báo cáo/ }))
    await user.click(await screen.findByRole('button', { name: 'Gửi' }))
    expect(await screen.findByText(/cho mọi dòng của phần tick trước khi gửi/)).toBeInTheDocument()
    expect(saved).toHaveLength(1)

    for (const label of ['Vấn đề chuyên môn', 'An ninh trật tự']) {
      await user.click(within(screen.getByRole('radiogroup', { name: label })).getByRole('radio', { name: 'Không có sự cố/ Ổn' }))
    }
    await user.click(screen.getByRole('button', { name: /Gửi báo cáo/ }))
    await user.click(await screen.findByRole('button', { name: 'Gửi' }))

    await waitFor(() => expect(saved).toHaveLength(2))
    expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/daily')!.body).toMatchObject({
      checks: { professional: 'ok', security: 'ok' },
      submit: true,
    })
    expect(await screen.findByText('Báo cáo đã gửi Phòng Điều dưỡng.')).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Số NB hiện tại' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Lưu nháp/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /In PDF/ })).toBeInTheDocument()
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

  it('hiện mục A–F như bản giấy, xem báo cáo một khoa và mở lại kèm lý do', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(officeSession()),
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
    renderApp('/dieu-duong/tong-hop')

    expect(await screen.findByText('A. Số liệu người bệnh nội trú')).toBeInTheDocument()
    expect(screen.getByText(/Khoa đã gửi:/)).toHaveTextContent('Khoa đã gửi: 1/2')
    expect(screen.getByText(/Số liệu HIS lấy lúc/)).toBeInTheDocument()
    const table = screen.getAllByRole('table')[0]
    expect(within(table).getByText('Khoa Ngoại B (giả lập)')).toBeInTheDocument()
    expect(within(table).getByText('Tổng')).toBeInTheDocument()
    expect(within(table).getByText('65')).toBeInTheDocument()
    expect(screen.getByText('686')).toBeInTheDocument()
    expect(screen.getByText('Không ổn: Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(screen.getByText(/Người bệnh A \(giả lập\) sốt về đêm/)).toBeInTheDocument()

    await user.click(within(table).getByText('Khoa Nội tổng hợp'))
    const drawer = await screen.findByRole('dialog', { name: /Khoa Nội tổng hợp/ })
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/daily')!.search).toContain('departmentId=d-noi')
    expect(await within(drawer).findByRole('spinbutton', { name: 'Số NB hiện tại' })).toBeDisabled()

    await user.click(within(drawer).getByRole('button', { name: /Mở lại/ }))
    // Hộp thoại mở trên ngăn kéo: AntD trong môi trường test đặt cùng id tiêu đề, nên tìm theo chữ thay vì theo tên.
    const modal = (await screen.findByText('Mở lại báo cáo cho khoa sửa')).closest<HTMLElement>('[role="dialog"]')!
    expect(within(modal).getByRole('button', { name: 'Mở lại' })).toBeDisabled()
    await user.type(within(modal).getByRole('textbox', { name: 'Lý do mở lại' }), 'Sai số NB hiện tại')
    await user.click(within(modal).getByRole('button', { name: 'Mở lại' }))

    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/nursing-reports/r-noi/reopen')?.body).toEqual({ reason: 'Sai số NB hiện tại' }),
    )
    expect(await screen.findByText('Đã mở lại báo cáo. Khoa sửa và gửi lại.')).toBeInTheDocument()
  })

  it('xuất Excel, lấy số liệu HIS (báo mã khoa không khớp), lỗi HIS thì báo để nhập tay', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    let refreshes = 0
    const { calls } = mockApi({
      ...baseRoutes(officeSession()),
      'GET /api/v1/nursing-reports/summary': summary,
      'GET /api/v1/nursing-reports/summary/xlsx': () => new Response(new Blob(['PK']), { status: 200 }),
      'POST /api/v1/nursing-reports/statistics/refresh': () =>
        ++refreshes === 1
          ? json({ summary, unmatchedDepartmentCodes: ['KHONGCO'] })
          : problem(422, 'Không lấy được số liệu từ HIS (HIS lỗi hoặc chưa có câu truy vấn số liệu). Có thể nhập tay.', {
              code: 'nursing_reports.his_unavailable',
            }),
    })
    renderApp('/dieu-duong/tong-hop')

    await user.click(await screen.findByRole('button', { name: /Xuất Excel/ }))
    await waitFor(() => expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary/xlsx')).toBeDefined())
    expect(lastCall(calls, 'GET', '/api/v1/nursing-reports/summary/xlsx')!.search).toMatch(/^\?date=/)

    await user.click(screen.getByRole('button', { name: /Lấy số liệu HIS/ }))
    expect(await screen.findByText(/Mã khoa không khớp khoa nào: KHONGCO/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Lấy số liệu HIS/ }))
    expect(await screen.findByText(/Có thể nhập tay/)).toBeInTheDocument()
  })

  it('cài khoa báo cáo, số giường và nhập tay số liệu HIS', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(officeSession()),
      'GET /api/v1/nursing-reports/summary': summary,
      'GET /api/v1/nursing-reports/departments': departments,
      'PUT /api/v1/nursing-reports/departments': departments,
      'PUT /api/v1/nursing-reports/statistics': summary,
    })
    renderApp('/dieu-duong/tong-hop')

    await user.click(await screen.findByRole('button', { name: /Khoa báo cáo và số giường/ }))
    const modal = await screen.findByRole('dialog', { name: 'Khoa báo cáo và số giường thực kê' })
    await user.click(await within(modal).findByRole('checkbox', { name: 'Khoa Khám bệnh phải báo cáo' }))
    await user.type(within(modal).getByRole('spinbutton', { name: 'Số giường Khoa Khám bệnh' }), '10')
    await user.click(within(modal).getByRole('checkbox', { name: 'Khoa Ngoại B (giả lập) phải báo cáo' }))
    await user.click(within(modal).getByRole('button', { name: 'Lưu' }))

    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/departments')?.body).toEqual({
        items: [
          { departmentId: 'd-kkb', bedCount: 10 },
          { departmentId: 'd-noi', bedCount: 40 },
        ],
      }),
    )

    await user.click(screen.getByRole('button', { name: /Nhập số liệu HIS/ }))
    const statistics = await screen.findByRole('dialog', { name: /Nhập số liệu HIS ngày/ })
    const visits = within(statistics).getByRole('spinbutton', { name: 'TSNB đến khám' })
    await user.clear(visits)
    await user.type(visits, '700')
    await user.clear(within(statistics).getByRole('spinbutton', { name: 'TS X-quang' }))
    await user.click(within(statistics).getByRole('button', { name: 'Lưu' }))

    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/nursing-reports/statistics')?.body).toEqual({
        reportDate: '2026-11-16',
        hospital: { outpatientVisits: 700, xray: null },
        waitingAdmission: { 'd-ngoai': 1, 'd-noi': 2 },
      }),
    )
  })
})
