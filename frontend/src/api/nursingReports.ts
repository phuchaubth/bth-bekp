import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiBlob, apiRequest } from './http'

/** Báo cáo điều dưỡng hằng ngày (quyết định 31). "New": chưa lưu lần nào (mẫu trống hoặc điền sẵn). */
export type NursingReportState = 'New' | 'Draft' | 'Submitted'

export type NursingCheckValue = 'ok' | 'issue'

/**
 * Dòng tính tự động (quyết định 46): `base` cộng các dòng `add`, trừ các dòng `subtract`; ô trống tính 0, riêng `base` trống thì
 * chưa tính được.
 */
export interface NursingReportFormula {
  base: string
  add: string[]
  subtract: string[]
}

export interface NursingReportItem {
  key: string
  label: string
  carryOver: boolean
  /** Có công thức: khoa không nhập, máy chủ tính khi lưu (Số NB hiện tại, quyết định 46). */
  formula?: NursingReportFormula | null
}

/** Một mục (I–VI) của phần số liệu; `valueKey`: dòng tiêu đề mục là ô nhập (mục IV. ADR). */
export interface NursingReportSection {
  title: string
  items: NursingReportItem[]
  valueKey: string | null
}

export interface NursingReportCheck {
  key: string
  label: string
  inSummary: boolean
}

/** Mẫu báo cáo khoa theo bản giấy, do máy chủ trả về. */
export interface NursingReportFormDefinition {
  title: string
  category: string
  sections: NursingReportSection[]
  checks: NursingReportCheck[]
  issueLabel: string
  okLabel: string
  notesLabel: string
  maxValue: number
  notesMaxLength: number
}

export interface NursingReport {
  id: string | null
  departmentId: string
  departmentName: string
  reportDate: string
  status: NursingReportState
  values: Record<string, number>
  checks: Record<string, NursingCheckValue>
  notes: string | null
  canEdit: boolean
  canReopen: boolean
  updatedBy: string | null
  updatedAtUtc: string | null
  submittedBy: string | null
  submittedAtUtc: string | null
  reopenedBy: string | null
  reopenedAtUtc: string | null
  reopenReason: string | null
  /** Dòng điền sẵn theo báo cáo đã gửi gần nhất (nhân sự, chỉ tiêu). */
  carriedOverKeys: string[]
  /** Khoa có trong danh sách khoa báo cáo do Phòng Điều dưỡng cài. */
  isReportingDepartment: boolean
  /** Số liệu HIS khoa nhập khi HIS lỗi (quyết định 35); null khi khoa không có dòng nào phải nhập. */
  hisEntry: NursingHisEntry | null
  /**
   * `false`: người xem chỉ được ủy quyền nhập số liệu HIS của khoa (quyết định 48), không xem báo cáo (các ô báo cáo để trống).
   */
  reportVisible: boolean
}

/** Một dòng số liệu HIS khoa nhập. `hisValue`: số lấy từ HIS của ngày (nếu có, bảng tổng hợp dùng số này). */
export interface NursingHisEntryLine {
  key: string
  label: string
  value: number | null
  hisValue: number | null
}

/** Số liệu HIS khoa nhập khi HIS lỗi: "NB PK chờ nhập viện" của khoa và chỉ số Phòng Điều dưỡng giao cho khoa. */
export interface NursingHisEntry {
  lines: NursingHisEntryLine[]
  canEdit: boolean
  /** Ngày này đã có số liệu lấy từ HIS. */
  hisHasValues: boolean
  hisUpdatedAtUtc: string | null
  updatedBy: string | null
  updatedAtUtc: string | null
}

export interface SaveNursingHisEntryInput {
  departmentId?: string
  reportDate: string
  values: Record<string, number | null>
}

export interface SaveNursingReportInput {
  departmentId?: string
  reportDate: string
  values: Record<string, number | null>
  checks: Record<string, NursingCheckValue | null>
  notes: string | null
  /** Lưu rồi gửi luôn (khóa lại). */
  submit: boolean
}

export interface NursingSummaryColumn {
  key: string
  header: string
}

export interface NursingSummaryRow {
  departmentId: string
  /** Mã khoa (tên viết tắt), in ở cột khoa phòng của bảng tổng hợp. */
  departmentCode: string
  departmentName: string
  isReporting: boolean
  reportId: string | null
  status: NursingReportState
  submittedBy: string | null
  submittedAtUtc: string | null
  cells: Record<string, number | null>
}

export interface NursingSummaryLine {
  key: string
  label: string
  value: number | null
  /** Mã khoa đã nhập tay số này khi HIS lỗi (mục B, D). */
  enteredBy?: string | null
}

export interface NursingSummaryCheck {
  key: string
  label: string
  text: string | null
  issueDepartments: string[]
}

export interface NursingSummaryNote {
  departmentId: string
  departmentCode: string
  departmentName: string
  text: string
}

/**
 * Người báo cáo từng mục B–F ("Họ tên (mã khoa)", cách nhau bởi dấu chấm phẩy); `null` khi mục chưa có số liệu.
 */
export interface NursingSummaryReporters {
  outpatient: string | null
  procedures: string | null
  imaging: string | null
  situation: string | null
  incidents: string | null
}

export interface NursingStatisticsInfo {
  /** Đã cấu hình API thông tin người bệnh (lấy được số liệu HIS). */
  hisAvailable: boolean
  /** Ngày này đã có số liệu lấy từ HIS ("Manual": số Phòng Điều dưỡng nhập trước quyết định 35). */
  hasValues: boolean
  source: 'His' | 'Manual' | null
  updatedAtUtc: string | null
  updatedBy: string | null
  /** Các khoa phải nhập tay khi HIS lỗi, và đã nhập chưa. */
  entries: NursingHisEntryStatus[]
  /** Bảng có số do khoa nhập tay. */
  usesDepartmentEntries: boolean
}

/** Một khoa phải nhập số liệu HIS khi HIS lỗi: mã các dòng của khoa, đã nhập lúc nào, bởi ai. */
export interface NursingHisEntryStatus {
  departmentId: string
  departmentName: string
  keys: string[]
  enteredAtUtc: string | null
  enteredBy: string | null
  /** Người ủy quyền khi người nhập làm theo ủy quyền (quyết định 48). */
  enteredByDelegator?: string | null
}

/** Bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" (mục A–F). */
export interface NursingSummary {
  reportDate: string
  columns: NursingSummaryColumn[]
  rows: NursingSummaryRow[]
  totals: Record<string, number | null>
  outpatient: NursingSummaryLine[]
  procedures: NursingSummaryLine[]
  imaging: NursingSummaryLine[]
  situation: NursingSummaryCheck[]
  incidents: NursingSummaryNote[]
  statistics: NursingStatisticsInfo
  submittedCount: number
  reportingCount: number
  reporters: NursingSummaryReporters
  /** Ủy quyền báo cáo có hiệu lực trong ngày (quyết định 48). */
  delegations?: NursingSummaryDelegation[] | null
}

/** Nội dung ủy quyền: báo cáo của khoa, số liệu HIS khi HIS lỗi. */
export type NursingDelegationContent = 'report' | 'hisEntry'

export const DELEGATION_CONTENT_LABELS: Record<NursingDelegationContent, string> = {
  report: 'Báo cáo của khoa',
  hisEntry: 'Số liệu HIS khi HIS lỗi',
}

/** Một ủy quyền có hiệu lực trong ngày của bảng tổng hợp. */
export interface NursingSummaryDelegation {
  departmentId: string
  departmentCode: string
  departmentName: string
  delegateName: string
  delegatorName: string
  startDate: string
  endDate: string
  contents: NursingDelegationContent[]
}

export type NursingDelegationStatus = 'Upcoming' | 'Active' | 'Expired' | 'Revoked'

/** Ủy quyền báo cáo điều dưỡng khi người báo cáo vắng (quyết định 48). */
export interface NursingDelegation {
  id: string
  departmentId: string
  /** Tên viết tắt của khoa (như bảng tổng hợp). */
  departmentCode: string
  departmentName: string
  delegatorUserId: string
  delegatorName: string
  delegateUserId: string
  delegateName: string
  startDate: string
  endDate: string
  contents: NursingDelegationContent[]
  reason: string | null
  status: NursingDelegationStatus
  createdAtUtc: string
  revokedBy: string | null
  revokedAtUtc: string | null
  canRevoke: boolean
}

export interface CreateNursingDelegationInput {
  departmentId?: string
  delegateUserId: string
  startDate: string
  endDate: string
  report: boolean
  hisEntry: boolean
  reason: string | null
}

/** Người có thể được ủy quyền: tài khoản đang dùng, thuộc một khoa, có quyền lập báo cáo điều dưỡng. */
export interface NursingDelegateCandidate {
  userId: string
  fullName: string
  title: string | null
  departmentName: string
}

export interface NursingReportDepartment {
  departmentId: string
  code: string
  name: string
  /** Tên viết tắt Phòng Điều dưỡng đặt, in trên bảng tổng hợp thay cho mã khoa (quyết định 45); `null` thì in mã khoa. */
  shortName: string | null
  /**
   * `false`: khoa đã ngừng sử dụng (không còn trong danh mục khoa/phòng, ví dụ sau khi lấy danh mục từ HIS) nhưng còn trong danh
   * sách báo cáo hoặc còn được giao chỉ số. Máy chủ không nhận khoa này khi lưu.
   */
  isActive: boolean
  isReporting: boolean
  bedCount: number | null
  /** Chỉ số toàn viện (mục B, D) khoa nhập tay khi HIS lỗi. */
  hisIndicators: string[]
}

export interface NursingStatisticsRefresh {
  summary: NursingSummary
  /** Mã khoa HIS trả về mà không khớp khoa nào (phòng CNTT sửa câu truy vấn). */
  unmatchedDepartmentCodes: string[]
}

/**
 * Một ngày của dashboard điều dưỡng (quyết định 47), theo ngày báo cáo. `values`: tổng từng chỉ số của các báo cáo đã gửi
 * (`null` khi chưa khoa nào gửi). `patientChange`: Số NB hiện tại trừ Số NB ngày hôm trước, trên các báo cáo có cả hai số.
 * `beds`, `bedPatients`: số giường và Số NB hiện tại của các khoa đã gửi có cài số giường (công suất = bedPatients / beds).
 */
export interface NursingDashboardDay {
  date: string
  submitted: number
  values: Record<string, number | null>
  patientChange: number | null
  beds: number | null
  bedPatients: number | null
  /** Số khoa báo "Không ổn" ít nhất một dòng. */
  issueDepartments: number
  /** Số báo cáo có phần nhập thêm (sự cố, bất thường). */
  incidents: number
}

/** Một khoa của dashboard (theo thứ tự bảng tổng hợp), số liệu ngày cuối kỳ. */
export interface NursingDashboardDepartment {
  departmentId: string
  /** Tên viết tắt (như bảng tổng hợp). */
  code: string
  name: string
  isReporting: boolean
  bedCount: number | null
  /** Số ngày đã gửi báo cáo trong kỳ. */
  submittedDays: number
  /** Đã gửi báo cáo ngày cuối kỳ; chưa gửi thì `values` rỗng. */
  submitted: boolean
  values: Record<string, number | null>
  /** Khóa các dòng tick báo "Không ổn" ngày cuối kỳ. */
  issues: string[]
  hasIncident: boolean
}

/** Một dòng tick: số lượt khoa báo "Không ổn" trong kỳ, các khoa báo ngày cuối kỳ. */
export interface NursingDashboardCheck {
  key: string
  label: string
  issueCount: number
  lastDayDepartments: string[]
}

/** Dashboard điều dưỡng của Phòng Điều dưỡng (quyết định 47): chỉ báo cáo đã gửi được tính. */
export interface NursingDashboard {
  from: string
  to: string
  /** Số khoa phải báo cáo (danh sách hiện tại). */
  reportingCount: number
  days: NursingDashboardDay[]
  departments: NursingDashboardDepartment[]
  checks: NursingDashboardCheck[]
}

/** Mã chỉ số HIS của cột "NB PK chờ nhập viện" (theo khoa). */
export const WAITING_ADMISSION = 'waitingAdmission'

const base = '/api/v1/nursing-reports'

export const nursingReportsApi = {
  form: (signal?: AbortSignal) => apiRequest<NursingReportFormDefinition>(`${base}/form`, { signal }),
  daily: (date: string, departmentId?: string, signal?: AbortSignal) =>
    apiRequest<NursingReport>(
      `${base}/daily?date=${date}${departmentId ? `&departmentId=${encodeURIComponent(departmentId)}` : ''}`,
      { signal },
    ),
  save: (input: SaveNursingReportInput) => apiRequest<NursingReport>(`${base}/daily`, { method: 'PUT', body: input }),
  saveHisEntry: (input: SaveNursingHisEntryInput) => apiRequest<NursingReport>(`${base}/his-entry`, { method: 'PUT', body: input }),
  reopen: (id: string, reason: string) => apiRequest<NursingReport>(`${base}/${id}/reopen`, { method: 'POST', body: { reason } }),
  pdf: (id: string) => apiBlob(`${base}/${id}/pdf`),
  summary: (date: string, signal?: AbortSignal) => apiRequest<NursingSummary>(`${base}/summary?date=${date}`, { signal }),
  summaryPdf: (date: string) => apiBlob(`${base}/summary/pdf?date=${date}`),
  summaryWorkbook: (date: string) => apiBlob(`${base}/summary/xlsx?date=${date}`),
  /** Ủy quyền của tôi (đã tạo hoặc được giao); `all` (Phòng Điều dưỡng) là mọi ủy quyền (quyết định 48). */
  delegations: (all: boolean, signal?: AbortSignal) =>
    apiRequest<NursingDelegation[]>(`${base}/delegations${all ? '?all=true' : ''}`, { signal }),
  delegationCandidates: (signal?: AbortSignal) => apiRequest<NursingDelegateCandidate[]>(`${base}/delegations/candidates`, { signal }),
  createDelegation: (input: CreateNursingDelegationInput) =>
    apiRequest<NursingDelegation>(`${base}/delegations`, { method: 'POST', body: input }),
  revokeDelegation: (id: string) => apiRequest<NursingDelegation>(`${base}/delegations/${id}/revoke`, { method: 'POST' }),
  /** Dashboard `days` ngày (1–92) đến hết ngày `to` (quyết định 47). */
  dashboard: (to: string, days: number, signal?: AbortSignal) =>
    apiRequest<NursingDashboard>(`${base}/dashboard?to=${to}&days=${days}`, { signal }),
  refreshStatistics: (date: string) =>
    apiRequest<NursingStatisticsRefresh>(`${base}/statistics/refresh`, { method: 'POST', body: { reportDate: date } }),
  /** Khoa phải báo cáo đứng trước theo thứ tự trên bảng tổng hợp, rồi các khoa khác theo tên. */
  departments: (signal?: AbortSignal) => apiRequest<NursingReportDepartment[]>(`${base}/departments`, { signal }),
  /**
   * `items`: khoa phải báo cáo theo thứ tự in trên bảng tổng hợp (quyết định 45). `hisEntry`: khoa nhập tay từng chỉ số toàn
   * viện khi HIS lỗi (đủ cả bảng; null là chưa giao). `shortNames`: chỉ các tên viết tắt đổi (null là bỏ, in lại mã khoa).
   */
  updateDepartments: (
    items: { departmentId: string; bedCount: number }[],
    hisEntry: Record<string, string | null>,
    shortNames: Record<string, string | null> = {},
  ) => apiRequest<NursingReportDepartment[]>(`${base}/departments`, { method: 'PUT', body: { items, hisEntry, shortNames } }),
}

export const nursingKeys = {
  all: ['nursing-reports'] as const,
  form: ['nursing-reports', 'form'] as const,
  daily: (date: string, departmentId?: string) => ['nursing-reports', 'daily', date, departmentId ?? 'mine'] as const,
  summary: (date: string) => ['nursing-reports', 'summary', date] as const,
  departments: ['nursing-reports', 'departments'] as const,
  dashboard: (to: string, days: number) => ['nursing-reports', 'dashboard', to, days] as const,
  delegations: (all: boolean) => ['nursing-reports', 'delegations', all ? 'all' : 'mine'] as const,
  delegationCandidates: ['nursing-reports', 'delegation-candidates'] as const,
}

export function useNursingReportForm() {
  return useQuery({
    queryKey: nursingKeys.form,
    queryFn: ({ signal }) => nursingReportsApi.form(signal),
    staleTime: Infinity,
  })
}

export function useNursingReport(date: string, departmentId?: string, enabled = true) {
  return useQuery({
    queryKey: nursingKeys.daily(date, departmentId),
    queryFn: ({ signal }) => nursingReportsApi.daily(date, departmentId, signal),
    enabled,
  })
}

export function useNursingSummary(date: string) {
  return useQuery({
    queryKey: nursingKeys.summary(date),
    queryFn: ({ signal }) => nursingReportsApi.summary(date, signal),
  })
}

/** Đổi khoảng ngày thì giữ số liệu cũ trên màn hình (mờ đi) cho tới khi có số liệu mới, không nháy trang. */
export function useNursingDashboard(to: string, days: number) {
  return useQuery({
    queryKey: nursingKeys.dashboard(to, days),
    queryFn: ({ signal }) => nursingReportsApi.dashboard(to, days, signal),
    placeholderData: keepPreviousData,
  })
}

export function useNursingDelegations(all: boolean, enabled = true) {
  return useQuery({
    queryKey: nursingKeys.delegations(all),
    queryFn: ({ signal }) => nursingReportsApi.delegations(all, signal),
    enabled,
  })
}

export function useNursingDelegationCandidates(enabled: boolean) {
  return useQuery({
    queryKey: nursingKeys.delegationCandidates,
    queryFn: ({ signal }) => nursingReportsApi.delegationCandidates(signal),
    enabled,
  })
}

export function useNursingReportDepartments(enabled: boolean) {
  return useQuery({
    queryKey: nursingKeys.departments,
    queryFn: ({ signal }) => nursingReportsApi.departments(signal),
    enabled,
  })
}
