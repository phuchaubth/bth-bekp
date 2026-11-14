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

export type ConsentDecision = 'Agreed' | 'Refused'
export type ConsentSignerRole = 'Patient' | 'Representative' | 'Hospital' | 'Staff'
export type ConsentSignatureMethod = 'Handwritten' | 'AccountConfirmation'

/** Một chữ ký trên phiếu (ảnh chữ ký tay chỉ có trong PDF). */
export interface ConsentSignature {
  role: ConsentSignerRole
  method: ConsentSignatureMethod
  signerName: string
  signerTitle: string | null
  signedAtUtc: string
  /** Bước xác nhận (chỉ với vai trò `Staff`). */
  stepKey?: string | null
}

/** Một bước xác nhận trung gian theo vai trò (định nghĩa ở `signatures.steps` của mẫu). */
export interface ConsentFormStep {
  key: string
  label: string
  roleCode: string
  roleName: string
  /** Bước đang chờ xác nhận (các bước làm lần lượt sau khi người bệnh ký). */
  isNext: boolean
  confirmation: ConsentSignature | null
}

export interface ConsentFormRef {
  id: string
  formNo: string
}

/** Thao tác người dùng hiện tại được phép làm với phiếu (máy chủ vẫn kiểm tra lại). */
export interface ConsentFormActions {
  canEdit: boolean
  canSubmit: boolean
  canReturnToDraft: boolean
  canCancel: boolean
  canStartSigning: boolean
  /** Người dùng hiện tại xác nhận được bước đang chờ. */
  canConfirmStep: boolean
  canConfirm: boolean
  canRevoke: boolean
  canReplace: boolean
}

export interface SigningSessionToken {
  token: string
  expiresAtUtc: string
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
  decision: ConsentDecision | null
  signatures: ConsentSignature[]
  steps: ConsentFormStep[]
  /** SHA-256 của PDF đã ký (phiếu đã hoàn tất). */
  signedPdfSha256: string | null
  replacesForm: ConsentFormRef | null
  replacedBy: ConsentFormRef | null
  actions: ConsentFormActions
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
  create: (input: { patientId: string; templateId: string; departmentId?: string | null; replacesFormId?: string }) =>
    apiRequest<ConsentFormDetail>(base, { method: 'POST', body: input }),
  update: (id: string, input: { data: FormData; signerKind: SignerKind; representativeId: string | null }) =>
    apiRequest<ConsentFormDetail>(`${base}/${id}`, { method: 'PUT', body: input }),
  submit: (id: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/submit`, { method: 'POST' }),
  returnToDraft: (id: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/return-to-draft`, { method: 'POST' }),
  cancel: (id: string, reason: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/cancel`, { method: 'POST', body: { reason } }),
  pdf: (id: string) => apiBlob(`${base}/${id}/pdf`),
  /** Mở phiên ký trên điện thoại: token chỉ dùng để người bệnh ký đúng phiếu này. */
  startSigning: (id: string) => apiRequest<SigningSessionToken>(`${base}/${id}/signing-session`, { method: 'POST' }),
  /** Gửi chữ ký của người bệnh bằng token phiên ký (không dùng phiên đăng nhập của nhân viên). */
  /** `decision` bỏ trống khi mẫu không hỏi Đồng ý/Từ chối (`signatures.decision = 'none'`). */
  signAsPatient: (id: string, signingToken: string, input: { decision?: ConsentDecision; image: string }) =>
    apiRequest<{ decision: ConsentDecision | null; signedAtUtc: string }>(`${base}/${id}/patient-signature`, {
      method: 'POST',
      body: input,
      token: signingToken,
    }),
  /** Xác nhận bước trung gian, kèm ảnh chữ ký tay của nhân viên (PNG Base64, quyết định 17). */
  confirmStep: (id: string, stepKey: string, image: string) =>
    apiRequest<ConsentFormDetail>(`${base}/${id}/confirm-step`, { method: 'POST', body: { stepKey, image } }),
  /** Xác nhận cuối và hoàn tất phiếu, kèm ảnh chữ ký tay của nhân viên. */
  complete: (id: string, image: string) =>
    apiRequest<ConsentFormDetail>(`${base}/${id}/complete`, { method: 'POST', body: { image } }),
  revoke: (id: string, reason: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/revoke`, { method: 'POST', body: { reason } }),
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

export const decisionLabels: Record<ConsentDecision, string> = {
  Agreed: 'Đồng ý',
  Refused: 'Từ chối',
}

export const signerRoleLabels: Record<ConsentSignerRole, string> = {
  Patient: 'Người bệnh',
  Representative: 'Người đại diện',
  Hospital: 'Phía bệnh viện',
  Staff: 'Nhân viên xác nhận',
}

export const signatureMethodLabels: Record<ConsentSignatureMethod, string> = {
  Handwritten: 'Ký tay trên điện thoại',
  AccountConfirmation: 'Xác nhận bằng tài khoản, ký tay',
}
