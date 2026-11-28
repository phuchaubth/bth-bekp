import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { QcBook, QcBookList, QcCatalog, QcDay, QcLot, QcRun } from '../../api/qc'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, doctorPermissions, session } from '../../test/fixtures'
import { mockApi, problem, type ApiCall } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập cho test (không phải dữ liệu thật). Sổ của tháng này, ngày đầu tháng.
const month = dayjs().format('YYYY-MM')
const day = (n: number) => `${month}-${String(n).padStart(2, '0')}`
const opened = dayjs().subtract(2, 'month').format('YYYY-MM-DD')
const expiry = dayjs().add(1, 'year').format('YYYY-MM-DD')

const lot = (overrides: Partial<QcLot>): QcLot => ({
  id: 's-1',
  kind: 'TestStrip',
  productName: 'Que thử A (giả lập)',
  lotNumber: 'QT-01',
  expiryDate: expiry,
  level: null,
  rangeLow: null,
  rangeHigh: null,
  unit: null,
  isActive: true,
  createdAtUtc: '2026-11-01T01:00:00Z',
  ...overrides,
})

const lots: QcLot[] = [
  lot({}),
  lot({ id: 'c-1', kind: 'ControlSolution', productName: 'Dung dịch chứng A', lotNumber: 'DC-01', level: 1, rangeLow: 40, rangeHigh: 70, unit: 'MgPerDl' }),
  lot({ id: 'c-2', kind: 'ControlSolution', productName: 'Dung dịch chứng A', lotNumber: 'DC-02', level: 2, rangeLow: 100, rangeHigh: 140, unit: 'MgPerDl' }),
]

const control = (level: number, value: number, low: number, high: number, repeatValue: number | null = null) => ({
  lotId: level === 1 ? 'c-1' : 'c-2',
  level,
  lotNumber: level === 1 ? 'DC-01' : 'DC-02',
  expiryDate: expiry,
  openedOn: opened,
  low,
  high,
  unit: 'MgPerDl' as const,
  value,
  passed: value >= low && value <= high,
  repeatValue,
  repeatPassed: repeatValue === null ? null : repeatValue >= low && repeatValue <= high,
})

const run = (overrides: Partial<QcRun> = {}): QcRun => ({
  id: 'r-1',
  runDate: day(1),
  strip: { lotId: 's-1', lotNumber: 'QT-01', expiryDate: expiry, openedOn: opened },
  control1: control(1, 80, 40, 70),
  control2: control(2, 120, 100, 140),
  passed: false,
  failureReason: null,
  hasRepeat: false,
  repeatPassed: null,
  followUpBy: null,
  followUpAtUtc: null,
  performedBy: 'Điều dưỡng Demo',
  recordedAtUtc: '2026-11-01T01:00:00Z',
  late: false,
  replacesRunId: null,
  cancelReason: null,
  cancelledBy: null,
  cancelledAtUtc: null,
  canFollowUp: true,
  canCorrect: true,
  ...overrides,
})

const qcDay = (n: number, overrides: Partial<QcDay> = {}): QcDay => ({
  date: day(n),
  run: null,
  cancelledRuns: [],
  review: null,
  returnedReviews: [],
  suggestedOutcome: 'NoQc',
  allowedOutcomes: ['NoQc'],
  canRecord: false,
  canReview: false,
  canReturn: false,
  ...overrides,
})

const book = (overrides: Partial<QcBook> = {}): QcBook => ({
  id: 'b-1',
  meterId: 'm-1',
  meterLabel: 'Máy đo A (giả lập) GL-0001',
  departmentId: 'd-noi',
  departmentName: 'Khoa Nội tổng hợp',
  year: dayjs().year(),
  month: dayjs().month() + 1,
  status: 'Open',
  submittedBy: null,
  submittedAtUtc: null,
  confirmedBy: null,
  confirmedAtUtc: null,
  returnReason: null,
  returnedBy: null,
  returnedAtUtc: null,
  header: { strips: [], controls1: [], controls2: [] },
  days: [qcDay(1, { canRecord: true })],
  availableLots: lots,
  defaults: { stripLotId: 's-1', stripOpenedOn: opened, control1LotId: 'c-1', control1OpenedOn: opened, control2LotId: 'c-2', control2OpenedOn: opened },
  actions: { record: true, submit: false, review: false, confirm: false, return: false },
  ...overrides,
})

const nurseSession = () =>
  session({
    id: 'u-dieuduong',
    userName: 'dieuduong.demo',
    fullName: 'Điều dưỡng Demo',
    roles: [{ id: 'r-nurse', code: 'NURSE', name: 'Điều dưỡng' }],
    permissions: [...doctorPermissions, Permissions.NursingReportsSubmit, Permissions.QcRecord],
  })

const labSession = () =>
  session({
    id: 'u-hsh',
    userName: 'hsh.demo',
    fullName: 'Khoa HSH Demo',
    departmentId: 'd-hsh',
    departmentName: 'Khoa Huyết học – Sinh hóa',
    roles: [{ id: 'r-lab', code: 'LAB_QC', name: 'Khoa HSH (nội kiểm)' }],
    permissions: [Permissions.QcReview, Permissions.QcManage],
  })

const lastCall = (calls: ApiCall[], method: string, path: string) => calls.filter((c) => c.method === method && c.path === path).at(-1)

/** Hộp thoại theo tiêu đề (bỏ qua nút cùng chữ nằm ngoài hộp thoại). */
const dialogTitled = async (title: string) =>
  (await screen.findAllByText(title)).map((element) => element.closest('[role="dialog"]')).find((dialog) => dialog !== null) as HTMLElement

describe('Sổ nội kiểm đường huyết (khoa ghi kết quả)', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('mở sổ tháng, ghi kết quả với lô điền sẵn, phần mềm tự đánh giá', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const list: QcBookList = {
      month,
      items: [
        {
          bookId: null,
          meterId: 'm-1',
          meterLabel: 'Máy đo A (giả lập) GL-0001',
          departmentId: 'd-noi',
          departmentName: 'Khoa Nội tổng hợp',
          status: null,
          recordedDays: 0,
          failedDays: 0,
          reviewedDays: 0,
          daysToReview: 0,
          canOpen: true,
        },
      ],
    }
    const recorded = book({ days: [qcDay(1, { run: run(), suggestedOutcome: 'FailedNotRepeated', allowedOutcomes: ['FailedNotRepeated'] })] })
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/qc/books': list,
      'POST /api/v1/qc/books': book(),
      'GET /api/v1/qc/books/b-1': book(),
      'POST /api/v1/qc/books/b-1/runs': recorded,
    })
    renderApp('/noi-kiem/so')

    expect(await screen.findByText('Chưa mở sổ')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Mở sổ' }))
    await waitFor(() => expect(lastCall(calls, 'POST', '/api/v1/qc/books')?.body).toEqual({ meterId: 'm-1', month }))
    expect(await screen.findByText(`Sổ nội kiểm tháng ${dayjs().format('MM/YYYY')}`)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Ghi kết quả' }))
    const dialog = await dialogTitled('Ghi kết quả nội kiểm')
    // Lô và ngày mở điền sẵn theo kết quả ghi gần nhất của máy.
    expect(within(dialog).getByTitle(/QT-01 – Que thử A/)).toBeInTheDocument()
    expect(within(dialog).getByTitle(/Level 1: DC-01/)).toBeInTheDocument()
    const values = within(dialog).getAllByRole('spinbutton')
    await user.type(values[0], '80')
    await user.type(values[1], '120')
    // Đánh giá hiện ngay: mức 1 ngoài ngưỡng 40-70, mức 2 trong ngưỡng.
    expect(within(dialog).getByText('Không đạt/ Fail')).toBeInTheDocument()
    expect(within(dialog).getByText('Đạt/ Pass')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Lưu kết quả' }))

    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/runs')?.body).toEqual({
        runDate: day(1),
        stripLotId: 's-1',
        stripOpenedOn: opened,
        control1LotId: 'c-1',
        control1OpenedOn: opened,
        control1Value: 80,
        control2LotId: 'c-2',
        control2OpenedOn: opened,
        control2Value: 120,
      }),
    )
    expect(await screen.findByText('Đã ghi kết quả.')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Ghi nguyên nhân, chạy lại' })).toBeInTheDocument()
  })

  // Bệnh viện gặp lỗi "Chọn hai mức dung dịch chứng khác nhau, mức thấp hơn ở cột đầu." khi bấm Lưu: hai ô có mọi lô nên dễ chọn
  // trùng hoặc ngược mức. Nay ô mức thấp không có lô mức cao nhất, ô mức cao chỉ có lô mức cao hơn ô mức thấp.
  it('hai mức dung dịch chứng: ô mức cao chỉ có lô mức cao hơn, đổi mức thấp thì bỏ lô mức cao không hợp; thiếu mức thì báo ngay', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const level3 = lot({ id: 'c-3', kind: 'ControlSolution', productName: 'Dung dịch chứng A', lotNumber: 'DC-03', level: 3, rangeLow: 280, rangeHigh: 380, unit: 'MgPerDl' })
    const open = async (overrides: Partial<QcBook>) => {
      mockApi({ ...baseRoutes(nurseSession()), 'GET /api/v1/qc/books/b-1': book({ availableLots: [...lots, level3], ...overrides }) })
      const rendered = renderApp('/noi-kiem/so/b-1')
      await user.click(await screen.findByRole('button', { name: 'Ghi kết quả' }))
      return { dialog: await dialogTitled('Ghi kết quả nội kiểm'), unmount: rendered.unmount }
    }
    const selectOf = (dialog: HTMLElement, name: string) => within(dialog).getByRole('combobox', { name }).closest('.ant-select') as HTMLElement
    /** Danh sách lựa chọn của ô vừa mở (danh sách mở sau cùng). */
    const dropdown = async () => within(await waitFor(() => [...document.querySelectorAll<HTMLElement>('.ant-select-dropdown')].at(-1)!))

    // Mức thấp Level 1 (điền sẵn): ô mức cao có Level 2, Level 3, không có Level 1.
    let page = await open({})
    expect(within(page.dialog).queryByText(/chưa có đủ hai mức/)).not.toBeInTheDocument()
    await user.click(within(page.dialog).getByRole('combobox', { name: 'Mức cao' }))
    let options = await dropdown()
    expect(options.getByTitle(/^Level 3: DC-03/)).toBeInTheDocument()
    expect(options.getByTitle(/^Level 2: DC-02/)).toBeInTheDocument()
    expect(options.queryByTitle(/^Level 1: DC-01/)).toBeNull()
    page.unmount()

    // Ô mức thấp không có lô mức cao nhất (Level 3).
    page = await open({ defaults: null })
    await user.click(within(page.dialog).getByRole('combobox', { name: 'Mức thấp (cột đầu)' }))
    options = await dropdown()
    expect(options.getByTitle(/^Level 1: DC-01/)).toBeInTheDocument()
    expect(options.getByTitle(/^Level 2: DC-02/)).toBeInTheDocument()
    expect(options.queryByTitle(/^Level 3: DC-03/)).toBeNull()
    page.unmount()

    // Điền sẵn Level 1 và Level 2; đổi mức thấp sang Level 2 thì ô mức cao (Level 2) bị bỏ chọn, chỉ còn chọn được Level 3.
    page = await open({})
    expect(selectOf(page.dialog, 'Mức cao')).toHaveTextContent('Level 2: DC-02')
    await user.click(within(page.dialog).getByRole('combobox', { name: 'Mức thấp (cột đầu)' }))
    await user.click((await screen.findAllByTitle(/^Level 2: DC-02/)).at(-1)!)
    await waitFor(() => expect(selectOf(page.dialog, 'Mức cao')).not.toHaveTextContent('DC-02'))
    expect(selectOf(page.dialog, 'Mức cao')).toHaveTextContent('Chọn lô')
    page.unmount()

    // Danh mục chỉ có một mức: báo ngay trong hộp, không đợi bấm Lưu.
    page = await open({ availableLots: [lots[0], lots[1]] })
    expect(within(page.dialog).getByText('Danh mục chưa có đủ hai mức dung dịch chứng đang dùng (ví dụ Level 1 và Level 2).')).toBeInTheDocument()
    page.unmount()
  })

  it('không đạt thì ghi nguyên nhân và chạy lại; ghi sai thì ghi điều chỉnh có lý do, kết quả cũ gạch ngang', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const failed = book({ days: [qcDay(1, { run: run() })] })
    const corrected = book({
      days: [
        qcDay(1, {
          run: run({ id: 'r-2', control1: control(1, 55, 40, 70), passed: true, replacesRunId: 'r-1', canFollowUp: false }),
          cancelledRuns: [run({ cancelReason: 'Ghi nhầm kết quả mức 1', cancelledBy: 'Điều dưỡng Demo', canCorrect: false, canFollowUp: false })],
        }),
      ],
    })
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/qc/books/b-1': failed,
      'POST /api/v1/qc/books/b-1/runs/r-1/follow-up': failed,
      'POST /api/v1/qc/books/b-1/runs/r-1/correction': corrected,
    })
    renderApp('/noi-kiem/so/b-1')

    await user.click(await screen.findByRole('button', { name: 'Ghi nguyên nhân, chạy lại' }))
    const followUp = await dialogTitled(`Nguyên nhân không đạt và chạy lại – ngày ${dayjs(day(1)).format('DD/MM/YYYY')}`)
    await user.type(within(followUp).getByRole('textbox', { name: 'Nguyên nhân không đạt' }), 'Lọ dung dịch chứng để ngoài tủ mát')
    const repeats = within(followUp).getAllByRole('spinbutton')
    await user.type(repeats[0], '60')
    await user.type(repeats[1], '118')
    await user.click(within(followUp).getByRole('button', { name: 'Lưu' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/runs/r-1/follow-up')?.body).toEqual({
        reason: 'Lọ dung dịch chứng để ngoài tủ mát',
        control1RepeatValue: 60,
        control2RepeatValue: 118,
      }),
    )

    await user.click(await screen.findByRole('button', { name: 'Ghi điều chỉnh' }))
    const correction = await dialogTitled(`Ghi điều chỉnh kết quả ngày ${dayjs(day(1)).format('DD/MM/YYYY')}`)
    expect(within(correction).getByText(/Kết quả đã ghi không bị sửa/)).toBeInTheDocument()
    await user.click(within(correction).getByRole('button', { name: 'Lưu điều chỉnh' }))
    expect(await within(correction).findByText('Ghi lý do điều chỉnh.')).toBeInTheDocument()
    await user.type(within(correction).getByRole('textbox', { name: 'Lý do điều chỉnh' }), 'Ghi nhầm kết quả mức 1')
    // Điền sẵn kết quả cũ để sửa; sửa mức 1 thành 55.
    const values = within(correction).getAllByRole('spinbutton')
    expect(values[0]).toHaveValue('80.0')
    await user.clear(values[0])
    await user.type(values[0], '55')
    await user.click(within(correction).getByRole('button', { name: 'Lưu điều chỉnh' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/runs/r-1/correction')?.body).toMatchObject({
        reason: 'Ghi nhầm kết quả mức 1',
        replacement: { runDate: day(1), control1Value: 55, control2Value: 120, stripLotId: 's-1' },
      }),
    )
    expect(await screen.findByText(/Đã hủy: Ghi nhầm kết quả mức 1/)).toBeInTheDocument()
    expect(screen.getByText('80 / 120', { selector: 'del *, del' })).toBeInTheDocument()
  })

  it('chỉ hủy kết quả ghi nhầm (không ghi kết quả thay thế)', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/qc/books/b-1': book({ days: [qcDay(1, { run: run() })] }),
      'POST /api/v1/qc/books/b-1/runs/r-1/correction': book(),
    })
    renderApp('/noi-kiem/so/b-1')

    await user.click(await screen.findByRole('button', { name: 'Ghi điều chỉnh' }))
    const correction = await dialogTitled(`Ghi điều chỉnh kết quả ngày ${dayjs(day(1)).format('DD/MM/YYYY')}`)
    await user.type(within(correction).getByRole('textbox', { name: 'Lý do điều chỉnh' }), 'Ghi nhầm máy')
    await user.click(within(correction).getByRole('checkbox', { name: /Chỉ hủy kết quả này/ }))
    expect(within(correction).queryAllByRole('spinbutton')).toHaveLength(0)
    await user.click(within(correction).getByRole('button', { name: 'Lưu điều chỉnh' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/runs/r-1/correction')?.body).toEqual({ reason: 'Ghi nhầm máy', replacement: null }),
    )
  })

  it('lỗi quy tắc từ máy chủ hiện trong hộp ghi kết quả', { timeout: 20_000 }, async () => {
    const user = userEvent.setup()
    mockApi({
      ...baseRoutes(nurseSession()),
      'GET /api/v1/qc/books/b-1': book(),
      'POST /api/v1/qc/books/b-1/runs': () => problem(422, 'Khoa HSH đã xem xét ngày này; cần Khoa HSH trả lại ngày này để ghi điều chỉnh.', { code: 'qc.rule' }),
    })
    renderApp('/noi-kiem/so/b-1')

    await user.click(await screen.findByRole('button', { name: 'Ghi kết quả' }))
    const dialog = await dialogTitled('Ghi kết quả nội kiểm')
    const values = within(dialog).getAllByRole('spinbutton')
    await user.type(values[0], '55')
    await user.type(values[1], '120')
    await user.click(within(dialog).getByRole('button', { name: 'Lưu kết quả' }))
    expect(await within(dialog).findByText(/cần Khoa HSH trả lại ngày này/)).toBeInTheDocument()
  })

  it('menu theo quyền: khoa thấy Sổ nội kiểm, không mở được trang danh mục', async () => {
    mockApi({ ...baseRoutes(nurseSession()), 'GET /api/v1/qc/books': { month, items: [] } })
    renderApp('/noi-kiem/danh-muc')

    expect(await screen.findByText(/không có quyền/i)).toBeInTheDocument()
  })
})

describe('Sổ nội kiểm đường huyết (Khoa HSH)', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  const submitted = () =>
    book({
      status: 'Submitted',
      submittedBy: 'Điều dưỡng Demo',
      submittedAtUtc: '2026-11-30T09:00:00Z',
      availableLots: [],
      defaults: null,
      actions: { record: false, submit: false, review: true, confirm: true, return: true },
      days: [
        qcDay(1, { run: run({ passed: true, control1: control(1, 55, 40, 70), canFollowUp: false, canCorrect: false }), suggestedOutcome: 'Passed', allowedOutcomes: ['Passed'], canReview: true }),
        qcDay(2, { canReview: true }),
        qcDay(3, {
          review: { id: 'v-1', outcome: 'NoQc', note: null, reviewedBy: 'Khoa HSH Demo', reviewedAtUtc: '2026-11-04T01:00:00Z', returnReason: null, returnedBy: null, returnedAtUtc: null },
          canReturn: true,
        }),
        // Hôm nay, khoa chưa ghi: không nằm trong "Chọn mọi ngày chờ xem xét" (vẫn chọn tay được). Ngày 1–3 của tháng
        // trùng các ngày trên thì bỏ dòng này.
        ...([day(1), day(2), day(3)].includes(dayjs().format('YYYY-MM-DD'))
          ? []
          : [{ ...qcDay(1, { canReview: true }), date: dayjs().format('YYYY-MM-DD') }]),
      ],
    })

  it('xem xét nhiều ngày theo gợi ý, trả lại ngày có lý do, xác nhận sổ tháng', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(labSession()),
      'GET /api/v1/qc/books/b-1': submitted(),
      'POST /api/v1/qc/books/b-1/reviews': submitted(),
      'POST /api/v1/qc/books/b-1/reviews/return': submitted(),
      'POST /api/v1/qc/books/b-1/confirm': book({ status: 'Confirmed', confirmedBy: 'Khoa HSH Demo', confirmedAtUtc: '2026-12-01T02:00:00Z', actions: { record: false, submit: false, review: false, confirm: false, return: false } }),
    })
    renderApp('/noi-kiem/so/b-1')

    expect(await screen.findByText(/Khoa đã gửi sổ tháng \(Điều dưỡng Demo/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ghi kết quả' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Chọn mọi ngày chờ xem xét (2)' }))
    await user.click(screen.getByRole('button', { name: 'Xem xét 2 ngày đã chọn' }))
    const review = await dialogTitled('Xem xét 2 ngày')
    expect(within(review).getByTitle('Đã kiểm - ĐẠT')).toBeInTheDocument()
    expect(within(review).getByTitle('Đã kiểm - Không có QC ngày này')).toBeInTheDocument()
    await user.click(within(review).getByRole('button', { name: 'Lưu xem xét' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/reviews')?.body).toEqual({
        days: [
          { date: day(1), outcome: 'Passed', note: null },
          { date: day(2), outcome: 'NoQc', note: null },
        ],
      }),
    )

    await user.click(await screen.findByRole('button', { name: 'Trả lại ngày' }))
    const returnDay = await dialogTitled(`Trả lại ngày ${dayjs(day(3)).format('DD/MM/YYYY')}`)
    await user.click(within(returnDay).getByRole('button', { name: 'Trả lại ngày' }))
    expect(await within(returnDay).findByText('Ghi lý do.')).toBeInTheDocument()
    await user.type(within(returnDay).getByRole('textbox', { name: 'Lý do' }), 'Khoa báo có kết quả ngày này')
    await user.click(within(returnDay).getByRole('button', { name: 'Trả lại ngày' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/reviews/return')?.body).toEqual({ date: day(3), reason: 'Khoa báo có kết quả ngày này' }),
    )

    await user.click(await screen.findByRole('button', { name: 'Xác nhận sổ tháng' }))
    const confirm = await screen.findByRole('dialog', { name: 'Xác nhận sổ tháng' })
    expect(within(confirm).getByText(/sổ khóa hẳn/)).toBeInTheDocument()
    await user.click(within(confirm).getByRole('button', { name: 'Xác nhận sổ tháng' }))
    await waitFor(() => expect(lastCall(calls, 'POST', '/api/v1/qc/books/b-1/confirm')).toBeDefined())
    expect(await screen.findByText(/Khoa HSH đã xác nhận sổ tháng \(Khoa HSH Demo/)).toBeInTheDocument()
  })

  it('khai báo lô dung dịch chứng có mức và ngưỡng; máy đo theo khoa', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const catalog: QcCatalog = {
      meters: [{ id: 'm-1', departmentId: 'd-noi', departmentName: 'Khoa Nội tổng hợp', model: 'Máy đo A (giả lập)', serialNumber: 'GL-0001', isActive: true, createdAtUtc: '2026-11-01T01:00:00Z' }],
      lots,
    }
    const { calls } = mockApi({
      ...baseRoutes(labSession()),
      'GET /api/v1/qc/catalog': catalog,
      'GET /api/v1/departments': [{ id: 'd-noi', code: 'NOI', name: 'Khoa Nội tổng hợp', isActive: true }],
      'POST /api/v1/qc/lots': lots[2],
      'PUT /api/v1/qc/meters/m-1': catalog.meters[0],
    })
    renderApp('/noi-kiem/danh-muc')

    expect(await screen.findByText('GL-0001')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Sửa' }))
    const meter = await dialogTitled('Sửa máy đo')
    await user.click(within(meter).getByRole('switch'))
    await user.click(within(meter).getByRole('button', { name: 'Lưu' }))
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/qc/meters/m-1')?.body).toEqual({
        departmentId: 'd-noi',
        model: 'Máy đo A (giả lập)',
        serialNumber: 'GL-0001',
        isActive: false,
      }),
    )

    await user.click(screen.getByRole('tab', { name: 'Lô que thử, dung dịch chứng' }))
    expect(await screen.findByText('40-70 mg/dL')).toBeInTheDocument()
    // Ngưỡng chấp nhận nhập ở lô dung dịch chứng: nút riêng, hộp hiện ngay ô mức và ngưỡng (bệnh viện không thấy ô ngưỡng khi hộp
    // thêm lô mở sẵn loại que thử, phải đổi loại mới hiện).
    expect(screen.getByText(/^Ngưỡng chấp nhận khai báo ở lô dung dịch chứng, mỗi mức một dòng/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Thêm lô dung dịch chứng/ }))
    const dialog = await dialogTitled('Thêm lô dung dịch chứng')
    expect(within(dialog).queryByRole('combobox', { name: 'Loại vật tư' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('spinbutton', { name: 'Ngưỡng dưới' })).toBeInTheDocument()
    expect(within(dialog).getByText(/Kết quả trong khoảng, tính cả hai đầu, là Đạt\./)).toBeInTheDocument()
    await user.type(within(dialog).getByRole('textbox', { name: 'Tên vật tư' }), 'Dung dịch chứng B (giả lập)')
    await user.type(within(dialog).getByRole('textbox', { name: 'Số lô' }), 'DC-B-02')
    await user.type(within(dialog).getByPlaceholderText('Chọn ngày'), '30/06/2027{Enter}')
    await user.click(within(dialog).getByRole('combobox', { name: 'Mức (Level)' }))
    await user.click(await screen.findByTitle('Level 2'))
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Ngưỡng dưới' }), '13.1')
    await user.type(within(dialog).getByRole('spinbutton', { name: 'Ngưỡng trên' }), '19.7')
    await user.click(within(dialog).getByRole('combobox', { name: 'Đơn vị' }))
    await user.click(await screen.findByTitle('mmol/L'))
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/lots')?.body).toEqual({
        kind: 'ControlSolution',
        productName: 'Dung dịch chứng B (giả lập)',
        lotNumber: 'DC-B-02',
        expiryDate: '2027-06-30',
        level: 2,
        rangeLow: 13.1,
        rangeHigh: 19.7,
        unit: 'MmolPerL',
      }),
    )
  })

  it('lô que thử không có ô ngưỡng, ghi rõ ngưỡng nhập ở lô dung dịch chứng; sửa lô dung dịch chứng hiện sẵn ngưỡng', { timeout: 30_000 }, async () => {
    const user = userEvent.setup()
    const { calls } = mockApi({
      ...baseRoutes(labSession()),
      'GET /api/v1/qc/catalog': { meters: [], lots } satisfies QcCatalog,
      'POST /api/v1/qc/lots': lots[0],
      'PUT /api/v1/qc/lots/c-2': lots[2],
    })
    renderApp('/noi-kiem/danh-muc')

    await user.click(await screen.findByRole('tab', { name: 'Lô que thử, dung dịch chứng' }))
    await user.click(await screen.findByRole('button', { name: /Thêm lô que thử/ }))
    const strip = await dialogTitled('Thêm lô que thử')
    expect(within(strip).queryByRole('spinbutton', { name: 'Ngưỡng dưới' })).not.toBeInTheDocument()
    expect(within(strip).queryByRole('combobox', { name: 'Mức (Level)' })).not.toBeInTheDocument()
    expect(within(strip).getByText(/^Que thử không có ngưỡng chấp nhận\. Ngưỡng nhập ở lô dung dịch chứng/)).toBeInTheDocument()
    await user.type(within(strip).getByRole('textbox', { name: 'Tên vật tư' }), 'Que thử B (giả lập)')
    await user.type(within(strip).getByRole('textbox', { name: 'Số lô' }), 'QT-B-01')
    await user.type(within(strip).getByPlaceholderText('Chọn ngày'), '31/12/2027{Enter}')
    await user.click(within(strip).getByRole('button', { name: 'Lưu' }))
    await waitFor(() =>
      expect(lastCall(calls, 'POST', '/api/v1/qc/lots')?.body).toEqual({
        kind: 'TestStrip',
        productName: 'Que thử B (giả lập)',
        lotNumber: 'QT-B-01',
        expiryDate: '2027-12-31',
        level: null,
        rangeLow: null,
        rangeHigh: null,
        unit: null,
      }),
    )
    await waitFor(() => expect(screen.queryByText('Thêm lô que thử', { selector: '.ant-modal-title' })).not.toBeInTheDocument())

    const row = screen.getByText('DC-02').closest('tr')!
    await user.click(within(row).getByRole('button', { name: 'Sửa' }))
    const control = await dialogTitled('Sửa lô dung dịch chứng')
    expect(within(control).getByRole('spinbutton', { name: 'Ngưỡng dưới' })).toHaveValue('100.0')
    expect(within(control).getByRole('spinbutton', { name: 'Ngưỡng trên' })).toHaveValue('140.0')
    await user.clear(within(control).getByRole('spinbutton', { name: 'Ngưỡng trên' }))
    await user.type(within(control).getByRole('spinbutton', { name: 'Ngưỡng trên' }), '150')
    await user.click(within(control).getByRole('button', { name: 'Lưu' }))
    await waitFor(() =>
      expect(lastCall(calls, 'PUT', '/api/v1/qc/lots/c-2')?.body).toEqual({
        kind: 'ControlSolution',
        productName: 'Dung dịch chứng A',
        lotNumber: 'DC-02',
        expiryDate: lots[2].expiryDate,
        level: 2,
        rangeLow: 100,
        rangeHigh: 150,
        unit: 'MgPerDl',
        isActive: true,
      }),
    )
  })
})
