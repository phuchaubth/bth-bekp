import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConsentFormDetail, ConsentFormListItem } from '../../api/consentForms'
import type { TemplateDefinition } from '../../api/consentTemplates'
import type { PatientDetail } from '../../api/patients'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, session } from '../../test/fixtures'
import { json, mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập, không phải người thật.
const nurse = () =>
  session({
    userName: 'dieuduong.demo',
    fullName: 'Điều dưỡng Demo',
    permissions: [Permissions.PatientsRead, Permissions.ConsentsRead, Permissions.ConsentsCreate, Permissions.TemplatesRead],
  })

const commitment: TemplateDefinition = {
  title: 'Giấy cam kết\nsử dụng dịch vụ',
  sections: [
    {
      title: 'II. Hình thức khám',
      blocks: [
        {
          type: 'choice',
          key: 'examType',
          label: 'Hình thức khám',
          required: true,
          options: [
            { value: 'bhyt', label: 'Khám BHYT' },
            { value: 'service', label: 'Khám dịch vụ' },
          ],
        },
        { type: 'text', key: 'insuranceNumber', label: 'Số thẻ BHYT', required: true, visibleWhen: { field: 'examType', in: ['bhyt'] } },
        { type: 'paragraph', text: 'Tôi cam kết thanh toán chi phí ngoài phạm vi BHYT.' },
        { type: 'confirm', key: 'acknowledged', label: 'Tôi đã đọc và hiểu nội dung cam kết.', required: true },
      ],
    },
  ],
}

const fallRisk: TemplateDefinition = {
  title: 'Phiếu đánh giá nguy cơ té ngã',
  sections: [
    {
      title: 'Thang điểm',
      blocks: [
        {
          type: 'choice',
          key: 'q1',
          label: '1. Tiền sử té ngã',
          options: [
            { value: 'no', label: 'Không', score: 0 },
            { value: 'yes', label: 'Có', score: 25 },
          ],
        },
        {
          type: 'choice',
          key: 'q2',
          label: '2. Đang truyền dịch',
          options: [
            { value: 'no', label: 'Không', score: 0 },
            { value: 'yes', label: 'Có', score: 20 },
          ],
        },
        {
          type: 'score',
          key: 'total',
          label: 'Tổng điểm Morse',
          sumOf: ['q1', 'q2'],
          levels: [
            { code: 'low', label: 'Nguy cơ thấp', min: 0 },
            { code: 'medium', label: 'Nguy cơ trung bình', min: 25 },
            { code: 'high', label: 'Nguy cơ cao', min: 45 },
          ],
        },
      ],
    },
  ],
}

const patient: PatientDetail = {
  id: 'p-1',
  patientCode: 'BN000125',
  fullName: 'Phạm Thị Dung',
  dateOfBirth: '1988-04-05',
  gender: 'Female',
  citizenId: '036188000125',
  oldIdNumber: null,
  citizenIdIssueDate: null,
  permanentAddress: 'Số 9 Đường Thử Nghiệm',
  currentAddress: null,
  phoneNumber: '0900000125',
  ethnicity: null,
  occupation: null,
  nationality: 'Việt Nam',
  healthInsuranceNumber: null,
  hisVerified: false,
  hisVerifiedAtUtc: null,
  representatives: [
    {
      id: 'r-1',
      fullName: 'Phạm Văn Chồng',
      relationship: 'Spouse',
      citizenId: '036085000777',
      dateOfBirth: null,
      gender: 'Male',
      phoneNumber: null,
      address: null,
    },
  ],
  createdAtUtc: '2026-11-11T02:00:00Z',
  updatedAtUtc: '2026-11-11T02:00:00Z',
}

const detail = (overrides: Partial<ConsentFormDetail> = {}): ConsentFormDetail => ({
  id: 'f-1',
  formNo: 'PDT-2026-000001',
  status: 'Draft',
  templateId: 't-ck',
  templateCode: 'CK-01',
  templateName: 'Giấy cam kết sử dụng dịch vụ khám bệnh, chữa bệnh',
  templateVersionId: 'v-1',
  templateVersionNo: 1,
  hospitalSignerRoleCode: 'RECEPTIONIST',
  definition: commitment,
  data: {},
  scores: {},
  issues: {},
  patient: {
    id: 'p-1',
    fullName: 'Phạm Thị Dung',
    dateOfBirth: '1988-04-05',
    gender: 'Female',
    citizenId: '036188000125',
    phoneNumber: '0900000125',
    address: 'Số 9 Đường Thử Nghiệm',
    patientCode: 'BN000125',
  },
  signerKind: 'Patient',
  representative: null,
  departmentId: 'd-noi',
  departmentName: 'Khoa Nội tổng hợp',
  createdByUserId: 'u-1',
  createdByName: 'Điều dưỡng Demo',
  createdAtUtc: '2026-11-11T02:00:00Z',
  updatedAtUtc: '2026-11-11T02:00:00Z',
  submittedAtUtc: null,
  completedAtUtc: null,
  cancelledAtUtc: null,
  cancelReason: null,
  revokedAtUtc: null,
  revokeReason: null,
  actions: { canEdit: true, canSubmit: true, canReturnToDraft: false, canCancel: true },
  ...overrides,
})

const listItem: ConsentFormListItem = {
  id: 'f-1',
  formNo: 'PDT-2026-000001',
  status: 'PendingSignature',
  templateId: 't-ck',
  templateCode: 'CK-01',
  templateName: 'Giấy cam kết sử dụng dịch vụ khám bệnh, chữa bệnh',
  templateVersionNo: 1,
  patientId: 'p-1',
  patientName: 'Phạm Thị Dung',
  patientCode: 'BN000125',
  departmentId: 'd-noi',
  departmentName: 'Khoa Nội tổng hợp',
  createdByName: 'Điều dưỡng Demo',
  createdAtUtc: '2026-11-11T02:00:00Z',
  updatedAtUtc: '2026-11-11T02:00:00Z',
}

const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 })

describe('Danh sách phiếu (màn hình 3)', () => {
  it('hiển thị phiếu với trạng thái, lọc theo trạng thái gửi lên máy chủ', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms': page([listItem]),
      'GET /api/v1/consent-templates': [],
    })
    renderApp('/phieu-dong-thuan')
    const user = userEvent.setup()

    expect(await screen.findByRole('link', { name: 'PDT-2026-000001' })).toHaveAttribute('href', '/phieu-dong-thuan/f-1')
    expect(screen.getByText('Chờ ký')).toBeInTheDocument()
    expect(screen.getByText('Tổng 1 phiếu')).toBeInTheDocument()

    await user.click(screen.getByRole('combobox', { name: 'Lọc theo trạng thái' }))
    await user.click(await screen.findByTitle('Hoàn tất'))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/consent-forms' && c.search.includes('status=Completed'))).toBe(true))
  })
})

describe('Lập phiếu (màn hình 4–5)', () => {
  it('từ hồ sơ người bệnh: chọn mẫu đang phát hành rồi tạo phiếu nháp', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/patients/p-1': patient,
      'GET /api/v1/consent-templates/available': [
        { templateId: 't-ck', templateCode: 'CK-01', templateName: 'Giấy cam kết', versionId: 'v-1', versionNo: 1, status: 'Published', definition: commitment, updatedAtUtc: '', publishedAtUtc: '' },
      ],
      'POST /api/v1/consent-forms': () => json(detail(), 201),
      'GET /api/v1/consent-forms/f-1': detail(),
    })
    const { router } = renderApp('/phieu-dong-thuan/tao-moi?patientId=p-1')
    const user = userEvent.setup()

    expect(await screen.findByText('Phạm Thị Dung')).toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Lập phiếu CK-01' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/phieu-dong-thuan/f-1'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms')!.body).toEqual({ patientId: 'p-1', templateId: 't-ck' })
  })
})

describe('Soạn và trình ký phiếu (màn hình 5, 8)', () => {
  it('trường chỉ hiện khi đủ điều kiện; lưu nháp gửi nội dung và người ký', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms/f-1': detail(),
      'GET /api/v1/patients/p-1': patient,
      'PUT /api/v1/consent-forms/f-1': (call) => detail({ data: (call.body as { data: Record<string, string> }).data }),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await screen.findByText('Nội dung phiếu')
    expect(screen.queryByLabelText('Số thẻ BHYT')).not.toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: 'Khám BHYT' }))
    await user.type(await screen.findByLabelText('Số thẻ BHYT'), 'GD4790000000125')
    await user.click(screen.getByRole('checkbox', { name: 'Tôi đã đọc và hiểu nội dung cam kết.' }))
    await user.click(screen.getByRole('radio', { name: 'Người đại diện ký thay' }))
    await user.click(screen.getByRole('combobox', { name: 'Người đại diện ký thay' }))
    await user.click(await screen.findByTitle('Phạm Văn Chồng – Vợ/Chồng'))
    await user.click(screen.getByRole('button', { name: /Lưu nháp/ }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT')!.body).toEqual({
      data: { examType: 'bhyt', insuranceNumber: 'GD4790000000125', acknowledged: true },
      signerKind: 'Representative',
      representativeId: 'r-1',
    })
  })

  it('trình ký thiếu thông tin: hiện lỗi của máy chủ ngay tại trường', async () => {
    mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms/f-1': detail(),
      'GET /api/v1/patients/p-1': patient,
      'PUT /api/v1/consent-forms/f-1': detail(),
      'POST /api/v1/consent-forms/f-1/submit': () =>
        problem(400, 'Phiếu chưa đủ thông tin để trình ký.', {
          code: 'consent_forms.incomplete',
          errors: { examType: ['Vui lòng chọn một phương án.'], acknowledged: ['Cần xác nhận nội dung này.'] },
        }),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Trình ký/ }))

    expect(await screen.findByText('Cần xác nhận nội dung này.')).toBeInTheDocument()
    expect(screen.getByText('Vui lòng chọn một phương án.')).toBeInTheDocument()
    expect(screen.getByText('Còn 2 mục chưa đạt yêu cầu; xem các ô được đánh dấu đỏ.')).toBeInTheDocument()

    // Sửa ô nào thì lỗi cũ của ô đó mất ngay, không chờ trình ký lại.
    await user.click(screen.getByRole('radio', { name: 'Khám dịch vụ' }))
    expect(screen.getByText('Còn 1 mục chưa đạt yêu cầu; xem các ô được đánh dấu đỏ.')).toBeInTheDocument()
    // Dòng lỗi biến mất sau hiệu ứng của antd.
    await waitFor(() => expect(screen.queryByText('Vui lòng chọn một phương án.')).not.toBeInTheDocument())
    await user.click(screen.getByRole('checkbox', { name: 'Tôi đã đọc và hiểu nội dung cam kết.' }))
    expect(screen.queryByText(/mục chưa đạt yêu cầu/)).not.toBeInTheDocument()
  })

  it('thuộc tính trống dạng null trong định nghĩa được hiểu là "không có"', async () => {
    // Như JSON máy chủ có thể trả: lựa chọn không tính điểm, bảng không giới hạn số dòng.
    const withNulls = JSON.parse(`{
      "title": "Phiếu thử", "subtitle": null, "signatures": null,
      "sections": [{ "title": null, "blocks": [
        { "type": "choice", "key": "q", "label": "Câu hỏi", "required": false, "visibleWhen": null,
          "options": [{ "value": "a", "label": "Phương án A", "score": null }, { "value": "b", "label": "Phương án B", "score": null }] },
        { "type": "repeater", "key": "people", "label": "Người được giải thích", "maxItems": null, "minItems": null, "visibleWhen": null,
          "columns": [{ "key": "name", "label": "Họ tên", "required": false, "maxLength": null, "pattern": null }] }
      ] }]
    }`) as TemplateDefinition
    mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms/f-1': detail({ definition: withNulls }),
      'GET /api/v1/patients/p-1': patient,
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    expect(await screen.findByRole('radio', { name: 'Phương án A' })).toBeInTheDocument()
    expect(screen.queryByText(/điểm/)).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Thêm dòng/ }))
    await user.click(screen.getByRole('button', { name: /Thêm dòng/ }))
    expect(screen.getByLabelText('Họ tên (dòng 2)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Thêm dòng/ })).toBeEnabled()
  })

  it('thang điểm: tổng điểm và mức nguy cơ cập nhật ngay khi chọn', async () => {
    mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms/f-1': detail({ definition: fallRisk, templateCode: 'TN-01' }),
      'GET /api/v1/patients/p-1': patient,
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    const score = await screen.findByRole('status', { name: 'Tổng điểm Morse' })
    expect(within(score).getByText('trả lời đủ các câu để tính điểm')).toBeInTheDocument()
    const [q1Yes, q2Yes] = screen.getAllByRole('radio', { name: /Có/ })
    await user.click(q1Yes)
    await user.click(screen.getAllByRole('radio', { name: /Không/ })[1])
    expect(within(score).getByText('25 điểm')).toBeInTheDocument()
    expect(within(score).getByText('Nguy cơ trung bình')).toBeInTheDocument()

    await user.click(q2Yes)
    expect(within(score).getByText('45 điểm')).toBeInTheDocument()
    expect(within(score).getByText('Nguy cơ cao')).toBeInTheDocument()
  })
})

/**
 * Khi chạy test, antd gán cùng id "test-id" cho tiêu đề mọi hộp thoại (ở trình duyệt thật id là duy nhất),
 * nên khi trang có hơn một hộp thoại thì không tra được theo tên; tìm hộp thoại theo nội dung bên trong.
 */
async function findDialogWith(text: string): Promise<HTMLElement> {
  const dialog = (await screen.findByText(text)).closest<HTMLElement>('[role="dialog"]')
  expect(dialog).not.toBeNull()
  return dialog as HTMLElement
}

describe('Phiếu chờ ký', () => {
  beforeEach(() => {
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:pdf'), revokeObjectURL: vi.fn() }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const pending = () =>
    detail({
      status: 'PendingSignature',
      data: { examType: 'service', acknowledged: true },
      submittedAtUtc: '2026-11-11T03:00:00Z',
      actions: { canEdit: false, canSubmit: false, canReturnToDraft: true, canCancel: true },
    })

  it('chỉ xem; xem PDF; hủy phải có lý do', async () => {
    let current = pending()
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms/f-1': () => current,
      'POST /api/v1/consent-forms/f-1/cancel': (call) =>
        (current = detail({
          status: 'Cancelled',
          cancelReason: (call.body as { reason: string }).reason,
          cancelledAtUtc: '2026-11-11T04:00:00Z',
          actions: { canEdit: false, canSubmit: false, canReturnToDraft: false, canCancel: false },
        })),
      'GET /api/v1/consent-forms/f-1/pdf': () => new Response('%PDF-1.7', { headers: { 'Content-Type': 'application/pdf' } }),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    expect(await screen.findByText('Phiếu đang chờ ký')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Khám dịch vụ' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Lưu nháp/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Xem PDF/ }))
    expect(await screen.findByTitle('PDF phiếu PDT-2026-000001')).toHaveAttribute('src', 'blob:pdf')
    await user.click(within(screen.getByRole('dialog', { name: 'PDF phiếu PDT-2026-000001' })).getByRole('button', { name: 'Đóng' }))

    await user.click(screen.getByRole('button', { name: /Hủy phiếu/ }))
    const dialog = await findDialogWith('Phiếu đã hủy không dùng được nữa; muốn làm lại thì lập phiếu mới.')
    expect(within(dialog).getByText('Hủy phiếu PDT-2026-000001')).toBeInTheDocument()
    const confirm = within(dialog).getByRole('button', { name: 'Hủy phiếu' })
    const reason = within(dialog).getByLabelText('Lý do hủy')
    expect(confirm).toBeDisabled()
    await user.type(reason, 'Lập')
    expect(confirm).toBeDisabled()
    await user.type(reason, ' nhầm người bệnh')
    await user.click(confirm)

    await waitFor(() => expect(calls.find((c) => c.path === '/api/v1/consent-forms/f-1/cancel')?.body).toEqual({ reason: 'Lập nhầm người bệnh' }))
    expect(await screen.findByText('Đã hủy')).toBeInTheDocument()
    expect(screen.getByText(/Lập nhầm người bệnh/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Hủy phiếu/ })).not.toBeInTheDocument()
  })

  it('trả về nháp thì soạn lại được, giữ nội dung đã nhập', async () => {
    let current = pending()
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/consent-forms/f-1': () => current,
      'GET /api/v1/patients/p-1': patient,
      'POST /api/v1/consent-forms/f-1/return-to-draft': () => (current = detail({ data: { examType: 'service', acknowledged: true } })),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Trả về nháp/ }))

    expect(await screen.findByRole('button', { name: /Lưu nháp/ })).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/f-1/return-to-draft')).toBe(true)
    expect(screen.getByRole('radio', { name: 'Khám dịch vụ' })).toBeEnabled()
    expect(screen.getByRole('radio', { name: 'Khám dịch vụ' })).toBeChecked()
    expect(screen.queryByText('Phiếu đang chờ ký')).not.toBeInTheDocument()
  })
})
