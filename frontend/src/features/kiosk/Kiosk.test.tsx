import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { TemplateDefinition } from '../../api/consentTemplates'
import type { KioskConfig, KioskIdentity, KioskSubmitRequest } from '../../api/kiosk'
import { Permissions } from '../../auth/permissions'
import { fakeCameras, installFakeMediaDevices } from '../../test/fakeCamera'
import { baseRoutes, session } from '../../test/fixtures'
import { mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

vi.mock('@zxing/browser', () => import('../../test/fakeCamera').then((m) => m.fakeZxing))

// Dữ liệu giả lập, không phải người thật.
const bm02: TemplateDefinition = {
  title: 'Giấy đồng thuận',
  signatures: { decision: 'none', statement: { required: true, suggestions: ['Tôi đã nghe, hiểu và đồng ý'] } },
  sections: [
    {
      blocks: [
        {
          type: 'choice',
          key: 'careScheme',
          label: 'Chế độ khám chữa bệnh (Medical care scheme)',
          required: true,
          options: [
            { value: 'insurance', label: 'Khám BHYT' },
            { value: 'nonInsurance', label: 'Khám dịch vụ' },
          ],
        },
        {
          type: 'text',
          key: 'insuranceNumber',
          label: 'Số thẻ BHYT',
          required: true,
          prefill: 'patient.healthInsuranceNumber',
          visibleWhen: { field: 'careScheme', in: ['insurance'] },
        },
      ],
    },
  ],
}

const ttnb: TemplateDefinition = {
  title: 'Phiếu thông tin người bệnh',
  signatures: { decision: 'none' },
  sections: [
    {
      blocks: [
        { type: 'text', key: 'ethnicity', label: 'Dân tộc (Ethnicity)', required: true, prefill: 'patient.ethnicity' },
        { type: 'text', key: 'permanentAddress', label: 'Địa chỉ thường trú', required: true, prefill: 'patient.permanentAddress' },
        {
          type: 'text',
          key: 'familyPhone',
          label: 'Số điện thoại người nhà',
          required: true,
          pattern: '^\\+?[0-9][0-9 .-]{7,18}$',
          patternMessage: 'Số điện thoại chưa đúng.',
        },
      ],
    },
  ],
}

const tuvan: TemplateDefinition = {
  title: 'Phiếu tư vấn cổng thông tin',
  signatures: {
    decisionLabel: 'Ông/bà đồng ý tham gia cổng thông tin?',
    agreeLabel: 'Đồng ý (Agree)',
    refuseLabel: 'Không đồng ý (Disagree)',
  },
  sections: [{ blocks: [{ type: 'paragraph', text: 'Nội dung tư vấn cổng thông tin.' }] }],
}

const config: KioskConfig = {
  departmentName: 'Khoa Khám bệnh',
  ready: true,
  templates: [
    { id: 't-bm02', code: 'QT-KNGT-BM02', name: 'Giấy đồng thuận BHYT', definition: bm02 },
    { id: 't-ttnb', code: 'TTNB-NGOAITRU', name: 'Phiếu thông tin người bệnh', definition: ttnb },
    { id: 't-tuvan', code: 'TUVAN-CONGTT', name: 'Phiếu tư vấn cổng thông tin', definition: tuvan },
  ],
  statement: { label: 'Lời xác nhận của người ký', required: true, suggestions: ['Tôi đã nghe, hiểu và đồng ý'] },
}

const QR = '079085000123|123456789|Nguyễn Văn Kiosk|12031985|Nam|Số 1 Đường Thử Nghiệm, Phường Mẫu|15062021'

const cardIdentity: KioskIdentity = {
  method: 'CitizenIdCard',
  foundRecord: false,
  fullName: 'Nguyễn Văn Kiosk',
  dateOfBirth: '1985-03-12',
  gender: 'Male',
  citizenIdMasked: '0790******23',
  patientCode: null,
  permanentAddress: 'Số 1 Đường Thử Nghiệm, Phường Mẫu',
  hasAddressOnFile: true,
  hasPhoneNumber: false,
  missingFields: ['phoneNumber'],
  prefilledSources: ['patient.permanentAddress', 'patient.currentAddress'],
}

const kioskAccount = () =>
  session({
    id: 'u-kiosk',
    userName: 'kiosk.kkb',
    fullName: 'Kiosk Khoa Khám bệnh',
    title: null,
    departmentId: 'd-kkb',
    departmentName: 'Khoa Khám bệnh',
    roles: [{ id: 'r-kiosk', code: 'KIOSK', name: 'Kiosk người bệnh' }],
    permissions: [Permissions.KioskUse],
  })

/** jsdom không vẽ được canvas: giả lập phần vẽ và ảnh xuất ra. */
function mockCanvas() {
  const context = { setTransform: vi.fn(), fillRect: vi.fn(), clearRect: vi.fn(), beginPath: vi.fn(), moveTo: vi.fn(), lineTo: vi.fn(), stroke: vi.fn() }
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,S0lPU0tfS1k=')
  vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
    { left: 0, top: 0, right: 600, bottom: 240, width: 600, height: 240, x: 0, y: 0, toJSON: () => ({}) } as DOMRect,
  )
}

function drawStroke(canvas: HTMLElement) {
  fireEvent.pointerDown(canvas, { clientX: 40, clientY: 120, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerMove(canvas, { clientX: 120, clientY: 80, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerMove(canvas, { clientX: 220, clientY: 140, pointerId: 1, pointerType: 'touch' })
  fireEvent.pointerUp(canvas, { clientX: 220, clientY: 140, pointerId: 1, pointerType: 'touch' })
}

const pressKeys = async (user: ReturnType<typeof userEvent.setup>, keys: string) => {
  const pad = screen.getByRole('group', { name: 'Bàn phím số' })
  for (const key of keys) {
    await user.click(within(pad).getByRole('button', { name: key }))
  }
}

beforeEach(() => mockCanvas())

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('Kiosk người bệnh tự làm phiếu', () => {
  it('tài khoản kiosk đăng nhập vào thẳng màn hình kiosk; nhân viên không mở được kiosk', async () => {
    mockApi({ ...baseRoutes(kioskAccount()), 'GET /api/v1/kiosk/config': config })
    const { router } = renderApp('/phieu-dong-thuan')

    expect(await screen.findByRole('heading', { name: /Xin chào/ })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/kiosk')
    expect(screen.getByRole('button', { name: /Quét CCCD/ })).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('nhân viên thường mở trang kiosk thì bị chặn', async () => {
    mockApi({ ...baseRoutes(session()), 'GET /api/v1/kiosk/config': config })
    renderApp('/kiosk')
    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
  })

  it('quét CCCD → xác nhận, thêm số điện thoại → trả lời từng câu → ký một lần cho cả bộ → hoàn tất', async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(kioskAccount()),
      'GET /api/v1/kiosk/config': config,
      'POST /api/v1/kiosk/identify': cardIdentity,
      'POST /api/v1/kiosk/submit': {
        patientName: 'Nguyễn Văn Kiosk',
        patientCode: null,
        bundleId: 'b-1',
        forms: [
          { id: 'f-1', formNo: 'PDT-2026-000201', templateName: 'Giấy đồng thuận BHYT', decision: null },
          { id: 'f-2', formNo: 'PDT-2026-000202', templateName: 'Phiếu thông tin người bệnh', decision: null },
          { id: 'f-3', formNo: 'PDT-2026-000203', templateName: 'Phiếu tư vấn cổng thông tin', decision: 'Agreed' },
        ],
        signedAtUtc: '2026-11-15T02:00:00Z',
      },
    })
    renderApp('/kiosk')

    await user.click(await screen.findByRole('button', { name: /Quét CCCD/ }))
    expect(screen.getByText('Đang chờ quét thẻ…')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Mã QR trên CCCD (máy quét tự nhập)'), `${QR}{Enter}`)

    expect(await screen.findByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).toBeInTheDocument()
    const identify = calls.find((c) => c.path === '/api/v1/kiosk/identify')!
    expect(identify.body).toMatchObject({
      method: 'CitizenIdCard',
      dateOfBirth: '1985-03-12',
      citizenId: '079085000123',
      card: { fullName: 'Nguyễn Văn Kiosk', gender: 'Male', issueDate: '2021-06-15', oldIdNumber: '123456789' },
    })
    expect(screen.getByText('NGUYỄN VĂN KIOSK')).toBeInTheDocument()
    expect(screen.getByText('0790******23')).toBeInTheDocument()

    // Hồ sơ chưa có số điện thoại: bắt buộc nhập trước khi tiếp tục.
    await user.click(screen.getByRole('button', { name: 'Đúng, tiếp tục' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng nhập số điện thoại')
    await pressKeys(user, '0900000555')
    await user.click(screen.getByRole('button', { name: 'Đúng, tiếp tục' }))

    // Câu 1: chọn bằng nút lớn; số thẻ BHYT bị ẩn khi khám dịch vụ.
    expect(await screen.findByRole('heading', { name: /Chế độ khám chữa bệnh/ })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng chọn một câu trả lời.')
    await user.click(screen.getByRole('radio', { name: /Khám dịch vụ/ }))
    await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))

    // Câu 2: dân tộc gõ bằng bàn phím tiếng Việt (địa chỉ đã có từ thẻ nên không hỏi).
    expect(await screen.findByRole('heading', { name: /Dân tộc/ })).toBeInTheDocument()
    const keyboard = screen.getByRole('group', { name: 'Bàn phím tiếng Việt' })
    for (const key of ['k', 'i', 'n', 'h']) {
      await user.click(within(keyboard).getByRole('button', { name: key }))
    }
    expect(screen.getByRole('textbox', { name: /Dân tộc/ })).toHaveValue('kinh')
    await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))

    // Câu 3: trường chỉ nhận số dùng bàn phím số.
    expect(await screen.findByRole('heading', { name: /Số điện thoại người nhà/ })).toBeInTheDocument()
    await pressKeys(user, '0900000111')
    await user.click(screen.getAllByRole('button', { name: 'Tiếp tục' })[0])

    // Câu cuối: Đồng ý/Không đồng ý của phiếu tư vấn.
    expect(await screen.findByRole('heading', { name: /cổng thông tin/ })).toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: /^Đồng ý/ }))
    await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))

    // Ký một lần cho cả bộ.
    expect(await screen.findByRole('heading', { name: 'Ký tên cho các phiếu' })).toBeInTheDocument()
    const sign = screen.getByRole('button', { name: 'Ký xác nhận cho cả 3 phiếu' })
    expect(sign).toBeDisabled()
    drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
    await user.click(sign)

    expect(await screen.findByRole('heading', { name: 'Ông/bà đã ký xong 3 phiếu' })).toBeInTheDocument()
    expect(screen.getByText('PDT-2026-000201 → 000203')).toBeInTheDocument()
    const submit = calls.find((c) => c.path === '/api/v1/kiosk/submit')!.body as KioskSubmitRequest
    expect(submit.identity).toEqual(identify.body)
    expect(submit.phoneNumber).toBe('0900000555')
    expect(submit.statement).toBe('Tôi đã nghe, hiểu và đồng ý')
    expect(submit.image).toBe('S0lPU0tfS1k=')
    expect(submit.forms).toEqual([
      { templateId: 't-bm02', data: { careScheme: 'nonInsurance' }, decision: null },
      { templateId: 't-ttnb', data: { ethnicity: 'kinh', familyPhone: '0900000111' }, decision: null },
      { templateId: 't-tuvan', data: {}, decision: 'Agreed' },
    ])
  })

  describe('phiếu lần trước (quyết định 27)', () => {
    // Hồ sơ có sẵn, đã làm BM02 và TTNB gần đây: máy chủ chỉ báo tên các câu đã trả lời, không gửi câu trả lời.
    const returning: KioskIdentity = {
      ...cardIdentity,
      foundRecord: true,
      hasPhoneNumber: true,
      missingFields: [],
      inherited: [
        { templateId: 't-bm02', keys: ['careScheme'] },
        { templateId: 't-ttnb', keys: ['ethnicity', 'familyPhone'] },
      ],
    }
    const submitted = {
      patientName: 'Nguyễn Văn Kiosk',
      patientCode: 'BN000125',
      bundleId: 'b-1',
      forms: [
        { id: 'f-1', formNo: 'PDT-2026-000301', templateName: 'Giấy đồng thuận BHYT', decision: null },
        { id: 'f-2', formNo: 'PDT-2026-000302', templateName: 'Phiếu thông tin người bệnh', decision: null },
        { id: 'f-3', formNo: 'PDT-2026-000303', templateName: 'Phiếu tư vấn cổng thông tin', decision: 'Agreed' },
      ],
      signedAtUtc: '2026-11-15T02:00:00Z',
    }

    const identifyByCard = async (user: ReturnType<typeof userEvent.setup>) => {
      await user.click(await screen.findByRole('button', { name: /Quét CCCD/ }))
      await user.type(screen.getByLabelText('Mã QR trên CCCD (máy quét tự nhập)'), `${QR}{Enter}`)
      await user.click(await screen.findByRole('button', { name: 'Đúng, tiếp tục' }))
    }

    it('hỏi một câu "giữ như lần trước", không hiện câu trả lời cũ; giữ thì chỉ còn câu Đồng ý và ký', async () => {
      const user = userEvent.setup()
      const { calls } = mockApi({
        ...baseRoutes(kioskAccount()),
        'GET /api/v1/kiosk/config': config,
        'POST /api/v1/kiosk/identify': returning,
        'POST /api/v1/kiosk/submit': submitted,
      })
      renderApp('/kiosk')

      await identifyByCard(user)
      expect(await screen.findByRole('heading', { name: /có thay đổi so với lần trước/ })).toBeInTheDocument()
      expect(screen.queryByText(/Khám dịch vụ|Khám BHYT/)).not.toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: /Không thay đổi/ }))

      expect(await screen.findByRole('heading', { name: /cổng thông tin/ })).toBeInTheDocument()
      await user.click(screen.getByRole('radio', { name: /^Đồng ý/ }))
      await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))

      expect(await screen.findByRole('heading', { name: 'Ký tên cho các phiếu' })).toBeInTheDocument()
      expect(screen.getAllByText('✓ Giữ như lần trước')).toHaveLength(2)
      drawStroke(screen.getByRole('img', { name: /Khung ký tên/ }))
      await user.click(screen.getByRole('button', { name: 'Ký xác nhận cho cả 3 phiếu' }))

      expect(await screen.findByRole('heading', { name: 'Ông/bà đã ký xong 3 phiếu' })).toBeInTheDocument()
      const submit = calls.find((c) => c.path === '/api/v1/kiosk/submit')!.body as KioskSubmitRequest
      expect(submit.keepPrevious).toBe(true)
      expect(submit.forms).toEqual([
        { templateId: 't-bm02', data: {}, decision: null },
        { templateId: 't-ttnb', data: {}, decision: null },
        { templateId: 't-tuvan', data: {}, decision: 'Agreed' },
      ])
    })

    it('"có thay đổi" thì hỏi lại đủ các câu; quay lại từ câu đầu về câu hỏi giữ như lần trước', async () => {
      const user = userEvent.setup()
      mockApi({ ...baseRoutes(kioskAccount()), 'GET /api/v1/kiosk/config': config, 'POST /api/v1/kiosk/identify': returning })
      renderApp('/kiosk')

      await identifyByCard(user)
      await user.click(await screen.findByRole('button', { name: /Có thay đổi/ }))

      expect(await screen.findByRole('heading', { name: /Chế độ khám chữa bệnh/ })).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: 'Quay lại' }))
      expect(await screen.findByRole('heading', { name: /có thay đổi so với lần trước/ })).toBeInTheDocument()
    })

    it('không có phiếu lần trước thì không hỏi, vào thẳng câu đầu', async () => {
      const user = userEvent.setup()
      mockApi({
        ...baseRoutes(kioskAccount()),
        'GET /api/v1/kiosk/config': config,
        'POST /api/v1/kiosk/identify': { ...returning, inherited: [] },
      })
      renderApp('/kiosk')

      await identifyByCard(user)
      expect(await screen.findByRole('heading', { name: /Chế độ khám chữa bệnh/ })).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: /có thay đổi so với lần trước/ })).not.toBeInTheDocument()
    })
  })

  it('nhập mã sai ngày sinh: báo chung và cho biết còn bao nhiêu lần thử', async () => {
    const user = userEvent.setup()
    mockApi({
      ...baseRoutes(kioskAccount()),
      'GET /api/v1/kiosk/config': config,
      'POST /api/v1/kiosk/identify': () =>
        problem(422, 'Không tìm thấy hồ sơ khớp với mã và ngày sinh đã nhập.', { code: 'kiosk.lookup_failed', remainingAttempts: 4 }),
    })
    renderApp('/kiosk')

    await user.click(await screen.findByRole('button', { name: /Nhập mã y tế hoặc số CCCD/ }))
    await pressKeys(user, '079085000123')
    await user.click(screen.getByRole('button', { name: 'Ô tiếp' }))
    await pressKeys(user, '12031985')
    expect(screen.getByLabelText(/Ngày sinh/)).toHaveValue('12/03/1985')
    await user.click(screen.getByRole('button', { name: 'Lấy thông tin' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Còn 4 lần thử.')
  })

  it('mã y tế chỉ nhập được chữ số: bàn phím số, chữ gõ từ bàn phím rời bị bỏ qua', async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(kioskAccount()),
      'GET /api/v1/kiosk/config': config,
      'POST /api/v1/kiosk/identify': { ...cardIdentity, method: 'LookupCode', foundRecord: true, patientCode: '20240015' },
    })
    renderApp('/kiosk')

    await user.click(await screen.findByRole('button', { name: /Nhập mã y tế hoặc số CCCD/ }))
    await user.click(screen.getByRole('radio', { name: /Mã y tế/ }))
    expect(screen.queryByRole('group', { name: 'Bàn phím nhập mã' })).not.toBeInTheDocument()
    const input = screen.getByLabelText('Mã y tế')
    await user.type(input, 'BN-2024')
    expect(input).toHaveValue('2024')
    await pressKeys(user, '0015')
    expect(input).toHaveValue('20240015')
    await user.click(screen.getByRole('button', { name: 'Ô tiếp' }))
    await pressKeys(user, '12031985')
    await user.click(screen.getByRole('button', { name: 'Lấy thông tin' }))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/kiosk/identify')).toBe(true))
    expect(calls.find((c) => c.path === '/api/v1/kiosk/identify')!.body).toEqual({
      method: 'LookupCode',
      dateOfBirth: '1985-03-12',
      patientCode: '20240015',
      citizenId: null,
    })
  })

  it('60 giây không ai chạm thì xóa thông tin và về trang đầu', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    mockApi({ ...baseRoutes(kioskAccount()), 'GET /api/v1/kiosk/config': config })
    renderApp('/kiosk')

    await user.click(await screen.findByRole('button', { name: /Nhập thông tin/ }))
    expect(screen.getByRole('heading', { name: 'Nhập thông tin của ông/bà' })).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(52_000)
    })
    expect(await screen.findByText(/tự về trang đầu sau/)).toBeInTheDocument()
    act(() => {
      vi.advanceTimersByTime(9_000)
    })
    await waitFor(() => expect(screen.getByRole('heading', { name: /Xin chào/ })).toBeInTheDocument())
  })
})

describe('Kiosk quét CCCD bằng camera', () => {
  const CAMERA_HEADING = 'Đưa mã QR trên thẻ căn cước vào trước camera'
  const SCANNER_HEADING = 'Đưa mã QR trên thẻ căn cước vào máy quét'
  let removeCamera: (() => void) | undefined

  afterEach(() => {
    removeCamera?.()
    removeCamera = undefined
  })

  const openScan = async (identify: object | (() => Response) = cardIdentity) => {
    const user = userEvent.setup()
    const api = mockApi({ ...baseRoutes(kioskAccount()), 'GET /api/v1/kiosk/config': config, 'POST /api/v1/kiosk/identify': identify })
    renderApp('/kiosk')
    await user.click(await screen.findByRole('button', { name: /Quét CCCD/ }))
    return { user, identifyCalls: () => api.calls.filter((c) => c.path === '/api/v1/kiosk/identify') }
  }

  it('kiosk có camera: hiện khung hình camera, đọc mã trên thẻ → lấy thông tin, sang bước xác nhận, tắt camera', async () => {
    removeCamera = installFakeMediaDevices()
    const { identifyCalls } = await openScan()

    expect(await screen.findByRole('heading', { name: CAMERA_HEADING })).toBeInTheDocument()
    await waitFor(() => expect(fakeCameras).toHaveLength(1))
    const [camera] = fakeCameras
    expect(camera.constraints).toMatchObject({ video: { facingMode: 'user' } })
    expect(screen.getByRole('status')).toHaveTextContent('Đang mở camera…')
    expect(screen.getByText(/không lưu và không gửi đi/)).toBeInTheDocument()

    await act(async () => camera.start())
    expect(screen.getByRole('status')).toHaveTextContent('Đang chờ quét thẻ…')
    // Camera hướng về người bệnh, hình lật như soi gương.
    expect(camera.video).toHaveStyle({ transform: 'scaleX(-1)' })

    // Mã QR không phải của CCCD: nhắc đưa lại, camera vẫn đọc tiếp.
    act(() => camera.read('https://vi-du.test/khong-phai-cccd'))
    expect(screen.getByRole('alert')).toHaveTextContent('Chưa đọc được mã QR trên thẻ căn cước')

    act(() => {
      camera.read(QR)
      camera.read(QR)
    })
    expect(await screen.findByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).toBeInTheDocument()
    expect(identifyCalls()).toHaveLength(1)
    expect(identifyCalls()[0].body).toMatchObject({ method: 'CitizenIdCard', dateOfBirth: '1985-03-12', citizenId: '079085000123' })
    // Camera tắt khi React dọn khung hình cũ, việc này chạy sau khi màn hình mới hiện.
    await waitFor(() => expect(camera.stop).toHaveBeenCalled())
  })

  it('thẻ còn trong khung hình thì không gửi lại; đưa thẻ ra rồi đưa lại mới thử lại (tránh tự tính nhiều lần sai)', async () => {
    removeCamera = installFakeMediaDevices()
    const now = vi.spyOn(Date, 'now')
    const start = Date.now()
    const { identifyCalls } = await openScan(() =>
      problem(422, 'Hồ sơ cần nhân viên kiểm tra. Mời ông/bà đến quầy tiếp đón.', { code: 'kiosk.needs_staff' }),
    )
    await waitFor(() => expect(fakeCameras).toHaveLength(1))
    const [camera] = fakeCameras
    await act(async () => camera.start())

    now.mockReturnValue(start)
    act(() => camera.read(QR))
    expect(await screen.findByRole('alert')).toHaveTextContent('Hồ sơ cần nhân viên kiểm tra')

    // Thẻ vẫn trong khung hình: camera đọc lại liên tục nhưng không gửi lại.
    for (const elapsed of [500, 1000, 1500, 2000, 2500]) {
      now.mockReturnValue(start + elapsed)
      act(() => camera.read(QR))
    }
    expect(identifyCalls()).toHaveLength(1)

    // Thẻ rời khung hình hơn 2 giây rồi đưa lại: thử lại.
    now.mockReturnValue(start + 5000)
    act(() => camera.read(QR))
    await waitFor(() => expect(identifyCalls()).toHaveLength(2))
  })

  it('máy quét 2D vẫn dùng được khi camera đang bật', async () => {
    removeCamera = installFakeMediaDevices()
    const { user } = await openScan()
    await waitFor(() => expect(fakeCameras).toHaveLength(1))
    await act(async () => fakeCameras[0].start())

    await user.type(screen.getByLabelText('Mã QR trên CCCD (máy quét tự nhập)'), `${QR}{Enter}`)

    expect(await screen.findByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).toBeInTheDocument()
    await waitFor(() => expect(fakeCameras[0].stop).toHaveBeenCalled())
  })

  it.each([
    ['sửa lại họ tên và địa chỉ', false, 'Số 1 Đường Thử Nghiệm, Phường Mẫu'],
    ['địa chỉ không sửa được thì không gửi', true, null],
  ])('máy quét gõ chữ có dấu thành ký tự lạ (Windows-1252): %s', async (_, dropUnassigned, permanentAddress) => {
    removeCamera = installFakeMediaDevices(['audioinput'])
    const { user, identifyCalls } = await openScan()
    // Máy quét chưa cài đọc tiếng Việt gõ từng byte UTF-8 thành một ký tự Windows-1252; có máy bỏ mất byte chưa gán ký tự.
    const decoder = new TextDecoder('windows-1252')
    const typed = [...new TextEncoder().encode(QR)]
      .map((byte) => decoder.decode(Uint8Array.of(byte)))
      .filter((char) => !dropUnassigned || !/[\u0080-\u009f]/.test(char))
      .join('')
    expect(typed).toContain('Nguyá»…n')

    await user.type(screen.getByLabelText('Mã QR trên CCCD (máy quét tự nhập)'), `${typed}{Enter}`)

    expect(await screen.findByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).toBeInTheDocument()
    expect(identifyCalls()[0].body).toMatchObject({
      citizenId: '079085000123',
      card: { fullName: 'Nguyễn Văn Kiosk', permanentAddress },
    })
  })

  it.each([
    ['bệnh viện đã có hồ sơ: dùng họ tên trong hồ sơ, không gửi địa chỉ hỏng', true],
    ['chưa có hồ sơ: không lập hồ sơ bằng họ tên hỏng, mời nhập thông tin', false],
  ])('máy quét bỏ hẳn chữ có dấu: %s', async (_, foundRecord) => {
    removeCamera = installFakeMediaDevices(['audioinput'])
    const { user, identifyCalls } = await openScan({ ...cardIdentity, foundRecord })
    // Máy quét không gõ được ký tự ngoài bảng chữ ASCII: "Nguyễn" thành "Nguyn", "Đường" thành "ng".
    const typed = QR.replace(/[^\u0020-\u007e]/g, '')

    await user.type(screen.getByLabelText('Mã QR trên CCCD (máy quét tự nhập)'), `${typed}{Enter}`)

    await waitFor(() => expect(identifyCalls()).toHaveLength(1))
    expect(identifyCalls()[0].body).toMatchObject({ citizenId: '079085000123', card: { permanentAddress: null } })
    if (foundRecord) {
      expect(await screen.findByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).toBeInTheDocument()
    } else {
      expect(await screen.findByRole('alert')).toHaveTextContent('Máy chưa đọc đúng họ tên có dấu trên thẻ')
      expect(screen.queryByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).not.toBeInTheDocument()
    }
  })

  it('kiosk không có camera: chỉ dùng máy quét như trước, không bật camera', async () => {
    removeCamera = installFakeMediaDevices(['audioinput'])
    await openScan()

    expect(await screen.findByRole('heading', { name: SCANNER_HEADING })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Đang chờ quét thẻ…')
    expect(screen.queryByText(/Camera của kiosk chưa dùng được/)).not.toBeInTheDocument()
    expect(fakeCameras).toHaveLength(0)
  })

  it('trình duyệt của kiosk chặn camera (chủ ý chỉ dùng máy quét dù máy có webcam): màn hình máy quét, không bật camera, không báo lỗi', async () => {
    removeCamera = installFakeMediaDevices(['videoinput'], 'denied')
    await openScan()

    expect(await screen.findByRole('heading', { name: SCANNER_HEADING })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Đang chờ quét thẻ…')
    expect(screen.queryByText(/Camera của kiosk chưa dùng được/)).not.toBeInTheDocument()
    expect(fakeCameras).toHaveLength(0)
  })

  it('camera không mở được: về màn hình máy quét, ghi lý do để bộ phận CNTT xử lý; máy quét vẫn dùng được', async () => {
    removeCamera = installFakeMediaDevices()
    const { user } = await openScan()
    await waitFor(() => expect(fakeCameras).toHaveLength(1))

    await act(async () => fakeCameras[0].fail('NotAllowedError'))

    expect(screen.getByRole('heading', { name: SCANNER_HEADING })).toBeInTheDocument()
    expect(screen.getByText(/Camera của kiosk chưa dùng được \(trình duyệt chưa cho phép dùng camera\)/)).toBeInTheDocument()
    await user.type(screen.getByLabelText('Mã QR trên CCCD (máy quét tự nhập)'), `${QR}{Enter}`)
    expect(await screen.findByRole('heading', { name: 'Đây có phải thông tin của ông/bà?' })).toBeInTheDocument()
  })

  it('trang không chạy HTTPS: không có camera, dùng máy quét như trước', async () => {
    await openScan()

    expect(screen.getByRole('heading', { name: SCANNER_HEADING })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Đang chờ quét thẻ…')
    expect(fakeCameras).toHaveLength(0)
  })
})
