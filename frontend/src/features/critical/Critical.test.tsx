import { act, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type {
  CriticalAlertItem,
  CriticalAlerts,
  CriticalDashboard,
  CriticalNotification,
  CriticalResult,
  CriticalSettings,
  CriticalTest,
} from '../../api/critical'
import { filterNavItems, navItems } from '../../app/navigation'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, session } from '../../test/fixtures'
import { mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật): kết quả nguy cấp (quyết định 60).
const base = '/api/v1/critical-results'
const now = dayjs()
const ago = (minutes: number) => now.subtract(minutes, 'minute').toISOString()

const departments = [
  { id: 'd-hsh', code: 'HSH', name: 'Khoa Huyết học – Sinh hóa', isActive: true },
  { id: 'd-noi', code: 'NOI', name: 'Khoa Nội tổng hợp', isActive: true },
  { id: 'd-kkb', code: 'KKB', name: 'Khoa Khám bệnh', isActive: true },
]

const settings: CriticalSettings = {
  values: {
    notifyMinutes: 10,
    actionMinutes: 10,
    warnMinutes: 3,
    pollSeconds: 15,
    soundEnabled: true,
    soundRepeatSeconds: 60,
    flashEnabled: true,
    flashPeriodMs: 1200,
    keepHours: 24,
    lateEntryMinutes: 30,
    requireAddress: true,
    displayFullName: false,
    targetNotifyPercent: 95,
    targetActionPercent: 90,
    targetReadBackPercent: 98,
    targetCompletePercent: 98,
    codeLaboratory: 'QT-KQNC/ST01',
    codeClinical: 'QT-KQNC/ST02',
    codeMicrobiology: 'QT-VS-21/ST02',
  },
  updatedAtUtc: null,
  updatedBy: null,
}

const potassium: CriticalTest = {
  id: 't-k',
  name: 'Kali (K+) (giả lập)',
  adultRange: 'Ví dụ: ≤ 2,5 hoặc ≥ 6,0 mmol/L',
  childRange: null,
  unit: 'mmol/L',
  section: 'Huyết học – Sinh hóa',
  logbook: 'Laboratory',
  textResult: false,
  sortOrder: 1,
  isActive: true,
}

const culture: CriticalTest = {
  ...potassium,
  id: 't-c',
  name: 'Cấy máu (giả lập)',
  adultRange: 'Ví dụ: dương tính',
  unit: null,
  section: 'Vi sinh',
  logbook: 'Microbiology',
  textResult: true,
  sortOrder: 3,
}

const call = (overrides: Partial<CriticalNotification> = {}): CriticalNotification => ({
  id: 'n-1',
  attemptNo: 1,
  receivingDepartmentId: 'd-noi',
  receivingDepartmentName: 'Khoa Nội tổng hợp',
  method: 'Phone',
  notifiedAtUtc: ago(3),
  receiverName: 'Điều dưỡng nghe máy (giả lập)',
  incorrectReadBacks: 0,
  repeatedTwice: false,
  notifiedByName: 'Kỹ thuật viên Demo',
  recordedAtUtc: ago(3),
  rejectedAtUtc: null,
  rejectedByName: null,
  rejectReason: null,
  calledAtUtc: null,
  callRecordedByName: null,
  callRecordedAtUtc: null,
  callPending: false,
  ...overrides,
})

/** Lần báo trên phần mềm ("Lưu, gọi sau", quyết định 68), chưa gọi xác nhận người nhận. */
const appCall = (overrides: Partial<CriticalNotification> = {}): CriticalNotification =>
  call({ method: 'App', receiverName: null, callPending: true, ...overrides })

const none = { notify: false, acknowledge: false, reject: false, act: false, cancel: false, confirmCall: false }

function record(overrides: Partial<CriticalResult> = {}): CriticalResult {
  return {
    id: 'kq-1',
    number: 'KQNC-2026-000001',
    status: 'AwaitingAcknowledgement',
    sendingDepartmentId: 'd-hsh',
    sendingDepartmentName: 'Khoa Huyết học – Sinh hóa',
    receivingDepartmentId: 'd-noi',
    receivingDepartmentName: 'Khoa Nội tổng hợp',
    patientCode: 'NB-GL-0001',
    patientName: 'Người Bệnh Giả Lập',
    patientInitials: 'N.B.G.L',
    patientDateOfBirth: '1960-01-02',
    patientGender: 'Female',
    patientAddress: 'Ấp Mẫu (giả lập)',
    patientSource: 'Manual',
    testId: potassium.id,
    testName: potassium.name,
    testAdultRange: potassium.adultRange,
    testChildRange: null,
    logbook: 'Laboratory',
    textResult: false,
    value: '6,8',
    unit: 'mmol/L',
    orderDate: now.format('YYYY-MM-DD'),
    resultAtUtc: ago(5),
    sampleId: null,
    urgent: false,
    notifyDeadlineMinutes: 10,
    actionDeadlineMinutes: 10,
    notifyDueAtUtc: ago(-5),
    notifiedAtUtc: ago(3),
    actionDueAtUtc: ago(-7),
    recordedByName: 'Kỹ thuật viên Demo',
    recordedAtUtc: ago(3),
    acknowledgedByName: null,
    acknowledgedAtUtc: null,
    actionByName: null,
    actionAtUtc: null,
    actionSummary: null,
    recordedInMedicalRecord: false,
    actionRecordedAtUtc: null,
    cancelledByName: null,
    cancelledAtUtc: null,
    cancelReason: null,
    receivingDepartmentInformed: false,
    notifiedOnTime: true,
    actedOnTime: null,
    lateEntry: false,
    callPending: false,
    notifications: [call()],
    can: none,
    ...overrides,
  }
}

const alertItem = (overrides: Partial<CriticalAlertItem> = {}): CriticalAlertItem => ({
  id: 'kq-1',
  number: 'KQNC-2026-000001',
  status: 'AwaitingAcknowledgement',
  receivingDepartmentId: 'd-noi',
  receivingDepartmentName: 'Khoa Nội tổng hợp',
  sendingDepartmentName: 'Khoa Huyết học – Sinh hóa',
  patientName: 'Người Bệnh Giả Lập',
  patientBirthYear: 1960,
  patientCodeTail: '…0001',
  testName: potassium.name,
  value: '6,8',
  unit: 'mmol/L',
  notifiedAtUtc: ago(3),
  actionDueAtUtc: ago(-7),
  receiverName: 'Điều dưỡng nghe máy (giả lập)',
  callPending: false,
  notifiedByName: 'Kỹ thuật viên Demo',
  acknowledgedByName: null,
  acknowledgedAtUtc: null,
  actionByName: null,
  actionAtUtc: null,
  actedOnTime: null,
  ...overrides,
})

const alerts = (overrides: Partial<CriticalAlerts> = {}): CriticalAlerts => ({
  serverTimeUtc: now.toISOString(),
  settings: { warnMinutes: 3, pollSeconds: 15, soundEnabled: true, soundRepeatSeconds: 60, flashEnabled: true, flashPeriodMs: 1200 },
  receivingDepartmentNames: ['Khoa Nội tổng hợp'],
  awaitingAcknowledgement: [],
  awaitingAction: [],
  recentlyCompleted: [],
  todayReceived: 0,
  todayActedOnTime: 0,
  todayActed: 0,
  awaitingNotification: 0,
  overdueNotification: 0,
  ...overrides,
})

const person = (id: string, userName: string, fullName: string, departmentId: string, departmentName: string, permissions: string[]) =>
  session({ id, userName, fullName, departmentId, departmentName, roles: [{ id: `r-${id}`, code: 'X', name: 'Vai trò' }], permissions })

const labSession = () => person('u-ktv', 'ktv.demo', 'Kỹ thuật viên Demo', 'd-hsh', 'Khoa Huyết học – Sinh hóa', [Permissions.CriticalRecord])
const nurseSession = () => person('u-dd', 'dieuduong.demo', 'Điều dưỡng Demo', 'd-noi', 'Khoa Nội tổng hợp', [Permissions.CriticalReceive])
const doctorSession = () =>
  person('u-bs', 'bacsi.demo', 'Bác sĩ Demo', 'd-noi', 'Khoa Nội tổng hợp', [Permissions.CriticalReceive, Permissions.CriticalTreat])
const qualitySession = () =>
  person('u-qlcl', 'qlcl.demo', 'QLCL Demo', 'd-khth', 'Phòng Kế hoạch tổng hợp', [Permissions.CriticalMonitor, Permissions.CriticalManage])
const screenSession = () => person('u-tv', 'manhinh.demo', 'Màn hình Khoa Nội Demo', 'd-noi', 'Khoa Nội tổng hợp', [Permissions.CriticalDisplay])

const common = (who: object) => ({
  ...baseRoutes(who),
  'GET /api/v1/departments': departments,
  [`GET ${base}/settings`]: settings,
  [`GET ${base}/tests`]: [potassium, culture],
  [`GET ${base}/alerts`]: alerts(),
})

async function pick(user: ReturnType<typeof userEvent.setup>, scope: HTMLElement, label: string, option: string) {
  await user.click(await within(scope).findByRole('combobox', { name: label }))
  const options = await screen.findAllByTitle(option)
  await user.click(options.find((o) => o.classList.contains('ant-select-item-option'))!)
}

/** Âm thanh trình duyệt giả (jsdom không có Web Audio): đếm số lần chuông kêu. */
/**
 * AudioContext giả theo chính sách tự phát âm thanh của trình duyệt: chỉ chạy được sau khi người dùng bấm, gõ phím trên trang,
 * hoặc khi trình duyệt cho tự phát (`autoplay`, như máy TV đặt cờ khởi động). Trạng thái để tĩnh vì phần mềm dùng chung một
 * AudioContext cho cả trang; mỗi test bắt đầu lại từ "chưa cho phát".
 */
class FakeAudioContext {
  static chimes = 0
  static running = false
  static gesture = false
  static autoplay = false
  currentTime = 0
  destination = {}
  get state() {
    return FakeAudioContext.running ? 'running' : 'suspended'
  }
  async resume() {
    if (FakeAudioContext.gesture || FakeAudioContext.autoplay) FakeAudioContext.running = true
  }
  createOscillator() {
    FakeAudioContext.chimes++
    return { type: 'sine', frequency: { value: 0 }, connect: (node: unknown) => node, start() {}, stop() {} }
  }
  createGain() {
    return { gain: { setValueAtTime() {}, exponentialRampToValueAtTime() {} }, connect: (node: unknown) => node }
  }
}
Object.defineProperty(window, 'AudioContext', { configurable: true, writable: true, value: FakeAudioContext })
for (const type of ['click', 'keydown']) {
  window.addEventListener(type, () => (FakeAudioContext.gesture = true), true)
}

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
  FakeAudioContext.running = false
  FakeAudioContext.gesture = false
  FakeAudioContext.autoplay = false
  FakeAudioContext.chimes = 0
})

describe('Kết quả nguy cấp: khoa cận lâm sàng', () => {
  it('Khoa tôi báo: ô chỉ số, hồ sơ chờ theo hạn gần nhất; ghi lần báo khi người nhận đọc lại chính xác (đếm lần đọc sai)', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const waiting = record({
      id: 'kq-2',
      number: 'KQNC-2026-000002',
      status: 'AwaitingNotification',
      receivingDepartmentId: null,
      receivingDepartmentName: null,
      notifiedAtUtc: null,
      actionDueAtUtc: null,
      notifyDueAtUtc: ago(-2),
      notifiedOnTime: null,
      notifications: [],
      patientName: 'Người Bệnh Chờ Báo',
      can: { ...none, notify: true, cancel: true },
    })
    const { calls } = mockApi({
      ...common(labSession()),
      [`GET ${base}/sent`]: { serverTimeUtc: now.toISOString(), items: [waiting, record({ can: { ...none, cancel: true } })] },
      [`POST ${base}/kq-2/notifications`]: (c) => record({ id: 'kq-2', notifications: [call(c.body as Partial<CriticalNotification>)] }),
    })
    renderApp('/ket-qua-nguy-cap/khoa-bao')

    const waitingTile = await screen.findByRole('group', { name: 'Chờ gọi báo' })
    await waitFor(() => expect(within(waitingTile).getByText('1')).toBeInTheDocument())
    expect(within(waitingTile).getByText('1 hồ sơ còn dưới 3 phút')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Đã báo, khoa điều trị chưa xong' })).getByText('1')).toBeInTheDocument()
    const card = screen.getByRole('article', { name: /Người Bệnh Chờ Báo/ })
    expect(within(card).getByRole('timer')).toHaveTextContent(/Hạn báo: Còn [12]:\d\d/)
    expect(within(card).getByText('Chờ gọi báo')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Ghi kết quả nguy cấp/ })).toBeInTheDocument()

    await user.click(within(card).getByRole('button', { name: 'Ghi lần báo' }))
    const dialog = await screen.findByRole('dialog')
    await pick(user, dialog, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.type(within(dialog).getByLabelText('Người nghe máy'), 'Điều dưỡng nghe máy (giả lập)')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu, đã báo' }))
    expect(await within(dialog).findByText('Chọn kết quả người nhận đọc lại.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: /Không chính xác/ }))
    expect(await within(dialog).findByText('Đọc lại chưa chính xác (lần 1).')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: /^(check )?Chính xác$/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Lưu, đã báo' }))

    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-2/notifications`)?.body).toMatchObject({
        receivingDepartmentId: 'd-noi',
        method: 'Phone',
        receiverName: 'Điều dưỡng nghe máy (giả lập)',
        readBackCorrect: true,
        incorrectReadBacks: 1,
        repeatedTwice: false,
      }),
    )
  })

  it('Ghi kết quả: lưu, gọi sau phải chọn khoa nhận, báo ngay trên phần mềm rồi ghi cuộc gọi sau; lưu, đã báo phải có người nhận đọc lại chính xác', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    // Quyết định 68: "Lưu, gọi sau" báo khoa nhận ngay trên phần mềm; hồ sơ chờ khoa xác nhận, có nhãn Chưa gọi xác nhận.
    const saved = record({ callPending: true, notifications: [appCall()], can: { ...none, cancel: true, confirmCall: true } })
    const { calls } = mockApi({
      ...common(labSession()),
      [`POST ${base}`]: saved,
      [`POST ${base}/kq-1/call`]: (c) =>
        record({ notifications: [call({ ...(c.body as object), calledAtUtc: now.toISOString() })], can: { ...none, cancel: true } }),
    })
    renderApp('/ket-qua-nguy-cap/ghi')

    await user.type(await screen.findByLabelText('Mã người bệnh'), 'NB-GL-0009')
    // Chưa nối API thông tin người bệnh: không có nút Tra HIS, nhập tay.
    expect(screen.queryByRole('button', { name: /Tra HIS/ })).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Họ tên'), 'Người Bệnh Mới')
    await user.type(screen.getByLabelText('Ngày sinh'), '02/01/1960{Enter}')
    await user.type(screen.getByLabelText('Địa chỉ'), 'Ấp Mẫu (giả lập)')
    await pick(user, document.body, 'Xét nghiệm, kỹ thuật (danh mục kết quả nguy cấp)', 'Kali (K+) (giả lập) · Huyết học – Sinh hóa')
    expect(await screen.findByText('Người lớn: Ví dụ: ≤ 2,5 hoặc ≥ 6,0 mmol/L')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText('Đơn vị')).toHaveValue('mmol/L'))
    await user.type(screen.getByLabelText('Giá trị'), '6,8')
    expect(screen.getByText(/“Người bệnh Người Bệnh Mới, sinh 02\/01\/1960, Ấp Mẫu \(giả lập\)\.”/)).toBeInTheDocument()
    expect(screen.getByText(/“Kali \(K\+\) \(giả lập\): 6,8 mmol\/L/)).toBeInTheDocument()

    // Chưa có lần báo đọc lại chính xác: không lưu được kiểu "đã báo".
    await user.click(screen.getByRole('button', { name: 'Lưu, đã báo' }))
    expect(await screen.findByText('Chọn khoa nhận.')).toBeInTheDocument()
    expect(screen.getByText('Chọn kết quả người nhận đọc lại.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(false)

    // "Lưu, gọi sau" cũng phải chọn khoa nhận (khoa được báo ngay); không cần người nhận, đọc lại.
    await user.click(screen.getByRole('button', { name: 'Lưu, gọi sau' }))
    await waitFor(() => expect(screen.queryByText('Chọn kết quả người nhận đọc lại.')).not.toBeInTheDocument())
    expect(screen.getByText('Chọn khoa nhận.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(false)
    await pick(user, document.body, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.click(screen.getByRole('button', { name: 'Lưu, gọi sau' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(true))
    const body = calls.find((c) => c.method === 'POST' && c.path === base)!.body as Record<string, unknown>
    expect(body).toMatchObject({
      sendingDepartmentId: 'd-hsh',
      testId: 't-k',
      value: '6,8',
      unit: 'mmol/L',
      urgent: false,
      patient: { code: 'NB-GL-0009', fullName: 'Người Bệnh Mới', dateOfBirth: '1960-01-02', address: 'Ấp Mẫu (giả lập)', source: 'Manual' },
    })
    expect(body.notification).toEqual({ receivingDepartmentId: 'd-noi', method: 'App' })
    expect(body.orderDate).toBe(now.format('YYYY-MM-DD'))
    expect(await screen.findByText('Đã lưu hồ sơ KQNC-2026-000001, báo sau 2 phút')).toBeInTheDocument()
    expect(screen.getByText(/Khoa Nội tổng hợp đã được báo trên phần mềm, thấy hồ sơ cùng dải cảnh báo đỏ\. .*bấm Ghi cuộc gọi/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ghi kết quả khác' })).toBeInTheDocument()

    // Gọi được ngay sau khi lưu: ghi người nhận thông tin, không có ô khoa nhận.
    await user.click(screen.getByRole('button', { name: /Ghi cuộc gọi/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ghi cuộc gọi · Người Bệnh Giả Lập/ })
    expect(within(dialog).getByText(/trên phần mềm lúc/)).toBeInTheDocument()
    expect(within(dialog).queryByRole('combobox', { name: 'Khoa nhận' })).not.toBeInTheDocument()
    expect(within(dialog).getByText(/Người gọi:/)).toHaveTextContent('Người gọi: Kỹ thuật viên Demo (tài khoản đang đăng nhập)')
    await user.click(within(dialog).getByRole('button', { name: /Lưu cuộc gọi/ }))
    expect(await within(dialog).findByText('Nhập họ tên người nhận.')).toBeInTheDocument()
    expect(calls.some((c) => c.path === `${base}/kq-1/call`)).toBe(false)
    await user.click(within(dialog).getByText('Trực tiếp tại khoa'))
    await user.type(within(dialog).getByLabelText('Người nhận tại khoa'), ' Điều dưỡng nhận sau (giả lập) ')
    await user.click(within(dialog).getByRole('button', { name: /^(check )?Chính xác$/ }))
    await user.click(within(dialog).getByRole('button', { name: /Lưu cuộc gọi/ }))

    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-1/call`)?.body).toMatchObject({
        method: 'InPerson',
        receiverName: 'Điều dưỡng nhận sau (giả lập)',
        readBackCorrect: true,
        incorrectReadBacks: 0,
        repeatedTwice: false,
      }),
    )
    const calledAt = (calls.find((c) => c.path === `${base}/kq-1/call`)!.body as { calledAtUtc: string }).calledAtUtc
    expect(Math.abs(Date.parse(calledAt) - Date.now())).toBeLessThan(5 * 60_000)
    // Đã ghi cuộc gọi: không còn nút Ghi cuộc gọi trên màn hình đã lưu.
    await waitFor(() => expect(screen.queryByRole('button', { name: /Ghi cuộc gọi/ })).not.toBeInTheDocument())
  })

  it('Ghi kết quả Vi sinh: kết quả bằng chữ, bắt buộc SID và người nhận nhắc lại 2 lần', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({ ...common(labSession()), [`POST ${base}`]: (c) => record({ ...(c.body as object) }) })
    renderApp('/ket-qua-nguy-cap/ghi')

    await pick(user, document.body, 'Xét nghiệm, kỹ thuật (danh mục kết quả nguy cấp)', 'Cấy máu (giả lập) · Vi sinh')
    expect(await screen.findByLabelText('Kết quả (ghi bằng chữ)')).toBeInTheDocument()
    expect(screen.queryByLabelText('Đơn vị')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Mẫu khẩn')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Lưu, đã báo' }))
    expect(await screen.findByText('Sổ Vi sinh: nhập SID.')).toBeInTheDocument()
    expect(screen.getByText('Sổ Vi sinh: người nhận phải nhắc lại 2 lần.')).toBeInTheDocument()

    // Điền đủ rồi lưu: gửi SID, mẫu khẩn, người nhận đã nhắc lại 2 lần; kết quả bằng chữ không có đơn vị.
    await user.type(screen.getByLabelText('Mã người bệnh'), 'NB-GL-0010')
    await user.type(screen.getByLabelText('Họ tên'), 'Người Bệnh Vi Sinh')
    await user.type(screen.getByLabelText('Ngày sinh'), '02/01/1960{Enter}')
    await user.type(screen.getByLabelText('Địa chỉ'), 'Ấp Mẫu (giả lập)')
    await user.type(screen.getByLabelText('Kết quả (ghi bằng chữ)'), 'Dương tính (giả lập)')
    await user.type(screen.getByLabelText('SID (mã mẫu)'), 'VS-0010')
    await user.click(screen.getByLabelText('Mẫu khẩn'))
    await pick(user, document.body, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.type(screen.getByLabelText('Người nghe máy'), 'Điều dưỡng nghe máy (giả lập)')
    await user.click(screen.getByRole('button', { name: /^(check )?Chính xác$/ }))
    await user.click(screen.getByLabelText('Người nhận đã nhắc lại 2 lần (sổ Vi sinh)'))
    await user.click(screen.getByRole('button', { name: 'Lưu, đã báo' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(true))
    expect(calls.find((c) => c.method === 'POST' && c.path === base)!.body).toMatchObject({
      testId: 't-c',
      value: 'Dương tính (giả lập)',
      unit: null,
      sampleId: 'VS-0010',
      urgent: true,
      notification: { method: 'Phone', receiverName: 'Điều dưỡng nghe máy (giả lập)', readBackCorrect: true, incorrectReadBacks: 0, repeatedTwice: true },
    })
  })

  /** Đã nối API thông tin người bệnh; HIS trả người bệnh theo mã (giả lập). */
  const hisRoutes = {
    'GET /api/v1/features': { his: false, smartCa: false, patientSource: true, departmentsFromHis: true },
    [`GET ${base}/patient`]: (c: ApiCall) => {
      const code = new URLSearchParams(c.search).get('code')
      if (code === 'NB-HIS-0001') {
        return { status: 'Found', code, fullName: 'Người Bệnh HIS (giả lập)', dateOfBirth: '1960-01-02', gender: 'Female', address: 'Ấp HIS (giả lập)' }
      }
      return { status: code === 'NB-HIS-LOI' ? 'Unavailable' : 'NotFound', code, fullName: null, dateOfBirth: null, gender: null, address: null }
    },
  }

  it('Tra HIS: nhập mã người bệnh, bấm Tra HIS điền sẵn họ tên, ngày sinh, giới tính, địa chỉ; hồ sơ ghi Lấy từ HIS', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...common(labSession()),
      ...hisRoutes,
      [`POST ${base}`]: (c) => record({ status: 'AwaitingNotification', receivingDepartmentId: null, notifiedAtUtc: null, notifications: [], ...(c.body as object) }),
    })
    renderApp('/ket-qua-nguy-cap/ghi')

    // Chưa nhập mã: nhắc nhập mã, không gọi HIS.
    await user.click(await screen.findByRole('button', { name: /Tra HIS/ }))
    expect(await screen.findByText('Nhập mã người bệnh để tra HIS.')).toBeInTheDocument()
    expect(calls.some((c) => c.path === `${base}/patient`)).toBe(false)

    await user.type(screen.getByLabelText('Mã người bệnh'), ' NB-HIS-0001 ')
    await user.click(screen.getByRole('button', { name: /Tra HIS/ }))
    await waitFor(() => expect(screen.getByLabelText('Họ tên')).toHaveValue('Người Bệnh HIS (giả lập)'))
    expect(calls.find((c) => c.path === `${base}/patient`)!.search).toBe('?code=NB-HIS-0001')
    expect(screen.getByLabelText('Mã người bệnh')).toHaveValue('NB-HIS-0001')
    // Ô chọn ngày hiện giá trị sau một nhịp vẽ lại (giá trị của form đã có ngay: nhãn có số tuổi).
    expect(screen.getByText(/^Ngày sinh \(\d+ tuổi\)$/)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText(/^Ngày sinh/)).toHaveValue('02/01/1960'))
    expect(screen.getByLabelText('Nữ')).toBeChecked()
    expect(screen.getByLabelText('Địa chỉ')).toHaveValue('Ấp HIS (giả lập)')
    expect(screen.getByText('Lấy từ HIS')).toBeInTheDocument()
    // Kịch bản gọi điện điền theo thông tin vừa lấy.
    expect(screen.getByText(/“Người bệnh Người Bệnh HIS \(giả lập\), sinh 02\/01\/1960, Ấp HIS \(giả lập\)\.”/)).toBeInTheDocument()

    await pick(user, document.body, 'Xét nghiệm, kỹ thuật (danh mục kết quả nguy cấp)', 'Kali (K+) (giả lập) · Huyết học – Sinh hóa')
    await user.type(screen.getByLabelText('Giá trị'), '6,8')
    await pick(user, document.body, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.click(screen.getByRole('button', { name: 'Lưu, gọi sau' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(true))
    expect((calls.find((c) => c.method === 'POST' && c.path === base)!.body as { patient: unknown }).patient).toEqual({
      code: 'NB-HIS-0001',
      fullName: 'Người Bệnh HIS (giả lập)',
      dateOfBirth: '1960-01-02',
      gender: 'Female',
      address: 'Ấp HIS (giả lập)',
      source: 'His',
    })
  })

  it('Tra HIS: sửa thông tin đã lấy thì hồ sơ ghi Nhập tay; không tìm thấy hoặc HIS lỗi thì báo để nhập tay', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({ ...common(labSession()), ...hisRoutes, [`POST ${base}`]: (c) => record({ ...(c.body as object) }) })
    renderApp('/ket-qua-nguy-cap/ghi')

    await user.type(await screen.findByLabelText('Mã người bệnh'), 'NB-HIS-0001')
    await user.click(screen.getByRole('button', { name: /Tra HIS/ }))
    expect(await screen.findByText('Lấy từ HIS')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Địa chỉ'), ', tổ 2')
    expect(await screen.findByText('Nhập tay')).toBeInTheDocument()
    expect(screen.queryByText('Lấy từ HIS')).not.toBeInTheDocument()

    await pick(user, document.body, 'Xét nghiệm, kỹ thuật (danh mục kết quả nguy cấp)', 'Kali (K+) (giả lập) · Huyết học – Sinh hóa')
    await user.type(screen.getByLabelText('Giá trị'), '6,8')
    await pick(user, document.body, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.click(screen.getByRole('button', { name: 'Lưu, gọi sau' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(true))
    expect((calls.find((c) => c.method === 'POST' && c.path === base)!.body as { patient: unknown }).patient).toMatchObject({
      address: 'Ấp HIS (giả lập), tổ 2',
      source: 'Manual',
    })
  })

  it('Tra HIS: không tìm thấy người bệnh hoặc HIS lỗi thì báo để nhập tay, các ô không bị xóa', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    mockApi({ ...common(labSession()), ...hisRoutes })
    renderApp('/ket-qua-nguy-cap/ghi')

    await user.type(await screen.findByLabelText('Họ tên'), 'Người Bệnh Gõ Tay')
    await user.type(screen.getByLabelText('Mã người bệnh'), 'NB-KHONG-CO')
    await user.click(screen.getByRole('button', { name: /Tra HIS/ }))
    expect(await screen.findByText('Không tìm thấy người bệnh trên HIS: nhập tay.')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Mã người bệnh'))
    await user.type(screen.getByLabelText('Mã người bệnh'), 'NB-HIS-LOI')
    await user.click(screen.getByRole('button', { name: /Tra HIS/ }))
    expect(await screen.findByText('HIS đang lỗi: nhập tay họ tên, ngày sinh, địa chỉ.')).toBeInTheDocument()
    expect(screen.getByLabelText('Họ tên')).toHaveValue('Người Bệnh Gõ Tay')
    expect(screen.getByText('Nhập tay')).toBeInTheDocument()
  })
})

describe('Kết quả nguy cấp: khoa điều trị và bác sĩ', () => {
  it('Gửi đến khoa tôi: xác nhận đã nhận, trả lại không phải khoa tôi; dải đỏ chớp tắt, tắt tiếng trên máy này', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const waiting = record({ can: { ...none, acknowledge: true, reject: true } })
    const late = record({
      id: 'kq-3',
      status: 'AwaitingAction',
      patientName: 'Người Bệnh Quá Hạn',
      actionDueAtUtc: ago(2),
      acknowledgedByName: 'Điều dưỡng Demo',
      acknowledgedAtUtc: ago(11),
    })
    const cancelled = record({ id: 'kq-4', status: 'Cancelled', patientName: 'Người Bệnh Báo Nhầm', cancelReason: 'Nhập nhầm người bệnh', cancelledAtUtc: ago(1) })
    // Hồ sơ chờ xác nhận mà người xem không được xác nhận, trả lại (máy chủ quyết định qua "can"): không có nút.
    const viewOnly = record({ id: 'kq-6', patientName: 'Người Bệnh Chỉ Xem', can: none })
    const { calls } = mockApi({
      ...common(nurseSession()),
      [`GET ${base}/alerts`]: alerts({ awaitingAcknowledgement: [alertItem()] }),
      [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [waiting, late, cancelled, viewOnly] },
      [`POST ${base}/kq-1/acknowledge`]: record({ status: 'AwaitingAction', acknowledgedByName: 'Điều dưỡng Demo', acknowledgedAtUtc: now.toISOString() }),
      [`POST ${base}/kq-1/reject`]: record({ status: 'AwaitingNotification', receivingDepartmentId: null }),
    })
    renderApp('/ket-qua-nguy-cap/khoa-nhan')

    const banner = await screen.findByRole('region', { name: 'Cảnh báo kết quả nguy cấp' })
    expect(banner).toHaveTextContent('1 kết quả nguy cấp chờ khoa xác nhận')
    expect(banner).toHaveTextContent(/Người Bệnh Giả Lập · Kali \(K\+\) \(giả lập\) 6,8 mmol\/L · Khoa Huyết học – Sinh hóa báo lúc/)
    expect(banner).toHaveTextContent(/bác sĩ còn [67]:\d\d để xử trí/)
    expect(banner).toHaveClass('critical-flash')
    expect(banner.style.animationDuration).toBe('1200ms')
    // Chưa bấm gì trên trang: trình duyệt chưa cho phát âm thanh. Bấm "Bật chuông": chuông kêu cho hồ sơ đang chờ.
    await user.click(within(banner).getByRole('button', { name: /Bật chuông/ }))
    await waitFor(() => expect(FakeAudioContext.chimes).toBeGreaterThan(0))
    await user.click(await within(banner).findByRole('button', { name: /Tắt tiếng/ }))
    expect(window.localStorage.getItem('critical.muted')).toBe('1')
    expect(within(banner).getByRole('button', { name: /Bật tiếng/ })).toBeInTheDocument()

    const card = await screen.findByRole('article', { name: 'Người Bệnh Giả Lập' })
    // Tài khoản một khoa: thẻ không ghi tên khoa (quyết định 63 chỉ cho tài khoản xem nhiều khoa).
    expect(within(card).queryByText('Khoa Nội tổng hợp')).not.toBeInTheDocument()
    expect(within(card).getByText(/Người nghe máy: Điều dưỡng nghe máy \(giả lập\) · đọc lại chính xác/)).toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: 'Bác sĩ ghi hướng xử trí' })).not.toBeInTheDocument()
    expect(within(screen.getByRole('article', { name: 'Người Bệnh Quá Hạn' })).getByText('Quá hạn: nhắc lại và báo trực tiếp bác sĩ.')).toBeInTheDocument()
    expect(screen.getByText('Nhập nhầm người bệnh')).toBeInTheDocument()
    const viewOnlyCard = screen.getByRole('article', { name: 'Người Bệnh Chỉ Xem' })
    expect(within(viewOnlyCard).queryByRole('button', { name: 'Đã nhận cuộc gọi' })).not.toBeInTheDocument()
    expect(within(viewOnlyCard).queryByRole('button', { name: 'Không phải khoa tôi' })).not.toBeInTheDocument()

    await user.click(within(card).getByRole('button', { name: 'Đã nhận cuộc gọi' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === `${base}/kq-1/acknowledge`)).toBe(true))

    await user.click(within(card).getByRole('button', { name: 'Không phải khoa tôi' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('combobox', { name: 'Lý do (khoa báo thấy ngay)' }), 'Người bệnh đã chuyển khoa')
    await user.click(within(dialog).getByRole('button', { name: 'Gửi trả khoa báo' }))
    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-1/reject`)?.body).toEqual({ reason: 'Người bệnh đã chuyển khoa' }),
    )
  })

  it('Máy đã tắt tiếng: bấm trên trang vẫn không kêu chuông, bật tiếng lại thì kêu; QLCL tắt chớp tắt thì dải không chớp', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    window.localStorage.setItem('critical.muted', '1')
    mockApi({
      ...common(nurseSession()),
      [`GET ${base}/alerts`]: alerts({
        awaitingAcknowledgement: [alertItem()],
        settings: { warnMinutes: 3, pollSeconds: 15, soundEnabled: true, soundRepeatSeconds: 60, flashEnabled: false, flashPeriodMs: 1200 },
      }),
      [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [] },
    })
    renderApp('/ket-qua-nguy-cap/khoa-nhan')

    const banner = await screen.findByRole('region', { name: 'Cảnh báo kết quả nguy cấp' })
    expect(banner).not.toHaveClass('critical-flash')
    // Bấm vào trang: trình duyệt cho phát âm thanh, nhưng máy này đã tắt tiếng nên chuông không kêu.
    await user.click(document.body)
    await waitFor(() => expect(FakeAudioContext.running).toBe(true))
    expect(FakeAudioContext.chimes).toBe(0)

    await user.click(within(banner).getByRole('button', { name: /Bật tiếng/ }))
    await waitFor(() => expect(FakeAudioContext.chimes).toBeGreaterThan(0))
    expect(window.localStorage.getItem('critical.muted')).toBeNull()
  })

  it('Bác sĩ nghe máy ghi hướng xử trí ngay trên hồ sơ: xác nhận đã nhận và lưu bằng tài khoản của mình', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...common(doctorSession()),
      [`GET ${base}/kq-1`]: record({ can: { ...none, acknowledge: true, reject: true, act: true } }),
      [`POST ${base}/kq-1/action`]: record({
        status: 'Completed',
        acknowledgedByName: 'Bác sĩ Demo',
        acknowledgedAtUtc: now.toISOString(),
        actionByName: 'Bác sĩ Demo',
        actionAtUtc: now.toISOString(),
        actionRecordedAtUtc: now.toISOString(),
        actionSummary: 'Ngưng thuốc giữ kali (giả lập)',
        actedOnTime: true,
      }),
    })
    renderApp('/ket-qua-nguy-cap/ho-so/kq-1')

    expect(await screen.findByRole('heading', { name: 'Hồ sơ KQNC-2026-000001' })).toBeInTheDocument()
    expect(screen.getByText(/Lần báo 1: gọi điện cho Khoa Nội tổng hợp/)).toBeInTheDocument()
    expect(screen.getByText(/Bác sĩ:/).closest('div')).toHaveTextContent('Bác sĩ Demo (tài khoản đang đăng nhập, thay chữ ký cột 9 sổ ST02)')
    await user.click(screen.getByRole('button', { name: 'Xác nhận đã nhận và lưu' }))
    expect(await screen.findByText('Nhập hướng xử trí ban đầu.')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Hướng xử trí ban đầu'), 'Ngưng thuốc giữ kali (giả lập)')
    await user.click(screen.getByRole('button', { name: 'Xác nhận đã nhận và lưu' }))

    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-1/action`)?.body).toEqual({
        summary: 'Ngưng thuốc giữ kali (giả lập)',
        actionAtUtc: null,
        recordedInMedicalRecord: true,
      }),
    )
    expect(await screen.findByText(/Đã lưu hướng xử trí lúc/)).toBeInTheDocument()
  })

  it('Khoa báo hủy hồ sơ báo nhầm: bắt buộc lý do và đã gọi báo khoa nhận về việc hủy', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...common(labSession()),
      [`GET ${base}/kq-1`]: record({ can: { ...none, cancel: true } }),
      [`POST ${base}/kq-1/cancel`]: record({ status: 'Cancelled', cancelReason: 'Nhập nhầm người bệnh', cancelledByName: 'Kỹ thuật viên Demo', cancelledAtUtc: now.toISOString() }),
    })
    renderApp('/ket-qua-nguy-cap/ho-so/kq-1')

    await user.click(await screen.findByRole('button', { name: 'Hủy hồ sơ báo nhầm' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Lý do hủy'), 'Nhập nhầm người bệnh')
    await user.click(within(dialog).getByRole('button', { name: 'Hủy hồ sơ' }))
    expect(await within(dialog).findByText('Gọi báo khoa nhận về việc hủy trước khi hủy.')).toBeInTheDocument()
    expect(calls.some((c) => c.path === `${base}/kq-1/cancel`)).toBe(false)
    await user.click(within(dialog).getByLabelText('Đã gọi báo Khoa Nội tổng hợp về việc hủy'))
    await user.click(within(dialog).getByRole('button', { name: 'Hủy hồ sơ' }))
    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-1/cancel`)?.body).toEqual({ reason: 'Nhập nhầm người bệnh', receivingDepartmentInformed: true }),
    )
  })
})

describe('Kết quả nguy cấp: màn hình theo dõi tại khoa', () => {
  it('Khoa điều trị mở màn hình theo dõi bằng nút ở trang Gửi đến khoa tôi', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    mockApi({ ...common(nurseSession()), [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [] } })
    const { router } = renderApp('/ket-qua-nguy-cap/khoa-nhan')

    await user.click(await screen.findByRole('button', { name: /Mở màn hình theo dõi \(TV\)/ }))

    expect(router.state.location.pathname).toBe('/man-hinh-kqnc')
    expect(await screen.findByText('Không có kết quả nguy cấp chờ khoa xác nhận.')).toBeInTheDocument()
  })

  it('QLCL mở màn hình theo dõi của mọi khoa (quyết định 66): thẻ ghi khoa; chọn một khoa thì cột chỉ còn khoa đó, dải đỏ vẫn tính mọi khoa', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const kkb = { receivingDepartmentId: 'd-kkb', receivingDepartmentName: 'Khoa Khám bệnh' }
    const waiting = [alertItem({ patientName: 'Người Bệnh Khoa Nội' }), alertItem({ id: 'kq-9', patientName: 'Người Bệnh Khoa Khám', ...kkb })]
    const { calls } = mockApi({
      ...common(qualitySession()),
      [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [] },
      [`GET ${base}/alerts`]: (c: ApiCall) => {
        const id = new URLSearchParams(c.search).get('departmentId')
        return id
          ? alerts({
              receivingDepartmentNames: [departments.find((d) => d.id === id)!.name],
              awaitingAcknowledgement: waiting.filter((a) => a.receivingDepartmentId === id),
              todayReceived: 1,
            })
          : alerts({ receivingDepartmentNames: ['Toàn viện'], awaitingAcknowledgement: waiting, todayReceived: 2 })
      },
    })
    const { router } = renderApp('/ket-qua-nguy-cap/khoa-nhan')

    await user.click(await screen.findByRole('button', { name: /Mở màn hình theo dõi \(TV\)/ }))
    expect(router.state.location.pathname).toBe('/man-hinh-kqnc')
    expect(await screen.findByText('Toàn viện')).toBeInTheDocument()
    expect(within(await screen.findByRole('article', { name: /Người Bệnh Khoa Khám/ })).getByText('Khoa Khám bệnh')).toBeInTheDocument()
    expect(calls.some((c) => c.path === `${base}/alerts` && c.search === '?allDepartments=true')).toBe(true)
    const band = /KẾT QUẢ NGUY CẤP MỚI · 2 hồ sơ chờ khoa xác nhận · Khoa Nội tổng hợp, Khoa Khám bệnh/
    expect(screen.getByText(band)).toBeInTheDocument()

    await pick(user, document.body, 'Khoa', 'Khoa Khám bệnh')
    await waitFor(() => expect(screen.queryByRole('article', { name: /Người Bệnh Khoa Nội/ })).not.toBeInTheDocument())
    expect(within(screen.getByRole('article', { name: /Người Bệnh Khoa Khám/ })).queryByText('Khoa Khám bệnh')).not.toBeInTheDocument()
    expect(new URLSearchParams(router.state.location.search).get('khoa')).toBe('d-kkb')
    expect(calls.some((c) => c.path === `${base}/alerts` && c.search === '?departmentId=d-kkb')).toBe(true)
    // Dải đỏ vẫn là của mọi khoa: hồ sơ mới ở khoa khác không bị lỡ khi đang xem một khoa.
    expect(screen.getByText(band)).toBeInTheDocument()
  })

  it('Đang chọn một khoa ở trang Gửi đến khoa tôi thì nút TV mở màn hình của khoa đó (quyết định 66)', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    mockApi({ ...common(qualitySession()), [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [] } })
    const { router } = renderApp('/ket-qua-nguy-cap/khoa-nhan')

    await pick(user, document.body, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.click(screen.getByRole('button', { name: /Mở màn hình theo dõi \(TV\)/ }))
    expect(router.state.location.pathname).toBe('/man-hinh-kqnc')
    expect(router.state.location.search).toBe('?khoa=d-noi')
  })

  it('Màn hình của QLCL: địa chỉ lưu sẵn một khoa đã xóa thì hiện mọi khoa, không báo mất kết nối; khoa ngưng dùng ghi (ngưng)', { timeout: 30_000 }, async () => {
    const old = { id: 'd-cu', code: 'CU', name: 'Khoa Cũ', isActive: false }
    const { calls } = mockApi({
      ...common(qualitySession()),
      // Khoa ngưng dùng chỉ có khi hỏi cả khoa ngưng dùng.
      'GET /api/v1/departments': (c: ApiCall) => (c.search.includes('includeInactive=true') ? [...departments, old] : departments),
      [`GET ${base}/alerts`]: (c: ApiCall) => {
        const id = new URLSearchParams(c.search).get('departmentId')
        if (id === 'd-da-xoa') return problem(400, 'Dữ liệu không hợp lệ.', { errors: { departmentId: ['Không có khoa/phòng này.'] } })
        return alerts({ receivingDepartmentNames: [id ? [...departments, old].find((d) => d.id === id)!.name : 'Toàn viện'] })
      },
    })
    const { router } = renderApp('/man-hinh-kqnc?khoa=d-da-xoa')

    expect(await screen.findByText('Toàn viện')).toBeInTheDocument()
    expect(screen.queryByText(/MẤT KẾT NỐI/)).not.toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Khoa' }).closest('.ant-select')).toHaveTextContent('Mọi khoa')

    const before = calls.length
    await act(() => router.navigate('/man-hinh-kqnc?khoa=d-cu'))
    expect(await screen.findByText('Khoa Cũ')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Khoa' }).closest('.ant-select')).toHaveTextContent('Khoa Cũ (ngưng)')
    expect(calls.slice(before).some((c) => c.search === '?departmentId=d-da-xoa')).toBe(false)
  })

  it('Đang tải khoa vừa chọn: tiêu đề là khoa mới, các cột đang hiện mờ đi, không thoáng hiện cột trống (quyết định 66)', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    let release = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    mockApi({
      ...common(qualitySession()),
      [`GET ${base}/alerts`]: async (c: ApiCall) => {
        if (new URLSearchParams(c.search).get('departmentId') === 'd-kkb') {
          await gate
          return alerts({ receivingDepartmentNames: ['Khoa Khám bệnh'] })
        }
        return alerts({ receivingDepartmentNames: ['Toàn viện'], awaitingAcknowledgement: [alertItem({ patientName: 'Người Bệnh Khoa Nội' })] })
      },
    })
    renderApp('/man-hinh-kqnc')
    expect(await screen.findByRole('article', { name: /Người Bệnh Khoa Nội/ })).toBeInTheDocument()
    const columns = () => document.querySelector('main > div[aria-busy]')!
    expect(columns().getAttribute('aria-busy')).toBe('false')

    await pick(user, document.body, 'Khoa', 'Khoa Khám bệnh')
    expect(await screen.findByText('Khoa Khám bệnh', { selector: 'header div' })).toBeInTheDocument()
    expect(columns().getAttribute('aria-busy')).toBe('true')
    expect(columns()).toHaveStyle({ opacity: '0.6' })
    expect(screen.getByRole('article', { name: /Người Bệnh Khoa Nội/ })).toBeInTheDocument()
    expect(screen.queryByText('Không có kết quả nguy cấp chờ khoa xác nhận.')).not.toBeInTheDocument()

    release()
    await waitFor(() => expect(screen.queryByRole('article', { name: /Người Bệnh Khoa Nội/ })).not.toBeInTheDocument())
    expect(columns().getAttribute('aria-busy')).toBe('false')
    expect(columns()).toHaveStyle({ opacity: '1' })
    expect(screen.getByText('Không có kết quả nguy cấp chờ khoa xác nhận.')).toBeInTheDocument()
  })

  it('Tài khoản màn hình mở trang nào cũng về màn hình theo dõi: tên viết tắt, không có nút thao tác', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(screenSession()),
      [`GET ${base}/alerts`]: alerts({
        awaitingAcknowledgement: [alertItem({ patientName: 'N.B.G.L' })],
        awaitingAction: [alertItem({ id: 'kq-3', status: 'AwaitingAction', patientName: 'N.B.Q.H', actionDueAtUtc: ago(2), acknowledgedByName: 'Điều dưỡng Demo', acknowledgedAtUtc: ago(11) })],
        recentlyCompleted: [alertItem({ id: 'kq-5', status: 'Completed', patientName: 'N.B.X', actionAtUtc: ago(20), actedOnTime: true, actionByName: 'Bác sĩ Demo' })],
        todayReceived: 3,
        todayActedOnTime: 1,
        todayActed: 1,
      }),
    })
    const { router } = renderApp('/ket-qua-nguy-cap/khoa-nhan')

    expect(await screen.findByText('KẾT QUẢ NGUY CẤP MỚI · 1 hồ sơ chờ khoa xác nhận')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/man-hinh-kqnc')
    expect(screen.getByRole('status')).toHaveClass('critical-flash')
    expect(screen.getByRole('article', { name: /N\.B\.G\.L/ })).toHaveTextContent('Năm sinh 1960 · mã …0001')
    expect(within(screen.getByRole('article', { name: /N\.B\.G\.L/ })).queryByText('Khoa Nội tổng hợp')).not.toBeInTheDocument()
    expect(screen.getByText('QUÁ HẠN · nhắc lại, báo trực tiếp bác sĩ')).toBeInTheDocument()
    expect(screen.getByText('1/1')).toBeInTheDocument()
    expect(await screen.findByText(/tên viết tắt để giữ bí mật thông tin người bệnh/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Bấm để bật chuông/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Đã nhận cuộc gọi' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('Máy TV cho tự phát âm thanh: chuông bật ngay khi mở, không cần ai bấm, kêu cho hồ sơ chờ khoa xác nhận', { timeout: 30_000 }, async () => {
    FakeAudioContext.autoplay = true
    mockApi({ ...common(screenSession()), [`GET ${base}/alerts`]: alerts({ awaitingAcknowledgement: [alertItem({ patientName: 'N.B.G.L' })] }) })
    renderApp('/man-hinh-kqnc')

    expect(await screen.findByRole('button', { name: /Chuông bật · thử chuông/ })).toBeInTheDocument()
    await waitFor(() => expect(FakeAudioContext.chimes).toBeGreaterThan(0))
    expect(FakeAudioContext.gesture).toBe(false)
  })

  it('Không cập nhật được thì báo mất kết nối trên toàn màn hình', { timeout: 30_000 }, async () => {
    mockApi({ ...common(screenSession()), [`GET ${base}/alerts`]: () => problem(500, 'Lỗi máy chủ.') })
    renderApp('/man-hinh-kqnc')

    expect(await screen.findByRole('alert')).toHaveTextContent('MẤT KẾT NỐI · MÀN HÌNH KHÔNG CÒN CẬP NHẬT')
  })
})

describe('Kết quả nguy cấp: báo trên phần mềm, gọi sau để ghi người nhận (quyết định 68)', () => {
  it('Khoa tôi báo: ô Chưa gọi xác nhận, nhãn ở hồ sơ đang chờ và vừa xong; Ghi cuộc gọi không có ô khoa nhận', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const inWard = record({
      id: 'kq-5',
      status: 'AwaitingAction',
      patientName: 'Người Bệnh Chưa Gọi',
      acknowledgedByName: 'Điều dưỡng Demo',
      acknowledgedAtUtc: ago(2),
      callPending: true,
      notifications: [appCall()],
      can: { ...none, cancel: true, confirmCall: true },
    })
    const done = record({
      id: 'kq-6',
      status: 'Completed',
      patientName: 'Người Bệnh Xong Chưa Gọi',
      actionAtUtc: ago(1),
      actedOnTime: true,
      callPending: true,
      notifications: [appCall()],
      can: { ...none, cancel: true, confirmCall: true },
    })
    const { calls } = mockApi({
      ...common(labSession()),
      [`GET ${base}/sent`]: { serverTimeUtc: now.toISOString(), items: [inWard, record(), done] },
      [`POST ${base}/kq-5/call`]: (c) => record({ id: 'kq-5', status: 'AwaitingAction', notifications: [call(c.body as Partial<CriticalNotification>)] }),
    })
    renderApp('/ket-qua-nguy-cap/khoa-bao')

    const tile = await screen.findByRole('group', { name: 'Chưa gọi xác nhận' })
    await waitFor(() => expect(within(tile).getByText('2')).toBeInTheDocument())
    expect(within(tile).getByText('Đã báo trên phần mềm: gọi để ghi người nhận')).toBeInTheDocument()
    const card = screen.getByRole('article', { name: /Người Bệnh Chưa Gọi/ })
    expect(within(card).getByText('Chưa gọi xác nhận')).toBeInTheDocument()
    expect(within(card).getByText(/đã nhận lúc .* · chưa gọi xác nhận người nhận · chờ bác sĩ xử trí/)).toBeInTheDocument()
    // Hồ sơ gọi điện rồi mới ghi: không có nhãn, không có nút.
    const called = screen.getByRole('article', { name: /Người Bệnh Giả Lập/ })
    expect(within(called).getByText(/Điều dưỡng nghe máy \(giả lập\) nghe máy · chờ khoa xác nhận/)).toBeInTheDocument()
    expect(within(called).queryByText('Chưa gọi xác nhận')).not.toBeInTheDocument()
    expect(within(called).queryByRole('button', { name: /Ghi cuộc gọi/ })).not.toBeInTheDocument()
    // Hồ sơ đã hoàn tất mà chưa gọi: bảng Vừa xong có nhãn và nút.
    const row = screen.getByRole('row', { name: /Người Bệnh Xong Chưa Gọi/ })
    expect(within(row).getByText('Chưa gọi xác nhận')).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: /Ghi cuộc gọi/ })).toBeInTheDocument()

    await user.click(within(card).getByRole('button', { name: /Ghi cuộc gọi/ }))
    const dialog = await screen.findByRole('dialog', { name: /Ghi cuộc gọi · Người Bệnh Chưa Gọi/ })
    expect(within(dialog).queryByRole('combobox', { name: 'Khoa nhận' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('textbox', { name: 'Giờ gọi' })).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Người nghe máy'), 'Điều dưỡng nghe máy sau (giả lập)')
    await user.click(within(dialog).getByRole('button', { name: /Không chính xác/ }))
    await user.click(within(dialog).getByRole('button', { name: /^(check )?Chính xác$/ }))
    await user.click(within(dialog).getByRole('button', { name: /Lưu cuộc gọi/ }))

    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-5/call`)?.body).toMatchObject({
        method: 'Phone',
        receiverName: 'Điều dưỡng nghe máy sau (giả lập)',
        readBackCorrect: true,
        incorrectReadBacks: 1,
        repeatedTwice: false,
      }),
    )
  })

  it('Ghi lần báo sau khi khoa nhận trả lại: Lưu, gọi sau chỉ cần khoa nhận, báo ngay trên phần mềm', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const returned = record({
      id: 'kq-2',
      status: 'AwaitingNotification',
      receivingDepartmentId: null,
      receivingDepartmentName: null,
      notifiedAtUtc: null,
      actionDueAtUtc: null,
      notifiedOnTime: null,
      patientName: 'Người Bệnh Bị Trả Lại',
      notifications: [
        appCall({
          receivingDepartmentId: 'd-kkb',
          receivingDepartmentName: 'Khoa Khám bệnh',
          rejectedAtUtc: ago(1),
          rejectedByName: 'Điều dưỡng Demo',
          rejectReason: 'Người bệnh đã chuyển khoa',
          callPending: false,
        }),
      ],
      can: { ...none, notify: true, cancel: true },
    })
    const { calls } = mockApi({
      ...common(labSession()),
      [`GET ${base}/sent`]: { serverTimeUtc: now.toISOString(), items: [returned] },
      [`POST ${base}/kq-2/notifications`]: record({ id: 'kq-2', callPending: true, notifications: [appCall()] }),
    })
    renderApp('/ket-qua-nguy-cap/khoa-bao')

    const card = await screen.findByRole('article', { name: /Người Bệnh Bị Trả Lại/ })
    expect(within(card).getByText(/Khoa Khám bệnh trả lại lúc .*: Người bệnh đã chuyển khoa\. Gọi lại đúng khoa\./)).toBeInTheDocument()
    await user.click(within(card).getByRole('button', { name: 'Ghi lần báo' }))
    const dialog = await screen.findByRole('dialog')
    // Lỡ bấm Lưu, đã báo: báo thiếu người nhận, đọc lại; bấm Lưu, gọi sau thì chỉ còn báo thiếu khoa nhận.
    await user.click(within(dialog).getByRole('button', { name: 'Lưu, đã báo' }))
    expect(await within(dialog).findByText('Nhập họ tên người nhận.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Lưu, gọi sau' }))
    await waitFor(() => expect(within(dialog).queryByText('Nhập họ tên người nhận.')).not.toBeInTheDocument())
    expect(within(dialog).queryByText('Chọn kết quả người nhận đọc lại.')).not.toBeInTheDocument()
    expect(within(dialog).getByText('Chọn khoa nhận.')).toBeInTheDocument()
    expect(calls.some((c) => c.path === `${base}/kq-2/notifications`)).toBe(false)
    await pick(user, dialog, 'Khoa nhận', 'Khoa Nội tổng hợp')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu, gọi sau' }))

    await waitFor(() =>
      expect(calls.find((c) => c.path === `${base}/kq-2/notifications`)?.body).toEqual({ receivingDepartmentId: 'd-noi', method: 'App' }),
    )
    expect(await screen.findByText(/Đã báo Khoa Nội tổng hợp trên phần mềm\. Gọi cho khoa rồi bấm Ghi cuộc gọi/)).toBeInTheDocument()
  })

  it('Gửi đến khoa tôi: hồ sơ báo trên phần mềm hiện ngay với nút Đã nhận thông báo; dải đỏ ghi báo trên phần mềm', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const waiting = record({ callPending: true, notifications: [appCall()], can: { ...none, acknowledge: true, reject: true } })
    const { calls } = mockApi({
      ...common(nurseSession()),
      [`GET ${base}/alerts`]: alerts({ awaitingAcknowledgement: [alertItem({ receiverName: null, callPending: true })] }),
      [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [waiting] },
      [`POST ${base}/kq-1/acknowledge`]: record({
        status: 'AwaitingAction',
        callPending: true,
        notifications: [appCall()],
        acknowledgedByName: 'Điều dưỡng Demo',
        acknowledgedAtUtc: now.toISOString(),
      }),
    })
    renderApp('/ket-qua-nguy-cap/khoa-nhan')

    const banner = await screen.findByRole('region', { name: 'Cảnh báo kết quả nguy cấp' })
    expect(banner).toHaveTextContent(/Khoa Huyết học – Sinh hóa báo trên phần mềm lúc \d\d:\d\d/)
    const card = await screen.findByRole('article', { name: 'Người Bệnh Giả Lập' })
    expect(within(card).getByText('Chưa gọi xác nhận')).toBeInTheDocument()
    expect(within(card).getByText(/Báo trên phần mềm: khoa báo sẽ gọi sau để ghi người nhận thông tin/)).toBeInTheDocument()
    expect(within(card).queryByText(/Người nghe máy:/)).not.toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: 'Đã nhận cuộc gọi' })).not.toBeInTheDocument()
    await user.click(within(card).getByRole('button', { name: 'Đã nhận thông báo' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === `${base}/kq-1/acknowledge`)).toBe(true))
  })

  it('Màn hình TV: hồ sơ báo trên phần mềm ghi rõ, không có tên người nghe máy', { timeout: 30_000 }, async () => {
    mockApi({ ...common(screenSession()), [`GET ${base}/alerts`]: alerts({ awaitingAcknowledgement: [alertItem({ receiverName: null, callPending: true })] }) })
    renderApp('/man-hinh-kqnc')

    const card = await screen.findByRole('article', { name: /N\.B\.G\.L|Người Bệnh Giả Lập/ })
    expect(card).toHaveTextContent('Khoa Huyết học – Sinh hóa · Kỹ thuật viên Demo báo trên phần mềm')
    expect(card).not.toHaveTextContent('nghe máy')
  })

  it('Chi tiết hồ sơ chưa gọi: nhãn, diễn biến ghi báo trên phần mềm; khoa báo có nút Ghi cuộc gọi', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(labSession()),
      [`GET ${base}/kq-7`]: record({ id: 'kq-7', callPending: true, notifications: [appCall()], can: { ...none, cancel: true, confirmCall: true } }),
    })
    renderApp('/ket-qua-nguy-cap/ho-so/kq-7')

    expect(await screen.findByText(/Lần báo 1: báo trên phần mềm cho Khoa Nội tổng hợp/)).toBeInTheDocument()
    expect(screen.getByText('Chưa gọi xác nhận người nhận thông tin.')).toBeInTheDocument()
    expect(screen.getByText('Chưa gọi xác nhận')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Ghi cuộc gọi/ })).toBeInTheDocument()
  })

  it('Chi tiết hồ sơ: khoa nhận bấm Đã nhận thông báo; đồng hồ bác sĩ tính từ giờ báo trên phần mềm', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...common(nurseSession()),
      [`GET ${base}/kq-7`]: record({ id: 'kq-7', callPending: true, notifications: [appCall()], can: { ...none, acknowledge: true, reject: true } }),
      [`POST ${base}/kq-7/acknowledge`]: record({ id: 'kq-7', status: 'AwaitingAction', callPending: true, notifications: [appCall()] }),
    })
    renderApp('/ket-qua-nguy-cap/ho-so/kq-7')

    expect(await screen.findByText(/10 phút từ giờ báo khoa nhận \(\d\d:\d\d\)\. Khoa báo đã báo trên phần mềm, sẽ gọi sau/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Ghi cuộc gọi/ })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Đã nhận thông báo' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === `${base}/kq-7/acknowledge`)).toBe(true))
    expect(await screen.findByText('Đã xác nhận đã nhận. Báo ngay bác sĩ điều trị.')).toBeInTheDocument()
  })

  it('Chi tiết hồ sơ đã gọi xác nhận: diễn biến có lần báo trên phần mềm, cuộc gọi ghi sau và người nhận', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(nurseSession()),
      [`GET ${base}/kq-1`]: record({
        status: 'AwaitingAction',
        acknowledgedByName: 'Điều dưỡng Demo',
        acknowledgedAtUtc: ago(2),
        notifications: [
          call({
            // Giờ gọi theo đồng hồ máy trạm, sớm hơn giờ báo (lúc lưu): diễn biến vẫn ghi cuộc gọi sau lần báo.
            calledAtUtc: ago(3.5),
            callRecordedByName: 'Kỹ thuật viên Demo',
            callRecordedAtUtc: ago(1),
            receiverName: 'Điều dưỡng nghe máy sau (giả lập)',
          }),
        ],
      }),
    })
    renderApp('/ket-qua-nguy-cap/ho-so/kq-1')

    const notified = await screen.findByText(/Lần báo 1: báo trên phần mềm cho Khoa Nội tổng hợp/)
    const confirmed = screen.getByText('Gọi xác nhận người nhận (gọi điện)')
    expect(notified.compareDocumentPosition(confirmed) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByText(/^Kỹ thuật viên Demo · ghi lúc/)).toBeInTheDocument()
    expect(screen.getByText('Người nhận Điều dưỡng nghe máy sau (giả lập); đọc lại chính xác.')).toBeInTheDocument()
    expect(screen.getByText('Khoa xác nhận đã nhận thông báo')).toBeInTheDocument()
    expect(screen.queryByText('Chưa gọi xác nhận')).not.toBeInTheDocument()
    expect(screen.queryByText('Chưa gọi xác nhận người nhận thông tin.')).not.toBeInTheDocument()
  })
})

describe('Kết quả nguy cấp: khoa của người bệnh cho tài khoản xem nhiều khoa (quyết định 63)', () => {
  const kkb = { receivingDepartmentId: 'd-kkb', receivingDepartmentName: 'Khoa Khám bệnh' }
  const twoWards = (permissions: string[]) =>
    session({
      id: 'u-hai-khoa',
      userName: 'haikhoa.demo',
      fullName: 'Tài khoản Hai Khoa',
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      departments: [
        { id: 'd-noi', name: 'Khoa Nội tổng hợp' },
        { id: 'd-kkb', name: 'Khoa Khám bệnh' },
      ],
      roles: [{ id: 'r-hai-khoa', code: 'X', name: 'Vai trò' }],
      permissions,
    })

  it('QLCL xem mọi khoa: mỗi thẻ ở Gửi đến khoa tôi ghi khoa của người bệnh', { timeout: 30_000 }, async () => {
    const waiting = record({ id: 'kq-1', patientName: 'Người Bệnh Khoa Nội', can: none })
    const doctor = record({
      id: 'kq-2',
      status: 'AwaitingAction',
      patientName: 'Người Bệnh Khoa Khám',
      acknowledgedByName: 'Điều dưỡng Demo',
      acknowledgedAtUtc: ago(4),
      can: none,
      ...kkb,
    })
    const done = record({ id: 'kq-3', status: 'Completed', patientName: 'Người Bệnh Đã Xong', actionAtUtc: ago(1), actedOnTime: true, can: none, ...kkb })
    mockApi({ ...common(qualitySession()), [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [waiting, doctor, done] } })
    renderApp('/ket-qua-nguy-cap/khoa-nhan')

    expect(within(await screen.findByRole('article', { name: 'Người Bệnh Khoa Nội' })).getByText('Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(within(screen.getByRole('article', { name: 'Người Bệnh Khoa Khám' })).getByText('Khoa Khám bệnh')).toBeInTheDocument()
    // Thẻ "Vừa xong" cũng ghi khoa: một lần ở thẻ chờ bác sĩ, một lần ở hồ sơ vừa xong.
    expect(screen.getAllByText('Khoa Khám bệnh')).toHaveLength(2)
  })

  it('Tài khoản thuộc hai khoa: dải đỏ ghi khoa của người bệnh và các khoa đang có hồ sơ chờ', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(twoWards([Permissions.CriticalReceive])),
      [`GET ${base}/alerts`]: alerts({
        receivingDepartmentNames: ['Khoa Nội tổng hợp', 'Khoa Khám bệnh'],
        awaitingAcknowledgement: [alertItem(), alertItem({ id: 'kq-9', patientName: 'Người Bệnh Khoa Khám', ...kkb }), alertItem({ id: 'kq-10', patientName: 'Người Bệnh Thứ Ba' })],
      }),
    })
    renderApp('/')

    const banner = await screen.findByRole('region', { name: 'Cảnh báo kết quả nguy cấp' })
    expect(banner).toHaveTextContent('3 kết quả nguy cấp chờ khoa xác nhận · Khoa Nội tổng hợp, Khoa Khám bệnh')
    expect(banner).toHaveTextContent(/Người Bệnh Giả Lập \(Khoa Nội tổng hợp\) · Kali \(K\+\) \(giả lập\) 6,8 mmol\/L · Khoa Huyết học – Sinh hóa báo lúc/)
  })

  it('Tài khoản thuộc hai khoa: dải cam bác sĩ quá hạn ghi các khoa có hồ sơ quá hạn', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(twoWards([Permissions.CriticalReceive])),
      [`GET ${base}/alerts`]: alerts({
        awaitingAction: [
          alertItem({ id: 'kq-4', status: 'AwaitingAction', actionDueAtUtc: ago(2), ...kkb }),
          alertItem({ id: 'kq-5', status: 'AwaitingAction', actionDueAtUtc: ago(-5) }),
        ],
      }),
    })
    renderApp('/')

    const banner = await screen.findByRole('region', { name: 'Cảnh báo kết quả nguy cấp' })
    expect(banner).toHaveTextContent('1 kết quả nguy cấp bác sĩ quá hạn xử trí · Khoa Khám bệnh')
  })

  it('Màn hình theo dõi dùng chung hai khoa (quyết định 66): ô Khoa chỉ có hai khoa, chạy lướt hai khoa, dải đỏ vẫn tính cả hai khoa', { timeout: 30_000 }, async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
      const waiting = [alertItem({ patientName: 'N.B.N' }), alertItem({ id: 'kq-9', patientName: 'N.B.K', ...kkb })]
      const { calls } = mockApi({
        ...common(twoWards([Permissions.CriticalDisplay])),
        [`GET ${base}/alerts`]: (c: ApiCall) => {
          const id = new URLSearchParams(c.search).get('departmentId')
          return alerts({
            receivingDepartmentNames: id ? [departments.find((d) => d.id === id)!.name] : ['Khoa Nội tổng hợp', 'Khoa Khám bệnh'],
            awaitingAcknowledgement: id ? waiting.filter((a) => a.receivingDepartmentId === id) : waiting,
          })
        },
      })
      // Địa chỉ trang trỏ khoa không phải của mình: vẫn xem cả hai khoa của mình.
      const { router } = renderApp('/man-hinh-kqnc?khoa=d-hsh')
      const query = () => new URLSearchParams(router.state.location.search)

      expect(await screen.findByRole('article', { name: /N\.B\.K/ })).toBeInTheDocument()
      expect(screen.getByRole('article', { name: /N\.B\.N/ })).toBeInTheDocument()
      expect(calls.some((c) => c.search.includes('d-hsh'))).toBe(false)
      await user.click(screen.getByRole('combobox', { name: 'Khoa' }))
      await waitFor(() => expect(document.querySelectorAll('.ant-select-item-option').length).toBe(2))
      expect(Array.from(document.querySelectorAll('.ant-select-item-option'), (o) => o.getAttribute('title'))).toEqual([
        'Khoa Khám bệnh',
        'Khoa Nội tổng hợp',
      ])
      await user.keyboard('{Escape}')

      await user.click(screen.getByRole('button', { name: /Chạy lướt từng khoa/ }))
      expect(await screen.findByText('Khoa 1/2 · mỗi khoa 15 giây')).toBeInTheDocument()
      expect(query().get('khoa')).toBe('d-kkb')
      await waitFor(() => expect(screen.queryByRole('article', { name: /N\.B\.N/ })).not.toBeInTheDocument())
      act(() => {
        vi.advanceTimersByTime(15_000)
      })
      expect(query().get('khoa')).toBe('d-noi')
      expect(await screen.findByText('Khoa 2/2 · mỗi khoa 15 giây')).toBeInTheDocument()
      expect(await screen.findByRole('article', { name: /N\.B\.N/ })).toBeInTheDocument()
      // Dải đỏ tính cả hai khoa; chỉ gọi khoa của mình.
      expect(screen.getByText(/KẾT QUẢ NGUY CẤP MỚI · 2 hồ sơ chờ khoa xác nhận · Khoa Nội tổng hợp, Khoa Khám bệnh/)).toBeInTheDocument()
      expect(calls.filter((c) => c.path === `${base}/alerts`).every((c) => c.search === '' || /departmentId=d-(noi|kkb)$/.test(c.search))).toBe(true)
      expect(calls.some((c) => c.path === `${base}/alerts` && c.search === '?departmentId=d-noi')).toBe(true)
    } finally {
      vi.useRealTimers()
    }
  })

  it('Màn hình theo dõi của tài khoản nhiều khoa ghi khoa của người bệnh trên mỗi thẻ', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(twoWards([Permissions.CriticalDisplay])),
      [`GET ${base}/alerts`]: alerts({
        receivingDepartmentNames: ['Khoa Nội tổng hợp', 'Khoa Khám bệnh'],
        awaitingAcknowledgement: [alertItem({ patientName: 'N.B.G.L' })],
        awaitingAction: [
          alertItem({ id: 'kq-3', status: 'AwaitingAction', patientName: 'N.B.K', acknowledgedByName: 'Điều dưỡng Demo', acknowledgedAtUtc: ago(4), ...kkb }),
        ],
      }),
    })
    renderApp('/man-hinh-kqnc')

    expect(within(await screen.findByRole('article', { name: /N\.B\.G\.L/ })).getByText('Khoa Nội tổng hợp')).toBeInTheDocument()
    expect(within(screen.getByRole('article', { name: /N\.B\.K/ })).getByText('Khoa Khám bệnh')).toBeInTheDocument()
  })
})

describe('Kết quả nguy cấp: QLCL', () => {
  const dashboard: CriticalDashboard = {
    period: 'month',
    month: now.format('YYYY-MM'),
    label: `Tháng ${now.format('MM/YYYY')}`,
    from: now.startOf('month').format('YYYY-MM-DD'),
    to: now.format('YYYY-MM-DD'),
    serverTimeUtc: now.toISOString(),
    targets: { notifyPercent: 95, actionPercent: 90, readBackPercent: 98, completePercent: 98, notifyMinutes: 10, actionMinutes: 10 },
    kpis: {
      total: 5,
      cancelled: 1,
      notifyOnTime: { numerator: 2, denominator: 4, percent: 50 },
      actionOnTime: { numerator: 9, denominator: 9, percent: 100 },
      readBackFirstTime: { numerator: 2, denominator: 3, percent: 66.7 },
      complete: { numerator: 0, denominator: 0, percent: null },
      medianNotifyMinutes: 7,
      notifyQ1Minutes: 6,
      notifyQ3Minutes: 11,
      medianActionMinutes: 16,
      actionQ1Minutes: 11.5,
      actionQ3Minutes: 20.5,
    },
    trend: Array.from({ length: 12 }, (_, i) => ({
      month: now.subtract(11 - i, 'month').format('YYYY-MM'),
      total: i === 11 ? 5 : 0,
      notifyOnTimePercent: i === 11 ? 50 : null,
      actionOnTimePercent: i === 11 ? 100 : null,
    })),
    senders: [{ departmentId: 'd-hsh', name: 'Khoa Huyết học – Sinh hóa', total: 4, onTime: { numerator: 1, denominator: 3, percent: 33.3 }, medianMinutes: 10, open: 2 }],
    receivers: [{ departmentId: 'd-noi', name: 'Khoa Nội tổng hợp', total: 2, onTime: { numerator: 1, denominator: 2, percent: 50 }, medianMinutes: 16, open: 0 }],
    late: [
      {
        id: 'kq-9',
        number: 'KQNC-2026-000009',
        resultAtUtc: ago(30),
        patientInitials: 'N.B.B',
        patientCode: 'NB-GL-0003',
        testName: potassium.name,
        value: '6,8',
        unit: 'mmol/L',
        sendingDepartmentName: 'Khoa Huyết học – Sinh hóa',
        receivingDepartmentName: null,
        miss: 'Chưa báo, quá hạn 20 phút',
      },
    ],
  }

  it('Dashboard: tỷ lệ so với mục tiêu (kèm biểu tượng và chữ), trung vị, theo khoa, trường hợp quá hạn mở được hồ sơ', { timeout: 30_000 }, async () => {
    mockApi({ ...common(qualitySession()), [`GET ${base}/dashboard`]: dashboard })
    renderApp('/ket-qua-nguy-cap/dashboard')

    const notify = await screen.findByRole('group', { name: 'Báo trong 10 phút' })
    expect(within(notify).getByText('50%')).toBeInTheDocument()
    expect(within(notify).getByText(/2\/4 · chưa đạt mục tiêu ≥ 95%/)).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'BS xử trí trong 10 phút' })).getByText(/9\/9 · đạt mục tiêu ≥ 90%/)).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Hồ sơ đủ các bước' })).getByText('Chưa có hồ sơ tới hạn · mục tiêu ≥ 98%')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Kết quả nguy cấp' })).getByText('1 hồ sơ đã hủy (đếm riêng)')).toBeInTheDocument()
    expect(screen.getByText('(Q1–Q3: 6–11 phút)')).toBeInTheDocument()
    const late = screen.getByText('Chưa báo, quá hạn 20 phút').closest('tr') as HTMLElement
    expect(within(late).getByText('N.B.B')).toBeInTheDocument()
    expect(within(late).getByRole('link', { name: 'Xem hồ sơ' })).toHaveAttribute('href', '/ket-qua-nguy-cap/ho-so/kq-9')
    expect(screen.getByRole('cell', { name: 'Khoa Huyết học – Sinh hóa' })).toBeInTheDocument()
  })

  /** Dashboard một khoa (quyết định 64): máy chủ trả số của riêng khoa đó kèm tên khoa (số giả lập). */
  const byDepartment = (overall: CriticalDashboard) => (c: ApiCall) => {
    const id = new URLSearchParams(c.search).get('departmentId')
    if (!id) return overall
    const totals: Record<string, number> = { 'd-hsh': 4, 'd-kkb': 1, 'd-noi': 2 }
    return {
      ...overall,
      departmentId: id,
      departmentName: departments.find((d) => d.id === id)?.name ?? null,
      kpis: { ...overall.kpis, total: totals[id] ?? 0 },
    }
  }
  const kkbReceiver = {
    departmentId: 'd-kkb',
    name: 'Khoa Khám bệnh',
    total: 1,
    onTime: { numerator: 1, denominator: 1, percent: 100 },
    medianMinutes: 12,
    open: 0,
  }
  const totalTile = () => screen.getByRole('group', { name: 'Kết quả nguy cấp' })

  it('Dashboard từng khoa (quyết định 64): chọn khoa thì xem số của riêng khoa đó, bỏ chọn thì về toàn viện', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({ ...common(qualitySession()), [`GET ${base}/dashboard`]: byDepartment(dashboard) })
    renderApp('/ket-qua-nguy-cap/dashboard')

    expect(within(await screen.findByRole('group', { name: 'Kết quả nguy cấp' })).getByText('5')).toBeInTheDocument()
    expect(screen.getByText(/· mọi khoa ·/)).toBeInTheDocument()

    await pick(user, document.body, 'Khoa', 'Khoa Nội tổng hợp')
    expect(await screen.findByRole('heading', { name: 'Khoa Nội tổng hợp', level: 4 })).toBeInTheDocument()
    await waitFor(() => expect(within(totalTile()).getByText('2')).toBeInTheDocument())
    expect(screen.getByText(/Khoa Nội tổng hợp: hồ sơ khoa này báo đi và hồ sơ gửi đến khoa này/)).toBeInTheDocument()
    expect(calls.filter((c) => c.path === `${base}/dashboard`).map((c) => c.search)).toContain(
      `?month=${now.format('YYYY-MM')}&period=month&departmentId=d-noi`,
    )

    await user.click(document.querySelector('.ant-select-clear')!)
    expect(await screen.findByText(/· mọi khoa ·/)).toBeInTheDocument()
    expect(within(totalTile()).getByText('5')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Khoa Nội tổng hợp' })).not.toBeInTheDocument()
  })

  it(
    'Chạy lướt từng khoa (quyết định 64): lần lượt các khoa có hồ sơ trong kỳ theo tên; tạm dừng, đổi số giây, dừng; tự chọn khoa thì thôi chạy lướt',
    { timeout: 30_000 },
    async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true })
      try {
        const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
        mockApi({
          ...common(qualitySession()),
          [`GET ${base}/dashboard`]: byDepartment({ ...dashboard, receivers: [...dashboard.receivers, kkbReceiver] }),
        })
        const { router } = renderApp('/ket-qua-nguy-cap/dashboard')
        const query = () => new URLSearchParams(router.state.location.search)
        const wait = (seconds: number) =>
          act(() => {
            vi.advanceTimersByTime(seconds * 1000)
          })
        const showing = async (name: string, status: string) => {
          const region = await screen.findByRole('region', { name: 'Chạy lướt từng khoa' })
          expect(await within(region).findByRole('heading', { name })).toBeInTheDocument()
          expect(within(region).getByText(status)).toBeInTheDocument()
          return region
        }

        await user.click(await screen.findByRole('button', { name: /Chạy lướt từng khoa/ }))
        // Theo tên: Huyết học – Sinh hóa (khoa báo), Khám bệnh, Nội tổng hợp (khoa nhận).
        await showing('Khoa Huyết học – Sinh hóa', 'Khoa 1/3 · mỗi khoa 15 giây')
        expect(query().get('chay')).toBe('15')
        expect(within(totalTile()).getByText('4')).toBeInTheDocument()
        wait(14)
        expect(query().get('khoa')).toBe('d-hsh')
        wait(1)
        await showing('Khoa Khám bệnh', 'Khoa 2/3 · mỗi khoa 15 giây')
        wait(15)
        await showing('Khoa Nội tổng hợp', 'Khoa 3/3 · mỗi khoa 15 giây')
        wait(15)
        let region = await showing('Khoa Huyết học – Sinh hóa', 'Khoa 1/3 · mỗi khoa 15 giây')

        await user.click(within(region).getByRole('button', { name: /Tạm dừng/ }))
        expect(within(region).getByText('Khoa 1/3 · đã tạm dừng')).toBeInTheDocument()
        wait(60)
        expect(query().get('khoa')).toBe('d-hsh')

        await user.click(within(region).getByRole('button', { name: /Tiếp tục/ }))
        await pick(user, region, 'Mỗi khoa', 'Mỗi khoa 10 giây')
        expect(query().get('chay')).toBe('10')
        wait(10)
        region = await showing('Khoa Khám bệnh', 'Khoa 2/3 · mỗi khoa 10 giây')

        await user.click(within(region).getByRole('button', { name: /Dừng chạy lướt/ }))
        expect(screen.queryByRole('region', { name: 'Chạy lướt từng khoa' })).not.toBeInTheDocument()
        expect(query().get('chay')).toBeNull()
        expect(screen.getByRole('heading', { name: 'Khoa Khám bệnh', level: 4 })).toBeInTheDocument()
        wait(30)
        expect(query().get('khoa')).toBe('d-kkb')

        // Chạy lại thì bắt đầu từ khoa đang xem; tự chọn một khoa thì thôi chạy lướt.
        await user.click(screen.getByRole('button', { name: /Chạy lướt từng khoa/ }))
        await showing('Khoa Khám bệnh', 'Khoa 2/3 · mỗi khoa 15 giây')
        await pick(user, document.body, 'Khoa', 'Khoa Nội tổng hợp')
        expect(query().get('chay')).toBeNull()
        expect(query().get('khoa')).toBe('d-noi')
        expect(screen.queryByRole('region', { name: 'Chạy lướt từng khoa' })).not.toBeInTheDocument()
        expect(await screen.findByRole('heading', { name: 'Khoa Nội tổng hợp', level: 4 })).toBeInTheDocument()
      } finally {
        vi.useRealTimers()
      }
    },
  )

  it('Tài khoản một khoa (quyết định 65): xem dashboard của khoa mình, không có toàn viện, không chọn khoa khác', { timeout: 30_000 }, async () => {
    const { calls } = mockApi({ ...common(nurseSession()), [`GET ${base}/dashboard`]: byDepartment(dashboard) })
    // Địa chỉ trang trỏ sang khoa khác: vẫn chỉ xem khoa của mình.
    renderApp('/ket-qua-nguy-cap/dashboard?khoa=d-hsh')

    expect(await screen.findByRole('heading', { name: 'Khoa Nội tổng hợp', level: 4 })).toBeInTheDocument()
    await waitFor(() => expect(within(totalTile()).getByText('2')).toBeInTheDocument())
    expect(screen.getByRole('tab', { name: 'Dashboard' })).toHaveAttribute('aria-selected', 'true')
    const requested = calls.filter((c) => c.path === `${base}/dashboard`).map((c) => new URLSearchParams(c.search).get('departmentId'))
    expect(requested.length).toBeGreaterThan(0)
    expect(requested.every((id) => id === 'd-noi')).toBe(true)
    expect(screen.queryByRole('combobox', { name: 'Khoa' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Chạy lướt từng khoa/ })).not.toBeInTheDocument()
    expect(screen.queryByText(/· mọi khoa ·/)).not.toBeInTheDocument()
  })

  it('Tài khoản hai khoa (quyết định 65): ô Khoa chỉ có hai khoa của mình, không bỏ chọn được; chạy lướt hai khoa đó', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const twoWards = session({
      id: 'u-hai-khoa',
      userName: 'haikhoa.demo',
      fullName: 'Điều dưỡng Hai Khoa',
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      departments: [
        { id: 'd-noi', name: 'Khoa Nội tổng hợp' },
        { id: 'd-kkb', name: 'Khoa Khám bệnh' },
      ],
      roles: [{ id: 'r-hai-khoa', code: 'X', name: 'Vai trò' }],
      permissions: [Permissions.CriticalReceive],
    })
    const { calls } = mockApi({ ...common(twoWards), [`GET ${base}/dashboard`]: byDepartment(dashboard) })
    renderApp('/ket-qua-nguy-cap/dashboard')

    expect(await screen.findByRole('heading', { name: 'Khoa Nội tổng hợp', level: 4 })).toBeInTheDocument()
    await user.click(screen.getByRole('combobox', { name: 'Khoa' }))
    await waitFor(() => expect(document.querySelectorAll('.ant-select-item-option').length).toBeGreaterThan(0))
    const options = Array.from(document.querySelectorAll('.ant-select-item-option'), (o) => o.getAttribute('title'))
    expect(options).toEqual(['Khoa Khám bệnh', 'Khoa Nội tổng hợp'])
    expect(document.querySelector('.ant-select-clear')).toBeNull()
    await pick(user, document.body, 'Khoa', 'Khoa Khám bệnh')
    expect(await screen.findByRole('heading', { name: 'Khoa Khám bệnh', level: 4 })).toBeInTheDocument()
    await waitFor(() => expect(within(totalTile()).getByText('1')).toBeInTheDocument())

    // Chạy lướt: hai khoa của mình, xếp theo tên, bắt đầu từ khoa đang xem.
    await user.click(screen.getByRole('button', { name: /Chạy lướt từng khoa/ }))
    const region = await screen.findByRole('region', { name: 'Chạy lướt từng khoa' })
    expect(await within(region).findByText('Khoa 1/2 · mỗi khoa 15 giây')).toBeInTheDocument()
    expect(within(region).getByRole('heading', { name: 'Khoa Khám bệnh' })).toBeInTheDocument()
    expect(calls.filter((c) => c.path === `${base}/dashboard`).every((c) => /departmentId=d-(noi|kkb)/.test(c.search))).toBe(true)
  })

  it('Chạy lướt: mở thẳng địa chỉ đã lưu thì chạy tiếp (mỗi khoa 30 giây); kỳ chưa có hồ sơ thì không chạy lướt được', { timeout: 30_000 }, async () => {
    mockApi({
      ...common(qualitySession()),
      [`GET ${base}/dashboard`]: byDepartment({ ...dashboard, receivers: [...dashboard.receivers, kkbReceiver] }),
    })
    const first = renderApp('/ket-qua-nguy-cap/dashboard?khoa=d-noi&chay=30')
    const region = await screen.findByRole('region', { name: 'Chạy lướt từng khoa' })
    expect(await within(region).findByText('Khoa 3/3 · mỗi khoa 30 giây')).toBeInTheDocument()
    expect(within(region).getByRole('heading', { name: 'Khoa Nội tổng hợp' })).toBeInTheDocument()
    first.unmount()

    mockApi({ ...common(qualitySession()), [`GET ${base}/dashboard`]: { ...dashboard, senders: [], receivers: [], late: [] } })
    renderApp('/ket-qua-nguy-cap/dashboard')
    expect(await screen.findAllByText('Chưa có hồ sơ trong kỳ.')).toHaveLength(2)
    expect(screen.getByRole('button', { name: /Chạy lướt từng khoa/ })).toBeDisabled()
  })

  it('Sổ: đúng cột của mẫu; dòng đã hủy gạch ngang kèm ghi chú', { timeout: 30_000 }, async () => {
    const { calls } = mockApi({
      ...common(qualitySession()),
      [`GET ${base}/logbook`]: {
        form: 'Clinical',
        code: 'QT-KQNC/ST02',
        departmentId: 'd-noi',
        departmentName: 'Khoa Nội tổng hợp',
        month: now.format('YYYY-MM'),
        columns: ['Ngày chỉ định', 'Thông tin NB (họ tên, ngày sinh)', 'Tên xét nghiệm'],
        rows: [
          { cells: ['27/11/2026', 'Người Bệnh Một, 02/01/1960', 'Kali (K+) (giả lập)'], cancelled: false, note: null },
          { cells: ['27/11/2026', 'Người Bệnh Năm, 02/01/1960', 'Kali (K+) (giả lập)'], cancelled: true, note: 'Đã hủy 09:50 27/11/2026 (KTV): Nhập nhầm người bệnh' },
        ],
      },
    })
    renderApp('/ket-qua-nguy-cap/so?mau=Clinical&khoa=d-noi')

    expect(await screen.findByText('QT-KQNC/ST02')).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Thông tin NB (họ tên, ngày sinh)' })).toBeInTheDocument()
    const struck = screen.getByText('Người Bệnh Năm, 02/01/1960')
    expect(struck).toHaveStyle({ textDecoration: 'line-through' })
    expect(screen.getByText('Người Bệnh Một, 02/01/1960')).not.toHaveStyle({ textDecoration: 'line-through' })
    expect(screen.getByText('Đã hủy 09:50 27/11/2026 (KTV): Nhập nhầm người bệnh')).toBeInTheDocument()
    expect(calls.find((c) => c.path === `${base}/logbook`)?.search).toBe(`?form=Clinical&departmentId=d-noi&month=${now.format('YYYY-MM')}`)
  })

  it('Thông số: ô đã sửa có nhãn, lưu gửi đủ thông số; thêm mục danh mục', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...common(qualitySession()),
      [`PUT ${base}/settings`]: (c) => ({ ...settings, values: c.body, updatedAtUtc: now.toISOString(), updatedBy: 'QLCL Demo (qlcl.demo)' }),
      [`POST ${base}/tests`]: (c) => ({ ...potassium, id: 't-new', ...(c.body as object) }),
    })
    renderApp('/ket-qua-nguy-cap/danh-muc')

    const notify = await screen.findByRole('spinbutton', { name: /Thời hạn báo kết quả/ })
    expect(screen.getByRole('button', { name: 'Lưu thay đổi' })).toBeDisabled()
    await user.clear(notify)
    await user.type(notify, '15')
    expect(await screen.findByText('đã sửa')).toBeInTheDocument()
    expect(screen.getByText('1 thông số chưa lưu')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ ...settings.values, notifyMinutes: 15 }))

    await user.click(screen.getByRole('tab', { name: 'Danh mục kết quả nguy cấp' }))
    await user.click(await screen.findByRole('button', { name: /Thêm mục/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Xét nghiệm, kỹ thuật'), 'Natri (giả lập)')
    await user.type(within(dialog).getByLabelText('Khoảng nguy cấp: người lớn'), 'Ví dụ: ≤ 120 mmol/L')
    await user.type(within(dialog).getByLabelText('Đơn vị'), 'mmol/L')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))
    await waitFor(() =>
      expect(calls.find((c) => c.method === 'POST' && c.path === `${base}/tests`)?.body).toEqual({
        name: 'Natri (giả lập)',
        adultRange: 'Ví dụ: ≤ 120 mmol/L',
        childRange: null,
        unit: 'mmol/L',
        section: null,
        logbook: 'Laboratory',
        textResult: false,
        sortOrder: 0,
        isActive: true,
      }),
    )
  })
})

describe('Kết quả nguy cấp: menu', () => {
  const critical = (granted: string[]) =>
    filterNavItems(navItems, (p) => granted.includes(p))
      .find((i) => i.label === 'Kết quả nguy cấp')
      ?.children?.map((c) => c.label)

  it('khoa báo, khoa nhận, QLCL thấy đúng mục; màn hình theo dõi không có menu', () => {
    // Khoa báo, khoa nhận có Dashboard của khoa mình (quyết định 65).
    expect(critical([Permissions.CriticalRecord])).toEqual(['Khoa tôi báo', 'Sổ kết quả nguy cấp', 'Dashboard'])
    expect(critical([Permissions.CriticalReceive])).toEqual(['Gửi đến khoa tôi', 'Sổ kết quả nguy cấp', 'Dashboard'])
    expect(critical([Permissions.CriticalTreat])).toEqual(['Gửi đến khoa tôi', 'Sổ kết quả nguy cấp', 'Dashboard'])
    expect(critical([Permissions.CriticalMonitor, Permissions.CriticalManage])).toEqual([
      'Khoa tôi báo',
      'Gửi đến khoa tôi',
      'Sổ kết quả nguy cấp',
      'Dashboard',
      'Danh mục và cấu hình',
    ])
    expect(critical([Permissions.CriticalDisplay])).toBeUndefined()
  })
})
