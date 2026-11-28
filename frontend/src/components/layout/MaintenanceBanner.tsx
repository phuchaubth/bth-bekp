import { ToolOutlined } from '@ant-design/icons'
import { Alert, Button } from 'antd'
import { useNavigate } from 'react-router'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { useMaintenanceStatus } from '../../api/maintenance'

export const MAINTENANCE_SETTINGS_PATH = '/quan-tri/bao-tri'

/**
 * Dải báo cho tài khoản có quyền bảo trì khi đang bật chế độ bảo trì (quyết định 53): họ vẫn làm việc như thường, còn người dùng
 * khác đang thấy màn hình "Hệ thống đang bảo trì". Trạng thái do {@link MaintenanceGate} hỏi định kỳ.
 */
export function MaintenanceBanner() {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const canEnter = hasPermission(Permissions.SystemMaintenance)
  // Chỉ đọc kết quả MaintenanceGate đã hỏi (không tự gửi yêu cầu).
  const { data } = useMaintenanceStatus(false)

  if (!canEnter || !data?.enabled) {
    return null
  }
  return (
    <Alert
      banner
      type="warning"
      showIcon
      icon={<ToolOutlined />}
      role="status"
      title={`Đang bật chế độ bảo trì${data.reason ? `: ${data.reason}` : ''}. Người dùng không có quyền bảo trì (kể cả kiosk, người bệnh ký qua mã QR) đang thấy màn hình "Hệ thống đang bảo trì".`}
      action={
        <Button size="small" onClick={() => navigate(MAINTENANCE_SETTINGS_PATH)}>
          Bảo trì hệ thống
        </Button>
      }
    />
  )
}
