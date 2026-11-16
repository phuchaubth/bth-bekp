import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConsentFormDetail } from '../../api/consentForms'
import type { TemplateDefinition } from '../../api/consentTemplates'
import { Permissions } from '../../auth/permissions'
import { anonymous, baseRoutes, session } from '../../test/fixtures'
import { mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

/** Mã giả lập trong QR (22 ký tự base64url như máy chủ sinh). */
const CODE = 'MaGiaLapChoTest_0123ab'
const DEVICE_KEY = 'KhoaThietBiGiaLap_0123456789abcdefABCDEFGHI'

const definition: TemplateDefinition = {
  title: 'Giấy cam kết khám chữa bệnh (giả lập)',
  sections: [
    {
      title: 'Thông tin',
      blocks: [{ type: 'text', key: 'primaryCareFacility', label: 'Nơi đăng ký khám ban đầu (Primary care facility)' }],
    },
  ],
}

const form = (overrides: Partial<ConsentFormDetail> = {}): ConsentFormDetail => ({
  id: 'f-1',
  formNo: 'PDT-2026-000101',
  status: 'PendingSignature',
  templateId: 't-ck',
  templateCode: 'CK-01',
  templateName: 'Giấy cam kết',
  templateVersionId: 'v-1',
  templateVersionNo: 1,
  hospitalSignerRoleCode: 'RECEPTIONIST',
  definition,
  data: { primaryCareFacility: 'Trạm Y tế Phường Mẫu' },
  scores: {},
  issues: {},
  patient: {
    id: 'p-1',
    fullName: 'Phạm Thị Dung (giả lập)',
    dateOfBirth: '1988-04-05',
    gender: 'Female',
    citizenId: '0361******25',
    phoneNumber: '090*****25',
    address: 'Số 9 Đường Thử Nghiệm',
    patientCode: null,
  },
  signerKind: 'Patient',
  representative: null,
  departmentId: 'd-kkb',
  departmentName: 'Khoa Khám bệnh',
  createdByUserId: 'u-1',
  createdByName: 'Tiếp đón Demo',
  createdAtUtc: '2026-11-16T01:00:00Z',
  updatedAtUtc: '2026-11-16T01:00:00Z',
  submittedAtUtc: '2026-11-16T01:05:00Z',
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

const receptionist = () =>
  session({
    userName: 'tiepdon.demo',
    fullName: 'Tiếp đón Demo',
    roles: [{ id: 'r-rec', code: 'RECEPTIONIST', name: 'Tiếp đón' }],
    permissions: [Permissions.ConsentsRead, Permissions.ConsentsCreate, Permissions.ConsentsSign],
  })

/** jsdom không vẽ được canvas: giả lập phần vẽ của khung ký. */
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

beforeEach(() => {
  mockCanvas()
  window.sessionStorage.clear()
})

afterEach(() => {
  window.history.replaceState(null, '', '/')
  window.sessionStorage.clear()
})

describe('Nhân viên tạo mã QR để người bệnh ký trên điện thoại riêng', () => {
  it('tạo mã, hiện mã QR chứa đường dẫn ký, hủy mã được', async () => {
    const { calls } = mockApi({
      ...baseRoutes(receptionist()),
      'GET /api/v1/consent-forms/f-1': form(),
      'POST /api/v1/consent-forms/f-1/remote-signing': { id: 'l-1', code: CODE, expiresAtUtc: future(), formIds: ['f-1'] },
      'GET /api/v1/remote-signing/l-1': { id: 'l-1', state: 'Waiting', expiresAtUtc: future(), openedAtUtc: null, signedAtUtc: null },
      'POST /api/v1/remote-signing/l-1/cancel': { id: 'l-1', state: 'Cancelled', expiresAtUtc: future(), openedAtUtc: null, signedAtUtc: null },
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Mã QR cho người bệnh/ }))
    const dialog = await screen.findByRole('dialog', { name: /mã QR/ })
    expect(within(dialog).getByText(/ngày sinh của người bệnh/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Tạo mã QR' }))

    expect(await within(dialog).findByText(/quét mã bằng camera điện thoại/)).toBeInTheDocument()
    // Mã QR vẽ bằng SVG (sắc nét ở mọi cỡ màn hình).
    expect(dialog.querySelector('.ant-qrcode svg')).not.toBeNull()
    // Máy nhân viên vẫn giữ phiên đăng nhập (khác chế độ ký trên máy bệnh viện).
    expect(calls.some((c) => c.path === '/api/v1/auth/logout')).toBe(false)

    await user.click(within(dialog).getByRole('button', { name: 'Hủy mã' }))
    expect(await within(dialog).findByText('Mã đã bị hủy.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Tạo mã mới' })).toBeInTheDocument()
    expect(calls.filter((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/f-1/remote-signing')).toHaveLength(1)
  })

  it('người bệnh ký xong trên điện thoại: hộp báo đã ký và tải lại phiếu', async () => {
    const { calls } = mockApi({
      ...baseRoutes(receptionist()),
      'GET /api/v1/consent-forms/f-1': form(),
      'POST /api/v1/consent-forms/f-1/remote-signing': { id: 'l-1', code: CODE, expiresAtUtc: future(), formIds: ['f-1'] },
      'GET /api/v1/remote-signing/l-1': {
        id: 'l-1',
        state: 'Signed',
        expiresAtUtc: future(),
        openedAtUtc: '2026-11-16T02:00:00Z',
        signedAtUtc: '2026-11-16T02:03:00Z',
      },
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Mã QR cho người bệnh/ }))
    const dialog = await screen.findByRole('dialog', { name: /mã QR/ })
    const formLoads = () => calls.filter((c) => c.method === 'GET' && c.path === '/api/v1/consent-forms/f-1').length
    const before = formLoads()
    await user.click(within(dialog).getByRole('button', { name: 'Tạo mã QR' }))

    expect(await within(dialog).findByText('Người bệnh đã ký xong')).toBeInTheDocument()
    await waitFor(() => expect(formLoads()).toBeGreaterThan(before))
  })
})

describe('Trang ký trên điện thoại riêng của người bệnh', () => {
  const opened = () => ({ token: 'token-phien-ky', expiresAtUtc: future(), deviceKey: DEVICE_KEY, bundleId: null, forms: [form()] })

  it('nhập ngày sinh để mở phiếu, ký bằng token phiên ký, không có nút dành cho nhân viên', async () => {
    window.history.replaceState(null, '', `/ky-tu-xa#${CODE}`)
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/remote-signing/claim': (call) =>
        (call.body as { dateOfBirth?: string }).dateOfBirth === '1988-04-05'
          ? opened()
          : problem(422, 'Ngày sinh không đúng với hồ sơ của người bệnh. Ông/bà kiểm tra lại.', {
              code: 'remote_signing.wrong_birth_date',
              remainingAttempts: 4,
            }),
      'POST /api/v1/consent-forms/f-1/patient-signature': { decision: 'Agreed', signedAtUtc: '2026-11-16T02:10:00Z' },
    })
    renderApp('/ky-tu-xa')
    const user = userEvent.setup()

    const birthDate = await screen.findByLabelText('Ngày sinh (ngày/tháng/năm)')
    // Mã được lấy khỏi thanh địa chỉ, giữ trong bộ nhớ tạm của thẻ.
    expect(window.location.hash).toBe('')
    await user.type(birthDate, '06041988')
    expect(birthDate).toHaveValue('06/04/1988')
    await user.click(screen.getByRole('button', { name: /Mở phiếu/ }))
    expect(await screen.findByText(/Còn 4 lần thử/)).toBeInTheDocument()

    await user.clear(birthDate)
    await user.type(birthDate, '05041988')
    await user.click(screen.getByRole('button', { name: /Mở phiếu/ }))

    expect(await screen.findByText('Nội dung phiếu (chỉ đọc)')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Nhân viên: thoát chế độ ký/ })).not.toBeInTheDocument()
    const claims = calls.filter((c) => c.path === '/api/v1/remote-signing/claim')
    expect(claims.map((c) => c.body)).toEqual([
      { code: CODE, dateOfBirth: '1988-04-06' },
      { code: CODE, dateOfBirth: '1988-04-05' },
    ])
    expect(claims.every((c) => !c.headers.authorization && !c.headers.Authorization)).toBe(true)
    expect(JSON.parse(window.sessionStorage.getItem('consent.remoteSigning')!)).toEqual({ code: CODE, deviceKey: DEVICE_KEY })

    await user.click(screen.getByText('Đồng ý'))
    drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận chữ ký' }))

    expect(await screen.findByText('Đã ghi nhận chữ ký')).toBeInTheDocument()
    expect(screen.getByText(/Có thể đóng trang này/)).toBeInTheDocument()
    const signature = calls.find((c) => c.path === '/api/v1/consent-forms/f-1/patient-signature')!
    expect(signature.headers.authorization ?? signature.headers.Authorization).toBe('Bearer token-phien-ky')
    expect(window.sessionStorage.getItem('consent.remoteSigning')).toBeNull()
  })

  it('tải lại trang trên điện thoại đã mở mã: dùng khóa thiết bị, không phải nhập lại ngày sinh', async () => {
    window.sessionStorage.setItem('consent.remoteSigning', JSON.stringify({ code: CODE, deviceKey: DEVICE_KEY }))
    const { calls } = mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/remote-signing/claim': { ...opened(), deviceKey: null },
    })
    renderApp('/ky-tu-xa')

    expect(await screen.findByText('Nội dung phiếu (chỉ đọc)')).toBeInTheDocument()
    expect(screen.queryByLabelText('Ngày sinh (ngày/tháng/năm)')).not.toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/remote-signing/claim')!.body).toEqual({ code: CODE, deviceKey: DEVICE_KEY })
  })

  it('mã đã hết hạn hoặc đã mở trên điện thoại khác: báo nhờ nhân viên tạo mã mới', async () => {
    window.history.replaceState(null, '', `/ky-tu-xa#${CODE}`)
    mockApi({
      ...baseRoutes(anonymous),
      'POST /api/v1/remote-signing/claim': () =>
        problem(409, 'Mã ký này đã được mở trên một điện thoại khác. Ông/bà nhờ nhân viên tạo mã mới.', {
          code: 'remote_signing.opened_elsewhere',
        }),
    })
    renderApp('/ky-tu-xa')
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('Ngày sinh (ngày/tháng/năm)'), '05041988')
    await user.click(screen.getByRole('button', { name: /Mở phiếu/ }))

    expect(await screen.findByText('Không mở được phiếu')).toBeInTheDocument()
    expect(screen.getByText(/đã được mở trên một điện thoại khác/)).toBeInTheDocument()
  })

  it('không có mã trong đường dẫn: hướng dẫn quét lại mã', async () => {
    mockApi({ ...baseRoutes(anonymous) })
    renderApp('/ky-tu-xa')

    expect(await screen.findByText(/quét lại mã QR/)).toBeInTheDocument()
  })
})
