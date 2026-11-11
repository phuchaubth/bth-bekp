import { Flex, Spin } from 'antd'
import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { ForbiddenPage } from '../pages/ForbiddenPage'
import { useAuth } from './authContext'
import { FORCED_PASSWORD_CHANGE_PATH, LOGIN_PATH } from './paths'

export function FullPageSpinner({ tip = 'Đang tải…' }: { tip?: string }) {
  return (
    <Flex align="center" justify="center" style={{ minHeight: '100vh' }} vertical gap={12}>
      <Spin size="large" />
      <span>{tip}</span>
    </Flex>
  )
}

/** Yêu cầu đăng nhập; người dùng phải đổi mật khẩu thì chuyển tới trang đổi mật khẩu bắt buộc. */
export function RequireAuth({ children, allowPasswordChange = false }: { children: ReactNode; allowPasswordChange?: boolean }) {
  const { status, user, signedOutByUser } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return <FullPageSpinner tip="Đang kiểm tra phiên đăng nhập…" />
  }

  if (status === 'anonymous' || !user) {
    // Phiên hết hạn: quay lại đúng trang sau khi đăng nhập. Chủ động đăng xuất: về trang đăng nhập "sạch".
    const target = signedOutByUser
      ? LOGIN_PATH
      : `${LOGIN_PATH}?redirect=${encodeURIComponent(location.pathname + location.search)}`
    return <Navigate to={target} replace />
  }

  if (user.mustChangePassword && !allowPasswordChange) {
    return <Navigate to={FORCED_PASSWORD_CHANGE_PATH} replace />
  }

  return children
}

/** Hiển thị trang 403 nếu người dùng không có quyền. */
export function RequirePermission({ permission, children }: { permission?: string; children: ReactNode }) {
  const { hasPermission } = useAuth()
  if (permission && !hasPermission(permission)) {
    return <ForbiddenPage />
  }
  return children
}
