/** Mã quyền, khớp với backend (Consent.Domain.Authorization.Permissions). */
export const Permissions = {
  UsersRead: 'users.read',
  UsersManage: 'users.manage',
  RolesManage: 'roles.manage',
  DepartmentsManage: 'departments.manage',
  AuditRead: 'audit.read',
  SettingsManage: 'settings.manage',
  PatientsRead: 'patients.read',
  PatientsManage: 'patients.manage',
  TemplatesRead: 'templates.read',
  TemplatesManage: 'templates.manage',
  ConsentsRead: 'consents.read',
  ConsentsCreate: 'consents.create',
  ConsentsSign: 'consents.sign',
  ConsentsRevoke: 'consents.revoke',
  /** Dùng màn hình kiosk cho người bệnh tự làm phiếu (quyết định 24). */
  KioskUse: 'kiosk.use',
  ReportsRead: 'reports.read',
  ExportsRun: 'exports.run',
  ExportsSensitive: 'exports.sensitive',
  ScopeAllDepartments: 'scope.all_departments',
} as const

export type Permission = (typeof Permissions)[keyof typeof Permissions]

/** Mã vai trò Kiosk: tài khoản máy kiosk chỉ dùng màn hình kiosk. */
export const KIOSK_ROLE_CODE = 'KIOSK'
