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
}

export const settingsApi = {
  get: (signal?: AbortSignal) => apiRequest<SystemSettings>('/api/v1/settings', { signal }),
  updateHospital: (input: HospitalSettingsInput) =>
    apiRequest<SystemSettings>('/api/v1/settings/hospital', { method: 'PUT', body: input }),
  /** Ảnh PNG/JPG dạng data URL hoặc Base64. */
  uploadLogo: (image: string) => apiRequest<SystemSettings>('/api/v1/settings/logo', { method: 'PUT', body: { image } }),
  deleteLogo: () => apiRequest<SystemSettings>('/api/v1/settings/logo', { method: 'DELETE' }),
  logo: (signal?: AbortSignal) => apiBlob('/api/v1/settings/logo', signal),
}

export const settingsKeys = { all: ['settings'] as const }

export function useSystemSettings() {
  return useQuery({ queryKey: settingsKeys.all, queryFn: ({ signal }) => settingsApi.get(signal) })
}
