import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { App as AntdApp, ConfigProvider } from 'antd'
import viVN from 'antd/locale/vi_VN'
import dayjs from 'dayjs'
import 'dayjs/locale/vi'
import { useState } from 'react'
import { RouterProvider, type DataRouter } from 'react-router'
import { AuthProvider } from '../auth/AuthProvider'
import { EnvironmentBanner } from '../components/EnvironmentBanner'
import { LogoFavicon } from '../components/layout/LogoFavicon'
import { createQueryClient } from './queryClient'
import { appTheme } from './theme'

dayjs.locale('vi')

interface AppProps {
  router: DataRouter
  queryClient?: QueryClient
}

/** Gốc ứng dụng: Ant Design (tiếng Việt), TanStack Query, dải báo môi trường kiểm thử, phiên đăng nhập và router. */
export function App({ router, queryClient }: AppProps) {
  const [client] = useState(() => queryClient ?? createQueryClient())

  return (
    <ConfigProvider locale={viVN} theme={appTheme}>
      <AntdApp>
        <QueryClientProvider client={client}>
          {/* UAT: dải báo môi trường kiểm thử trên mọi trang (quyết định 50). */}
          <EnvironmentBanner />
          {/* Quyết định 61: biểu tượng thẻ trình duyệt theo logo bệnh viện. */}
          <LogoFavicon />
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
        </QueryClientProvider>
      </AntdApp>
    </ConfigProvider>
  )
}
