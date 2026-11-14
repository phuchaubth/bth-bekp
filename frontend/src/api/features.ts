import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './http'

/** Tích hợp đang bật trên máy chủ. Hiện chưa kết nối HIS và chưa tích hợp ký số SmartCA. */
export interface Features {
  his: boolean
  smartCa: boolean
}

// Trong lúc chờ (hoặc lỗi) coi như chưa kết nối: không hiển thị thông tin HIS.
const notConnected: Features = { his: false, smartCa: false }

export const featuresApi = {
  get: (signal?: AbortSignal) => apiRequest<Features>('/api/v1/features', { signal }),
}

export function useFeatures(): Features {
  const { data } = useQuery({
    queryKey: ['features'],
    queryFn: ({ signal }) => featuresApi.get(signal),
    // Cấu hình máy chủ chỉ đổi khi triển khai lại.
    staleTime: Infinity,
  })
  return data ?? notConnected
}
