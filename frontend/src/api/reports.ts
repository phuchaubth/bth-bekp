import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { PagedResult } from './admin'
import type { ConsentFormStatus } from './consentForms'
import { apiDownload, apiRequest } from './http'

// ---------- Xuất dữ liệu (màn hình 13) ----------

export type ExportFormat = 'Xlsx' | 'Csv'

export interface ExportColumn {
  key: string
  header: string
  group: string
  default: boolean
  sensitive: boolean
}

export interface ExportFilter {
  status?: ConsentFormStatus
  templateId?: string
  departmentId?: string
  from?: string
  to?: string
}

export interface ExportRequest {
  format: ExportFormat
  filter: ExportFilter
  columns: string[]
  includeContent: boolean
  includeSensitiveData: boolean
}

export interface ExportFilterSnapshot {
  status: ConsentFormStatus | null
  templateId: string | null
  templateCode: string | null
  templateName: string | null
  departmentId: string | null
  departmentName: string | null
  from: string | null
  to: string | null
}

export interface ExportJob {
  id: string
  requestedAtUtc: string
  requestedByUserId: string
  requestedByName: string
  format: ExportFormat
  filter: ExportFilterSnapshot
  columns: string[]
  includesContent: boolean
  includesSensitiveData: boolean
  rowCount: number
  fileName: string
  fileSizeBytes: number
  fileSha256: string
  durationMs: number
}

export const exportFormatLabels: Record<ExportFormat, string> = { Xlsx: 'Excel (.xlsx)', Csv: 'CSV (.csv)' }

export const exportsApi = {
  columns: (signal?: AbortSignal) => apiRequest<ExportColumn[]>('/api/v1/exports/consent-forms/columns', { signal }),
  exportConsentForms: (request: ExportRequest) => apiDownload('/api/v1/exports/consent-forms', request),
  history: (page: number, pageSize: number, signal?: AbortSignal) =>
    apiRequest<PagedResult<ExportJob>>(`/api/v1/exports?page=${page}&pageSize=${pageSize}`, { signal }),
}

export const exportKeys = {
  columns: ['exports', 'columns'] as const,
  history: (page: number, pageSize: number) => ['exports', 'history', page, pageSize] as const,
  allHistory: ['exports', 'history'] as const,
}

export function useExportColumns() {
  return useQuery({ queryKey: exportKeys.columns, queryFn: ({ signal }) => exportsApi.columns(signal), staleTime: Infinity })
}

export function useExportHistory(page: number, pageSize: number) {
  return useQuery({
    queryKey: exportKeys.history(page, pageSize),
    queryFn: ({ signal }) => exportsApi.history(page, pageSize, signal),
    placeholderData: keepPreviousData,
  })
}

// ---------- Báo cáo thống kê (màn hình 14) ----------

export interface ReportQuery {
  from?: string
  to?: string
  departmentId?: string
  templateId?: string
}

export interface StatusCounts {
  total: number
  draft: number
  pendingSignature: number
  completed: number
  cancelled: number
  revoked: number
}

export interface DecisionCounts {
  agreed: number
  refused: number
  refusalRate: number | null
}

export interface GroupStats {
  id: string
  code: string
  name: string
  counts: StatusCounts
  decisions: DecisionCounts
}

export interface ReportSummary {
  from: string
  to: string
  totals: StatusCounts
  decisions: DecisionCounts
  signers: { patient: number; representative: number }
  completionTime: {
    count: number
    averageMinutes: number | null
    medianMinutes: number | null
    percentile90Minutes: number | null
    upTo15Minutes: number
    upTo1Hour: number
    upTo1Day: number
    over1Day: number
  }
  byDepartment: GroupStats[]
  byTemplate: GroupStats[]
  daily: { date: string; counts: StatusCounts }[]
}

export const reportsApi = {
  summary: (query: ReportQuery, signal?: AbortSignal) => {
    const params = new URLSearchParams()
    for (const key of ['from', 'to', 'departmentId', 'templateId'] as const) {
      if (query[key]) {
        params.set(key, query[key]!)
      }
    }
    const text = params.toString()
    return apiRequest<ReportSummary>(`/api/v1/reports/summary${text ? `?${text}` : ''}`, { signal })
  },
}

export function useReportSummary(query: ReportQuery) {
  return useQuery({
    queryKey: ['reports', 'summary', query],
    queryFn: ({ signal }) => reportsApi.summary(query, signal),
    placeholderData: keepPreviousData,
  })
}

// ---------- Tổng quan (màn hình 2) ----------

export interface DashboardFormItem {
  id: string
  formNo: string
  status: ConsentFormStatus
  patientName: string
  templateCode: string
  templateName: string
  departmentName: string
  atUtc: string
}

export interface Dashboard {
  drafts: number
  pendingSignature: number
  completedToday: number
  revokedLast30Days: number
  awaitingMyConfirmation: { total: number; items: DashboardFormItem[] }
  myOpenForms: { total: number; items: DashboardFormItem[] }
  last14Days: { date: string; created: number; completed: number }[]
}

export function useDashboard(enabled: boolean) {
  return useQuery({
    queryKey: ['dashboard'],
    queryFn: ({ signal }) => apiRequest<Dashboard>('/api/v1/dashboard', { signal }),
    enabled,
    refetchInterval: 60_000,
  })
}

// ---------- Nhật ký hệ thống (màn hình 15) ----------

export interface AuditLogQuery {
  userId?: string
  action?: string
  entityType?: string
  entityId?: string
  fromUtc?: string
  toUtc?: string
  page: number
  pageSize: number
}

export interface AuditLogEntry {
  id: string
  occurredAtUtc: string
  action: string
  entityType: string | null
  entityId: string | null
  userId: string | null
  userName: string | null
  ipAddress: string | null
  correlationId: string | null
  details: string | null
}

export const auditApi = {
  search: (query: AuditLogQuery, signal?: AbortSignal) => {
    const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
    for (const key of ['userId', 'action', 'entityType', 'entityId', 'fromUtc', 'toUtc'] as const) {
      if (query[key]) {
        params.set(key, query[key]!)
      }
    }
    return apiRequest<PagedResult<AuditLogEntry>>(`/api/v1/audit-logs?${params}`, { signal })
  },
}

export function useAuditLogs(query: AuditLogQuery) {
  return useQuery({
    queryKey: ['audit-logs', query],
    queryFn: ({ signal }) => auditApi.search(query, signal),
    placeholderData: keepPreviousData,
  })
}

/** Tên tiếng Việt của các hành động audit thường gặp (hành động lạ hiển thị nguyên mã). */
export const auditActionLabels: Record<string, string> = {
  Create: 'Tạo mới',
  Update: 'Cập nhật',
  Delete: 'Xóa',
  'Auth.LoginSucceeded': 'Đăng nhập thành công',
  'Auth.LoginFailed': 'Đăng nhập thất bại',
  'Auth.LockedOut': 'Khóa tài khoản tạm thời',
  'Auth.Logout': 'Đăng xuất',
  'Auth.RefreshTokenReuseDetected': 'Phát hiện dùng lại refresh token',
  'Auth.PasswordChanged': 'Đổi mật khẩu',
  'Patients.Viewed': 'Xem hồ sơ người bệnh',
  'Patients.LookedUp': 'Tra cứu người bệnh theo CCCD',
  'ConsentForms.Viewed': 'Xem phiếu',
  'ConsentForms.PdfViewed': 'Tải PDF phiếu',
  'ConsentForms.SigningStarted': 'Mở phiên ký',
  'ConsentForms.PatientSigned': 'Người bệnh ký',
  'ConsentForms.Completed': 'Hoàn tất phiếu',
  'ConsentForms.Revoked': 'Thu hồi phiếu',
  'Exports.ConsentForms': 'Xuất dữ liệu phiếu',
}

export const auditEntityLabels: Record<string, string> = {
  User: 'Người dùng',
  Role: 'Vai trò',
  Department: 'Khoa/phòng',
  Patient: 'Người bệnh',
  LegalRepresentative: 'Người đại diện',
  ConsentTemplate: 'Mẫu phiếu',
  ConsentTemplateVersion: 'Phiên bản mẫu',
  ConsentForm: 'Phiếu đồng thuận',
  ConsentSignature: 'Chữ ký',
  ExportJob: 'Lần xuất dữ liệu',
  RefreshToken: 'Phiên đăng nhập',
}
