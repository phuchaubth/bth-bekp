import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { PagedResult } from './admin'
import { apiRequest } from './http'

export type Gender = 'Male' | 'Female'
export type Relationship = 'Father' | 'Mother' | 'Spouse' | 'Child' | 'Sibling' | 'Guardian' | 'Relative' | 'Other'
export type HisStatus = 'Found' | 'NotFound' | 'Unavailable'

export interface PatientListItem {
  id: string
  patientCode: string | null
  fullName: string
  dateOfBirth: string
  gender: Gender
  citizenIdMasked: string | null
  hisVerified: boolean
  updatedAtUtc: string
}

export interface Representative {
  id: string
  fullName: string
  relationship: Relationship
  citizenId: string | null
  dateOfBirth: string | null
  gender: Gender | null
  phoneNumber: string | null
  address: string | null
}

export interface PatientDetail {
  id: string
  patientCode: string | null
  fullName: string
  dateOfBirth: string
  gender: Gender
  citizenId: string | null
  oldIdNumber: string | null
  citizenIdIssueDate: string | null
  permanentAddress: string | null
  currentAddress: string | null
  phoneNumber: string | null
  ethnicity: string | null
  occupation: string | null
  nationality: string | null
  healthInsuranceNumber: string | null
  hisVerified: boolean
  hisVerifiedAtUtc: string | null
  representatives: Representative[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface PatientInput {
  fullName: string
  dateOfBirth: string
  gender: Gender
  citizenId?: string | null
  oldIdNumber?: string | null
  citizenIdIssueDate?: string | null
  permanentAddress?: string | null
  currentAddress?: string | null
  phoneNumber?: string | null
  ethnicity?: string | null
  occupation?: string | null
  nationality?: string | null
  healthInsuranceNumber?: string | null
  patientCode?: string | null
}

export interface RepresentativeInput {
  fullName: string
  relationship: Relationship
  citizenId?: string | null
  dateOfBirth?: string | null
  gender?: Gender | null
  phoneNumber?: string | null
  address?: string | null
}

export interface HisPatient {
  patientCode: string
  fullName: string | null
  dateOfBirth: string | null
  coverageType: string
  healthInsuranceNumber: string | null
  healthInsuranceValidTo: string | null
  currentEncounter: {
    encounterCode: string
    admittedAtUtc: string | null
    departmentName: string | null
    clinicName: string | null
  } | null
}

export interface PatientLookupResult {
  existingPatient: PatientDetail | null
  hisStatus: HisStatus
  hisPatient: HisPatient | null
}

export interface PatientQuery {
  keyword?: string
  page: number
  pageSize: number
}

export const patientsApi = {
  search: (query: PatientQuery, signal?: AbortSignal) => {
    const params = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
    if (query.keyword) {
      params.set('keyword', query.keyword)
    }
    return apiRequest<PagedResult<PatientListItem>>(`/api/v1/patients?${params}`, { signal })
  },
  get: (id: string, signal?: AbortSignal) => apiRequest<PatientDetail>(`/api/v1/patients/${id}`, { signal }),
  // Số CCCD gửi trong body (không đưa vào URL để tránh lộ qua log/lịch sử trình duyệt).
  lookup: (citizenId: string) =>
    apiRequest<PatientLookupResult>('/api/v1/patients/lookup', { method: 'POST', body: { citizenId } }),
  create: (input: PatientInput) => apiRequest<PatientDetail>('/api/v1/patients', { method: 'POST', body: input }),
  update: (id: string, input: PatientInput) =>
    apiRequest<PatientDetail>(`/api/v1/patients/${id}`, { method: 'PUT', body: input }),
  addRepresentative: (id: string, input: RepresentativeInput) =>
    apiRequest<PatientDetail>(`/api/v1/patients/${id}/representatives`, { method: 'POST', body: input }),
  updateRepresentative: (id: string, representativeId: string, input: RepresentativeInput) =>
    apiRequest<PatientDetail>(`/api/v1/patients/${id}/representatives/${representativeId}`, { method: 'PUT', body: input }),
  removeRepresentative: (id: string, representativeId: string) =>
    apiRequest<PatientDetail>(`/api/v1/patients/${id}/representatives/${representativeId}`, { method: 'DELETE' }),
}

export const patientKeys = {
  all: ['patients'] as const,
  list: (query: PatientQuery) => ['patients', 'list', query] as const,
  detail: (id: string) => ['patients', 'detail', id] as const,
}

export function usePatients(query: PatientQuery) {
  return useQuery({
    queryKey: patientKeys.list(query),
    queryFn: ({ signal }) => patientsApi.search(query, signal),
    placeholderData: keepPreviousData,
  })
}

export function usePatient(id: string) {
  return useQuery({ queryKey: patientKeys.detail(id), queryFn: ({ signal }) => patientsApi.get(id, signal) })
}
