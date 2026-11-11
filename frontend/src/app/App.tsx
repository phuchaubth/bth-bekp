import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { App as AntdApp, ConfigProvider } from 'antd'
import viVN from 'antd/locale/vi_VN'
import dayjs from 'dayjs'
import 'dayjs/locale/vi'
import { useState } from 'react'
import { RouterProvider, type DataRouter } from 'react-router'
import { createQueryClient } from './queryClient'
import { appTheme } from './theme'

dayjs.locale('vi')

interface AppProps {
  router: DataRouter
  queryClient?: QueryClient
}

/** Gốc ứng dụng: cấu hình Ant Design (tiếng Việt), TanStack Query và router. */
export function App({ router, queryClient }: AppProps) {
  const [client] = useState(() => queryClient ?? createQueryClient())

  return (
    <ConfigProvider locale={viVN} theme={appTheme}>
      <AntdApp>
        <QueryClientProvider client={client}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </AntdApp>
    </ConfigProvider>
  )
}
