import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { App as AntdApp, ConfigProvider } from 'antd'
import viVN from 'antd/locale/vi_VN'
import dayjs from 'dayjs'
import 'dayjs/locale/vi'
import { useState } from 'react'
import { RouterProvider, type DataRouter } from 'react-router'
import { AuthProvider } from '../auth/AuthProvider'
import { createQueryClient } from './queryClient'
import { appTheme } from './theme'

dayjs.locale('vi')

interface AppProps {
  router: DataRouter
  queryClient?: QueryClient
}

/** Gốc ứng dụng: Ant Design (tiếng Việt), TanStack Query, phiên đăng nhập và router. */
export function App({ router, queryClient }: AppProps) {
  const [client] = useState(() => queryClient ?? createQueryClient())

  return (
    <ConfigProvider locale={viVN} theme={appTheme}>
      <AntdApp>
        <QueryClientProvider client={client}>
          <AuthProvider>
            <RouterProvider router={router} />
          </AuthProvider>
        </QueryClientProvider>
      </AntdApp>
    </ConfigProvider>
  )
}
