import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { RequireAuth, RequirePermission } from '../auth/guards'
import { FORCED_PASSWORD_CHANGE_PATH, LOGIN_PATH } from '../auth/paths'
import { AppLayout } from '../components/layout/AppLayout'
import { PagePlaceholder } from '../components/PagePlaceholder'
import { DashboardPage } from '../features/dashboard/DashboardPage'
import { ErrorPage } from '../pages/ErrorPage'
import { ForcedChangePasswordPage } from '../pages/ForcedChangePasswordPage'
import { LoginPage } from '../pages/LoginPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { flattenNavItems } from './navigation'
import { Permissions } from '../auth/permissions'
import {
  AccountPage,
  DepartmentsPage,
  Lazy,
  PatientIntakePage,
  PatientProfilePage,
  PatientsPage,
  RolesPage,
  UsersPage,
} from './lazyPages'

/** Trang đã hoàn thành; các mục menu còn lại hiển thị trang "đang phát triển". */
const implementedPages: Record<string, ReactNode> = {
  '/nguoi-benh': <PatientsPage />,
  '/quan-tri/nguoi-dung': <UsersPage />,
  '/quan-tri/vai-tro': <RolesPage />,
  '/quan-tri/khoa-phong': <DepartmentsPage />,
}

const featureRoutes: RouteObject[] = flattenNavItems()
  .filter((item) => item.path !== '/')
  .map((item) => ({
    path: item.path.slice(1),
    element: (
      <RequirePermission permission={item.permission}>
        {implementedPages[item.path] ? (
          <Lazy>{implementedPages[item.path]}</Lazy>
        ) : (
          <PagePlaceholder title={item.label} sprint={item.sprint} />
        )}
      </RequirePermission>
    ),
  }))

export const appRoutes: RouteObject[] = [
  { path: LOGIN_PATH, element: <LoginPage />, errorElement: <ErrorPage /> },
  {
    path: FORCED_PASSWORD_CHANGE_PATH,
    element: (
      <RequireAuth allowPasswordChange>
        <ForcedChangePasswordPage />
      </RequireAuth>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/',
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    errorElement: <ErrorPage />,
    children: [
      { index: true, element: <DashboardPage /> },
      {
        path: 'tai-khoan',
        element: (
          <Lazy>
            <AccountPage />
          </Lazy>
        ),
      },
      ...featureRoutes,
      {
        path: 'nguoi-benh/tiep-nhan',
        element: (
          <RequirePermission permission={Permissions.PatientsManage}>
            <Lazy>
              <PatientIntakePage />
            </Lazy>
          </RequirePermission>
        ),
      },
      {
        path: 'nguoi-benh/:id',
        element: (
          <RequirePermission permission={Permissions.PatientsRead}>
            <Lazy>
              <PatientProfilePage />
            </Lazy>
          </RequirePermission>
        ),
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]
