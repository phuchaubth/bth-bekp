import { useQuery } from '@tanstack/react-query'
import type { ConsentDecision, KioskIdentityMethod } from './consentForms'
import type { TemplateDefinition, TemplateStatement } from './consentTemplates'
import { apiRequest } from './http'
import type { Gender } from './patients'

/** Một mẫu người bệnh tự làm trên kiosk, kèm định nghĩa để hiện câu hỏi và nội dung phiếu. */
export interface KioskTemplate {
  id: string
  code: string
  name: string
  definition: TemplateDefinition
}

/** Cấu hình màn hình kiosk: khoa của máy, các mẫu theo thứ tự làm, một ô lời xác nhận chung cho cả bộ. */
export interface KioskConfig {
  departmentName: string
  /** Có ít nhất một mẫu dùng được; chưa sẵn sàng thì mời người bệnh đến quầy. */
  ready: boolean
  templates: KioskTemplate[]
  statement: TemplateStatement | null
}

/** Thông tin đọc từ mã QR trên CCCD (máy kiosk tách trường trước khi gửi). */
export interface KioskCardData {
  fullName: string
  gender: Gender
  permanentAddress: string | null
  issueDate: string | null
  oldIdNumber: string | null
}

/** Thông tin người bệnh tự khai. */
export interface KioskSelfReport {
  fullName: string
  gender: Gender
  phoneNumber: string
  currentAddress: string
}

export interface KioskIdentityRequest {
  method: KioskIdentityMethod
  /** yyyy-MM-dd */
  dateOfBirth: string
  citizenId?: string | null
  patientCode?: string | null
  card?: KioskCardData | null
  selfReport?: KioskSelfReport | null
}

/**
 * Kết quả nhận diện cho màn hình "Đây có phải thông tin của ông/bà?". Số CCCD đã che; không có số điện thoại; nhập mã thì
 * không có địa chỉ (chỉ báo đã có).
 */
export interface KioskIdentity {
  method: KioskIdentityMethod
  foundRecord: boolean
  fullName: string
  dateOfBirth: string
  gender: Gender
  citizenIdMasked: string | null
  patientCode: string | null
  permanentAddress: string | null
  hasAddressOnFile: boolean
  hasPhoneNumber: boolean
  /** Thông tin hồ sơ còn thiếu, kiosk hỏi thêm (hiện là `phoneNumber`). */
  missingFields: string[]
  /** Nguồn tự điền đã có giá trị: câu hỏi lấy từ các nguồn này không hỏi lại. */
  prefilledSources: string[]
}

export interface KioskFormAnswers {
  templateId: string
  data: Record<string, unknown>
  decision: ConsentDecision | null
}

export interface KioskSubmitRequest {
  identity: KioskIdentityRequest
  phoneNumber: string | null
  forms: KioskFormAnswers[]
  statement: string
  image: string
}

export interface KioskSubmittedForm {
  id: string
  formNo: string
  templateName: string
  decision: ConsentDecision | null
}

export interface KioskSubmitResult {
  patientName: string
  patientCode: string | null
  bundleId: string | null
  forms: KioskSubmittedForm[]
  signedAtUtc: string
}

export const kioskApi = {
  config: (signal?: AbortSignal) => apiRequest<KioskConfig>('/api/v1/kiosk/config', { signal }),
  identify: (request: KioskIdentityRequest) =>
    apiRequest<KioskIdentity>('/api/v1/kiosk/identify', { method: 'POST', body: request }),
  submit: (request: KioskSubmitRequest) =>
    apiRequest<KioskSubmitResult>('/api/v1/kiosk/submit', { method: 'POST', body: request }),
}

export const kioskKeys = { config: ['kiosk', 'config'] as const, settings: ['settings', 'kiosk'] as const }

export function useKioskConfig() {
  return useQuery({ queryKey: kioskKeys.config, queryFn: ({ signal }) => kioskApi.config(signal), staleTime: 5 * 60_000 })
}

/** Một mẫu trong cấu hình kiosk, kèm lý do chưa dùng được (nếu có). */
export interface KioskTemplateSetting {
  id: string
  code: string
  name: string
  problems: string[]
}

export interface KioskSettings {
  templates: KioskTemplateSetting[]
  updatedAtUtc: string | null
}

export const kioskSettingsApi = {
  get: (signal?: AbortSignal) => apiRequest<KioskSettings>('/api/v1/settings/kiosk', { signal }),
  update: (templateIds: string[]) =>
    apiRequest<KioskSettings>('/api/v1/settings/kiosk', { method: 'PUT', body: { templateIds } }),
}

export function useKioskSettings() {
  return useQuery({ queryKey: kioskKeys.settings, queryFn: ({ signal }) => kioskSettingsApi.get(signal) })
}
