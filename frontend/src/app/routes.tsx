import type { RouteObject } from 'react-router'
import { AppLayout } from '../components/layout/AppLayout'
import { PagePlaceholder } from '../components/PagePlaceholder'
import { DashboardPage } from '../features/dashboard/DashboardPage'
import { NotFoundPage } from '../pages/NotFoundPage'
import { flattenNavItems } from './navigation'

const placeholderRoutes: RouteObject[] = flattenNavItems()
  .filter((item) => item.path !== '/')
  .map((item) => ({
    path: item.path.slice(1),
    element: <PagePlaceholder title={item.label} sprint={item.sprint} />,
  }))

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      ...placeholderRoutes,
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]
