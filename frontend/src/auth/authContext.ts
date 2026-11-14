import { createContext, useContext } from 'react'
import type { AuthResponse, CurrentUser } from '../api/auth'

export type AuthStatus = 'loading' | 'anonymous' | 'authenticated'

export interface AuthContextValue {
  status: AuthStatus
  user: CurrentUser | null
  /**
   * Người dùng vừa chủ động đăng xuất. Khi đó trang đăng nhập không giữ lại đường dẫn cũ
   * (máy dùng chung: người đăng nhập sau không bị đưa về trang của người trước).
   */
  signedOutByUser: boolean
  login: (userName: string, password: string) => Promise<CurrentUser>
  logout: () => Promise<void>
  /** Áp dụng phiên mới (ví dụ sau khi đổi mật khẩu). */
  applySession: (session: AuthResponse) => void
  hasPermission: (permission: string) => boolean
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth phải được dùng bên trong AuthProvider.')
  }
  return context
}
