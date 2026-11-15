import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './http'

/** Khớp với định nghĩa mẫu phía máy chủ (Consent.Application.ConsentTemplates.TemplateDefinition). */
export type BlockType =
  | 'paragraph'
  | 'list'
  | 'text'
  | 'date'
  | 'datetime'
  | 'choice'
  | 'multiChoice'
  | 'confirm'
  | 'repeater'
  | 'score'
  | 'computed'

export interface TemplateOption {
  value: string
  label: string
  score?: number
  /** Khi câu hỏi có `valueFrom`: giá trị nguồn ≥ min và < max (bỏ trống: không giới hạn). */
  min?: number
  max?: number
}

export interface TemplateColumn {
  key: string
  label: string
  required?: boolean
  maxLength?: number
  pattern?: string
  patternMessage?: string
}

export interface TemplateCondition {
  field: string
  in?: string[]
  minLevel?: string
}

export interface ScoreLevel {
  code: string
  label: string
  min: number
}

export interface TemplateBlock {
  type: BlockType
  key?: string
  label?: string
  text?: string
  items?: string[]
  required?: boolean
  requireAll?: boolean
  minItems?: number
  maxItems?: number
  maxLength?: number
  pattern?: string
  patternMessage?: string
  prefill?: string
  layout?: 'inline' | 'ordered'
  /** PDF in trường này tiếp trên dòng của trường đứng trước (trường "nhãn: giá trị"); màn hình nhập liệu không đổi. */
  sameLine?: boolean
  /** Câu hỏi chọn: PDF chỉ in các ô lựa chọn (chữ đậm, dòng riêng), không in câu hỏi; màn hình vẫn hiện câu hỏi. */
  printOptionsOnly?: boolean
  options?: TemplateOption[]
  columns?: TemplateColumn[]
  visibleWhen?: TemplateCondition
  sumOf?: string[]
  levels?: ScoreLevel[]
  /** Câu hỏi chọn một tự chọn theo giá trị số của trường đứng trước (ví dụ mức BMI theo BMI tự tính). */
  valueFrom?: string
  /** Khối `computed`: công thức (hiện có `bmi`) và các trường đầu vào theo thứ tự (bmi: cân nặng kg, chiều cao cm). */
  formula?: string
  inputs?: string[]
}

export interface TemplateSection {
  title?: string
  blocks: TemplateBlock[]
}

export interface TemplateDefinition {
  title: string
  subtitle?: string
  /** PDF không in dòng "Khoa/phòng: …" dưới tên bệnh viện. */
  hideDepartment?: boolean
  /** Mẫu song ngữ: phần hệ thống in trên PDF (mục I, dòng ngày tháng, ghi chú ô ký) kèm tiếng Anh. */
  bilingual?: boolean
  /** Mã tài liệu: đầu PDF in như bản giấy (logo, quốc hiệu ở giữa, ô mã số – lần ban hành – ngày hiệu lực). */
  documentControl?: { code?: string; issueNo?: string; effectiveDate?: string }
  /** Dòng thông tin người bệnh in như bản giấy, thay cho mục "I. Thông tin người bệnh"; mỗi dòng 1–2 mục. */
  patientLines?: PatientLineItem[][]
  sections: TemplateSection[]
  signatures?: TemplateSignatures
}

/** Một mục trên dòng thông tin người bệnh: thông tin hồ sơ (`patient`) hoặc trường của phiếu (`field`). */
export interface PatientLineItem {
  patient?: 'fullName' | 'dateOfBirth' | 'gender' | 'patientCode' | 'citizenId' | 'phoneNumber' | 'address'
  field?: string
  label?: string
  /** Giới tính: nhãn ô Nam, ô Nữ (ví dụ "Nam/Male", "Nữ/Female"). */
  options?: string[]
}

/**
 * Khối chữ ký. `decision: 'none'`: người bệnh chỉ ký xác nhận (phiếu khai thông tin, phiếu nhận định), không hỏi
 * Đồng ý/Từ chối. Mặc định bắt buộc chọn; `decisionLabel`, `agreeLabel`, `refuseLabel` thay câu hỏi và nhãn mặc định.
 */
export interface TemplateSignatures {
  patient?: string
  representative?: string
  hospital?: string
  hospitalHint?: string
  decision?: 'required' | 'none'
  decisionLabel?: string
  agreeLabel?: string
  refuseLabel?: string
  /** Các bước xác nhận trung gian theo vai trò, làm lần lượt trước bước xác nhận cuối. */
  steps?: { key: string; roleCode: string; label: string; hint?: string }[]
  /** Ô lời xác nhận của người ký (ví dụ "Tôi đã nghe, hiểu và đồng ý") kèm câu gợi ý bấm chọn nhanh. */
  statement?: TemplateStatement
  /** PDF in các ô ký phía bệnh viện trước (bên trái), ô của người bệnh sau cùng. */
  hospitalFirst?: boolean
  /** PDF in tên ô ký đúng như đã ghi (không viết hoa toàn bộ, không in đậm). */
  plainTitles?: boolean
}

export interface TemplateStatement {
  label?: string
  required?: boolean
  suggestions?: string[]
}

/** Giới hạn của máy chủ (TemplateStatement). */
export const STATEMENT_MAX_LENGTH = 300
export const STATEMENT_MAX_SUGGESTIONS = 8

export type TemplateVersionStatus = 'Draft' | 'Published' | 'Retired'

export interface ConsentTemplateListItem {
  id: string
  code: string
  name: string
  hospitalSignerRoleCode: string
  departmentId: string | null
  isActive: boolean
  publishedVersionNo: number | null
  publishedAtUtc: string | null
  draftVersionNo: number | null
  updatedAtUtc: string
}

export interface ConsentTemplateVersionSummary {
  versionNo: number
  status: TemplateVersionStatus
  createdAtUtc: string
  updatedAtUtc: string
  publishedAtUtc: string | null
  retiredAtUtc: string | null
}

export interface ConsentTemplateDetail {
  id: string
  code: string
  name: string
  hospitalSignerRoleCode: string
  departmentId: string | null
  isActive: boolean
  versions: ConsentTemplateVersionSummary[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ConsentTemplateVersion {
  templateId: string
  templateCode: string
  templateName: string
  versionId: string
  versionNo: number
  status: TemplateVersionStatus
  definition: TemplateDefinition
  updatedAtUtc: string
  publishedAtUtc: string | null
}

export interface TemplateInput {
  code?: string
  name: string
  hospitalSignerRoleCode: string
  departmentId: string | null
  isActive?: boolean
}

const base = '/api/v1/consent-templates'

export const consentTemplatesApi = {
  list: (includeInactive: boolean, signal?: AbortSignal) =>
    apiRequest<ConsentTemplateListItem[]>(`${base}?includeInactive=${includeInactive}`, { signal }),
  get: (id: string, signal?: AbortSignal) => apiRequest<ConsentTemplateDetail>(`${base}/${id}`, { signal }),
  getVersion: (id: string, versionNo: number, signal?: AbortSignal) =>
    apiRequest<ConsentTemplateVersion>(`${base}/${id}/versions/${versionNo}`, { signal }),
  available: (departmentId: string | null, signal?: AbortSignal) =>
    apiRequest<ConsentTemplateVersion[]>(`${base}/available${departmentId ? `?departmentId=${departmentId}` : ''}`, { signal }),
  create: (input: TemplateInput) => apiRequest<ConsentTemplateDetail>(base, { method: 'POST', body: input }),
  update: (id: string, input: TemplateInput) => apiRequest<ConsentTemplateDetail>(`${base}/${id}`, { method: 'PUT', body: input }),
  createDraft: (id: string) => apiRequest<ConsentTemplateVersion>(`${base}/${id}/versions`, { method: 'POST' }),
  updateDraft: (id: string, versionNo: number, definition: unknown) =>
    apiRequest<ConsentTemplateVersion>(`${base}/${id}/versions/${versionNo}`, { method: 'PUT', body: { definition } }),
  publish: (id: string, versionNo: number) =>
    apiRequest<ConsentTemplateDetail>(`${base}/${id}/versions/${versionNo}/publish`, { method: 'POST' }),
  retire: (id: string, versionNo: number) =>
    apiRequest<ConsentTemplateDetail>(`${base}/${id}/versions/${versionNo}/retire`, { method: 'POST' }),
}

export const templateKeys = {
  all: ['consent-templates'] as const,
  list: (includeInactive: boolean) => ['consent-templates', 'list', includeInactive] as const,
  detail: (id: string) => ['consent-templates', 'detail', id] as const,
  version: (id: string, versionNo: number) => ['consent-templates', 'version', id, versionNo] as const,
  available: (departmentId: string | null) => ['consent-templates', 'available', departmentId] as const,
}

export function useConsentTemplates(includeInactive: boolean, enabled = true) {
  return useQuery({
    queryKey: templateKeys.list(includeInactive),
    queryFn: ({ signal }) => consentTemplatesApi.list(includeInactive, signal),
    enabled,
  })
}

export function useConsentTemplate(id: string) {
  return useQuery({ queryKey: templateKeys.detail(id), queryFn: ({ signal }) => consentTemplatesApi.get(id, signal) })
}

export function useTemplateVersion(id: string, versionNo: number) {
  return useQuery({
    queryKey: templateKeys.version(id, versionNo),
    queryFn: ({ signal }) => consentTemplatesApi.getVersion(id, versionNo, signal),
    enabled: versionNo > 0,
  })
}

export function useAvailableTemplates(departmentId: string | null) {
  return useQuery({
    queryKey: templateKeys.available(departmentId),
    queryFn: ({ signal }) => consentTemplatesApi.available(departmentId, signal),
  })
}

export const versionStatusLabels: Record<TemplateVersionStatus, string> = {
  Draft: 'Nháp',
  Published: 'Đang phát hành',
  Retired: 'Ngừng dùng',
}
