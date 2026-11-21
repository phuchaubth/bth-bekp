import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiBlob, apiRequest } from './http'

/** Kết quả kiểm thử (quyết định 49). */
export type TestResult = 'Passed' | 'PartiallyPassed' | 'Failed'

/** Trạng thái phân công so với hôm nay. */
export type TestAssignmentStatus = 'Upcoming' | 'InProgress' | 'Overdue' | 'Reported' | 'Cancelled'

export const TEST_RESULT_LABELS: Record<TestResult, string> = {
  Passed: 'Đạt',
  PartiallyPassed: 'Đạt một phần',
  Failed: 'Không đạt',
}

export const TEST_STATUS_LABELS: Record<TestAssignmentStatus, string> = {
  Upcoming: 'Chưa tới ngày',
  InProgress: 'Đang kiểm thử',
  Overdue: 'Quá hạn',
  Reported: 'Đã báo cáo',
  Cancelled: 'Đã hủy',
}

/** Một phân công kiểm thử tính năng mới cho một người, có thời gian (quyết định 49). */
export interface TestAssignment {
  id: string
  /** Đợt kiểm thử: bản cài hoặc gói. */
  version: string
  feature: string
  instructions: string | null
  assigneeUserId: string
  assigneeName: string
  assigneeDepartment: string | null
  startDate: string
  dueDate: string
  status: TestAssignmentStatus
  result: TestResult | null
  findings: string | null
  suggestions: string | null
  reportedAtUtc: string | null
  /** Báo cáo gửi sau hạn. */
  late: boolean
  reviewNote: string | null
  reviewedBy: string | null
  reviewedAtUtc: string | null
  createdBy: string
  createdAtUtc: string
  cancelledBy: string | null
  cancelledAtUtc: string | null
  /** Người xem là người được phân công và gửi (sửa) được báo cáo. */
  canReport: boolean
  /** Người quản lý kiểm thử còn sửa, hủy được (chưa báo cáo). */
  canEdit: boolean
}

export interface TestAssignmentList {
  items: TestAssignment[]
  versions: string[]
}

export interface TestCandidate {
  userId: string
  fullName: string
  title: string | null
  departmentName: string | null
}

export interface CreateTestAssignmentsInput {
  version: string
  feature: string
  instructions: string | null
  assignees: { userId: string; startDate: string; dueDate: string }[]
}

export interface UpdateTestAssignmentInput {
  version: string
  feature: string
  instructions: string | null
  startDate: string
  dueDate: string
}

export interface SubmitTestReportInput {
  result: TestResult
  findings: string | null
  suggestions: string | null
}

const base = '/api/v1/testing/assignments'

export const testingApi = {
  list: (version: string | undefined, signal?: AbortSignal) =>
    apiRequest<TestAssignmentList>(`${base}${version ? `?version=${encodeURIComponent(version)}` : ''}`, { signal }),
  mine: (signal?: AbortSignal) => apiRequest<TestAssignment[]>(`${base}/mine`, { signal }),
  candidates: (signal?: AbortSignal) => apiRequest<TestCandidate[]>(`${base}/candidates`, { signal }),
  create: (input: CreateTestAssignmentsInput) => apiRequest<TestAssignment[]>(base, { method: 'POST', body: input }),
  update: (id: string, input: UpdateTestAssignmentInput) => apiRequest<TestAssignment>(`${base}/${id}`, { method: 'PUT', body: input }),
  cancel: (id: string) => apiRequest<TestAssignment>(`${base}/${id}/cancel`, { method: 'POST' }),
  report: (id: string, input: SubmitTestReportInput) => apiRequest<TestAssignment>(`${base}/${id}/report`, { method: 'PUT', body: input }),
  review: (id: string, note: string) => apiRequest<TestAssignment>(`${base}/${id}/review`, { method: 'POST', body: { note } }),
  workbook: (version: string | undefined) => apiBlob(`${base}/xlsx${version ? `?version=${encodeURIComponent(version)}` : ''}`),
}

export const testingKeys = {
  all: ['testing'] as const,
  list: (version: string | undefined) => ['testing', 'list', version ?? ''] as const,
  mine: ['testing', 'mine'] as const,
  candidates: ['testing', 'candidates'] as const,
}

export function useTestAssignments(version: string | undefined, enabled = true) {
  return useQuery({
    queryKey: testingKeys.list(version),
    queryFn: ({ signal }) => testingApi.list(version, signal),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function useMyTestAssignments(enabled = true) {
  return useQuery({ queryKey: testingKeys.mine, queryFn: ({ signal }) => testingApi.mine(signal), enabled })
}

export function useTestCandidates(enabled = true) {
  return useQuery({ queryKey: testingKeys.candidates, queryFn: ({ signal }) => testingApi.candidates(signal), enabled })
}
