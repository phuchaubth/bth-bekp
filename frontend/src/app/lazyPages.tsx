import { Spin } from 'antd'
import { lazy, Suspense, type ReactNode } from 'react'

// Các trang chức năng được tải khi cần (giảm dung lượng tải lần đầu).
export const AccountPage = lazy(() => import('../features/account/AccountPage').then((m) => ({ default: m.AccountPage })))
export const DepartmentsPage = lazy(() =>
  import('../features/admin/departments/DepartmentsPage').then((m) => ({ default: m.DepartmentsPage })),
)
export const RolesPage = lazy(() => import('../features/admin/roles/RolesPage').then((m) => ({ default: m.RolesPage })))
export const PatientsPage = lazy(() => import('../features/patients/PatientsPage').then((m) => ({ default: m.PatientsPage })))
export const PatientIntakePage = lazy(() =>
  import('../features/patients/PatientIntakePage').then((m) => ({ default: m.PatientIntakePage })),
)
export const PatientProfilePage = lazy(() =>
  import('../features/patients/PatientProfilePage').then((m) => ({ default: m.PatientProfilePage })),
)
export const ConsentFormsPage = lazy(() =>
  import('../features/consents/ConsentFormsPage').then((m) => ({ default: m.ConsentFormsPage })),
)
export const ConsentFormCreatePage = lazy(() =>
  import('../features/consents/ConsentFormCreatePage').then((m) => ({ default: m.ConsentFormCreatePage })),
)
export const ConsentFormDetailPage = lazy(() =>
  import('../features/consents/ConsentFormDetailPage').then((m) => ({ default: m.ConsentFormDetailPage })),
)
export const ConsentTemplatesPage = lazy(() =>
  import('../features/templates/ConsentTemplatesPage').then((m) => ({ default: m.ConsentTemplatesPage })),
)
export const ConsentTemplateDetailPage = lazy(() =>
  import('../features/templates/ConsentTemplateDetailPage').then((m) => ({ default: m.ConsentTemplateDetailPage })),
)
export const SigningKioskPage = lazy(() =>
  import('../features/signing/SigningKioskPage').then((m) => ({ default: m.SigningKioskPage })),
)
export const RemoteSigningPage = lazy(() =>
  import('../features/signing/RemoteSigningPage').then((m) => ({ default: m.RemoteSigningPage })),
)
export const KioskPage = lazy(() => import('../features/kiosk/KioskPage').then((m) => ({ default: m.KioskPage })))
export const ExportsPage = lazy(() => import('../features/exports/ExportsPage').then((m) => ({ default: m.ExportsPage })))
export const ReportsPage = lazy(() => import('../features/reports/ReportsPage').then((m) => ({ default: m.ReportsPage })))
export const NursingReportPage = lazy(() =>
  import('../features/nursing/NursingReportPage').then((m) => ({ default: m.NursingReportPage })),
)
export const NursingDashboardPage = lazy(() =>
  import('../features/nursing/NursingDashboardPage').then((m) => ({ default: m.NursingDashboardPage })),
)
export const NursingDelegationsPage = lazy(() =>
  import('../features/nursing/NursingDelegationsPage').then((m) => ({ default: m.NursingDelegationsPage })),
)
export const QcBooksPage = lazy(() => import('../features/qc/QcBooksPage').then((m) => ({ default: m.QcBooksPage })))
export const QcBookPage = lazy(() => import('../features/qc/QcBookPage').then((m) => ({ default: m.QcBookPage })))
export const QcCatalogPage = lazy(() => import('../features/qc/QcCatalogPage').then((m) => ({ default: m.QcCatalogPage })))
export const TestAssignmentsPage = lazy(() =>
  import('../features/testing/TestAssignmentsPage').then((m) => ({ default: m.TestAssignmentsPage })),
)
export const MyTestAssignmentsPage = lazy(() =>
  import('../features/testing/MyTestAssignmentsPage').then((m) => ({ default: m.MyTestAssignmentsPage })),
)
export const AuditLogsPage = lazy(() => import('../features/audit/AuditLogsPage').then((m) => ({ default: m.AuditLogsPage })))
export const SettingsPage = lazy(() => import('../features/admin/settings/SettingsPage').then((m) => ({ default: m.SettingsPage })))
export const MaintenanceSettingsPage = lazy(() =>
  import('../features/admin/maintenance/MaintenanceSettingsPage').then((m) => ({ default: m.MaintenanceSettingsPage })),
)
export const UsersPage = lazy(() => import('../features/admin/users/UsersPage').then((m) => ({ default: m.UsersPage })))

export function Lazy({ children }: { children: ReactNode }) {
  return <Suspense fallback={<Spin style={{ display: 'block', padding: 48 }} />}>{children}</Suspense>
}
