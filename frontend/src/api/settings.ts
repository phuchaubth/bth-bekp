import { useQuery } from '@tanstack/react-query'
import { apiBlob, apiRequest } from './http'

export interface HospitalSettings {
  parentAgency: string
  name: string
  place: string
  updatedAtUtc: string | null
}

export interface HospitalSettingsInput {
  parentAgency: string
  name: string
  place: string
}

/** Logo bệnh viện đang dùng (in ở góc trên bên trái phiếu). */
export interface LogoInfo {
  contentType: 'image/png' | 'image/jpeg'
  width: number
  height: number
  updatedAtUtc: string
}

/** Giới hạn của máy chủ (LogoImage). */
export const LOGO_MAX_BYTES = 1024 * 1024

/** Cấu hình hệ thống đang có hiệu lực (màn hình 19). Không chứa bí mật. */
export interface SystemSettings {
  hospital: HospitalSettings
  logo: LogoInfo | null
  hisEnabled: boolean
  smartCaEnabled: boolean
  exportMaxRows: number
  retentionPolicy: string
  /** Ảnh khung chữ ký (nền thiết kế của bệnh viện quanh khung ký trên màn hình); chưa có thì null. */
  signatureFrame: LogoInfo | null
}

export const settingsApi = {
  get: (signal?: AbortSignal) => apiRequest<SystemSettings>('/api/v1/settings', { signal }),
  updateHospital: (input: HospitalSettingsInput) =>
    apiRequest<SystemSettings>('/api/v1/settings/hospital', { method: 'PUT', body: input }),
  /** Ảnh PNG/JPG dạng data URL hoặc Base64. */
  uploadLogo: (image: string) => apiRequest<SystemSettings>('/api/v1/settings/logo', { method: 'PUT', body: { image } }),
  deleteLogo: () => apiRequest<SystemSettings>('/api/v1/settings/logo', { method: 'DELETE' }),
  logo: (signal?: AbortSignal) => apiBlob('/api/v1/settings/logo', signal),
  /** Ảnh khung chữ ký PNG/JPG dạng data URL hoặc Base64. */
  uploadSignatureFrame: (image: string) =>
    apiRequest<SystemSettings>('/api/v1/settings/signature-frame', { method: 'PUT', body: { image } }),
  deleteSignatureFrame: () => apiRequest<SystemSettings>('/api/v1/settings/signature-frame', { method: 'DELETE' }),
}

export const settingsKeys = {
  all: ['settings'] as const,
  encounters: ['settings', 'encounters'] as const,
  formInheritance: ['settings', 'form-inheritance'] as const,
}

/** Kế thừa phiếu lần trước (quyết định 27): số ngày được kế thừa, 0 là tắt. */
export interface FormInheritanceSettings {
  days: number
  updatedAtUtc: string | null
}

/** Giới hạn của máy chủ (FormInheritanceService.MaxDays). */
export const FORM_INHERITANCE_MAX_DAYS = 365

export const formInheritanceApi = {
  get: (signal?: AbortSignal) => apiRequest<FormInheritanceSettings>('/api/v1/settings/form-inheritance', { signal }),
  update: (days: number) =>
    apiRequest<FormInheritanceSettings>('/api/v1/settings/form-inheritance', { method: 'PUT', body: { days } }),
}

export function useFormInheritanceSettings() {
  return useQuery({ queryKey: settingsKeys.formInheritance, queryFn: ({ signal }) => formInheritanceApi.get(signal) })
}

export function useSystemSettings() {
  return useQuery({ queryKey: settingsKeys.all, queryFn: ({ signal }) => settingsApi.get(signal) })
}

/** Gắn phiếu với lượt tiếp nhận HIS khi ký hoàn tất (quyết định 25), bật/tắt ở Cấu hình hệ thống. */
export interface EncounterSettings {
  /** Đang bắt buộc chọn lượt tiếp nhận khi ký hoàn tất. */
  enabled: boolean
  /** Đã cấu hình API thông tin người bệnh nên bật được. */
  available: boolean
  updatedAtUtc: string | null
}

export const encounterSettingsApi = {
  get: (signal?: AbortSignal) => apiRequest<EncounterSettings>('/api/v1/settings/encounters', { signal }),
  update: (enabled: boolean) =>
    apiRequest<EncounterSettings>('/api/v1/settings/encounters', { method: 'PUT', body: { enabled } }),
}

export function useEncounterSettings() {
  return useQuery({ queryKey: settingsKeys.encounters, queryFn: ({ signal }) => encounterSettingsApi.get(signal) })
}
