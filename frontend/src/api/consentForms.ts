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
/** Chữ ký của nhân viên khi xác nhận: dùng chữ ký mẫu đã lưu, hoặc ảnh vẽ trong khung ký (PNG Base64). */
export type StaffSignatureInput = { useSpecimen: true } | { image: string }

/**
 * Lượt tiếp nhận (HIS) chọn khi ký hoàn tất (quyết định 25): số tiếp nhận và ngày tiếp nhận (yyyy-MM-dd, giờ bệnh viện) để
 * máy chủ tra lại HIS trước khi gắn.
 */
export interface EncounterChoice {
  encounterNo: string
  encounterDate: string
}

/** Lượt tiếp nhận đã gắn với phiếu; thời gian là giờ bệnh viện (UTC+7). */
export interface ConsentFormEncounter {
  encounterNo: string
  receivedAt: string
  place: string | null
  receivedBy: string | null
}

/** Một lượt tiếp nhận để chọn, kèm các phiếu đã gắn vào lượt đó. */
export interface EncounterOption extends ConsentFormEncounter {
  forms: { id: string; formNo: string; templateName: string; status: ConsentFormStatus }[]
}

/** Danh sách lượt tiếp nhận của người bệnh từ ngày đến ngày (yyyy-MM-dd), mới nhất trước. */
export interface EncounterList {
  from: string
  to: string
  items: EncounterOption[]
}

/** Khoảng ngày tra lượt tiếp nhận; bỏ trống là hôm nay (theo máy chủ). */
export interface EncounterRange {
  from: string
  to: string
}

/** Nguồn danh sách lượt tiếp nhận: người bệnh của một phiếu, hoặc của một bộ phiếu. */
export type EncounterSource = { formId: string } | { bundleId: string }

/** Ngày tiếp nhận (giờ bệnh viện) của một lượt: máy chủ luôn trả thời gian kèm múi giờ +07:00. */
export function encounterDate(receivedAt: string): string {
  return receivedAt.slice(0, 10)
}

export interface ConsentSignature {
  role: ConsentSignerRole
  method: ConsentSignatureMethod
  signerName: string
  signerTitle: string | null
  signedAtUtc: string
  /** Bước xác nhận (chỉ với vai trò `Staff`). */
  stepKey?: string | null
  /** Lời xác nhận người bệnh/người đại diện ghi khi ký, ví dụ "Tôi đã nghe, hiểu và đồng ý". */
  statement?: string | null
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

/** Cách người bệnh tự nhận diện trên kiosk (quyết định 24). */
export type KioskIdentityMethod = 'CitizenIdCard' | 'LookupCode' | 'SelfReported'

export const kioskIdentityLabels: Record<KioskIdentityMethod, string> = {
  CitizenIdCard: 'Lập tại kiosk · quét CCCD',
  LookupCode: 'Lập tại kiosk · nhập mã y tế/CCCD',
  SelfReported: 'Lập tại kiosk · tự khai',
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
  /** Thông báo cho người lập phiếu khi phiếu còn sửa được (ví dụ: hồ sơ còn số thẻ BHYT mã cũ 15 ký tự). */
  notices: string[]
  /** Bộ phiếu (quyết định 23) mà phiếu thuộc về; rỗng với phiếu lập lẻ. */
  bundleId?: string | null
  /** Phiếu do người bệnh tự làm trên kiosk và cách người bệnh nhận diện (quyết định 24). */
  kioskIdentity?: KioskIdentityMethod | null
  /** Lượt tiếp nhận trên HIS mà phiếu thuộc về, gắn khi ký hoàn tất (quyết định 25). */
  encounter?: ConsentFormEncounter | null
}

/** Một phiếu trong bộ phiếu, kèm thao tác người dùng hiện tại được làm. */
export interface ConsentFormBundleItem {
  id: string
  formNo: string
  status: ConsentFormStatus
  templateId: string
  templateCode: string
  templateName: string
  patientSigned: boolean
  /** Mẫu hỏi Đồng ý/Không đồng ý khi người bệnh ký. */
  requiresDecision: boolean
  /** Bước xác nhận đang chờ sau khi người bệnh ký, ví dụ "Điều dưỡng khoa". */
  nextStepLabel: string | null
  actions: ConsentFormActions
}

/** Bộ phiếu của một người bệnh: lập cùng lúc, người bệnh ký một lần cho cả bộ. */
export interface ConsentFormBundle {
  bundleId: string
  patientId: string
  patientName: string
  forms: ConsentFormBundleItem[]
  /** Mở được một phiên ký cho mọi phiếu đang chờ người bệnh ký. */
  canStartSigning: boolean
  /** Người dùng hiện tại có phiếu đến lượt xác nhận. */
  canConfirm: boolean
  notices: string[]
  kioskIdentity?: KioskIdentityMethod | null
}

export interface BundleSigningSession {
  token: string
  expiresAtUtc: string
  formIds: string[]
}

export interface BundleConfirmResult {
  confirmed: { id: string; formNo: string; action: 'step' | 'completed'; label: string }[]
  failed: { id: string; formNo: string; message: string }[]
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
  signAsPatient: (id: string, signingToken: string, input: { decision?: ConsentDecision; image: string; statement?: string }) =>
    apiRequest<{ decision: ConsentDecision | null; signedAtUtc: string }>(`${base}/${id}/patient-signature`, {
      method: 'POST',
      body: input,
      token: signingToken,
    }),
  /** Xác nhận bước trung gian, kèm ảnh chữ ký tay của nhân viên (PNG Base64, quyết định 17). */
  confirmStep: (id: string, stepKey: string, signature: StaffSignatureInput) =>
    apiRequest<ConsentFormDetail>(`${base}/${id}/confirm-step`, { method: 'POST', body: { stepKey, ...signature } }),
  /**
   * Xác nhận cuối và hoàn tất phiếu, kèm ảnh chữ ký tay của nhân viên; khi bệnh viện bật gắn lượt tiếp nhận thì kèm lượt
   * tiếp nhận đã chọn (quyết định 25).
   */
  complete: (id: string, signature: StaffSignatureInput, encounter?: EncounterChoice | null) =>
    apiRequest<ConsentFormDetail>(`${base}/${id}/complete`, { method: 'POST', body: { ...signature, ...encounter } }),
  /** Lượt tiếp nhận (HIS) của người bệnh để chọn khi ký hoàn tất: mặc định hôm nay, hoặc từ ngày đến ngày. */
  encounters: (source: EncounterSource, range: EncounterRange | null, signal?: AbortSignal) => {
    const path = 'formId' in source ? `${base}/${source.formId}/encounters` : `${base}/bundles/${source.bundleId}/encounters`
    const query = range ? `?${new URLSearchParams({ from: range.from, to: range.to })}` : ''
    return apiRequest<EncounterList>(`${path}${query}`, { signal })
  },
  revoke: (id: string, reason: string) => apiRequest<ConsentFormDetail>(`${base}/${id}/revoke`, { method: 'POST', body: { reason } }),
  /** Lập bộ phiếu: nhiều mẫu cho cùng một người bệnh trong một lần. */
  createBundle: (input: { patientId: string; templateIds: string[]; departmentId?: string | null }) =>
    apiRequest<ConsentFormBundle>(`${base}/bundle`, { method: 'POST', body: input }),
  getBundle: (bundleId: string, signal?: AbortSignal) => apiRequest<ConsentFormBundle>(`${base}/bundles/${bundleId}`, { signal }),
  /** Một phiên ký cho mọi phiếu của bộ đang chờ người bệnh ký. */
  startBundleSigning: (bundleId: string) =>
    apiRequest<BundleSigningSession>(`${base}/bundles/${bundleId}/signing-session`, { method: 'POST' }),
  /** Người bệnh ký một lần cho cả bộ: một chữ ký, một lời xác nhận; quyết định riêng cho phiếu có hỏi (khóa: mã phiếu). */
  signBundleAsPatient: (
    signingToken: string,
    input: { image: string; statement?: string; decisions: Record<string, ConsentDecision> },
  ) =>
    apiRequest<{ signedAtUtc: string; forms: { id: string; formNo: string; decision: ConsentDecision | null }[] }>(
      `${base}/signing-bundle/patient-signature`,
      { method: 'POST', body: input, token: signingToken },
    ),
  /**
   * Xác nhận một lần các phiếu trong bộ đang đến lượt người dùng hiện tại; lượt tiếp nhận (nếu bật) gắn vào các phiếu được
   * hoàn tất trong lần này.
   */
  confirmBundle: (bundleId: string, signature: StaffSignatureInput, encounter?: EncounterChoice | null) =>
    apiRequest<BundleConfirmResult>(`${base}/bundles/${bundleId}/confirm`, { method: 'POST', body: { ...signature, ...encounter } }),
}

export const consentFormKeys = {
  all: ['consent-forms'] as const,
  list: (query: ConsentFormQuery) => ['consent-forms', 'list', query] as const,
  detail: (id: string) => ['consent-forms', 'detail', id] as const,
  bundle: (bundleId: string) => ['consent-forms', 'bundle', bundleId] as const,
  encounters: (source: EncounterSource, range: EncounterRange | null) => ['consent-forms', 'encounters', source, range] as const,
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

export function useConsentFormBundle(bundleId: string | null | undefined) {
  return useQuery({
    queryKey: consentFormKeys.bundle(bundleId ?? ''),
    queryFn: ({ signal }) => consentFormsApi.getBundle(bundleId!, signal),
    enabled: !!bundleId,
  })
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
