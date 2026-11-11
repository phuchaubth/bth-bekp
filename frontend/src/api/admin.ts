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
}

export interface UserListItem {
  id: string
  userName: string
  fullName: string
  title: string | null
  departmentId: string | null
  departmentName: string | null
  roleNames: string[]
  isActive: boolean
  isLockedOut: boolean
  lastLoginAtUtc: string | null
}

export interface UserDetail {
  id: string
  userName: string
  fullName: string
  email: string | null
  phoneNumber: string | null
  title: string | null
  departmentId: string | null
  departmentName: string | null
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
  departmentId?: string | null
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

  listRoles: (signal?: AbortSignal) => apiRequest<Role[]>('/api/v1/roles', { signal }),
  listPermissions: (signal?: AbortSignal) => apiRequest<PermissionDefinition[]>('/api/v1/permissions', { signal }),
  updateRolePermissions: (id: string, permissions: string[]) =>
    apiRequest<Role>(`/api/v1/roles/${id}/permissions`, { method: 'PUT', body: { permissions } }),

  searchUsers: (query: UserQuery, signal?: AbortSignal) =>
    apiRequest<PagedResult<UserListItem>>(`/api/v1/users${toQueryString({ ...query })}`, { signal }),
  getUser: (id: string, signal?: AbortSignal) => apiRequest<UserDetail>(`/api/v1/users/${id}`, { signal }),
  createUser: (input: CreateUserInput) => apiRequest<UserDetail>('/api/v1/users', { method: 'POST', body: input }),
  updateUser: (id: string, input: UserProfileInput) =>
    apiRequest<UserDetail>(`/api/v1/users/${id}`, { method: 'PUT', body: input }),
  lockUser: (id: string) => apiRequest<void>(`/api/v1/users/${id}/lock`, { method: 'POST' }),
  unlockUser: (id: string) => apiRequest<void>(`/api/v1/users/${id}/unlock`, { method: 'POST' }),
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
