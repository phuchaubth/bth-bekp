import type { Department, PermissionDefinition, Role, UserListItem } from '../api/admin'
import type { AuthResponse, CurrentUser } from '../api/auth'
import { Permissions } from '../auth/permissions'
import { noContent, problem } from './mockApi'

/** Dữ liệu giả lập dùng trong test giao diện (không phải dữ liệu thật). */
export const allPermissions = Object.values(Permissions)

export const doctorPermissions = [
  Permissions.PatientsRead,
  Permissions.PatientsManage,
  Permissions.TemplatesRead,
  Permissions.ConsentsRead,
  Permissions.ConsentsCreate,
  Permissions.ConsentsSign,
]

export function session(overrides: Partial<CurrentUser> = {}): AuthResponse {
  return {
    accessToken: `token-${Math.random().toString(36).slice(2)}`,
    accessTokenExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
    user: {
      id: 'u-bacsi',
      userName: 'bacsi.demo',
      fullName: 'Bác sĩ Demo',
      title: 'Bác sĩ',
      departmentId: 'd-noi',
      departmentName: 'Khoa Nội tổng hợp',
      roles: [{ id: 'r-doctor', code: 'DOCTOR', name: 'Bác sĩ' }],
      permissions: doctorPermissions,
      mustChangePassword: false,
      ...overrides,
    },
  }
}

export const adminSession = () =>
  session({
    id: 'u-admin',
    userName: 'admin',
    fullName: 'Quản trị Demo',
    title: null,
    departmentId: null,
    departmentName: null,
    roles: [{ id: 'r-admin', code: 'ADMIN', name: 'Quản trị hệ thống' }],
    permissions: allPermissions,
  })

export const anonymous = () => problem(401, 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.', { code: 'auth.refresh_invalid' })

export const healthy = { status: 'Healthy', checks: {} }

/** Các route luôn cần khi ứng dụng khởi động. */
export const baseRoutes = (refresh: object | (() => Response)) => ({
  'POST /api/v1/auth/refresh': refresh,
  'POST /api/v1/auth/logout': () => noContent(),
  'GET /health': healthy,
})

export const departments: Department[] = [
  { id: 'd-kkb', code: 'KKB', name: 'Khoa Khám bệnh', isActive: true },
  { id: 'd-noi', code: 'NOI', name: 'Khoa Nội tổng hợp', isActive: true },
  { id: 'd-cu', code: 'CU', name: 'Khoa Cũ', isActive: false },
]

export const permissionCatalog: PermissionDefinition[] = [
  { code: Permissions.UsersRead, name: 'Xem người dùng', group: 'Hệ thống' },
  { code: Permissions.UsersManage, name: 'Quản lý người dùng', group: 'Hệ thống' },
  { code: Permissions.ConsentsRead, name: 'Xem phiếu', group: 'Phiếu đồng thuận' },
  { code: Permissions.ConsentsCreate, name: 'Lập phiếu', group: 'Phiếu đồng thuận' },
]

export const roles: Role[] = [
  {
    id: 'r-admin',
    code: 'ADMIN',
    name: 'Quản trị hệ thống',
    description: 'Toàn quyền',
    isSystem: true,
    isAdministrator: true,
    permissions: permissionCatalog.map((p) => p.code),
  },
  {
    id: 'r-doctor',
    code: 'DOCTOR',
    name: 'Bác sĩ',
    description: 'Lập và ký phiếu',
    isSystem: true,
    isAdministrator: false,
    permissions: [Permissions.ConsentsRead, Permissions.ConsentsCreate],
  },
]

export const users: UserListItem[] = [
  {
    id: 'u-bacsi',
    userName: 'bacsi.demo',
    fullName: 'Bác sĩ Demo',
    title: 'Bác sĩ',
    departmentId: 'd-noi',
    departmentName: 'Khoa Nội tổng hợp',
    roleNames: ['Bác sĩ'],
    isActive: true,
    isLockedOut: false,
    lastLoginAtUtc: '2026-11-10T01:30:00Z',
  },
  {
    id: 'u-tiepdon',
    userName: 'tiepdon.demo',
    fullName: 'Tiếp đón Demo',
    title: null,
    departmentId: 'd-kkb',
    departmentName: 'Khoa Khám bệnh',
    roleNames: ['Tiếp đón'],
    isActive: false,
    isLockedOut: false,
    lastLoginAtUtc: null,
  },
]
