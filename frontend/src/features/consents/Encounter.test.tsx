import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  ConsentFormActions,
  ConsentFormBundle,
  ConsentFormDetail,
  ConsentSignature,
  EncounterList,
} from '../../api/consentForms'
import type { TemplateDefinition } from '../../api/consentTemplates'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, session } from '../../test/fixtures'
import { mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập, không phải người thật.
const staff = () =>
  session({
    userName: 'tiepdon.demo',
    fullName: 'Tiếp đón Demo',
    roles: [{ id: 'r-rec', code: 'RECEPTIONIST', name: 'Tiếp đón' }],
    permissions: [Permissions.PatientsRead, Permissions.ConsentsRead, Permissions.ConsentsCreate, Permissions.ConsentsSign],
  })

// Đã cấu hình API thông tin người bệnh; quản trị bật/tắt gắn lượt tiếp nhận thì máy chủ trả lời ở danh sách lượt.
const withPatientSource = { his: false, smartCa: false, patientSource: true }

const switchedOff = () =>
  problem(422, 'Gắn phiếu với lượt tiếp nhận đang tắt (Quản trị → Cấu hình hệ thống).', { code: 'consent_forms.encounters_disabled' })

const definition: TemplateDefinition = {
  title: 'Giấy đồng thuận BHYT',
  sections: [{ title: 'Nội dung', blocks: [{ type: 'paragraph', text: 'Người bệnh được giải thích về quyền lợi BHYT.' }] }],
  signatures: { decision: 'none' },
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

const patientSignature: ConsentSignature = {
  role: 'Patient',
  method: 'Handwritten',
  signerName: 'Phạm Thị Dung',
  signerTitle: null,
  signedAtUtc: '2026-11-15T03:05:00Z',
}

const form = (id: string, overrides: Partial<ConsentFormDetail> = {}): ConsentFormDetail => ({
  id,
  formNo: id === 'f-1' ? 'PDT-2026-000101' : 'PDT-2026-000102',
  status: 'PendingSignature',
  templateId: 't-1',
  templateCode: 'QT-KNGT-BM02',
  templateName: id === 'f-1' ? 'Giấy đồng thuận BHYT' : 'Phiếu tư vấn cổng thông tin',
  templateVersionId: 'v-1',
  templateVersionNo: 1,
  hospitalSignerRoleCode: 'RECEPTIONIST',
  definition,
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
    patientCode: 'BN000125',
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
  signatures: [patientSignature],
  steps: [],
  signedPdfSha256: null,
  replacesForm: null,
  replacedBy: null,
  notices: [],
  actions: actions({ canConfirm: true }),
  bundleId: null,
  ...overrides,
})

const completed = (id: string): ConsentFormDetail =>
  form(id, {
    status: 'Completed',
    completedAtUtc: '2026-11-15T04:00:00Z',
    signatures: [
      patientSignature,
      { role: 'Hospital', method: 'AccountConfirmation', signerName: 'Tiếp đón Demo', signerTitle: null, signedAtUtc: '2026-11-15T04:00:00Z' },
    ],
    signedPdfSha256: 'ab'.repeat(32),
    actions: actions(),
    encounter: { encounterNo: 'TN-0002', receivedAt: '2026-11-15T10:45:00+07:00', place: 'Phòng khám 3', receivedBy: 'Người tiếp đón B' },
  })

/** Lượt tiếp nhận hôm nay của người bệnh, mới nhất trước; lượt 07:30 đã có một phiếu gắn vào. */
const today: EncounterList = {
  from: '2026-11-15',
  to: '2026-11-15',
  items: [
    { encounterNo: 'TN-0002', receivedAt: '2026-11-15T10:45:00+07:00', place: 'Phòng khám 3', receivedBy: 'Người tiếp đón B', forms: [] },
    {
      encounterNo: 'TN-0001',
      receivedAt: '2026-11-15T07:30:00+07:00',
      place: 'Phòng khám 1',
      receivedBy: 'Người tiếp đón A',
      forms: [{ id: 'f-9', formNo: 'PDT-2026-000099', templateName: 'Giấy cam kết sử dụng dịch vụ', status: 'Completed' }],
    },
  ],
}

/** jsdom không vẽ được canvas: giả lập phần vẽ và ảnh xuất ra của khung ký. */
function mockCanvas() {
  const context = { setTransform: vi.fn(), fillRect: vi.fn(), clearRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,QU5IX0NIVV9LWQ==')
  vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
    { left: 0, top: 0, right: 600, bottom: 200, width: 600, height: 200, x: 0, y: 0, toJSON: () => ({}) } as DOMRect,
  )
}

/** Nhân viên ký một nét vào khung ký trong hộp thoại. */
function staffSigns(dialog: HTMLElement) {
  const canvas = within(dialog).getByRole('img', { name: /Khung ký tên/ })
  fireEvent.pointerDown(canvas, { clientX: 40, clientY: 120, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerMove(canvas, { clientX: 160, clientY: 80, pointerId: 1, pointerType: 'mouse' })
  fireEvent.pointerUp(canvas, { clientX: 160, clientY: 80, pointerId: 1, pointerType: 'mouse' })
}

describe('Gắn phiếu với lượt tiếp nhận khi ký hoàn tất (quyết định 25)', () => {
  beforeEach(() => mockCanvas())
  afterEach(() => vi.restoreAllMocks())

  it('ký hoàn tất một phiếu: chọn một lượt tiếp nhận hôm nay (mới nhất trước) rồi mới ký được', async () => {
    let current = form('f-1')
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/features': withPatientSource,
      'GET /api/v1/consent-forms/f-1': () => current,
      'GET /api/v1/consent-forms/f-1/encounters': today,
      'POST /api/v1/consent-forms/f-1/complete': () => (current = completed('f-1')),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    const encounters = within(dialog).getByRole('region', { name: 'Lượt tiếp nhận' })
    expect(within(encounters).getByText(/Lượt tiếp nhận hôm nay/)).toBeInTheDocument()
    for (const column of ['Thời gian tiếp nhận', 'Nơi tiếp nhận', 'Các phiếu đã gắn vào']) {
      expect(within(encounters).getByRole('columnheader', { name: column })).toBeInTheDocument()
    }
    const radios = await within(encounters).findAllByRole('radio')
    expect(radios.map((radio) => radio.getAttribute('aria-label'))).toEqual([
      'Chọn lượt tiếp nhận TN-0002',
      'Chọn lượt tiếp nhận TN-0001',
    ])
    expect(within(encounters).getByText('Phòng khám 3')).toBeInTheDocument()
    expect(within(encounters).getByText('PDT-2026-000099')).toBeInTheDocument()
    expect(within(encounters).getByText('Chưa có phiếu')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/consent-forms/f-1/encounters')!.search).toBe('')

    // Đã ký nhưng chưa chọn lượt tiếp nhận: chưa hoàn tất được.
    staffSigns(dialog)
    expect(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' })).toBeDisabled()
    expect(within(dialog).getByText('Chọn lượt tiếp nhận ở trên để bật nút Ký và hoàn tất.')).toBeInTheDocument()

    // Bấm vào dòng là chọn lượt đó.
    await user.click(within(encounters).getByText('Phòng khám 3'))
    expect(within(encounters).getByRole('radio', { name: 'Chọn lượt tiếp nhận TN-0002' })).toBeChecked()
    await user.click(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' }))

    expect(await screen.findByText(/Đã xác nhận và hoàn tất phiếu, gắn vào lượt tiếp nhận TN-0002/)).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/f-1/complete')!.body).toEqual({
      image: 'QU5IX0NIVV9LWQ==',
      encounterNo: 'TN-0002',
      encounterDate: '2026-11-15',
    })
    // Trang phiếu hiện lượt tiếp nhận như chân trang PDF.
    expect(await screen.findByText(/^Số tiếp nhận: TN-0002 · .* · Phòng khám 3 · Người tiếp nhận: Người tiếp đón B$/)).toBeInTheDocument()
  })

  it('chọn ngày khác: tra từ ngày đến ngày, lượt đã chọn phải chọn lại', async () => {
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/features': withPatientSource,
      'GET /api/v1/consent-forms/f-1': form('f-1'),
      'GET /api/v1/consent-forms/f-1/encounters': (call) =>
        call.search
          ? {
              from: '2026-11-10',
              to: '2026-11-12',
              items: [
                { encounterNo: 'TN-0000', receivedAt: '2026-11-10T09:00:00+07:00', place: 'Khoa Cấp cứu', receivedBy: null, forms: [] },
              ],
            }
          : today,
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    const encounters = within(dialog).getByRole('region', { name: 'Lượt tiếp nhận' })
    await user.click(await within(encounters).findByRole('radio', { name: 'Chọn lượt tiếp nhận TN-0001' }))

    await user.click(within(encounters).getByRole('button', { name: 'Chọn ngày khác' }))
    const from = within(encounters).getByPlaceholderText('Từ ngày')
    await user.click(from)
    await user.type(from, '10/11/2026{Enter}')
    await user.type(within(encounters).getByPlaceholderText('Đến ngày'), '12/11/2026{Enter}')

    expect(await within(encounters).findByText('Khoa Cấp cứu')).toBeInTheDocument()
    expect(within(encounters).getByText(/Lượt tiếp nhận từ 10\/11\/2026 đến 12\/11\/2026/)).toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/v1/consent-forms/f-1/encounters').at(-1)!.search).toBe('?from=2026-11-10&to=2026-11-12')
    expect(within(encounters).getByRole('radio', { name: 'Chọn lượt tiếp nhận TN-0000' })).not.toBeChecked()

    // Quay lại hôm nay.
    await user.click(within(encounters).getByRole('button', { name: 'Hôm nay' }))
    expect(await within(encounters).findByText('Phòng khám 3')).toBeInTheDocument()
  })

  it('không lấy được danh sách từ HIS: báo rõ, có nút thử lại, chưa hoàn tất được', async () => {
    let down = true
    mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/features': withPatientSource,
      'GET /api/v1/consent-forms/f-1': form('f-1'),
      'GET /api/v1/consent-forms/f-1/encounters': () =>
        down
          ? problem(422, 'Không lấy được danh sách tiếp nhận từ HIS. Vui lòng thử lại sau ít phút.', {
              code: 'consent_forms.encounters_unavailable',
            })
          : today,
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    expect(await within(dialog).findByText('Không lấy được danh sách tiếp nhận từ HIS. Vui lòng thử lại sau ít phút.')).toBeInTheDocument()
    // Hướng dẫn: quản trị tạm tắt ở Cấu hình hệ thống để hoàn tất tiếp.
    expect(within(dialog).getByText(/quản trị tạm tắt “Gắn phiếu với lượt tiếp nhận”/)).toBeInTheDocument()
    staffSigns(dialog)
    expect(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' })).toBeDisabled()

    down = false
    await user.click(within(dialog).getByRole('button', { name: 'Thử lại' }))
    expect(await within(dialog).findByRole('radio', { name: 'Chọn lượt tiếp nhận TN-0002' })).toBeInTheDocument()
  })

  it('HIS lỗi, quản trị tắt gắn lượt tiếp nhận: bấm Thử lại là ký hoàn tất được ngay, không gửi lượt', async () => {
    let state: 'down' | 'off' = 'down'
    let current = form('f-1')
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/features': withPatientSource,
      'GET /api/v1/consent-forms/f-1': () => current,
      'GET /api/v1/consent-forms/f-1/encounters': () =>
        state === 'down'
          ? problem(422, 'Không lấy được danh sách tiếp nhận từ HIS. Vui lòng thử lại sau ít phút.', {
              code: 'consent_forms.encounters_unavailable',
            })
          : switchedOff(),
      'POST /api/v1/consent-forms/f-1/complete': () => (current = { ...completed('f-1'), encounter: null }),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    await within(dialog).findByRole('button', { name: 'Thử lại' })
    staffSigns(dialog)
    expect(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' })).toBeDisabled()

    // Quản trị vừa tắt ở Cấu hình hệ thống.
    state = 'off'
    await user.click(within(dialog).getByRole('button', { name: 'Thử lại' }))
    await waitFor(() => expect(within(dialog).queryByRole('region', { name: 'Lượt tiếp nhận' })).not.toBeInTheDocument())
    expect(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' })).toBeEnabled()
    await user.click(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' }))

    expect(await screen.findByText('Phiếu đã hoàn tất')).toBeInTheDocument()
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/f-1/complete')!.body).toEqual({
      image: 'QU5IX0NIVV9LWQ==',
    })
  })

  it('lượt đã chọn không còn hợp lệ trên HIS: hiện đúng lý do của máy chủ và tải lại danh sách', async () => {
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/features': withPatientSource,
      'GET /api/v1/consent-forms/f-1': form('f-1'),
      'GET /api/v1/consent-forms/f-1/encounters': today,
      'POST /api/v1/consent-forms/f-1/complete': () =>
        problem(400, 'Dữ liệu không hợp lệ.', {
          errors: { encounterNo: ['Lượt tiếp nhận đã chọn không còn trên HIS hoặc không thuộc người bệnh này; hãy tải lại danh sách.'] },
        }),
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    await user.click(await within(dialog).findByRole('radio', { name: 'Chọn lượt tiếp nhận TN-0001' }))
    staffSigns(dialog)
    await user.click(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' }))

    expect(await screen.findByText(/Lượt tiếp nhận đã chọn không còn trên HIS/)).toBeInTheDocument()
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/v1/consent-forms/f-1/encounters').length).toBe(2))
  })

  it('chưa cấu hình API thông tin người bệnh: hộp ký không có danh sách lượt tiếp nhận, không gọi tra cứu', async () => {
    const { calls } = mockApi({ ...baseRoutes(staff()), 'GET /api/v1/consent-forms/f-1': form('f-1') })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    staffSigns(dialog)

    expect(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' })).toBeEnabled()
    expect(within(dialog).queryByRole('region', { name: 'Lượt tiếp nhận' })).not.toBeInTheDocument()
    expect(calls.some((c) => c.path.endsWith('/encounters'))).toBe(false)
  })

  it('quản trị đang tắt gắn lượt tiếp nhận: hộp ký không có danh sách, ký hoàn tất như thường', async () => {
    const { calls } = mockApi({
      ...baseRoutes(staff()),
      'GET /api/v1/features': withPatientSource,
      'GET /api/v1/consent-forms/f-1': form('f-1'),
      'GET /api/v1/consent-forms/f-1/encounters': switchedOff,
    })
    renderApp('/phieu-dong-thuan/f-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Xác nhận và hoàn tất/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ký xác nhận và hoàn tất phiếu/ })
    staffSigns(dialog)

    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Ký và hoàn tất' })).toBeEnabled())
    expect(within(dialog).queryByRole('region', { name: 'Lượt tiếp nhận' })).not.toBeInTheDocument()
    expect(calls.some((c) => c.path === '/api/v1/consent-forms/f-1/encounters')).toBe(true)
  })

  describe('xác nhận cả bộ', () => {
    const bundle = (items: ConsentFormBundle['forms']): ConsentFormBundle => ({
      bundleId: 'b-1',
      patientId: 'p-1',
      patientName: 'Phạm Thị Dung',
      forms: items,
      canStartSigning: false,
      canConfirm: true,
      notices: [],
    })
    const item = (id: string, itemActions: ConsentFormActions): ConsentFormBundle['forms'][number] => ({
      id,
      formNo: form(id).formNo,
      status: 'PendingSignature',
      templateId: 't-1',
      templateCode: 'QT-KNGT-BM02',
      templateName: form(id).templateName,
      patientSigned: true,
      requiresDecision: false,
      nextStepLabel: itemActions.canConfirmStep ? 'Người tư vấn' : null,
      actions: itemActions,
    })

    it('có phiếu được hoàn tất: chọn lượt tiếp nhận của bộ, gửi kèm khi xác nhận', async () => {
      const { calls } = mockApi({
        ...baseRoutes(staff()),
        'GET /api/v1/features': withPatientSource,
        'GET /api/v1/consent-forms/f-1': form('f-1', { bundleId: 'b-1' }),
        'GET /api/v1/consent-forms/bundles/b-1': bundle([
          item('f-1', actions({ canConfirm: true })),
          item('f-2', actions({ canConfirmStep: true })),
        ]),
        'GET /api/v1/consent-forms/bundles/b-1/encounters': today,
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
      expect(within(dialog).getByText(/1 phiếu hoàn tất lần này được gắn vào lượt tiếp nhận bạn chọn/)).toBeInTheDocument()
      const encounters = within(dialog).getByRole('region', { name: 'Lượt tiếp nhận' })
      staffSigns(dialog)
      expect(within(dialog).getByRole('button', { name: 'Ký và xác nhận cả bộ' })).toBeDisabled()
      await user.click(await within(encounters).findByRole('radio', { name: 'Chọn lượt tiếp nhận TN-0001' }))
      await user.click(within(dialog).getByRole('button', { name: 'Ký và xác nhận cả bộ' }))

      expect(await screen.findByText(/Đã xác nhận 2 phiếu \(hoàn tất 1 phiếu\)/)).toBeInTheDocument()
      expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-forms/bundles/b-1/confirm')!.body).toEqual({
        image: 'QU5IX0NIVV9LWQ==',
        encounterNo: 'TN-0001',
        encounterDate: '2026-11-15',
      })
    })

    it('chỉ xác nhận bước trung gian: không cần chọn lượt tiếp nhận', async () => {
      const { calls } = mockApi({
        ...baseRoutes(staff()),
        'GET /api/v1/features': withPatientSource,
        'GET /api/v1/consent-forms/f-2': form('f-2', { bundleId: 'b-1', actions: actions({ canConfirmStep: true }) }),
        'GET /api/v1/consent-forms/bundles/b-1': bundle([item('f-2', actions({ canConfirmStep: true }))]),
      })
      renderApp('/phieu-dong-thuan/f-2')
      const user = userEvent.setup()

      await user.click(await screen.findByRole('button', { name: /Xác nhận cả bộ \(1 phiếu\)/ }))
      const dialog = await screen.findByRole('dialog', { name: 'Xác nhận cả bộ phiếu' })
      staffSigns(dialog)

      expect(within(dialog).getByRole('button', { name: 'Ký và xác nhận cả bộ' })).toBeEnabled()
      expect(within(dialog).queryByRole('region', { name: 'Lượt tiếp nhận' })).not.toBeInTheDocument()
      expect(calls.some((c) => c.path.endsWith('/encounters'))).toBe(false)
    })
  })
})
