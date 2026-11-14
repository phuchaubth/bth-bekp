import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { authApi, type AuthResponse, type CurrentUser } from '../api/auth'
import { ApiError, setRefreshHandler } from '../api/http'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext'
import { tokenStore } from './tokenStore'

/** Làm mới access token trước khi hết hạn khoảng 1 phút. */
const REFRESH_MARGIN_MS = 60_000
const MIN_REFRESH_DELAY_MS = 5_000
/** Khi tab khác vừa làm mới phiên (cookie đã có token mới), chờ một chút rồi thử lại. */
const SUPERSEDED_RETRY_MS = 400

async function refreshWithRetry(): Promise<AuthResponse> {
  try {
    return await authApi.refresh()
  } catch (error) {
    if (error instanceof ApiError && error.code === 'auth.refresh_superseded') {
      await new Promise((resolve) => setTimeout(resolve, SUPERSEDED_RETRY_MS))
      return authApi.refresh()
    }
    throw error
  }
}

/**
 * Quản lý phiên đăng nhập: access token trong bộ nhớ, tự làm mới bằng refresh token (cookie HttpOnly),
 * khôi phục phiên khi tải lại trang và xóa dữ liệu đã cache khi đăng xuất.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [signedOutByUser, setSignedOutByUser] = useState(false)
  const refreshInFlight = useRef<Promise<string | null> | null>(null)
  const refreshTimer = useRef<number | undefined>(undefined)
  const scheduleRefresh = useRef<(delay: number) => void>(() => {})

  const clearSession = useCallback(() => {
    window.clearTimeout(refreshTimer.current)
    tokenStore.set(null)
    setUser(null)
    setStatus('anonymous')
    queryClient.clear()
  }, [queryClient])

  const applySession = useCallback((session: AuthResponse) => {
    tokenStore.set(session.accessToken)
    setUser(session.user)
    setStatus('authenticated')
    setSignedOutByUser(false)
    const expiresIn = new Date(session.accessTokenExpiresAtUtc).getTime() - Date.now()
    scheduleRefresh.current(Math.max(MIN_REFRESH_DELAY_MS, expiresIn - REFRESH_MARGIN_MS))
  }, [])

  const refresh = useCallback((): Promise<string | null> => {
    if (!refreshInFlight.current) {
      refreshInFlight.current = (async () => {
        try {
          const session = await refreshWithRetry()
          applySession(session)
          return session.accessToken
        } catch {
          clearSession()
          return null
        } finally {
          refreshInFlight.current = null
        }
      })()
    }
    return refreshInFlight.current
  }, [applySession, clearSession])

  useEffect(() => {
    scheduleRefresh.current = (delay: number) => {
      window.clearTimeout(refreshTimer.current)
      refreshTimer.current = window.setTimeout(() => void refresh(), delay)
    }
    setRefreshHandler(refresh)
    // Khôi phục phiên khi mở/tải lại ứng dụng.
    void refresh()
    return () => {
      setRefreshHandler(null)
      window.clearTimeout(refreshTimer.current)
    }
  }, [refresh])

  const login = useCallback(
    async (userName: string, password: string) => {
      const session = await authApi.login(userName, password)
      applySession(session)
      return session.user
    },
    [applySession],
  )

  const logout = useCallback(async () => {
    try {
      await authApi.logout()
    } catch {
      // Phiên phía máy chủ có thể đã hết hạn; vẫn xóa phiên phía trình duyệt.
    } finally {
      setSignedOutByUser(true)
      clearSession()
    }
  }, [clearSession])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      signedOutByUser,
      login,
      logout,
      applySession,
      hasPermission: (permission: string) => user?.permissions.includes(permission) ?? false,
    }),
    [status, user, signedOutByUser, login, logout, applySession],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
