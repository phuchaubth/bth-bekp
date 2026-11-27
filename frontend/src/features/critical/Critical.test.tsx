import { screen, waitFor, within } from '@testing-library/react'
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
import { mockApi, problem } from '../../test/mockApi'
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
  ...overrides,
})

const none = { notify: false, acknowledge: false, reject: false, act: false, cancel: false }

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

afterEach(() => {
  vi.restoreAllMocks()
  window.localStorage.clear()
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

  it('Ghi kết quả: lưu, gọi sau gửi hồ sơ không có lần báo; lưu, đã báo phải có người nhận đọc lại chính xác', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...common(labSession()),
      [`POST ${base}`]: (c) =>
        record({
          status: 'AwaitingNotification',
          receivingDepartmentId: null,
          notifiedAtUtc: null,
          notifications: [],
          ...(c.body as object),
        }),
    })
    renderApp('/ket-qua-nguy-cap/ghi')

    await user.type(await screen.findByLabelText('Mã người bệnh'), 'NB-GL-0009')
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
    expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(false)

    await user.click(screen.getByRole('button', { name: 'Lưu, gọi sau' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === base)).toBe(true))
    const body = calls.find((c) => c.method === 'POST' && c.path === base)!.body as Record<string, unknown>
    expect(body).toMatchObject({
      sendingDepartmentId: 'd-hsh',
      testId: 't-k',
      value: '6,8',
      unit: 'mmol/L',
      urgent: false,
      notification: null,
      patient: { code: 'NB-GL-0009', fullName: 'Người Bệnh Mới', dateOfBirth: '1960-01-02', address: 'Ấp Mẫu (giả lập)', source: 'Manual' },
    })
    expect(body.orderDate).toBe(now.format('YYYY-MM-DD'))
    expect(await screen.findByText('Đã lưu hồ sơ KQNC-2026-000001')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Ghi kết quả khác' })).toBeInTheDocument()
  })

  it('Ghi kết quả Vi sinh: kết quả bằng chữ, bắt buộc SID và người nhận nhắc lại 2 lần', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    mockApi(common(labSession()))
    renderApp('/ket-qua-nguy-cap/ghi')

    await pick(user, document.body, 'Xét nghiệm, kỹ thuật (danh mục kết quả nguy cấp)', 'Cấy máu (giả lập) · Vi sinh')
    expect(await screen.findByLabelText('Kết quả (ghi bằng chữ)')).toBeInTheDocument()
    expect(screen.queryByLabelText('Đơn vị')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Mẫu khẩn')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Lưu, đã báo' }))
    expect(await screen.findByText('Sổ Vi sinh: nhập SID.')).toBeInTheDocument()
    expect(screen.getByText('Sổ Vi sinh: người nhận phải nhắc lại 2 lần.')).toBeInTheDocument()
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
    const { calls } = mockApi({
      ...common(nurseSession()),
      [`GET ${base}/alerts`]: alerts({ awaitingAcknowledgement: [alertItem()] }),
      [`GET ${base}/received`]: { serverTimeUtc: now.toISOString(), items: [waiting, late, cancelled] },
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
    await user.click(within(banner).getByRole('button', { name: /Tắt tiếng/ }))
    expect(window.localStorage.getItem('critical.muted')).toBe('1')
    expect(within(banner).getByRole('button', { name: /Bật tiếng/ })).toBeInTheDocument()

    const card = await screen.findByRole('article', { name: 'Người Bệnh Giả Lập' })
    expect(within(card).getByText(/Người nghe máy: Điều dưỡng nghe máy \(giả lập\) · đọc lại chính xác/)).toBeInTheDocument()
    expect(within(card).queryByRole('button', { name: 'Bác sĩ ghi hướng xử trí' })).not.toBeInTheDocument()
    expect(within(screen.getByRole('article', { name: 'Người Bệnh Quá Hạn' })).getByText('Quá hạn: nhắc lại và báo trực tiếp bác sĩ.')).toBeInTheDocument()
    expect(screen.getByText('Nhập nhầm người bệnh')).toBeInTheDocument()

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
    expect(screen.getByText('QUÁ HẠN · nhắc lại, báo trực tiếp bác sĩ')).toBeInTheDocument()
    expect(screen.getByText('1/1')).toBeInTheDocument()
    expect(await screen.findByText(/tên viết tắt để giữ bí mật thông tin người bệnh/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Bấm để bật chuông/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Đã nhận cuộc gọi' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })

  it('Không cập nhật được thì báo mất kết nối trên toàn màn hình', { timeout: 30_000 }, async () => {
    mockApi({ ...common(screenSession()), [`GET ${base}/alerts`]: () => problem(500, 'Lỗi máy chủ.') })
    renderApp('/man-hinh-kqnc')

    expect(await screen.findByRole('alert')).toHaveTextContent('MẤT KẾT NỐI · MÀN HÌNH KHÔNG CÒN CẬP NHẬT')
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
    expect(critical([Permissions.CriticalRecord])).toEqual(['Khoa tôi báo', 'Sổ kết quả nguy cấp'])
    expect(critical([Permissions.CriticalReceive])).toEqual(['Gửi đến khoa tôi', 'Sổ kết quả nguy cấp'])
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
