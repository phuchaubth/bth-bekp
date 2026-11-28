import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiBlob, apiRequest } from './http'

/** Sổ theo dõi kết quả nội kiểm đường huyết tại giường (quyết định 57, mẫu CT-ĐHTG/BM01). */
export type QcLotKind = 'TestStrip' | 'ControlSolution'
export type QcUnit = 'MgPerDl' | 'MmolPerL'
export type QcBookStatus = 'Open' | 'Submitted' | 'Confirmed'
export type QcReviewOutcome = 'Passed' | 'NoQc' | 'FailedNotRepeated' | 'FailedRepeatedAfterReminder'

export const QC_UNIT_LABELS: Record<QcUnit, string> = { MgPerDl: 'mg/dL', MmolPerL: 'mmol/L' }

export const QC_STATUS_LABELS: Record<QcBookStatus, string> = {
  Open: 'Khoa đang ghi',
  Submitted: 'Đã gửi Khoa HSH',
  Confirmed: 'Khoa HSH đã xác nhận',
}

/** Bốn lựa chọn của cột "Nhân viên Khoa HSH xem xét" trên sổ giấy. */
export const QC_OUTCOME_LABELS: Record<QcReviewOutcome, string> = {
  Passed: 'Đã kiểm - ĐẠT',
  NoQc: 'Đã kiểm - Không có QC ngày này',
  FailedNotRepeated: 'Đã kiểm - KHÔNG ĐẠT, KHÔNG CHẠY LẠI QC',
  FailedRepeatedAfterReminder: 'Đã kiểm - KHÔNG ĐẠT, đã chạy lại QC sau nhắc nhở',
}

export interface QcMeter {
  id: string
  departmentId: string
  departmentName: string
  model: string
  serialNumber: string
  isActive: boolean
  createdAtUtc: string
}

export interface QcLot {
  id: string
  kind: QcLotKind
  productName: string
  lotNumber: string
  expiryDate: string
  level: number | null
  rangeLow: number | null
  rangeHigh: number | null
  unit: QcUnit | null
  isActive: boolean
  createdAtUtc: string
}

export interface QcCatalog {
  meters: QcMeter[]
  lots: QcLot[]
}

export interface SaveQcMeterInput {
  departmentId: string
  model: string
  serialNumber: string
  isActive?: boolean
}

export interface SaveQcLotInput {
  kind: QcLotKind
  productName: string
  lotNumber: string
  expiryDate: string
  level: number | null
  rangeLow: number | null
  rangeHigh: number | null
  unit: QcUnit | null
  isActive?: boolean
}

/** Một dòng danh sách sổ: sổ đã mở hoặc máy chưa mở sổ tháng này (bookId trống). */
export interface QcBookSummary {
  bookId: string | null
  meterId: string
  meterLabel: string
  departmentId: string
  departmentName: string
  status: QcBookStatus | null
  recordedDays: number
  failedDays: number
  reviewedDays: number
  /** Số ngày đến hôm nay Khoa HSH chưa xem xét. */
  daysToReview: number
  canOpen: boolean
}

export interface QcBookList {
  month: string
  items: QcBookSummary[]
}

export interface QcStrip {
  lotId: string
  lotNumber: string
  expiryDate: string
  openedOn: string
}

export interface QcControl {
  lotId: string
  level: number
  lotNumber: string
  expiryDate: string
  openedOn: string
  low: number
  high: number
  unit: QcUnit
  value: number
  passed: boolean
  repeatValue: number | null
  repeatPassed: boolean | null
}

export interface QcRun {
  id: string
  runDate: string
  strip: QcStrip
  control1: QcControl
  control2: QcControl
  passed: boolean
  failureReason: string | null
  hasRepeat: boolean
  repeatPassed: boolean | null
  followUpBy: string | null
  followUpAtUtc: string | null
  performedBy: string
  recordedAtUtc: string
  /** Ghi sau ngày thực hiện. */
  late: boolean
  replacesRunId: string | null
  cancelReason: string | null
  cancelledBy: string | null
  cancelledAtUtc: string | null
  canFollowUp: boolean
  canCorrect: boolean
}

export interface QcReview {
  id: string
  outcome: QcReviewOutcome
  note: string | null
  reviewedBy: string
  reviewedAtUtc: string
  returnReason: string | null
  returnedBy: string | null
  returnedAtUtc: string | null
}

export interface QcDay {
  date: string
  run: QcRun | null
  cancelledRuns: QcRun[]
  review: QcReview | null
  returnedReviews: QcReview[]
  suggestedOutcome: QcReviewOutcome
  allowedOutcomes: QcReviewOutcome[]
  canRecord: boolean
  canReview: boolean
  canReturn: boolean
}

export interface QcLotUsage {
  lotNumber: string
  expiryDate: string
  openedOn: string
  level: number | null
  low: number | null
  high: number | null
  unit: QcUnit | null
}

export interface QcRunDefaults {
  stripLotId: string | null
  stripOpenedOn: string | null
  control1LotId: string | null
  control1OpenedOn: string | null
  control2LotId: string | null
  control2OpenedOn: string | null
}

export interface QcBook {
  id: string
  meterId: string
  meterLabel: string
  departmentId: string
  departmentName: string
  year: number
  month: number
  status: QcBookStatus
  submittedBy: string | null
  submittedAtUtc: string | null
  confirmedBy: string | null
  confirmedAtUtc: string | null
  returnReason: string | null
  returnedBy: string | null
  returnedAtUtc: string | null
  header: { strips: QcLotUsage[]; controls1: QcLotUsage[]; controls2: QcLotUsage[] }
  days: QcDay[]
  availableLots: QcLot[]
  defaults: QcRunDefaults | null
  actions: { record: boolean; submit: boolean; review: boolean; confirm: boolean; return: boolean }
}

export interface QcRunInput {
  runDate: string
  stripLotId: string
  stripOpenedOn: string
  control1LotId: string
  control1OpenedOn: string
  control1Value: number
  control2LotId: string
  control2OpenedOn: string
  control2Value: number
}

export interface QcReviewDayInput {
  date: string
  outcome: QcReviewOutcome
  note?: string | null
}

/**
 * Số liệu nội kiểm của một sổ, một khoa hoặc cả tháng (dashboard, quyết định 59). Ngày phải thực hiện: mọi ngày đã qua của tháng
 * (hôm nay chỉ tính khi đã có kết quả); không đạt tính ở lần chạy đầu.
 */
export interface QcDashboardFigures {
  requiredDays: number
  recordedDays: number
  missingDays: number
  lateDays: number
  failedDays: number
  repeatPassedDays: number
  repeatFailedDays: number
  /** Không đạt mà khoa chưa chạy lại (cần nhắc). */
  notRepeatedDays: number
  reviewedDays: number
  /** Ngày đến hôm nay Khoa HSH chưa xem xét. */
  daysToReview: number
}

/** Số sổ theo trạng thái; `notOpened`: máy đang dùng chưa mở sổ. */
export interface QcDashboardBookCounts {
  open: number
  returned: number
  submitted: number
  confirmed: number
  notOpened: number
}

/** Lần không đạt gần nhất; `reason` trống là khoa chưa ghi nguyên nhân. */
export interface QcDashboardFailure {
  date: string
  meterLabel: string
  reason: string | null
}

export interface QcDashboardMeter {
  meterId: string
  meterLabel: string
  meterActive: boolean
  /** Trống: máy đang dùng chưa mở sổ tháng này. */
  bookId: string | null
  status: QcBookStatus | null
  /** Khoa HSH đã trả lại sổ, khoa đang ghi tiếp. */
  returned: boolean
  /** Người xem mở được sổ (quyền chỉ xem dashboard thì không). */
  canOpen: boolean
  figures: QcDashboardFigures
  missingDates: string[]
  latestFailure: QcDashboardFailure | null
}

export interface QcDashboardDepartment {
  departmentId: string
  departmentName: string
  figures: QcDashboardFigures
  books: QcDashboardBookCounts
  latestFailure: QcDashboardFailure | null
  meters: QcDashboardMeter[]
}

export interface QcDashboardMonth {
  month: string
  requiredDays: number
  recordedDays: number
  failedDays: number
  lateDays: number
}

/** Lô đang dùng hết hạn hoặc còn ≤ 30 ngày (`daysLeft` âm là đã hết hạn); `usedBy`: khoa đang dùng lô. */
export interface QcDashboardLotAlert {
  lotId: string
  kind: QcLotKind
  productName: string
  lotNumber: string
  level: number | null
  expiryDate: string
  daysLeft: number
  usedBy: string[]
}

export interface QcDashboardMeterRef {
  meterId: string
  meterLabel: string
  departmentId: string
  departmentName: string
}

export interface QcDashboardPendingBook {
  bookId: string
  month: string
  departmentId: string
  departmentName: string
  meterLabel: string
  status: QcBookStatus
  returned: boolean
  canOpen: boolean
}

/** Cảnh báo tính đến hôm nay (không theo tháng đang xem). */
export interface QcDashboardAlerts {
  currentMonth: string
  lots: QcDashboardLotAlert[]
  /** Các mức dung dịch chứng đang dùng còn hạn. */
  controlLevels: number[]
  /** Số lô que thử đang dùng còn hạn. */
  stripLots: number
  /** Máy đang dùng chưa mở sổ tháng này. */
  metersWithoutBook: QcDashboardMeterRef[]
  /** Sổ của 5 tháng trước chưa xác nhận. */
  pendingBooks: QcDashboardPendingBook[]
}

/** Dashboard giám sát nội kiểm của một tháng (quyết định 59); `today` là hôm nay theo giờ bệnh viện. */
export interface QcDashboard {
  month: string
  today: string
  totals: { departments: number; meters: number; figures: QcDashboardFigures; books: QcDashboardBookCounts }
  departments: QcDashboardDepartment[]
  /** 6 tháng, tháng đang xem ở cuối. */
  trend: QcDashboardMonth[]
  alerts: QcDashboardAlerts
}

const base = '/api/v1/qc'

export const qcApi = {
  catalog: (signal?: AbortSignal) => apiRequest<QcCatalog>(`${base}/catalog`, { signal }),
  createMeter: (input: SaveQcMeterInput) => apiRequest<QcMeter>(`${base}/meters`, { method: 'POST', body: input }),
  updateMeter: (id: string, input: SaveQcMeterInput) => apiRequest<QcMeter>(`${base}/meters/${id}`, { method: 'PUT', body: input }),
  createLot: (input: SaveQcLotInput) => apiRequest<QcLot>(`${base}/lots`, { method: 'POST', body: input }),
  updateLot: (id: string, input: SaveQcLotInput) => apiRequest<QcLot>(`${base}/lots/${id}`, { method: 'PUT', body: input }),
  books: (month: string, departmentId: string | undefined, signal?: AbortSignal) =>
    apiRequest<QcBookList>(`${base}/books?month=${month}${departmentId ? `&departmentId=${departmentId}` : ''}`, { signal }),
  open: (meterId: string, month: string) => apiRequest<QcBook>(`${base}/books`, { method: 'POST', body: { meterId, month } }),
  book: (id: string, signal?: AbortSignal) => apiRequest<QcBook>(`${base}/books/${id}`, { signal }),
  recordRun: (id: string, input: QcRunInput) => apiRequest<QcBook>(`${base}/books/${id}/runs`, { method: 'POST', body: input }),
  followUp: (id: string, runId: string, input: { reason: string; control1RepeatValue: number | null; control2RepeatValue: number | null }) =>
    apiRequest<QcBook>(`${base}/books/${id}/runs/${runId}/follow-up`, { method: 'POST', body: input }),
  correct: (id: string, runId: string, input: { reason: string; replacement: QcRunInput | null }) =>
    apiRequest<QcBook>(`${base}/books/${id}/runs/${runId}/correction`, { method: 'POST', body: input }),
  review: (id: string, days: QcReviewDayInput[]) => apiRequest<QcBook>(`${base}/books/${id}/reviews`, { method: 'POST', body: { days } }),
  returnDay: (id: string, date: string, reason: string) =>
    apiRequest<QcBook>(`${base}/books/${id}/reviews/return`, { method: 'POST', body: { date, reason } }),
  submit: (id: string) => apiRequest<QcBook>(`${base}/books/${id}/submit`, { method: 'POST' }),
  confirm: (id: string) => apiRequest<QcBook>(`${base}/books/${id}/confirm`, { method: 'POST' }),
  returnBook: (id: string, reason: string) => apiRequest<QcBook>(`${base}/books/${id}/return`, { method: 'POST', body: { reason } }),
  pdf: (id: string) => apiBlob(`${base}/books/${id}/pdf`),
  dashboard: (month: string, signal?: AbortSignal) => apiRequest<QcDashboard>(`${base}/dashboard?month=${month}`, { signal }),
}

export const qcKeys = {
  all: ['qc'] as const,
  catalog: ['qc', 'catalog'] as const,
  books: ['qc', 'books'] as const,
  list: (month: string, departmentId: string | undefined) => ['qc', 'books', month, departmentId ?? ''] as const,
  book: (id: string) => ['qc', 'book', id] as const,
  // Nằm dưới "books": sổ thay đổi (ghi kết quả, xem xét, gửi, xác nhận) thì dashboard cũng tải lại.
  dashboard: (month: string) => ['qc', 'books', 'dashboard', month] as const,
}

export function useQcCatalog(enabled = true) {
  return useQuery({ queryKey: qcKeys.catalog, queryFn: ({ signal }) => qcApi.catalog(signal), enabled })
}

export function useQcBooks(month: string, departmentId: string | undefined) {
  return useQuery({
    queryKey: qcKeys.list(month, departmentId),
    queryFn: ({ signal }) => qcApi.books(month, departmentId, signal),
    placeholderData: keepPreviousData,
  })
}

export function useQcBook(id: string) {
  return useQuery({ queryKey: qcKeys.book(id), queryFn: ({ signal }) => qcApi.book(id, signal) })
}

export function useQcDashboard(month: string) {
  return useQuery({
    queryKey: qcKeys.dashboard(month),
    queryFn: ({ signal }) => qcApi.dashboard(month, signal),
    placeholderData: keepPreviousData,
  })
}

/** Số in như sổ: bỏ phần thập phân bằng 0 (140; 6.5). */
export function formatQcValue(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1)
}

export function formatQcRange(low: number | null, high: number | null, unit: QcUnit | null): string {
  return low === null || high === null || unit === null ? '' : `${formatQcValue(low)}-${formatQcValue(high)} ${QC_UNIT_LABELS[unit]}`
}

/** Kết quả nằm trong ngưỡng chấp nhận (tính cả hai đầu), như máy chủ đánh giá. */
export function inQcRange(value: number, low: number, high: number): boolean {
  return value >= low && value <= high
}
