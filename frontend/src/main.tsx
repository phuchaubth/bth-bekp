import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter } from 'react-router'
import { App } from './app/App'
import { appRoutes } from './app/routes'
// Font Be Vietnam Pro đóng gói kèm ứng dụng (không tải từ Google: CSP chỉ cho phép tài nguyên của chính trang).
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/400-italic.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'
import './styles.css'

const router = createBrowserRouter(appRoutes)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App router={router} />
  </StrictMode>,
)
