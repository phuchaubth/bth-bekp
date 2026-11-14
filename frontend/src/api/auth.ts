import { apiRequest } from './http'

export interface RoleRef {
  id: string
  code: string
  name: string
}

export interface CurrentUser {
  id: string
  userName: string
  fullName: string
  title: string | null
  departmentId: string | null
  departmentName: string | null
  roles: RoleRef[]
  permissions: string[]
  mustChangePassword: boolean
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
