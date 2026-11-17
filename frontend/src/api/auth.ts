import { apiRequest } from './http'

export interface RoleRef {
  id: string
  code: string
  name: string
}

export interface DepartmentRef {
  id: string
  name: string
}

export interface CurrentUser {
  id: string
  userName: string
  fullName: string
  title: string | null
  /** Khoa/phòng chính: khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng. */
  departmentId: string | null
  departmentName: string | null
  /** Mọi khoa/phòng của tài khoản (quyết định 41), khoa chính đứng đầu: tài khoản thấy dữ liệu của các khoa này. */
  departments: DepartmentRef[]
  roles: RoleRef[]
  permissions: string[]
  mustChangePassword: boolean
}

/** Các khoa của tài khoản, khoa chính đứng đầu; rỗng khi tài khoản không thuộc khoa nào. */
export function accountDepartments(user: CurrentUser | null | undefined): DepartmentRef[] {
  if (!user) return []
  if (user.departments?.length) return user.departments
  return user.departmentId ? [{ id: user.departmentId, name: user.departmentName ?? '' }] : []
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAtUtc: string
  user: CurrentUser
}

export const authApi = {
  login: (userName: string, password: string) =>
    apiRequest<AuthResponse>('/api/v1/auth/login', { method: 'POST', body: { userName, password }, auth: false }),
  refresh: () => apiRequest<AuthResponse>('/api/v1/auth/refresh', { method: 'POST', auth: false }),
  logout: () => apiRequest<void>('/api/v1/auth/logout', { method: 'POST', auth: false }),
  changePassword: (currentPassword: string, newPassword: string) =>
    apiRequest<AuthResponse>('/api/v1/auth/change-password', {
      method: 'POST',
      body: { currentPassword, newPassword },
    }),
}
