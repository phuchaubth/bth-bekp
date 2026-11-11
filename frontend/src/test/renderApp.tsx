import { QueryClient } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { createMemoryRouter } from 'react-router'
import { App } from '../app/App'
import { appRoutes } from '../app/routes'

/** Render toàn bộ ứng dụng tại một đường dẫn, dùng router trong bộ nhớ. */
export function renderApp(path = '/') {
  const router = createMemoryRouter(appRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return { router, ...render(<App router={router} queryClient={queryClient} />) }
}

export function mockFetchJson(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  })
}
