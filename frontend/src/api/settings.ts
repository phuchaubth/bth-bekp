import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './http'

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

/** Cấu hình hệ thống đang có hiệu lực (màn hình 19). Không chứa bí mật. */
export interface SystemSettings {
  hospital: HospitalSettings
  hisEnabled: boolean
  smartCaEnabled: boolean
  exportMaxRows: number
  retentionPolicy: string
}

export const settingsApi = {
  get: (signal?: AbortSignal) => apiRequest<SystemSettings>('/api/v1/settings', { signal }),
  updateHospital: (input: HospitalSettingsInput) =>
    apiRequest<SystemSettings>('/api/v1/settings/hospital', { method: 'PUT', body: input }),
}

export const settingsKeys = { all: ['settings'] as const }

export function useSystemSettings() {
  return useQuery({ queryKey: settingsKeys.all, queryFn: ({ signal }) => settingsApi.get(signal) })
}
