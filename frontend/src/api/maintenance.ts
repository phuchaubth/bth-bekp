import { useQuery } from '@tanstack/react-query'
import { apiRequest, getJson } from './http'

/** Trạng thái bảo trì (quyết định 53) cho mọi người, không cần đăng nhập. */
export interface MaintenanceStatus {
  enabled: boolean
  /** Lý do hiện cho người dùng. */
  reason: string | null
  /** Giờ dự kiến xong, chỉ để hiển thị (hết giờ không tự tắt). */
  expectedEndAtUtc: string | null
}

/** Trạng thái kèm thời điểm bật và người bật (trang Bảo trì hệ thống). */
export interface MaintenanceSettings extends MaintenanceStatus {
  startedAtUtc: string | null
  startedBy: string | null
}

export interface MaintenanceInput {
  enabled: boolean
  reason?: string | null
  expectedEndAtUtc?: string | null
}

/** Giới hạn của máy chủ (MaintenanceService). */
export const MAINTENANCE_REASON_MAX_LENGTH = 200
export const MAINTENANCE_MAX_DAYS = 30

export const maintenanceKeys = {
  status: ['maintenance', 'status'] as const,
  settings: ['maintenance', 'settings'] as const,
}

export const maintenanceApi = {
  status: (signal?: AbortSignal) => getJson<MaintenanceStatus>('/api/v1/maintenance', signal),
  settings: (signal?: AbortSignal) => apiRequest<MaintenanceSettings>('/api/v1/maintenance/settings', { signal }),
  update: (input: MaintenanceInput) =>
    apiRequest<MaintenanceSettings>('/api/v1/maintenance/settings', { method: 'PUT', body: input }),
}

/**
 * Hỏi trạng thái bảo trì (không cần đăng nhập, vẫn trả lời khi đang bảo trì). `enabled = false`: chỉ đọc kết quả đã có (không
 * tự gửi yêu cầu).
 */
export function useMaintenanceStatus(enabled: boolean, refetchInterval: number | false = false) {
  return useQuery({
    queryKey: maintenanceKeys.status,
    queryFn: ({ signal }) => maintenanceApi.status(signal),
    enabled,
    refetchInterval,
    retry: false,
    staleTime: 0,
  })
}

export function useMaintenanceSettings() {
  return useQuery({
    queryKey: maintenanceKeys.settings,
    queryFn: ({ signal }) => maintenanceApi.settings(signal),
  })
}
