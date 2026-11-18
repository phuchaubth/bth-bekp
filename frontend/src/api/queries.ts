import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { apiGet } from './client';
import type { DepartmentDetail, DepartmentListItem, HospitalOverview, ReportPeriod, TimeStandardsReport } from './types';

/** Realtime pushes invalidate these keys; polling is only a safety net when the socket is down. */
export const queryKeys = {
  overview: ['overview'] as const,
  departments: ['departments'] as const,
  department: (code: string) => ['department', code] as const,
  timeStandards: (period: ReportPeriod, department?: string) => ['timeStandards', period, department ?? 'all'] as const,
};

const FALLBACK_POLL_MS = 60_000;

export function useOverview() {
  return useQuery({
    queryKey: queryKeys.overview,
    queryFn: ({ signal }) => apiGet<HospitalOverview>('/api/dashboard/overview', signal),
    refetchInterval: FALLBACK_POLL_MS,
  });
}

export function useDepartments() {
  return useQuery({
    queryKey: queryKeys.departments,
    queryFn: ({ signal }) => apiGet<DepartmentListItem[]>('/api/departments', signal),
    staleTime: 10 * 60_000,
  });
}

export function useDepartment(code: string | undefined) {
  return useQuery({
    queryKey: queryKeys.department(code ?? ''),
    queryFn: ({ signal }) => apiGet<DepartmentDetail>(`/api/dashboard/departments/${encodeURIComponent(code ?? '')}`, signal),
    enabled: Boolean(code),
    refetchInterval: FALLBACK_POLL_MS,
    placeholderData: keepPreviousData,
  });
}

export function useTimeStandards(period: ReportPeriod, department?: string) {
  const params = new URLSearchParams({ period });
  if (department) {
    params.set('department', department);
  }

  return useQuery({
    queryKey: queryKeys.timeStandards(period, department),
    queryFn: ({ signal }) => apiGet<TimeStandardsReport>(`/api/dashboard/time-standards?${params.toString()}`, signal),
    refetchInterval: FALLBACK_POLL_MS,
    placeholderData: keepPreviousData,
  });
}
