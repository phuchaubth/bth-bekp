import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConsentFormDetail } from '../../api/consentForms'
import type { TemplateDefinition } from '../../api/consentTemplates'
import { Permissions } from '../../auth/permissions'
import { anonymous, baseRoutes, session } from '../../test/fixtures'
import { mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'
import { signingSession } from './signingSession'

// Dữ liệu giả lập, không phải người thật.
const definition: TemplateDefinition = {
  title: 'Giấy cam kết',
  sections: [{ title: 'Nội dung', blocks: [{ type: 'confirm', key: 'acknowledged', label: 'Tôi đã đọc và hiểu nội dung cam kết.', required: true }] }],
}

const receptionist = () =>
  session({
    userName: 'tiepdon.demo',
    fullName: 'Tiếp đón Demo',
    roles: [{ id: 'r-rec', code: 'RECEPTIONIST', name: 'Tiếp đón' }],
    permissions: [Permissions.ConsentsRead, Permissions.ConsentsCreate, Permissions.ConsentsSign],
  })

const pending = (overrides: Partial<ConsentFormDetail> = {}): ConsentFormDetail => ({
  id: 'f-1',
  formNo: 'PDT-2026-000001',
  status: 'PendingSignature',
  templateId: 't-ck',
  templateCode: 'CK-01',
  templateName: 'Giấy cam kết sử dụng dịch vụ',
  templateVersionId: 'v-1',
  templateVersionNo: 1,
  hospitalSignerRoleCode: 'RECEPTIONIST',
  definition,
  data: { acknowledged: true },
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
  createdAtUtc: '2026-11-11T02:00:00Z',
  updatedAtUtc: '2026-11-11T02:05:00Z',
  submittedAtUtc: '2026-11-11T02:05:00Z',
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
  actions: {
    canEdit: false,
    canSubmit: false,
    canReturnToDraft: true,
    canCancel: true,
    canStartSigning: true,
    canConfirmStep: false,
    canConfirm: false,
    canRevoke: false,
    canReplace: false,
  },
  ...overrides,
})

const future = () => new Date(Date.now() + 30 * 60_000).toISOString()

/** jsdom không vẽ được canvas: giả lập phần vẽ và ảnh xuất ra. */
function mockCanvas() {
  const context = { setTransform: vi.fn(), fillRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,QU5IX0NIVV9LWQ==')
  vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
    { left: 0, top: 0, right: 600, bottom: 200, width: 600, height: 200, x: 0, y: 0, toJSON: () => ({}) } as DOMRect,
  )
}

/** Ký một nét bằng ngón tay (Pointer Events kiểu cảm ứng). */
function drawStroke(canvas: HTMLElement) {
  fireEvent.pointerDown(canvas, { clientX: 40, clientY: 120, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerMove(canvas, { clientX: 120, clientY: 80, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerMove(canvas, { clientX: 220, clientY: 140, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerUp(canvas, { clientX: 220, clientY: 140, pointerId: 1, pointerType: 'touch' })
}

beforeEach(() => mockCanvas())

afterEach(() => signingSession.clear())

describe('Chuyển sang chế độ ký', () => {
  it('lấy token phiên ký, đóng phiên đăng nhập của nhân viên rồi mở màn hình ký', async () => {
    const { calls } = mockApi({
      ...baseRoutes(receptionist()),
      'GET /api/v1/consent-forms/f-1': pending(),
      'POST /api/v1/consent-forms/f-1/signing-session': { token: 'token-phien-ky', expiresAtUtc: future() },
    })
    const { router } = renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Ký trên điện thoại/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Chuyển sang chế độ ký' })
    expect(within(dialog).getByText(/Phiên đăng nhập của bạn trên máy này sẽ được đóng/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Bắt đầu ký' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/ky-phieu'))
    expect(await screen.findByText('Nội dung phiếu (chỉ đọc)')).toBeInTheDocument()
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/auth/logout')).toBe(true))
    expect(signingSession.get()?.token).toBe('token-phien-ky')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()

    // Quay lại trang cũ (nút Back): vẫn bị giữ ở màn hình ký.
    await router.navigate('/nguoi-benh')
    await waitFor(() => expect(router.state.location.pathname).toBe('/ky-phieu'))
  })
})

describe('Màn hình ký trên điện thoại (màn hình 6)', () => {
  const staffLogin = (call: { body: unknown }) =>
    (call.body as { password: string }).password === 'Mat-Khau-Dung-2026'
      ? receptionist()
      : problem(401, 'Tên đăng nhập hoặc mật khẩu không đúng.', { code: 'auth.invalid_credentials' })

  it('phải chọn ý kiến và ký mới xác nhận được; gửi bằng token phiên ký; nhân viên đăng nhập lại để thoát', async () => {
    signingSession.set({ token: 'token-phien-ky', expiresAtUtc: future(), form: pending(), staffUserName: 'tiepdon.demo' })
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/consent-forms/f-1/patient-signature': { decision: 'Agreed', signedAtUtc: '2026-11-11T02:10:00Z' },
      'POST /api/v1/auth/login': staffLogin,
      'GET /api/v1/consent-forms/f-1': pending(),
    })
    const { router } = renderApp('/ky-phieu')
    const user = userEvent.setup()

    const confirm = await screen.findByRole('button', { name: 'Xác nhận chữ ký' })
    expect(screen.getByText(/Phạm Thị Dung \(người bệnh\)/)).toBeInTheDocument()
    expect(confirm).toBeDisabled()

    // Nút chọn kiểu nút bấm: ô radio thật bị ẩn, người dùng bấm vào nhãn.
    await user.click(screen.getByText('Đồng ý'))
    expect(confirm).toBeDisabled()
    drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
    expect(confirm).toBeEnabled()
    await user.click(confirm)

    expect(await screen.findByText('Đã ghi nhận chữ ký')).toBeInTheDocument()
    const signature = calls.find((c) => c.path === '/api/v1/consent-forms/f-1/patient-signature')!
    expect(signature.headers.Authorization).toBe('Bearer token-phien-ky')
    expect(signature.body).toEqual({ decision: 'Agreed', image: 'QU5IX0NIVV9LWQ==' })

    // Mật khẩu sai: vẫn ở chế độ ký.
    expect(screen.getByLabelText('Tên đăng nhập')).toHaveValue('tiepdon.demo')
    await user.type(screen.getByLabelText('Mật khẩu'), 'sai-mat-khau')
    await user.click(screen.getByRole('button', { name: /Đăng nhập và thoát chế độ ký/ }))
    expect(await screen.findByText('Tên đăng nhập hoặc mật khẩu không đúng.')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/ky-phieu')

    await user.clear(screen.getByLabelText('Mật khẩu'))
    await user.type(screen.getByLabelText('Mật khẩu'), 'Mat-Khau-Dung-2026')
    await user.click(screen.getByRole('button', { name: /Đăng nhập và thoát chế độ ký/ }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/phieu-dong-thuan/f-1'))
    expect(signingSession.get()).toBeNull()
  })

  it('"Ký lại" xóa khung ký; máy chủ từ chối chữ ký thì báo lỗi và cho ký lại', async () => {
    signingSession.set({ token: 'token-phien-ky', expiresAtUtc: future(), form: pending(), staffUserName: 'tiepdon.demo' })
    mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/consent-forms/f-1/patient-signature': () =>
        problem(422, 'Chưa có chữ ký: khung ký đang trống hoặc nét ký quá ngắn.', { code: 'consent_forms.signature_blank' }),
    })
    renderApp('/ky-phieu')
    const user = userEvent.setup()

    const confirm = await screen.findByRole('button', { name: 'Xác nhận chữ ký' })
    await user.click(screen.getByText('Từ chối'))
    const canvas = screen.getByRole('img', { name: /Khung ký tên/ })
    drawStroke(canvas)
    await user.click(screen.getByRole('button', { name: 'Ký lại' }))
    expect(confirm).toBeDisabled()

    drawStroke(canvas)
    await user.click(confirm)

    expect(await screen.findByText('Chưa có chữ ký: khung ký đang trống hoặc nét ký quá ngắn.')).toBeInTheDocument()
    // Vẫn ở màn hình ký và gửi lại được. jsdom không chạy hết hiệu ứng nên biểu tượng "đang gửi" còn trong tên nút.
    expect(screen.getByRole('button', { name: /Xác nhận chữ ký$/ })).toBeEnabled()
    expect(screen.getByRole('img', { name: /Khung ký tên/ })).toBeInTheDocument()
  })

  it('người đại diện ký: hiển thị đúng người ký và quan hệ', async () => {
    signingSession.set({
      token: 'token-phien-ky',
      expiresAtUtc: future(),
      form: pending({
        signerKind: 'Representative',
        representative: { id: 'r-1', fullName: 'Phạm Văn Chồng', relationship: 'Spouse', citizenId: '036085000777', phoneNumber: null },
      }),
      staffUserName: 'tiepdon.demo',
    })
    mockApi({ ...baseRoutes(anonymous) })
    renderApp('/ky-phieu')

    expect(await screen.findByText(/Phạm Văn Chồng \(Vợ\/Chồng\), ký thay cho người bệnh Phạm Thị Dung/)).toBeInTheDocument()
    expect(screen.getByText('Ý kiến của người đại diện')).toBeInTheDocument()
  })

  it('mẫu chỉ ký xác nhận (decision = none): không hỏi Đồng ý/Từ chối, ký là gửi được, không gửi ý kiến', async () => {
    const confirmOnly = pending({
      templateName: 'Phiếu thông tin người bệnh ngoại trú',
      definition: { ...definition, signatures: { patient: 'Người khai (Declarant)', decision: 'none' } },
    })
    signingSession.set({ token: 'token-phien-ky', expiresAtUtc: future(), form: confirmOnly, staffUserName: 'tiepdon.demo' })
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/consent-forms/f-1/patient-signature': { decision: null, signedAtUtc: '2026-11-11T02:10:00Z' },
    })
    renderApp('/ky-phieu')
    const user = userEvent.setup()

    const confirm = await screen.findByRole('button', { name: 'Xác nhận chữ ký' })
    expect(screen.getByText('Chữ ký xác nhận của người bệnh')).toBeInTheDocument()
    expect(screen.queryByRole('radiogroup')).not.toBeInTheDocument()
    expect(screen.queryByText('Đồng ý')).not.toBeInTheDocument()
    expect(screen.getByText('Ký vào khung trên để xác nhận.')).toBeInTheDocument()
    expect(confirm).toBeDisabled()

    drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
    expect(confirm).toBeEnabled()
    await user.click(confirm)

    expect(await screen.findByText('Đã ghi nhận chữ ký')).toBeInTheDocument()
    const signature = calls.find((c) => c.path === '/api/v1/consent-forms/f-1/patient-signature')!
    expect(signature.body).toEqual({ image: 'QU5IX0NIVV9LWQ==' })
  })

  it('mẫu có câu hỏi và nhãn riêng: hiển thị đúng câu hỏi, nhãn lựa chọn', async () => {
    const portal = pending({
      definition: {
        ...definition,
        signatures: {
          decisionLabel: 'Ông/bà đã xem và đồng ý tham gia sử dụng các Cổng thông tin chính thức tại bệnh viện?',
          agreeLabel: 'Đồng ý (Agree)',
          refuseLabel: 'Không đồng ý (Disagree)',
        },
      },
    })
    signingSession.set({ token: 'token-phien-ky', expiresAtUtc: future(), form: portal, staffUserName: 'tiepdon.demo' })
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/consent-forms/f-1/patient-signature': { decision: 'Refused', signedAtUtc: '2026-11-11T02:10:00Z' },
    })
    renderApp('/ky-phieu')
    const user = userEvent.setup()

    expect(
      await screen.findByRole('radiogroup', { name: /Cổng thông tin chính thức tại bệnh viện\?/ }),
    ).toBeInTheDocument()
    expect(screen.getByText('Chọn Đồng ý (Agree) hoặc Không đồng ý (Disagree) và ký vào khung trên để xác nhận.')).toBeInTheDocument()
    await user.click(screen.getByText('Không đồng ý (Disagree)'))
    drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận chữ ký' }))

    expect(await screen.findByText(/Không đồng ý \(Disagree\)\. Vui lòng đưa máy lại/)).toBeInTheDocument()
    const signature = calls.find((c) => c.path === '/api/v1/consent-forms/f-1/patient-signature')!
    expect(signature.body).toEqual({ decision: 'Refused', image: 'QU5IX0NIVV9LWQ==' })
  })

  it('điện thoại cầm dọc (màn hình hẹp): khung ký cao hơn và gợi ý xoay ngang', async () => {
    const original = window.matchMedia
    // Màn hình hẹp hơn mọi mốc của Ant Design (điện thoại cầm dọc).
    window.matchMedia = ((query: string) => ({ ...original(query), matches: false })) as typeof window.matchMedia
    try {
      signingSession.set({ token: 'token-phien-ky', expiresAtUtc: future(), form: pending(), staffUserName: 'tiepdon.demo' })
      mockApi({ ...baseRoutes(anonymous) })
      renderApp('/ky-phieu')

      const canvas = await screen.findByRole('img', { name: /Khung ký tên/ })
      expect(canvas.style.aspectRatio).toBe('2 / 1')
      expect(screen.getByText(/Có thể xoay ngang điện thoại để khung ký rộng hơn/)).toBeInTheDocument()
    } finally {
      window.matchMedia = original
    }
  })

  it('tải lại trang (mất phiên ký trong bộ nhớ): không ký được, phải đăng nhập lại', async () => {
    mockApi({ ...baseRoutes(anonymous) })
    renderApp('/ky-phieu')

    expect(await screen.findByText('Không có phiên ký đang mở')).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /Khung ký tên/ })).not.toBeInTheDocument()
  })

  it('phiên ký hết hạn: báo kết thúc, chỉ còn đăng nhập của nhân viên', async () => {
    signingSession.set({
      token: 'token-phien-ky',
      expiresAtUtc: new Date(Date.now() - 1000).toISOString(),
      form: pending(),
      staffUserName: 'tiepdon.demo',
    })
    mockApi({ ...baseRoutes(anonymous) })
    renderApp('/ky-phieu')

    expect(await screen.findByText('Phiên ký đã kết thúc')).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /Khung ký tên/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Đăng nhập và thoát chế độ ký/ })).toBeInTheDocument()
  })
})
