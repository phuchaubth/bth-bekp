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
export const UsersPage = lazy(() => import('../features/admin/users/UsersPage').then((m) => ({ default: m.UsersPage })))

export function Lazy({ children }: { children: ReactNode }) {
  return <Suspense fallback={<Spin style={{ display: 'block', padding: 48 }} />}>{children}</Suspense>
}
