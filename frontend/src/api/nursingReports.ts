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
}

export interface NursingSummaryCheck {
  key: string
  label: string
  text: string | null
  issueDepartments: string[]
}

export interface NursingSummaryNote {
  departmentId: string
  departmentName: string
  text: string
}

export interface NursingStatisticsInfo {
  /** Đã cấu hình API thông tin người bệnh (lấy được số liệu HIS). */
  hisAvailable: boolean
  hasValues: boolean
  source: 'His' | 'Manual' | null
  updatedAtUtc: string | null
  updatedBy: string | null
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
}

export interface NursingReportDepartment {
  departmentId: string
  code: string
  name: string
  isReporting: boolean
  bedCount: number | null
}

export interface NursingStatisticsInput {
  reportDate: string
  hospital: Record<string, number | null>
  waitingAdmission: Record<string, number | null>
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
  reopen: (id: string, reason: string) => apiRequest<NursingReport>(`${base}/${id}/reopen`, { method: 'POST', body: { reason } }),
  pdf: (id: string) => apiBlob(`${base}/${id}/pdf`),
  summary: (date: string, signal?: AbortSignal) => apiRequest<NursingSummary>(`${base}/summary?date=${date}`, { signal }),
  summaryPdf: (date: string) => apiBlob(`${base}/summary/pdf?date=${date}`),
  summaryWorkbook: (date: string) => apiBlob(`${base}/summary/xlsx?date=${date}`),
  refreshStatistics: (date: string) =>
    apiRequest<NursingStatisticsRefresh>(`${base}/statistics/refresh`, { method: 'POST', body: { reportDate: date } }),
  updateStatistics: (input: NursingStatisticsInput) => apiRequest<NursingSummary>(`${base}/statistics`, { method: 'PUT', body: input }),
  departments: (signal?: AbortSignal) => apiRequest<NursingReportDepartment[]>(`${base}/departments`, { signal }),
  updateDepartments: (items: { departmentId: string; bedCount: number }[]) =>
    apiRequest<NursingReportDepartment[]>(`${base}/departments`, { method: 'PUT', body: { items } }),
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
