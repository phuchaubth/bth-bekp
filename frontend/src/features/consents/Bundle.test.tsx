import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConsentFormActions, ConsentFormBundle, ConsentFormDetail } from '../../api/consentForms'
import type { TemplateDefinition } from '../../api/consentTemplates'
import type { PatientDetail } from '../../api/patients'
import { Permissions } from '../../auth/permissions'
import { anonymous, baseRoutes, departments, session } from '../../test/fixtures'
import { json, mockApi } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'
import { signingSession } from '../signing/signingSession'

// Dữ liệu giả lập, không phải người thật.
const staff = () =>
  session({
    userName: 'tiepdon.demo',
    fullName: 'Tiếp đón Demo',
    roles: [{ id: 'r-rec', code: 'RECEPTIONIST', name: 'Tiếp đón' }],
    permissions: [
      Permissions.PatientsRead,
      Permissions.ConsentsRead,
      Permissions.ConsentsCreate,
      Permissions.ConsentsSign,
      Permissions.TemplatesRead,
    ],
  })

/** Mẫu chỉ ký xác nhận (như BM02). */
const insurance: TemplateDefinition = {
  title: 'Giấy đồng thuận BHYT',
  sections: [{ title: 'Nội dung', blocks: [{ type: 'paragraph', text: 'Người bệnh được giải thích về quyền lợi BHYT.' }] }],
  signatures: {
    decision: 'none',
    statement: { label: 'Lời xác nhận của người ký', required: true, suggestions: ['Tôi đã nghe, hiểu và đồng ý'] },
  },
}

/** Mẫu có hỏi Đồng ý/Không đồng ý (như TUVAN-CONGTT). */
const portal: TemplateDefinition = {
  title: 'Phiếu tư vấn cổng thông tin',
  sections: [{ title: 'Nội dung', blocks: [{ type: 'paragraph', text: 'Bệnh viện có các cổng thông tin chính thức.' }] }],
  signatures: {
    decision: 'required',
    decisionLabel: 'Ý kiến của người bệnh',
    agreeLabel: 'Đồng ý tham gia',
    refuseLabel: 'Không đồng ý',
    statement: { required: true, suggestions: ['Tôi đã được tư vấn đầy đủ'] },
  },
}

const actions = (overrides: Partial<ConsentFormActions> = {}): ConsentFormActions => ({
  canEdit: false,
  canSubmit: false,
  canReturnToDraft: false,
  canCancel: false,
  canStartSigning: false,
  canConfirmStep: false,
  canConfirm: false,
  canRevoke: false,
  canReplace: false,
  ...overrides,
})

const form = (id: string, overrides: Partial<ConsentFormDetail> = {}): ConsentFormDetail => ({
  id,
  formNo: id === 'f-1' ? 'PDT-2026-000101' : 'PDT-2026-000102',
  status: 'PendingSignature',
  templateId: id === 'f-1' ? 't-bm02' : 't-tuvan',
  templateCode: id === 'f-1' ? 'QT-KNGT-BM02' : 'TUVAN-CONGTT',
  templateName: id === 'f-1' ? 'Giấy đồng thuận BHYT' : 'Phiếu tư vấn cổng thông tin',
  templateVersionId: 'v-1',
  templateVersionNo: 1,
  hospitalSignerRoleCode: 'RECEPTIONIST',
  definition: id === 'f-1' ? insurance : portal,
  data: {},
  scores: {},
  issues: {},
  patient: {
    id: 'p-1',
    fullName: 'Phạm Thị Dung',
    dateOfBirth: '1988-04-05',
    gender: 'Female',
    citizenId: '036188000125',
    phoneNumber: null,
    address: null,
    patientCode: null,
  },
  signerKind: 'Patient',
  representative: null,
  departmentId: 'd-kkb',
  departmentName: 'Khoa Khám bệnh',
  createdByUserId: 'u-1',
  createdByName: 'Tiếp đón Demo',
  createdAtUtc: '2026-11-15T02:00:00Z',
  updatedAtUtc: '2026-11-15T02:05:00Z',
  submittedAtUtc: '2026-11-15T02:05:00Z',
  completedAtUtc: null,
  cancelledAtUtc: null,
  cancelReason: null,
  revokedAtUtc: null,
  revokeReason: null,
  decision: null,
  signatures: [],
  steps: [],
  signedPdfSha256: null,
  replacesForm: null,
  replacedBy: null,
  notices: [],
  actions: actions({ canStartSigning: true }),
  bundleId: 'b-1',
  ...overrides,
})

const bundle = (overrides: Partial<ConsentFormBundle> = {}, items: ConsentFormBundle['forms'] = []): ConsentFormBundle => ({
  bundleId: 'b-1',
  patientId: 'p-1',
  patientName: 'Phạm Thị Dung',
  forms:
    items.length > 0
      ? items
      : (['f-1', 'f-2'] as const).map((id) => {
          const detail = form(id)
          return {
            id,
            formNo: detail.formNo,
            status: 'PendingSignature' as const,
            templateId: detail.templateId,
            templateCode: detail.templateCode,
            templateName: detail.templateName,
            patientSigned: false,
            requiresDecision: id === 'f-2',
            nextStepLabel: null,
            actions: actions({ canStartSigning: true }),
          }
        }),
  canStartSigning: true,
  canConfirm: false,
  notices: [],
  ...overrides,
})

const patient: PatientDetail = {
  id: 'p-1',
  patientCode: null,
  fullName: 'Phạm Thị Dung',
  dateOfBirth: '1988-04-05',
  gender: 'Female',
  citizenId: '036188000125',
  oldIdNumber: null,
  citizenIdIssueDate: null,
  permanentAddress: null,
  currentAddress: null,
  phoneNumber: null,
  ethnicity: null,
  occupation: null,
  nationality: 'Việt Nam',
  healthInsuranceNumber: null,
  hisVerified: false,
  hisVerifiedAtUtc: null,
  representatives: [],
  createdAtUtc: '2026-11-15T02:00:00Z',
  updatedAtUtc: '2026-11-15T02:00:00Z',
}

const future = () => new Date(Date.now() + 30 * 60_000).toISOString()

/** jsdom không vẽ được canvas: giả lập phần vẽ và ảnh xuất ra. */
function mockCanvas() {
  const context = { setTransform: vi.fn(), fillRect: vi.fn(), clearRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,QU5IX0NIVV9LWQ==')
  vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
    { left: 0, top: 0, right: 600, bottom: 200, width: 600, height: 200, x: 0, y: 0, toJSON: () => ({}) } as DOMRect,
  )
}

function drawStroke(canvas: HTMLElement) {
  fireEvent.pointerDown(canvas, { clientX: 40, clientY: 120, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerMove(canvas, { clientX: 120, clientY: 80, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerMove(canvas, { clientX: 220, clientY: 140, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerUp(canvas, { clientX: 220, clientY: 140, pointerId: 1, pointerType: 'touch' })
}

beforeEach(() => mockCanvas())

afterEach(() => signingSession.clear())

describe('Lập bộ phiếu', () => {
  const available = [
    { templateId: 't-bm02', templateCode: 'QT-KNGT-BM02', templateName: 'Giấy đồng thuận BHYT', versionId: 'v-1', versionNo: 1, status: 'Published', definition: insurance, updatedAtUtc: '', publishedAtUtc: '' },
    { templateId: 't-tuvan', templateCode: 'TUVAN-CONGTT', templateName: 'Phiếu tư vấn cổng thông tin', versionId: 'v-2', versionNo: 1, status: 'Published', definition: portal, updatedAtUtc: '', publishedAtUtc: '' },
  ]

  it('mặc định chọn hết các mẫu; bấm Lập bộ phiếu là lập cả bộ một lần rồi mở phiếu đầu', async () => {
    localStorage.clear()
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/patients/p-1': patient,
      'GET /api/v1/consent-templates/available': available,
      'GET /api/v1/departments': departments.filter((d) => d.isActive),
      'POST /api/v1/consent-forms/bundle': () => json(bundle({ canStartSigning: false }), 201),
      'GET /api/v1/consent-forms/f-1': form('f-1', { status: 'Draft', actions: actions({ canEdit: true, canSubmit: true }) }),
      'GET /api/v1/consent-forms/bundles/b-1': bundle({ canStartSigning: false }),
    })
    const { router } = renderApp('/phieu-dong-thuan/tao-moi?patientId=p-1')
    const user = userEvent.setup()

    const createBundle = await screen.findByRole('button', { name: /Lập bộ phiếu \(2 mẫu\)/ })
    await user.click(createBundle)

    await waitFor(() => expect(router.state.location.pathname).toBe('/phieu-dong-thuan/f-1'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/bundle')!.body).toMatchObject({
      patientId: 'p-1',
      templateIds: ['t-bm02', 't-tuvan'],
    })
    expect(await screen.findByText('Bộ phiếu của Phạm Thị Dung (2 phiếu)')).toBeInTheDocument()
  })

  it('bỏ chọn còn một mẫu thì không lập bộ được (vẫn lập lẻ từng phiếu được)', async () => {
    localStorage.clear()
    mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/patients/p-1': patient,
      'GET /api/v1/consent-templates/available': available,
      'GET /api/v1/departments': departments.filter((d) => d.isActive),
    })
    renderApp('/phieu-dong-thuan/tao-moi?patientId=p-1')
    const user = userEvent.setup()

    await screen.findByRole('button', { name: /Lập bộ phiếu \(2 mẫu\)/ })
    await user.click(screen.getByRole('checkbox', { name: 'Chọn mẫu TUVAN-CONGTT' }))

    expect(screen.getByRole('button', { name: /Lập bộ phiếu \(1 mẫu\)/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Lập phiếu TUVAN-CONGTT' })).toBeEnabled()
  })
})

describe('Thanh bộ phiếu trên trang phiếu', () => {
  it('liệt kê các phiếu; ký cả bộ thay cho ký lẻ; mở phiên ký cho cả bộ rồi chuyển sang màn hình ký', async () => {
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/consent-forms/f-1': form('f-1'),
      'GET /api/v1/consent-forms/f-2': form('f-2'),
      'GET /api/v1/consent-forms/bundles/b-1': bundle(),
      'POST /api/v1/consent-forms/bundles/b-1/signing-session': { token: 'token-bo-phieu', expiresAtUtc: future(), formIds: ['f-1', 'f-2'] },
    })
    const { router } = renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    const list = await screen.findByRole('list', { name: 'Các phiếu trong bộ' })
    expect(within(list).getByRole('link', { name: 'Phiếu tư vấn cổng thông tin' })).toHaveAttribute('href', '/phieu-dong-thuan/f-2')
    expect(screen.queryByRole('button', { name: /^Ký trên điện thoại/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Ký cả bộ trên điện thoại \(2 phiếu\)/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Ký cả bộ trên điện thoại' })
    await user.click(within(dialog).getByRole('button', { name: 'Bắt đầu ký' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/ky-phieu'))
    expect(await screen.findByText('Ký 2 phiếu một lần')).toBeInTheDocument()
    expect(signingSession.get()?.bundle?.forms.map((f) => f.id)).toEqual(['f-1', 'f-2'])
    expect(calls.some((c) => c.path === '/api/v1/auth/logout')).toBe(true)
  })

  it('trình ký xong một phiếu nháp thì chuyển sang phiếu nháp tiếp theo của bộ', async () => {
    const draft = (id: string) => form(id, { status: 'Draft', submittedAtUtc: null, actions: actions({ canEdit: true, canSubmit: true, canCancel: true }) })
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/consent-forms/f-1': draft('f-1'),
      'GET /api/v1/consent-forms/f-2': draft('f-2'),
      'GET /api/v1/patients/p-1': patient,
      'PUT /api/v1/consent-forms/f-1': draft('f-1'),
      'POST /api/v1/consent-forms/f-1/submit': form('f-1'),
      'GET /api/v1/consent-forms/bundles/b-1': bundle({ canStartSigning: false }, [
        { ...bundle().forms[0], status: 'Draft', actions: actions({ canEdit: true }) },
        { ...bundle().forms[1], status: 'Draft', actions: actions({ canEdit: true }) },
      ]),
    })
    const { router } = renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Trình ký/ }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/phieu-dong-thuan/f-2'))
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/f-1/submit')).toBe(true)
  })

  it('xác nhận cả bộ: một lần ký (chữ ký mẫu) cho các phiếu đến lượt mình', async () => {
    URL.createObjectURL = vi.fn(() => 'blob:chu-ky-mau')
    URL.revokeObjectURL = vi.fn()
    const signed = bundle({ canStartSigning: false, canConfirm: true }, [
      { ...bundle().forms[0], patientSigned: true, actions: actions({ canConfirm: true }) },
      { ...bundle().forms[1], patientSigned: true, nextStepLabel: 'Người tư vấn', actions: actions({ canConfirmStep: true }) },
    ])
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/consent-forms/f-1': form('f-1', { actions: actions({ canConfirm: true }) }),
      'GET /api/v1/consent-forms/bundles/b-1': signed,
      'GET /api/v1/account/signature-specimen': () => new Response('PNG-gia-lap', { headers: { 'Content-Type': 'image/png' } }),
      'POST /api/v1/consent-forms/bundles/b-1/confirm': {
        confirmed: [
          { id: 'f-1', formNo: 'PDT-2026-000101', action: 'completed', label: 'Nhân viên hướng dẫn' },
          { id: 'f-2', formNo: 'PDT-2026-000102', action: 'step', label: 'Người tư vấn' },
        ],
        failed: [],
      },
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận cả bộ \(2 phiếu\)/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Xác nhận cả bộ phiếu' })
    expect(within(dialog).getByText(/Phiếu tư vấn cổng thông tin \(PDT-2026-000102\)/)).toBeInTheDocument()
    await within(dialog).findByRole('img', { name: 'Chữ ký mẫu của bạn' })
    await user.click(within(dialog).getByRole('button', { name: 'Ký và xác nhận cả bộ' }))

    expect(await screen.findByText(/Đã xác nhận 2 phiếu \(hoàn tất 1 phiếu\)/)).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/bundles/b-1/confirm')!.body).toEqual({
      useSpecimen: true,
    })
  })
})

describe('Người bệnh ký cả bộ một lần (phương án b)', () => {
  it('chọn ý kiến ở phiếu có hỏi, ghi một lời xác nhận, ký một lần cho cả bộ bằng token phiên ký', async () => {
    signingSession.set({
      token: 'token-bo-phieu',
      expiresAtUtc: future(),
      form: form('f-1'),
      bundle: { bundleId: 'b-1', forms: [form('f-1'), form('f-2')] },
      staffUserName: 'tiepdon.demo',
    })
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/consent-forms/signing-bundle/patient-signature': {
        signedAtUtc: '2026-11-15T02:10:00Z',
        forms: [
          { id: 'f-1', formNo: 'PDT-2026-000101', decision: null },
          { id: 'f-2', formNo: 'PDT-2026-000102', decision: 'Agreed' },
        ],
      },
    })
    renderApp('/ky-phieu')
    const user = userEvent.setup()

    const confirm = await screen.findByRole('button', { name: 'Xác nhận chữ ký cho cả 2 phiếu' })
    expect(screen.getByText('1. Giấy đồng thuận BHYT')).toBeInTheDocument()
    expect(screen.getByText('2. Phiếu tư vấn cổng thông tin')).toBeInTheDocument()
    // Chỉ phiếu có hỏi mới có câu hỏi Đồng ý/Không đồng ý.
    expect(screen.getAllByRole('radiogroup')).toHaveLength(1)
    // Câu gợi ý của cả hai phiếu được gộp.
    expect(screen.getByRole('button', { name: /Tôi đã được tư vấn đầy đủ/ })).toBeInTheDocument()

    drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
    await user.click(screen.getByRole('button', { name: /Tôi đã nghe, hiểu và đồng ý/ }))
    expect(confirm).toBeDisabled()
    expect(screen.getByText(/Chọn ý kiến cho 1 phiếu còn thiếu/)).toBeInTheDocument()
    await user.click(screen.getByText('Đồng ý tham gia'))
    expect(confirm).toBeEnabled()
    await user.click(confirm)

    expect(await screen.findByText('Đã ký xong 2 phiếu')).toBeInTheDocument()
    const call = calls.find((c) => c.path === '/api/v1/consent-forms/signing-bundle/patient-signature')!
    expect(call.headers.Authorization).toBe('Bearer token-bo-phieu')
    expect(call.body).toEqual({
      image: 'QU5IX0NIVV9LWQ==',
      statement: 'Tôi đã nghe, hiểu và đồng ý',
      decisions: { 'f-2': 'Agreed' },
    })
  })
})
