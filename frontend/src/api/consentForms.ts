import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { PagedResult } from './admin'
import type { TemplateDefinition } from './consentTemplates'
import { apiBlob, apiRequest } from './http'
import type { Gender, Relationship } from './patients'

export type ConsentFormStatus = 'Draft' | 'PendingSignature' | 'Completed' | 'Cancelled' | 'Revoked'
export type SignerKind = 'Patient' | 'Representative'

/** Giá trị trường của phiếu: chuỗi, mảng chuỗi (chọn nhiều), true (xác nhận) hoặc các dòng của bảng. */
export type FormValue = string | string[] | boolean | Record<string, string>[]
export type FormData = Record<string, FormValue | undefined>

export interface ScoreResult {
  total: number | null
  levelCode: string | null
  levelLabel: string | null
}

export interface ConsentFormListItem {
  id: string
  formNo: string
  status: ConsentFormStatus
  templateId: string
  templateCode: string
  templateName: string
  templateVersionNo: number
  patientId: string
  patientName: string
  patientCode: string | null
  departmentId: string
  departmentName: string
  createdByName: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ConsentPatientInfo {
  id: string
  fullName: string
  dateOfBirth: string
  gender: Gender
  citizenId: string | null
  phoneNumber: string | null
  address: string | null
  patientCode: string | null
}

export interface ConsentRepresentativeInfo {
  id: string
  fullName: string
  relationship: Relationship
  citizenId: string | null
  phoneNumber: string | null
}

export interface ConsentFormDetail {
  id: string
  formNo: string
  status: ConsentFormStatus
  templateId: string
  templateCode: string
  templateName: string
  templateVersionId: string
  templateVersionNo: number
  hospitalSignerRoleCode: string
  definition: TemplateDefinition
  data: FormData
  scores: Record<string, ScoreResult>
  /** Thiếu sót còn lại trước khi trình ký (chỉ có ở phiếu nháp). */
  issues: Record<string, string[]>
  patient: ConsentPatientInfo
  signerKind: SignerKind
  representative: ConsentRepresentativeInfo | null
  departmentId: string
  departmentName: string
  createdByUserId: string
  createdByName: string
  createdAtUtc: string
  updatedAtUtc: string
  submittedAtUtc: string | null
  completedAtUtc: string | null
  cancelledAtUtc: string | null
  cancelReason: string | null
  revokedAtUtc: string | null
  revokeReason: string | null
  actions: { canEdit: boolean; canSubmit: boolean; canReturnToDraft: boolean; canCancel: boolean }
}

export interface ConsentFormQuery {
  status?: ConsentFormStatus
  templateId?: string
  patientId?: string
  from?: string
  to?: string
  keyword?: string
  page: number
  pageSize: number
}

const base = '/api/v1/consent-forms'

function toSearchParams(query: ConsentFormQuery): URLSearchParams {
  const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
  for (const key of ['status', 'templateId', 'patientId', 'from', 'to', 'keyword'] as const) {
    const value = query[key]
    if (value) {
      params.set(key, value)
    }
  }
  return params
}

export const consentFormsApi = {
  search: (query: ConsentFormQuery, signal?: AbortSignal) =>
    apiRequest<PagedResult<ConsentFormListItem>>(`${base}?${toSearchParams(query)}`, { signal }),
  get: (id: string, signal?: AbortSignal) => apiRequest<ConsentFormDetail>(`${base}/${id}`, { signal }),
  create: (input: { patientId: string; templateId: string; departmentId?: string | null }) =>
    apiRequest<ConsentFormDetail>(base, { method: 'POST', body: input }),
  update: (id: string, input: { data: FormData; signerKind: SignerKind; representativeId: string | null }) =>
    apiRequest<ConsentFormDetail>(`${base}/${id}`, { method: 'PUT', body: input }),
  submit: (id: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/submit`, { method: 'POST' }),
  returnToDraft: (id: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/return-to-draft`, { method: 'POST' }),
  cancel: (id: string, reason: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/cancel`, { method: 'POST', body: { reason } }),
  pdf: (id: string) => apiBlob(`${base}/${id}/pdf`),
}

export const consentFormKeys = {
  all: ['consent-forms'] as const,
  list: (query: ConsentFormQuery) => ['consent-forms', 'list', query] as const,
  detail: (id: string) => ['consent-forms', 'detail', id] as const,
}

export function useConsentForms(query: ConsentFormQuery, enabled = true) {
  return useQuery({
    queryKey: consentFormKeys.list(query),
    queryFn: ({ signal }) => consentFormsApi.search(query, signal),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function useConsentForm(id: string) {
  return useQuery({ queryKey: consentFormKeys.detail(id), queryFn: ({ signal }) => consentFormsApi.get(id, signal) })
}

export const formStatusLabels: Record<ConsentFormStatus, string> = {
  Draft: 'Nháp',
  PendingSignature: 'Chờ ký',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
  Revoked: 'Đã thu hồi',
}

export const formStatusColors: Record<ConsentFormStatus, string> = {
  Draft: 'default',
  PendingSignature: 'gold',
  Completed: 'green',
  Cancelled: 'red',
  Revoked: 'volcano',
}
