import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './http'

/** Tích hợp đang bật trên máy chủ. Hiện chưa kết nối HIS và chưa tích hợp ký số SmartCA. */
export interface Features {
  his: boolean
  smartCa: boolean
  /** Đã cấu hình API thông tin người bệnh: lấy thông tin hành chính theo mã người bệnh hoặc CCCD. */
  patientSource: boolean
  /** Bắt buộc gắn phiếu với lượt tiếp nhận trên HIS khi ký hoàn tất (quyết định 25). */
  encounters: boolean
}

// Trong lúc chờ (hoặc lỗi) coi như chưa kết nối: không hiển thị thông tin HIS. Máy chủ vẫn tự kiểm tra lượt tiếp nhận khi
// ký hoàn tất, nên nếu cờ chưa tải xong mà bệnh viện đã bật thì người dùng nhận lỗi rõ ràng chứ phiếu không thiếu lượt.
const notConnected: Features = { his: false, smartCa: false, patientSource: false, encounters: false }

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
