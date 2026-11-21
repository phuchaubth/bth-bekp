import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiRequest } from './http'

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface Department {
  id: string
  code: string
  name: string
  isActive: boolean
}

export interface DepartmentInput {
  code: string
  name: string
  isActive: boolean
}

/** Kết quả lấy danh mục khoa/phòng từ HIS (quyết định 42). */
export interface DepartmentSyncResult {
  /** Số khoa/phòng HIS trả về (sau khi bỏ dòng lỗi, mã lặp). */
  total: number
  added: number
  /** Đổi tên, nhận mã HIS hoặc dùng lại khoa đã ngừng sử dụng. */
  updated: number
  unchanged: number
  /** Tên khoa/phòng chuyển sang ngừng sử dụng vì không còn trên HIS. */
  deactivated: string[]
  /** Mã các dòng bị bỏ qua (mã rỗng hoặc dài quá 20 ký tự). */
  skipped: string[]
}

export interface PermissionDefinition {
  code: string
  name: string
  group: string
}

export interface Role {
  id: string
  code: string
  name: string
  description: string | null
  isSystem: boolean
  isAdministrator: boolean
  permissions: string[]
  /** Quyền cố định, không sửa được trên ma trận (Quản trị hệ thống, Kiosk). */
  isLocked?: boolean
}

/** Tên, mô tả của vai trò (quyết định 40); mã vai trò do máy chủ sinh và không đổi. */
export interface RoleInput {
  name: string
  description: string | null
}

export interface UserListItem {
  id: string
  userName: string
  fullName: string
  title: string | null
  /** Khoa/phòng chính. */
  departmentId: string | null
  departmentName: string | null
  /** Tên các khoa/phòng khác (quyết định 41). */
  additionalDepartmentNames?: string[] | null
  roleNames: string[]
  isActive: boolean
  isLockedOut: boolean
  lastLoginAtUtc: string | null
  /** Tài khoản kiosk đang tạm khóa tra cứu (người bệnh nhập sai ngày sinh nhiều lần); Mở khóa để tra cứu lại ngay. */
  isKioskLookupLocked?: boolean
}

export interface UserDetail {
  id: string
  userName: string
  fullName: string
  email: string | null
  phoneNumber: string | null
  title: string | null
  /** Khoa/phòng chính. */
  departmentId: string | null
  departmentName: string | null
  /** Khoa/phòng khác (quyết định 41). */
  additionalDepartments: { id: string; name: string }[]
  smartCaUserId: string | null
  roles: { id: string; code: string; name: string }[]
  isActive: boolean
  isLockedOut: boolean
  lockoutEndUtc: string | null
  mustChangePassword: boolean
  lastLoginAtUtc: string | null
  createdAtUtc: string
}

export interface UserQuery {
  keyword?: string
  departmentId?: string
  isActive?: boolean
  page: number
  pageSize: number
}

export interface UserProfileInput {
  fullName: string
  email?: string | null
  phoneNumber?: string | null
  title?: string | null
  /** Khoa/phòng chính. */
  departmentId?: string | null
  /** Khoa/phòng khác (quyết định 41); gửi danh sách đầy đủ, rỗng là không có. */
  additionalDepartmentIds?: string[]
  smartCaUserId?: string | null
  roleIds: string[]
}

export interface CreateUserInput extends UserProfileInput {
  userName: string
  password: string
}

function toQueryString(params: Record<string, string | number | boolean | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value))
    }
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const adminApi = {
  listDepartments: (includeInactive: boolean, signal?: AbortSignal) =>
    apiRequest<Department[]>(`/api/v1/departments${toQueryString({ includeInactive })}`, { signal }),
  createDepartment: (input: DepartmentInput) =>
    apiRequest<Department>('/api/v1/departments', { method: 'POST', body: input }),
  updateDepartment: (id: string, input: DepartmentInput) =>
    apiRequest<Department>(`/api/v1/departments/${id}`, { method: 'PUT', body: input }),
  deleteDepartment: (id: string) => apiRequest<void>(`/api/v1/departments/${id}`, { method: 'DELETE' }),
  syncDepartmentsFromHis: () => apiRequest<DepartmentSyncResult>('/api/v1/departments/sync-his', { method: 'POST' }),

  listRoles: (signal?: AbortSignal) => apiRequest<Role[]>('/api/v1/roles', { signal }),
  listPermissions: (signal?: AbortSignal) => apiRequest<PermissionDefinition[]>('/api/v1/permissions', { signal }),
  updateRolePermissions: (id: string, permissions: string[]) =>
    apiRequest<Role>(`/api/v1/roles/${id}/permissions`, { method: 'PUT', body: { permissions } }),
  createRole: (input: RoleInput & { permissions: string[] }) =>
    apiRequest<Role>('/api/v1/roles', { method: 'POST', body: input }),
  updateRole: (id: string, input: RoleInput) => apiRequest<Role>(`/api/v1/roles/${id}`, { method: 'PUT', body: input }),
  deleteRole: (id: string) => apiRequest<void>(`/api/v1/roles/${id}`, { method: 'DELETE' }),

  searchUsers: (query: UserQuery, signal?: AbortSignal) =>
    apiRequest<PagedResult<UserListItem>>(`/api/v1/users${toQueryString({ ...query })}`, { signal }),
  getUser: (id: string, signal?: AbortSignal) => apiRequest<UserDetail>(`/api/v1/users/${id}`, { signal }),
  createUser: (input: CreateUserInput) => apiRequest<UserDetail>('/api/v1/users', { method: 'POST', body: input }),
  updateUser: (id: string, input: UserProfileInput) =>
    apiRequest<UserDetail>(`/api/v1/users/${id}`, { method: 'PUT', body: input }),
  lockUser: (id: string) => apiRequest<void>(`/api/v1/users/${id}/lock`, { method: 'POST' }),
  unlockUser: (id: string) => apiRequest<void>(`/api/v1/users/${id}/unlock`, { method: 'POST' }),
  /** Xóa tài khoản đã ngưng sử dụng (quyết định 51). */
  deleteUser: (id: string) => apiRequest<void>(`/api/v1/users/${id}`, { method: 'DELETE' }),
  resetPassword: (id: string, newPassword: string) =>
    apiRequest<void>(`/api/v1/users/${id}/reset-password`, { method: 'POST', body: { newPassword } }),
}

export const adminKeys = {
  departments: (includeInactive: boolean) => ['departments', { includeInactive }] as const,
  allDepartments: ['departments'] as const,
  roles: ['roles'] as const,
  permissions: ['permissions'] as const,
  users: (query: UserQuery) => ['users', query] as const,
  allUsers: ['users'] as const,
  user: (id: string) => ['users', 'detail', id] as const,
}

export function useDepartments(includeInactive: boolean) {
  return useQuery({
    queryKey: adminKeys.departments(includeInactive),
    queryFn: ({ signal }) => adminApi.listDepartments(includeInactive, signal),
  })
}

export function useRoles() {
  return useQuery({ queryKey: adminKeys.roles, queryFn: ({ signal }) => adminApi.listRoles(signal) })
}

export function usePermissionCatalog() {
  return useQuery({
    queryKey: adminKeys.permissions,
    queryFn: ({ signal }) => adminApi.listPermissions(signal),
    staleTime: Infinity,
  })
}

export function useUsers(query: UserQuery) {
  return useQuery({
    queryKey: adminKeys.users(query),
    queryFn: ({ signal }) => adminApi.searchUsers(query, signal),
    placeholderData: keepPreviousData,
  })
}

export function useUser(id: string | null) {
  return useQuery({
    queryKey: adminKeys.user(id ?? ''),
    queryFn: ({ signal }) => adminApi.getUser(id!, signal),
    enabled: id !== null,
  })
}
