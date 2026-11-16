import { QueryClient } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { createMemoryRouter } from 'react-router'
import { App } from '../app/App'
import { appRoutes } from '../app/routes'

/**
 * Render toàn bộ ứng dụng tại một đường dẫn, dùng router trong bộ nhớ. `strictMode`: bọc trong StrictMode như main.tsx (React gỡ
 * thử rồi gắn lại component, chạy lại effect) để kiểm tra lỗi chỉ gặp khi chạy dev.
 */
export function renderApp(path = '/', { strictMode = false }: { strictMode?: boolean } = {}) {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return { router, queryClient, ...render(<App router={router} queryClient={queryClient} />, { reactStrictMode: strictMode }) }
}
