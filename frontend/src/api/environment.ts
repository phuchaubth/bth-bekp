import { useQuery } from '@tanstack/react-query'
import { getJson } from './http'

/** Nhãn môi trường (quyết định 50): môi trường kiểm thử (UAT) có nhãn; môi trường chạy thật không có. */
export interface EnvironmentInfo {
  label: string | null
}

export const environmentKeys = { info: ['environment'] as const }

/** Nhãn môi trường đang chạy, `null` ở môi trường chạy thật hoặc khi chưa tải được (không cần đăng nhập). */
export function useEnvironmentLabel(): string | null {
  const query = useQuery({
    queryKey: environmentKeys.info,
    queryFn: ({ signal }) => getJson<EnvironmentInfo>('/api/v1/environment', signal),
    staleTime: Infinity,
    gcTime: Infinity,
    retry: false,
  })
  return query.data?.label ?? null
}
