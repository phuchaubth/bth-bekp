import { useQuery } from '@tanstack/react-query'
import { getJson } from './http'

export interface HealthResponse {
  status: 'Healthy' | 'Degraded' | 'Unhealthy'
  checks: Record<string, string>
}

export function useHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => getJson<HealthResponse>('/health', signal),
    refetchInterval: 60_000,
  })
}
