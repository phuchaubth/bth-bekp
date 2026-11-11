import { Badge, Card, Typography } from 'antd'
import { useHealth } from '../../api/health'

/** Hiển thị trạng thái kết nối tới API. */
export function SystemStatus() {
  const { data, isPending, isError, error } = useHealth()

  let badge: 'processing' | 'success' | 'error' | 'warning' = 'processing'
  let text = 'Đang kiểm tra kết nối…'
  if (isError) {
    badge = 'error'
    text = error.message || 'Không kết nối được tới máy chủ.'
  } else if (!isPending) {
    badge = data.status === 'Healthy' ? 'success' : 'warning'
    text = data.status === 'Healthy' ? 'Máy chủ đang hoạt động' : 'Máy chủ hoạt động không ổn định'
  }

  return (
    <Card title="Trạng thái hệ thống" size="small">
      <Badge status={badge} text={<Typography.Text>{text}</Typography.Text>} />
    </Card>
  )
}
