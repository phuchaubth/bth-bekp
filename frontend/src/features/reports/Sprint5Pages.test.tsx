import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const statistic = (title: string) => screen.getByText(title).closest('.ant-statistic')
import type { AuditLogEntry, Dashboard, ExportColumn, ExportJob, ReportSummary } from '../../api/reports'
import { Permissions } from '../../auth/permissions'
import { adminSession, baseRoutes, departments, session } from '../../test/fixtures'
import { json, mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật).
const dashboard: Dashboard = {
  drafts: 3,
  pendingSignature: 2,
  completedToday: 5,
  revokedLast30Days: 1,
  awaitingMyConfirmation: {
    total: 1,
    items: [
      {
        id: 'f-1',
        formNo: 'PDT-2026-000010',
        status: 'PendingSignature',
        patientName: 'Nguyễn Văn An (giả lập)',
        templateCode: 'TN-01',
        templateName: 'Phiếu đánh giá nguy cơ té ngã',
        departmentName: 'Khoa Nội tổng hợp',
        atUtc: '2026-11-12T01:00:00Z',
      },
    ],
  },
  myOpenForms: { total: 0, items: [] },
  last14Days: Array.from({ length: 14 }, (_, i) => ({ date: `2026-10-${String(30 + i).padStart(2, '0')}`, created: i, completed: Math.floor(i / 2) }))
    .map((d, i) => ({ ...d, date: new Date(Date.UTC(2026, 9, 30 + i)).toISOString().slice(0, 10) })),
}

const columns: ExportColumn[] = [
  { key: 'formNo', header: 'Số phiếu', group: 'Phiếu', default: true, sensitive: false },
  { key: 'status', header: 'Trạng thái', group: 'Phiếu', default: true, sensitive: false },
  { key: 'patientName', header: 'Họ tên người bệnh', group: 'Người bệnh', default: true, sensitive: false },
  { key: 'citizenId', header: 'Số CCCD', group: 'Người bệnh', default: false, sensitive: true },
]

const templates = [{ id: 't-ck01', code: 'CK-01', name: 'Giấy cam kết', hospitalSignerRoleCode: 'RECEPTIONIST', departmentId: null, departmentName: null, isActive: true, publishedVersionNo: 1, draftVersionNo: null, updatedAtUtc: '2026-11-01T00:00:00Z' }]

const job: ExportJob = {
  id: 'job-1',
  requestedAtUtc: '2026-11-12T02:00:00Z',
  requestedByUserId: 'u-baocao',
  requestedByName: 'Báo cáo Demo',
  format: 'Csv',
  filter: { status: 'Completed', templateId: 't-ck01', templateCode: 'CK-01', templateName: 'Giấy cam kết', departmentId: null, departmentName: null, from: '2026-11-01', to: '2026-11-12' },
  columns: ['formNo', 'status'],
  includesContent: false,
  includesSensitiveData: false,
  rowCount: 42,
  fileName: 'phieu-dong-thuan_20261112-090000.csv',
  fileSizeBytes: 5120,
  fileSha256: 'a'.repeat(64),
  durationMs: 850,
}

const report: ReportSummary = {
  from: '2026-11-01',
  to: '2026-11-12',
  totals: { total: 7, draft: 2, pendingSignature: 0, completed: 4, cancelled: 1, revoked: 0 },
  decisions: { agreed: 3, refused: 1, refusalRate: 0.25 },
  signers: { patient: 3, representative: 1 },
  completionTime: { count: 4, averageMinutes: 760, medianMinutes: 75, percentile90Minutes: 2052, upTo15Minutes: 1, upTo1Hour: 1, upTo1Day: 1, over1Day: 1 },
  byDepartment: [
    { id: 'd-kkb', code: 'KKB', name: 'Khoa Khám bệnh', counts: { total: 5, draft: 1, pendingSignature: 0, completed: 4, cancelled: 0, revoked: 0 }, decisions: { agreed: 3, refused: 1, refusalRate: 0.25 } },
    { id: 'd-noi', code: 'NOI', name: 'Khoa Nội tổng hợp', counts: { total: 2, draft: 1, pendingSignature: 0, completed: 0, cancelled: 1, revoked: 0 }, decisions: { agreed: 0, refused: 0, refusalRate: null } },
  ],
  byTemplate: [],
  daily: [
    { date: '2026-11-11', counts: { total: 7, draft: 2, pendingSignature: 0, completed: 4, cancelled: 1, revoked: 0 } },
    { date: '2026-11-12', counts: { total: 0, draft: 0, pendingSignature: 0, completed: 0, cancelled: 0, revoked: 0 } },
  ],
}

const auditEntries: AuditLogEntry[] = [
  {
    id: 'a-1',
    occurredAtUtc: '2026-11-12T02:00:00Z',
    action: 'Exports.ConsentForms',
    entityType: 'ExportJob',
    entityId: 'job-1',
    userId: 'u-baocao',
    userName: 'baocao.demo',
    ipAddress: '10.0.0.5',
    correlationId: 'corr-1',
    details: '{"rows":42,"sensitive":false}',
  },
  {
    id: 'a-2',
    occurredAtUtc: '2026-11-12T01:00:00Z',
    action: 'ConsentForms.Viewed',
    entityType: 'ConsentForm',
    entityId: 'f-1',
    userId: 'u-bacsi',
    userName: 'bacsi.demo',
    ipAddress: '10.0.0.6',
    correlationId: null,
    details: null,
  },
  {
    // Script xóa dữ liệu chạy thử chạy trên máy chủ (quyết định 52): tài khoản Windows, không gắn đối tượng.
    id: 'a-3',
    occurredAtUtc: '2026-11-12T00:30:00Z',
    action: 'Maintenance.TrialDataPurged',
    entityType: null,
    entityId: null,
    userId: null,
    userName: 'BV\\quantri',
    ipAddress: null,
    correlationId: null,
    details: '{"keepPatients":false,"restartFormNumbers":false,"deleted":[{"table":"ConsentForms","rows":12}]}',
  },
]

const reporterSession = () =>
  session({
    id: 'u-baocao',
    userName: 'baocao.demo',
    fullName: 'Báo cáo Demo',
    departmentId: null,
    departmentName: null,
    roles: [{ id: 'r-reporter', code: 'REPORTER', name: 'Báo cáo' }],
    permissions: [Permissions.ConsentsRead, Permissions.TemplatesRead, Permissions.ReportsRead, Permissions.ExportsRun, Permissions.ScopeAllDepartments],
  })

describe('Tổng quan (màn hình 2)', () => {
  it('hiển thị số liệu thật, việc chờ xác nhận và biểu đồ 14 ngày', async () => {
    mockApi({ ...baseRoutes(session()), 'GET /api/v1/dashboard': dashboard })
    renderApp('/')

    expect(await screen.findByText('Chờ tôi xác nhận (1)')).toBeInTheDocument()
    expect(statistic('Hoàn tất hôm nay')).toHaveTextContent('5')
    expect(statistic('Thu hồi 30 ngày qua')).toHaveTextContent('1')
    expect(screen.getByRole('link', { name: 'PDT-2026-000010' })).toHaveAttribute('href', '/phieu-dong-thuan/f-1')
    expect(screen.getByText('Bạn không có phiếu nháp hay chờ ký nào.')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /14 ngày gần nhất/ })).toBeInTheDocument()
  })

  // Quyết định 41: tài khoản thuộc nhiều khoa.
  it('tài khoản thuộc nhiều khoa: lời chào và đầu trang ghi các khoa, khoa chính đứng đầu', async () => {
    const departments = [
      { id: 'd-noi', name: 'Khoa Nội tổng hợp' },
      { id: 'd-kkb', name: 'Khoa Khám bệnh' },
    ]
    mockApi({ ...baseRoutes(session({ departments })), 'GET /api/v1/dashboard': dashboard })
    renderApp('/')

    expect(await screen.findByText(/Xin chào Bác sĩ Demo – Khoa Nội tổng hợp, Khoa Khám bệnh\./)).toBeInTheDocument()
    expect(screen.getByText('Khoa Nội tổng hợp và 1 khoa khác')).toHaveAttribute('title', 'Khoa Nội tổng hợp, Khoa Khám bệnh')
  })
})

describe('Xuất dữ liệu (màn hình 13)', () => {
  const objectUrls: string[] = []
  beforeEach(() => {
    URL.createObjectURL = vi.fn(() => {
      const url = `blob:test/${objectUrls.length}`
      objectUrls.push(url)
      return url
    })
    URL.revokeObjectURL = vi.fn()
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  })
  afterEach(() => {
    objectUrls.length = 0
  })

  const routes = (overrides: Record<string, unknown> = {}) => ({
    ...baseRoutes(reporterSession()),
    'GET /api/v1/exports/consent-forms/columns': columns,
    'GET /api/v1/consent-templates': templates,
    'GET /api/v1/departments': departments,
    'GET /api/v1/exports': { items: [job], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 },
    ...overrides,
  })

  it('chọn cột mặc định, gửi đúng yêu cầu và tải file về; không có quyền thì không có tuỳ chọn dữ liệu nhạy cảm', { timeout: 15_000 }, async () => {
    const { calls } = mockApi(
      routes({
        'POST /api/v1/exports/consent-forms': () =>
          new Response('a,b\r\n1,2\r\n', {
            status: 200,
            headers: {
              'Content-Type': 'text/csv; charset=utf-8',
              'Content-Disposition': "attachment; filename=phieu-dong-thuan_20261112-090000.csv; filename*=UTF-8''phieu-dong-thuan_20261112-090000.csv",
              'X-Export-Row-Count': '2',
              'X-Export-Job-Id': 'job-2',
            },
          }),
      }),
    )
    renderApp('/xuat-du-lieu')
    const user = userEvent.setup()

    expect(await screen.findByRole('checkbox', { name: /^Số phiếu$/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Số CCCD/ })).not.toBeChecked()
    expect(screen.getByText('che')).toBeInTheDocument()
    expect(screen.queryByText(/Xuất đầy đủ số CCCD/)).not.toBeInTheDocument()
    expect(screen.getByText(/được che một phần/)).toBeInTheDocument()
    // Lịch sử xuất.
    expect(screen.getByText('phieu-dong-thuan_20261112-090000.csv')).toBeInTheDocument()
    expect(screen.getByText('Hoàn tất · CK-01 · 01/11/2026 – 12/11/2026')).toBeInTheDocument()

    await user.click(screen.getByRole('checkbox', { name: /Số CCCD/ }))
    await user.click(screen.getByRole('radio', { name: 'CSV (.csv)' }))
    await user.click(screen.getByRole('button', { name: /Xuất file/ }))

    // Thông báo nổi và hộp kết quả đều ghi cùng nội dung.
    expect(await screen.findAllByText('Đã xuất 2 phiếu: phieu-dong-thuan_20261112-090000.csv')).toHaveLength(2)
    const request = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/exports/consent-forms')!
    expect(request.body).toEqual({
      format: 'Csv',
      filter: {},
      columns: ['formNo', 'status', 'patientName', 'citizenId'],
      includeContent: false,
      includeSensitiveData: false,
    })
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled()
    expect(objectUrls).toHaveLength(1)
  })

  it('hiển thị lỗi của máy chủ khi không có phiếu nào khớp bộ lọc', async () => {
    mockApi(routes({ 'POST /api/v1/exports/consent-forms': () => problem(422, 'Không có phiếu nào khớp bộ lọc.', { code: 'exports.no_rows' }) }))
    renderApp('/xuat-du-lieu')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xuất file/ }))

    expect(await screen.findByText('Không có phiếu nào khớp bộ lọc.')).toBeInTheDocument()
  })

  it('quản trị: tuỳ chọn dữ liệu nhạy cảm kèm cảnh báo, kèm nội dung chỉ khi chọn một mẫu', { timeout: 15_000 }, async () => {
    const { calls } = mockApi({
      ...routes({ 'POST /api/v1/exports/consent-forms': () => json({}, 500) }),
      ...baseRoutes(adminSession()),
    })
    renderApp('/xuat-du-lieu')
    const user = userEvent.setup()

    const content = await screen.findByRole('checkbox', { name: /Kèm nội dung phiếu theo mẫu/ })
    expect(content).toBeDisabled()
    await user.click(screen.getByRole('checkbox', { name: /Xuất đầy đủ số CCCD/ }))
    expect(await screen.findByText(/File chứa dữ liệu cá nhân đầy đủ/)).toBeInTheDocument()
    expect(screen.getByText('đầy đủ')).toBeInTheDocument()

    await user.click(screen.getByRole('combobox', { name: 'Mẫu phiếu' }))
    await user.click(await screen.findByText('CK-01 – Giấy cam kết'))
    expect(content).toBeEnabled()
    await user.click(content)
    await user.click(screen.getByRole('button', { name: /Xuất file/ }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/exports/consent-forms')).toBe(true))
    const request = calls.find((c) => c.method === 'POST' && c.path === '/api/v1/exports/consent-forms')!
    expect(request.body).toMatchObject({ filter: { templateId: 't-ck01' }, includeContent: true, includeSensitiveData: true })
    expect(screen.getByText('Lịch sử xuất (toàn hệ thống)')).toBeInTheDocument()
  })
})

describe('Báo cáo thống kê (màn hình 14)', () => {
  it('hiển thị số liệu, tỉ lệ từ chối và bảng theo khoa; đổi khoảng ngày thì gọi lại API', async () => {
    const { calls } = mockApi({
      ...baseRoutes(reporterSession()),
      'GET /api/v1/reports/summary': report,
      'GET /api/v1/consent-templates': templates,
      'GET /api/v1/departments': departments,
    })
    renderApp('/bao-cao')

    expect(await screen.findByText('Phiếu lập từ 01/11/2026 đến 12/11/2026 (theo trạng thái hiện tại).')).toBeInTheDocument()
    expect(statistic('Tổng số phiếu')).toHaveTextContent('7')
    expect(screen.getAllByText('25.0%').length).toBeGreaterThan(0)
    expect(statistic('Người đại diện ký')).toHaveTextContent('1')
    expect(statistic('Trung vị')).toHaveTextContent('75 phút')
    expect(statistic('Trung bình')).toHaveTextContent('12.7 giờ')
    const byDepartment = screen.getByText('Theo khoa/phòng').closest<HTMLElement>('.ant-card')!
    expect(within(byDepartment).getByText('Khoa Khám bệnh')).toBeInTheDocument()
    expect(within(byDepartment).getByText('—')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Số phiếu lập theo ngày/ })).toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/v1/reports/summary')).toHaveLength(1)
  })
})

describe('Báo cáo thống kê: tài khoản thuộc nhiều khoa (quyết định 41)', () => {
  it('chọn được từng khoa của mình, không có khoa khác', async () => {
    const departmentHead = (departments: { id: string; name: string }[]) =>
      session({
        id: 'u-truongkhoa',
        userName: 'truongkhoa.demo',
        fullName: 'Trưởng khoa Demo',
        departments,
        roles: [{ id: 'r-head', code: 'DEPARTMENT_HEAD', name: 'Trưởng khoa' }],
        permissions: [Permissions.ReportsRead, Permissions.ConsentsRead],
      })
    const { calls } = mockApi({
      ...baseRoutes(
        departmentHead([
          { id: 'd-noi', name: 'Khoa Nội tổng hợp' },
          { id: 'd-kkb', name: 'Khoa Khám bệnh' },
        ]),
      ),
      'GET /api/v1/reports/summary': report,
      'GET /api/v1/departments': departments,
    })
    renderApp('/bao-cao')
    const user = userEvent.setup()

    expect(await screen.findByText('Số liệu trong phạm vi các khoa của bạn.')).toBeInTheDocument()
    await user.click(screen.getByRole('combobox', { name: 'Khoa/phòng' }))
    const kkb = await screen.findByTitle('Khoa Khám bệnh')
    const dropdown = kkb.closest<HTMLElement>('.ant-select-dropdown')!
    expect(within(dropdown).getByTitle('Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(within(dropdown).queryByTitle('Khoa Cũ')).not.toBeInTheDocument()
    await user.click(kkb)

    await waitFor(() =>
      expect(calls.filter((c) => c.path === '/api/v1/reports/summary').at(-1)!.search).toContain('departmentId=d-kkb'),
    )
  })

  it('tài khoản một khoa: không có ô chọn khoa', async () => {
    mockApi({
      ...baseRoutes(
        session({
          roles: [{ id: 'r-head', code: 'DEPARTMENT_HEAD', name: 'Trưởng khoa' }],
          permissions: [Permissions.ReportsRead, Permissions.ConsentsRead],
        }),
      ),
      'GET /api/v1/reports/summary': report,
      'GET /api/v1/departments': departments,
    })
    renderApp('/bao-cao')

    expect(await screen.findByText('Số liệu trong phạm vi khoa của bạn.')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Khoa/phòng' })).not.toBeInTheDocument()
  })
})

describe('Nhật ký hệ thống (màn hình 15)', () => {
  it('liệt kê bản ghi với tên hành động tiếng Việt, lọc theo hành động và xem chi tiết', { timeout: 15_000 }, async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/audit-logs': { items: auditEntries, page: 1, pageSize: 50, totalCount: 3, totalPages: 1 },
    })
    renderApp('/nhat-ky')
    const user = userEvent.setup()

    expect(await screen.findByText('Xuất dữ liệu phiếu')).toBeInTheDocument()
    expect(screen.getByText('Xem phiếu')).toBeInTheDocument()
    expect(screen.getByText('Xóa dữ liệu chạy thử')).toBeInTheDocument()
    expect(screen.getByText('BV\\quantri')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'f-1' })).toHaveAttribute('href', '/phieu-dong-thuan/f-1')
    expect(screen.getByText('Tổng 3 bản ghi')).toBeInTheDocument()

    await user.click(screen.getAllByRole('button', { name: 'Mở rộng dòng' })[0])
    expect(await screen.findByText(/"rows": 42/)).toBeInTheDocument()

    await user.type(screen.getByRole('combobox', { name: 'Hành động' }), 'Thu hồi')
    await user.click(await screen.findByText('Thu hồi phiếu (ConsentForms.Revoked)'))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/audit-logs' && c.search.includes('action=ConsentForms.Revoked'))).toBe(true))
  })

  it('người không có quyền xem nhật ký thấy trang 403', async () => {
    mockApi(baseRoutes(session()))
    renderApp('/nhat-ky')

    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
  })
})
