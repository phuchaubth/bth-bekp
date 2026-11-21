import { LoginOutlined, LogoutOutlined } from '@ant-design/icons'
import { useQueryClient } from '@tanstack/react-query'
import { Button } from 'antd'
import { useEffect } from 'react'
import { Outlet, useLocation } from 'react-router'
import { maintenanceKeys, useMaintenanceStatus } from '../api/maintenance'
import { maintenanceStore, useMaintenanceState } from '../api/maintenanceStore'
import { REMOTE_SIGNING_PATH } from '../api/remoteSigning'
import { useAuth } from '../auth/authContext'
import { KIOSK_PATH, LOGIN_PATH } from '../auth/paths'
import { Permissions } from '../auth/permissions'
import { SIGNING_PATH } from '../features/signing/signingSession'
import { MAINTENANCE_RECHECK_SECONDS, MaintenanceScreen } from './MaintenanceScreen'

/** Tài khoản có quyền bảo trì: hỏi trạng thái bảo trì định kỳ để hiện dải báo "đang bật chế độ bảo trì". */
const ADMIN_POLL_MS = 60_000

/** Màn hình người bệnh tự dùng: kiosk, ký trên máy tính bảng, ký qua mã QR. */
const PATIENT_PATHS = [KIOSK_PATH, SIGNING_PATH, REMOTE_SIGNING_PATH]

/**
 * Chế độ bảo trì (quyết định 53), bọc mọi trang. Máy chủ trả 503 cho bất kỳ yêu cầu nào (quản trị bật bảo trì, hoặc IIS trả khi
 * ứng dụng đang dừng để cài gói) thì màn hình "Hệ thống đang bảo trì" che trang đang mở; trang vẫn giữ nguyên bên dưới (dữ liệu
 * đang nhập không mất). Màn hình tự hỏi lại trạng thái: hết bảo trì (hoặc tài khoản này có quyền bảo trì) thì đóng lại và tải lại
 * dữ liệu đang hiện; lúc máy chủ dừng hẳn thì tải lại cả trang (có thể đã lên phiên bản mới).
 * Trang đăng nhập khi đang bảo trì cũng bị che; chọn "Đăng nhập bằng tài khoản quản trị" để mở ô đăng nhập.
 */
export function MaintenanceGate() {
  const { notice, adminLogin } = useMaintenanceState()
  const { status, user, hasPermission, logout } = useAuth()
  const { pathname } = useLocation()
  const queryClient = useQueryClient()
  const onLogin = pathname === LOGIN_PATH
  const forPatient = PATIENT_PATHS.some((path) => pathname === path || pathname.startsWith(`${path}/`))
  const canEnter = status === 'authenticated' && hasPermission(Permissions.SystemMaintenance)
  const statusQuery = useMaintenanceStatus(
    notice !== null || canEnter || (onLogin && status === 'anonymous'),
    notice ? MAINTENANCE_RECHECK_SECONDS * 1000 : canEnter ? ADMIN_POLL_MS : false,
  )
  const { data, dataUpdatedAt, isFetching, refetch } = statusQuery

  // Màn hình bảo trì vừa hiện: hỏi ngay lý do, giờ dự kiến xong (hoặc biết là đã hết bảo trì).
  const since = notice?.since
  useEffect(() => {
    if (since !== undefined) {
      void refetch()
    }
  }, [since, refetch])

  useEffect(() => {
    if (!data || dataUpdatedAt === 0) {
      return
    }
    if (data.enabled && !canEnter) {
      maintenanceStore.report({ kind: 'maintenance', reason: data.reason, expectedEndAtUtc: data.expectedEndAtUtc })
      return
    }
    // Chỉ tin kết quả hỏi sau lúc màn hình bảo trì hiện.
    if (!notice || dataUpdatedAt <= notice.since) {
      return
    }
    if (notice.reloadOnEnd || status === 'loading') {
      maintenanceStore.reloadPage()
      return
    }
    maintenanceStore.clear()
    void queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== maintenanceKeys.status[0] })
  }, [data, dataUpdatedAt, canEnter, notice, status, queryClient])

  const shown = notice !== null && !(adminLogin && onLogin) ? notice : null

  const actions =
    status === 'authenticated' ? (
      <Button type="link" icon={<LogoutOutlined />} onClick={() => void logout()}>
        Đăng xuất{user ? ` (${user.fullName})` : ''}
      </Button>
    ) : (
      <Button
        type="link"
        icon={<LoginOutlined />}
        onClick={async () => {
          // Máy đang giữ phiên của tài khoản khác (chưa vào được vì đang bảo trì): đăng xuất phiên đó trước.
          if (status === 'loading') {
            await logout()
          }
          maintenanceStore.startAdminLogin()
        }}
      >
        Đăng nhập bằng tài khoản quản trị
      </Button>
    )

  return (
    <>
      <div style={{ display: 'contents' }} inert={shown !== null} aria-hidden={shown !== null || undefined}>
        <Outlet />
      </div>
      {shown && (
        <MaintenanceScreen notice={shown} forPatient={forPatient} checking={isFetching} onRecheck={() => void refetch()} actions={actions} />
      )}
    </>
  )
}
