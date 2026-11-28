import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiBlob, apiRequest } from './http'
import type { HisStatus } from './patients'

/** Kết quả nguy cấp (quyết định 60, QT-KQNC lần 04): khoa cận lâm sàng báo, khoa điều trị nhận, bác sĩ xử trí. */
export type CriticalStatus = 'AwaitingNotification' | 'AwaitingAcknowledgement' | 'AwaitingAction' | 'Completed' | 'Cancelled'
export type CriticalLogbook = 'Laboratory' | 'Microbiology'
export type CriticalLogbookForm = 'Laboratory' | 'Clinical' | 'Microbiology'
export type CriticalMethod = 'Phone' | 'InPerson'
export type CriticalPatientSource = 'His' | 'Manual'
export type Gender = 'Male' | 'Female'

export const CRITICAL_STATUS_LABELS: Record<CriticalStatus, string> = {
  AwaitingNotification: 'Chờ gọi báo',
  AwaitingAcknowledgement: 'Chờ khoa xác nhận',
  AwaitingAction: 'Chờ bác sĩ xử trí',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
}

export const CRITICAL_METHOD_LABELS: Record<CriticalMethod, string> = { Phone: 'Gọi điện', InPerson: 'Trực tiếp tại khoa' }

export const CRITICAL_LOGBOOK_LABELS: Record<CriticalLogbook, string> = {
  Laboratory: 'Sổ khoa cận lâm sàng (ST01)',
  Microbiology: 'Sổ Khoa Vi sinh',
}

export const CRITICAL_FORM_LABELS: Record<CriticalLogbookForm, string> = {
  Laboratory: 'ST01 · khoa cận lâm sàng báo',
  Clinical: 'ST02 · khoa điều trị nhận',
  Microbiology: 'Sổ Vi sinh · báo qua điện thoại',
}

export const GENDER_LABELS: Record<Gender, string> = { Male: 'Nam', Female: 'Nữ' }

export interface CriticalTest {
  id: string
  name: string
  adultRange: string
  childRange: string | null
  unit: string | null
  section: string | null
  logbook: CriticalLogbook
  textResult: boolean
  sortOrder: number
  isActive: boolean
}

export interface SaveCriticalTestInput {
  name: string
  adultRange: string
  childRange: string | null
  unit: string | null
  section: string | null
  logbook: CriticalLogbook
  textResult: boolean
  sortOrder: number
  isActive: boolean
}

/** Thông số vận hành (chỉnh khi vận hành, không cần cài bản mới); hồ sơ giữ thời hạn áp dụng lúc ghi. */
export interface CriticalSettingsValues {
  notifyMinutes: number
  actionMinutes: number
  warnMinutes: number
  pollSeconds: number
  soundEnabled: boolean
  soundRepeatSeconds: number
  flashEnabled: boolean
  flashPeriodMs: number
  keepHours: number
  lateEntryMinutes: number
  requireAddress: boolean
  displayFullName: boolean
  targetNotifyPercent: number
  targetActionPercent: number
  targetReadBackPercent: number
  targetCompletePercent: number
  codeLaboratory: string
  codeClinical: string
  codeMicrobiology: string
}

export interface CriticalSettings {
  values: CriticalSettingsValues
  updatedAtUtc: string | null
  updatedBy: string | null
}

export interface CriticalPatientLookup {
  status: HisStatus
  code: string | null
  fullName: string | null
  dateOfBirth: string | null
  gender: Gender | null
  address: string | null
}

export interface CriticalPatientInput {
  code: string
  fullName: string
  dateOfBirth: string | null
  gender: Gender | null
  address: string | null
  source: CriticalPatientSource
}

export interface CriticalNotificationInput {
  receivingDepartmentId: string
  method: CriticalMethod
  notifiedAtUtc: string
  receiverName: string
  readBackCorrect: boolean
  incorrectReadBacks: number
  repeatedTwice: boolean
}

export interface CreateCriticalResultInput {
  sendingDepartmentId: string
  testId: string
  patient: CriticalPatientInput
  value: string
  unit: string | null
  orderDate: string
  resultAtUtc: string
  sampleId: string | null
  urgent: boolean
  notification: CriticalNotificationInput | null
}

export interface CriticalActionInput {
  summary: string
  actionAtUtc: string | null
  recordedInMedicalRecord: boolean
}

export interface CriticalNotification {
  id: string
  attemptNo: number
  receivingDepartmentId: string
  receivingDepartmentName: string
  method: CriticalMethod
  notifiedAtUtc: string
  receiverName: string
  incorrectReadBacks: number
  repeatedTwice: boolean
  notifiedByName: string
  recordedAtUtc: string
  rejectedAtUtc: string | null
  rejectedByName: string | null
  rejectReason: string | null
}

/** Người đang xem làm được gì (giao diện ẩn nút; máy chủ vẫn kiểm tra). */
export interface CriticalPermissions {
  notify: boolean
  acknowledge: boolean
  reject: boolean
  act: boolean
  cancel: boolean
}

export interface CriticalResult {
  id: string
  number: string
  status: CriticalStatus
  sendingDepartmentId: string
  sendingDepartmentName: string
  receivingDepartmentId: string | null
  receivingDepartmentName: string | null
  patientCode: string
  patientName: string
  patientInitials: string
  patientDateOfBirth: string
  patientGender: Gender | null
  patientAddress: string | null
  patientSource: CriticalPatientSource
  testId: string
  testName: string
  testAdultRange: string
  testChildRange: string | null
  logbook: CriticalLogbook
  textResult: boolean
  value: string
  unit: string | null
  orderDate: string
  resultAtUtc: string
  sampleId: string | null
  urgent: boolean
  notifyDeadlineMinutes: number
  actionDeadlineMinutes: number
  notifyDueAtUtc: string
  notifiedAtUtc: string | null
  actionDueAtUtc: string | null
  recordedByName: string
  recordedAtUtc: string
  acknowledgedByName: string | null
  acknowledgedAtUtc: string | null
  actionByName: string | null
  actionAtUtc: string | null
  actionSummary: string | null
  recordedInMedicalRecord: boolean
  actionRecordedAtUtc: string | null
  cancelledByName: string | null
  cancelledAtUtc: string | null
  cancelReason: string | null
  receivingDepartmentInformed: boolean
  notifiedOnTime: boolean | null
  actedOnTime: boolean | null
  lateEntry: boolean
  notifications: CriticalNotification[]
  can: CriticalPermissions
}

export interface CriticalResultList {
  serverTimeUtc: string
  items: CriticalResult[]
}

export interface CriticalAlertItem {
  id: string
  number: string
  status: CriticalStatus
  receivingDepartmentId: string
  receivingDepartmentName: string
  sendingDepartmentName: string
  /** Họ tên đầy đủ, hoặc tên viết tắt với tài khoản màn hình theo dõi (trừ khi cấu hình cho hiện đầy đủ). */
  patientName: string
  patientBirthYear: number
  patientCodeTail: string
  testName: string
  value: string
  unit: string | null
  notifiedAtUtc: string
  actionDueAtUtc: string
  receiverName: string
  notifiedByName: string
  acknowledgedByName: string | null
  acknowledgedAtUtc: string | null
  actionByName: string | null
  actionAtUtc: string | null
  actedOnTime: boolean | null
}

export interface CriticalAlertSettings {
  warnMinutes: number
  pollSeconds: number
  soundEnabled: boolean
  soundRepeatSeconds: number
  flashEnabled: boolean
  flashPeriodMs: number
}

export interface CriticalAlerts {
  serverTimeUtc: string
  settings: CriticalAlertSettings
  receivingDepartmentNames: string[]
  awaitingAcknowledgement: CriticalAlertItem[]
  awaitingAction: CriticalAlertItem[]
  recentlyCompleted: CriticalAlertItem[]
  todayReceived: number
  todayActedOnTime: number
  todayActed: number
  awaitingNotification: number
  overdueNotification: number
}

export interface CriticalRate {
  numerator: number
  denominator: number
  /** Phần trăm (1 chữ số thập phân); null khi mẫu số bằng 0. */
  percent: number | null
}

export interface CriticalDashboardKpis {
  total: number
  cancelled: number
  notifyOnTime: CriticalRate
  actionOnTime: CriticalRate
  readBackFirstTime: CriticalRate
  complete: CriticalRate
  medianNotifyMinutes: number | null
  notifyQ1Minutes: number | null
  notifyQ3Minutes: number | null
  medianActionMinutes: number | null
  actionQ1Minutes: number | null
  actionQ3Minutes: number | null
}

export interface CriticalDashboardMonth {
  month: string
  total: number
  notifyOnTimePercent: number | null
  actionOnTimePercent: number | null
}

export interface CriticalDashboardDepartment {
  departmentId: string
  name: string
  total: number
  onTime: CriticalRate
  medianMinutes: number | null
  open: number
}

export interface CriticalDashboardLate {
  id: string
  number: string
  resultAtUtc: string
  patientInitials: string
  patientCode: string
  testName: string
  value: string
  unit: string | null
  sendingDepartmentName: string
  receivingDepartmentName: string | null
  miss: string
}

export interface CriticalDashboard {
  period: 'month' | 'quarter'
  month: string
  label: string
  from: string
  to: string
  serverTimeUtc: string
  targets: {
    notifyPercent: number
    actionPercent: number
    readBackPercent: number
    completePercent: number
    notifyMinutes: number
    actionMinutes: number
  }
  kpis: CriticalDashboardKpis
  /** 12 tháng, tháng đang xem ở cuối. */
  trend: CriticalDashboardMonth[]
  senders: CriticalDashboardDepartment[]
  receivers: CriticalDashboardDepartment[]
  late: CriticalDashboardLate[]
  /** Dashboard của một khoa (quyết định 64): hồ sơ khoa đó báo đi và hồ sơ gửi đến khoa đó; null là toàn viện. */
  departmentId?: string | null
  departmentName?: string | null
}

export interface CriticalLogbookRow {
  cells: string[]
  cancelled: boolean
  note: string | null
}

export interface CriticalLogbookView {
  form: CriticalLogbookForm
  code: string
  departmentId: string
  departmentName: string
  month: string
  columns: string[]
  rows: CriticalLogbookRow[]
}

const base = '/api/v1/critical-results'

export const criticalApi = {
  settings: (signal?: AbortSignal) => apiRequest<CriticalSettings>(`${base}/settings`, { signal }),
  updateSettings: (values: CriticalSettingsValues) => apiRequest<CriticalSettings>(`${base}/settings`, { method: 'PUT', body: values }),
  tests: (signal?: AbortSignal) => apiRequest<CriticalTest[]>(`${base}/tests`, { signal }),
  createTest: (input: SaveCriticalTestInput) => apiRequest<CriticalTest>(`${base}/tests`, { method: 'POST', body: input }),
  updateTest: (id: string, input: SaveCriticalTestInput) => apiRequest<CriticalTest>(`${base}/tests/${id}`, { method: 'PUT', body: input }),
  lookupPatient: (code: string) => apiRequest<CriticalPatientLookup>(`${base}/patient?code=${encodeURIComponent(code)}`),
  sent: (departmentId: string | undefined, signal?: AbortSignal) =>
    apiRequest<CriticalResultList>(`${base}/sent${departmentId ? `?departmentId=${departmentId}` : ''}`, { signal }),
  received: (departmentId: string | undefined, signal?: AbortSignal) =>
    apiRequest<CriticalResultList>(`${base}/received${departmentId ? `?departmentId=${departmentId}` : ''}`, { signal }),
  alerts: (signal?: AbortSignal) => apiRequest<CriticalAlerts>(`${base}/alerts`, { signal }),
  /** departmentId: dashboard của một khoa (quyết định 64), bỏ trống là toàn viện. */
  dashboard: (month: string, period: 'month' | 'quarter', departmentId: string | undefined, signal?: AbortSignal) =>
    apiRequest<CriticalDashboard>(
      `${base}/dashboard?month=${month}&period=${period}${departmentId ? `&departmentId=${encodeURIComponent(departmentId)}` : ''}`,
      { signal },
    ),
  logbook: (form: CriticalLogbookForm, departmentId: string, month: string, signal?: AbortSignal) =>
    apiRequest<CriticalLogbookView>(`${base}/logbook?form=${form}&departmentId=${departmentId}&month=${month}`, { signal }),
  logbookPdf: (form: CriticalLogbookForm, departmentId: string, month: string) =>
    apiBlob(`${base}/logbook/pdf?form=${form}&departmentId=${departmentId}&month=${month}`),
  get: (id: string, signal?: AbortSignal) => apiRequest<CriticalResult>(`${base}/${id}`, { signal }),
  create: (input: CreateCriticalResultInput) => apiRequest<CriticalResult>(base, { method: 'POST', body: input }),
  notify: (id: string, input: CriticalNotificationInput) =>
    apiRequest<CriticalResult>(`${base}/${id}/notifications`, { method: 'POST', body: input }),
  acknowledge: (id: string) => apiRequest<CriticalResult>(`${base}/${id}/acknowledge`, { method: 'POST' }),
  reject: (id: string, reason: string) => apiRequest<CriticalResult>(`${base}/${id}/reject`, { method: 'POST', body: { reason } }),
  act: (id: string, input: CriticalActionInput) => apiRequest<CriticalResult>(`${base}/${id}/action`, { method: 'POST', body: input }),
  cancel: (id: string, reason: string, receivingDepartmentInformed: boolean) =>
    apiRequest<CriticalResult>(`${base}/${id}/cancel`, { method: 'POST', body: { reason, receivingDepartmentInformed } }),
}

export const criticalKeys = {
  all: ['critical'] as const,
  settings: ['critical', 'settings'] as const,
  tests: ['critical', 'tests'] as const,
  // Hồ sơ thay đổi (ghi, báo, xác nhận, xử trí, hủy) thì mọi danh sách, cảnh báo, sổ, dashboard dưới "records" tải lại.
  records: ['critical', 'records'] as const,
  sent: (departmentId: string | undefined) => ['critical', 'records', 'sent', departmentId ?? ''] as const,
  received: (departmentId: string | undefined) => ['critical', 'records', 'received', departmentId ?? ''] as const,
  alerts: ['critical', 'records', 'alerts'] as const,
  detail: (id: string) => ['critical', 'records', 'detail', id] as const,
  dashboard: (month: string, period: string, departmentId: string | undefined) =>
    ['critical', 'records', 'dashboard', month, period, departmentId ?? 'all'] as const,
  logbook: (form: string, departmentId: string, month: string) => ['critical', 'records', 'logbook', form, departmentId, month] as const,
}

export function useCriticalSettings(enabled = true) {
  return useQuery({ queryKey: criticalKeys.settings, queryFn: ({ signal }) => criticalApi.settings(signal), enabled })
}

export function useCriticalTests(enabled = true) {
  return useQuery({ queryKey: criticalKeys.tests, queryFn: ({ signal }) => criticalApi.tests(signal), enabled })
}

/** Bàn làm việc tự làm mới theo chu kỳ cấu hình (mặc định 15 giây). */
export function useCriticalSent(departmentId: string | undefined, pollSeconds: number) {
  return useQuery({
    queryKey: criticalKeys.sent(departmentId),
    queryFn: ({ signal }) => criticalApi.sent(departmentId, signal),
    refetchInterval: pollSeconds * 1000,
    placeholderData: keepPreviousData,
  })
}

export function useCriticalReceived(departmentId: string | undefined, pollSeconds: number) {
  return useQuery({
    queryKey: criticalKeys.received(departmentId),
    queryFn: ({ signal }) => criticalApi.received(departmentId, signal),
    refetchInterval: pollSeconds * 1000,
    placeholderData: keepPreviousData,
  })
}

/**
 * Cảnh báo của tài khoản đang đăng nhập. Chu kỳ hỏi lại lấy từ lần trả lời trước (thông số cấu hình), 15 giây khi chưa có;
 * vẫn hỏi khi thẻ trình duyệt ở nền (màn hình theo dõi, máy trực).
 */
export function useCriticalAlerts(enabled: boolean) {
  return useQuery({
    queryKey: criticalKeys.alerts,
    queryFn: ({ signal }) => criticalApi.alerts(signal),
    enabled,
    refetchInterval: (query) => (query.state.data?.settings.pollSeconds ?? 15) * 1000,
    refetchIntervalInBackground: true,
    retry: false,
  })
}

export function useCriticalResult(id: string, pollSeconds = 15) {
  return useQuery({
    queryKey: criticalKeys.detail(id),
    queryFn: ({ signal }) => criticalApi.get(id, signal),
    refetchInterval: pollSeconds * 1000,
  })
}

/** Dashboard toàn viện, hoặc của một khoa khi có departmentId (quyết định 64); tài khoản của khoa không tải toàn viện (65). */
export function useCriticalDashboard(month: string, period: 'month' | 'quarter', departmentId?: string, enabled = true) {
  return useQuery({
    queryKey: criticalKeys.dashboard(month, period, departmentId),
    queryFn: ({ signal }) => criticalApi.dashboard(month, period, departmentId, signal),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function useCriticalLogbook(form: CriticalLogbookForm, departmentId: string | undefined, month: string) {
  return useQuery({
    queryKey: criticalKeys.logbook(form, departmentId ?? '', month),
    queryFn: ({ signal }) => criticalApi.logbook(form, departmentId!, month, signal),
    enabled: Boolean(departmentId),
    placeholderData: keepPreviousData,
  })
}
