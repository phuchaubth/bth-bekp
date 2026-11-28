import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './http'

/**
 * Phiên bản một bản đã đóng gói (quyết định 67): số gói (số lần chạy quy trình "Gói triển khai IIS" trên GitHub Actions), mã
 * commit, thời điểm đóng gói. `null`: không rõ (bản phát triển, bản cài từ gói chưa ghi phiên bản).
 */
export interface AppVersion {
  package: number | null
  commit: string | null
  builtAtUtc: string | null
}

export const versionKeys = {
  server: ['version', 'server'] as const,
}

export const versionApi = {
  server: (signal?: AbortSignal) => apiRequest<AppVersion>('/api/v1/version', { signal }),
}

/** Phiên bản API đang chạy (cần quyền bảo trì hệ thống). */
export function useServerVersion() {
  return useQuery({
    queryKey: versionKeys.server,
    queryFn: ({ signal }) => versionApi.server(signal),
  })
}
