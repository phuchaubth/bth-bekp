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
  ReportsRead: 'reports.read',
  ExportsRun: 'exports.run',
  ExportsSensitive: 'exports.sensitive',
  ScopeAllDepartments: 'scope.all_departments',
} as const

export type Permission = (typeof Permissions)[keyof typeof Permissions]
