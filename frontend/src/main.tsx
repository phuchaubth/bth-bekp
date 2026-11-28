import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter } from 'react-router'
import { App } from './app/App'
import { appRoutes } from './app/routes'
import { watchStaleBuild } from './app/staleBuild'
// Font Be Vietnam Pro đóng gói kèm ứng dụng (không tải từ Google: CSP chỉ cho phép tài nguyên của chính trang).
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/400-italic.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'
import './styles.css'

// Vừa cài gói mới mà máy còn chạy giao diện cũ: không tải được file của trang thì tự tải lại trang (thay vì trang lỗi).
watchStaleBuild()

const router = createBrowserRouter(appRoutes)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App router={router} />
  </StrictMode>,
)
