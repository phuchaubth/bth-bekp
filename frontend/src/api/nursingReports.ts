import { useQuery } from '@tanstack/react-query'
import { apiBlob, apiRequest } from './http'

/** Báo cáo điều dưỡng hằng ngày (quyết định 31). "New": chưa lưu lần nào (mẫu trống hoặc điền sẵn). */
export type NursingReportState = 'New' | 'Draft' | 'Submitted'

export type NursingCheckValue = 'ok' | 'issue'

export interface NursingReportItem {
  key: string
  label: string
  carryOver: boolean
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
}

export interface NursingReportDepartment {
  departmentId: string
  code: string
  name: string
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
  refreshStatistics: (date: string) =>
    apiRequest<NursingStatisticsRefresh>(`${base}/statistics/refresh`, { method: 'POST', body: { reportDate: date } }),
  departments: (signal?: AbortSignal) => apiRequest<NursingReportDepartment[]>(`${base}/departments`, { signal }),
  /** `hisEntry`: khoa nhập tay từng chỉ số toàn viện khi HIS lỗi (đủ cả bảng; null là chưa giao). */
  updateDepartments: (items: { departmentId: string; bedCount: number }[], hisEntry: Record<string, string | null>) =>
    apiRequest<NursingReportDepartment[]>(`${base}/departments`, { method: 'PUT', body: { items, hisEntry } }),
}

export const nursingKeys = {
  all: ['nursing-reports'] as const,
  form: ['nursing-reports', 'form'] as const,
  daily: (date: string, departmentId?: string) => ['nursing-reports', 'daily', date, departmentId ?? 'mine'] as const,
  summary: (date: string) => ['nursing-reports', 'summary', date] as const,
  departments: ['nursing-reports', 'departments'] as const,
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

export function useNursingReportDepartments(enabled: boolean) {
  return useQuery({
    queryKey: nursingKeys.departments,
    queryFn: ({ signal }) => nursingReportsApi.departments(signal),
    enabled,
  })
}
