import type { ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { RedirectKioskAccount, RequireAuth, RequirePermission } from '../auth/guards'
import { FORCED_PASSWORD_CHANGE_PATH, KIOSK_PATH, LOGIN_PATH } from '../auth/paths'
import { AppLayout } from '../components/layout/AppLayout'
import { MaintenanceGate } from '../components/MaintenanceGate'
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
  AuditLogsPage,
  ConsentFormCreatePage,
  ConsentFormDetailPage,
  ConsentFormsPage,
  ConsentTemplateDetailPage,
  ConsentTemplatesPage,
  DepartmentsPage,
  ExportsPage,
  KioskPage,
  Lazy,
  MaintenanceSettingsPage,
  MyTestAssignmentsPage,
  NursingDashboardPage,
  NursingDelegationsPage,
  NursingReportPage,
  NursingSummaryPage,
  PatientIntakePage,
  PatientProfilePage,
  PatientsPage,
  QcBookPage,
  QcBooksPage,
  QcCatalogPage,
  RemoteSigningPage,
  ReportsPage,
  RolesPage,
  SettingsPage,
  SigningKioskPage,
  TestAssignmentsPage,
  UsersPage,
} from './lazyPages'
import { SIGNING_PATH } from '../features/signing/signingSession'
import { REMOTE_SIGNING_PATH } from '../api/remoteSigning'

/** Trang đã hoàn thành; các mục menu còn lại hiển thị trang "đang phát triển". */
const implementedPages: Record<string, ReactNode> = {
  '/phieu-dong-thuan': <ConsentFormsPage />,
  '/mau-phieu': <ConsentTemplatesPage />,
  '/nguoi-benh': <PatientsPage />,
  '/xuat-du-lieu': <ExportsPage />,
  '/bao-cao': <ReportsPage />,
  '/nhat-ky': <AuditLogsPage />,
  '/dieu-duong/bao-cao-khoa': <NursingReportPage />,
  '/dieu-duong/tong-hop': <NursingSummaryPage />,
  '/dieu-duong/dashboard': <NursingDashboardPage />,
  '/dieu-duong/uy-quyen': <NursingDelegationsPage />,
  '/noi-kiem/so': <QcBooksPage />,
  '/noi-kiem/danh-muc': <QcCatalogPage />,
  '/quan-tri/nguoi-dung': <UsersPage />,
  '/quan-tri/vai-tro': <RolesPage />,
  '/quan-tri/khoa-phong': <DepartmentsPage />,
  '/quan-tri/cau-hinh': <SettingsPage />,
  '/quan-tri/bao-tri': <MaintenanceSettingsPage />,
  '/quan-tri/kiem-thu': <TestAssignmentsPage />,
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

/** Các trang (đều nằm dưới màn hình bảo trì). */
const pageRoutes: RouteObject[] = [
  { path: LOGIN_PATH, element: <LoginPage />, errorElement: <ErrorPage /> },
  // Chế độ ký trên điện thoại: không dùng phiên đăng nhập (chỉ có token phiên ký), không có menu.
  {
    path: SIGNING_PATH,
    element: (
      <Lazy>
        <SigningKioskPage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  // Ký trên điện thoại riêng của người bệnh qua mã QR (quyết định 32): không đăng nhập, mở bằng ngày sinh của người bệnh.
  {
    path: REMOTE_SIGNING_PATH,
    element: (
      <Lazy>
        <RemoteSigningPage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  // Kiosk người bệnh tự làm phiếu (quyết định 24): toàn màn hình, không có menu.
  {
    path: KIOSK_PATH,
    element: (
      <RequireAuth>
        <RequirePermission permission={Permissions.KioskUse}>
          <Lazy>
            <KioskPage />
          </Lazy>
        </RequirePermission>
      </RequireAuth>
    ),
    errorElement: <ErrorPage />,
  },
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
        <RedirectKioskAccount>
          <AppLayout />
        </RedirectKioskAccount>
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
      // Việc kiểm thử của tôi (quyết định 49): mọi tài khoản mở được, chỉ thấy việc được phân công cho mình.
      {
        path: 'kiem-thu',
        element: (
          <Lazy>
            <MyTestAssignmentsPage />
          </Lazy>
        ),
      },
      ...featureRoutes,
      {
        path: 'phieu-dong-thuan/tao-moi',
        element: (
          <RequirePermission permission={Permissions.ConsentsCreate}>
            <Lazy>
              <ConsentFormCreatePage />
            </Lazy>
          </RequirePermission>
        ),
      },
      {
        path: 'phieu-dong-thuan/:id',
        element: (
          <RequirePermission permission={Permissions.ConsentsRead}>
            <Lazy>
              <ConsentFormDetailPage />
            </Lazy>
          </RequirePermission>
        ),
      },
      {
        path: 'mau-phieu/:id',
        element: (
          <RequirePermission permission={Permissions.TemplatesRead}>
            <Lazy>
              <ConsentTemplateDetailPage />
            </Lazy>
          </RequirePermission>
        ),
      },
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
        path: 'noi-kiem/so/:id',
        element: (
          <RequirePermission permission={[Permissions.QcRecord, Permissions.QcReview]}>
            <Lazy>
              <QcBookPage />
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

export const appRoutes: RouteObject[] = [
  // Chế độ bảo trì (quyết định 53): máy chủ trả 503 thì màn hình "Hệ thống đang bảo trì" che trang đang mở (kể cả kiosk, ký QR).
  { element: <MaintenanceGate />, errorElement: <ErrorPage />, children: pageRoutes },
]
