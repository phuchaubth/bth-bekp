import { ExperimentOutlined } from '@ant-design/icons'
import { Alert } from 'antd'
import { useEffect } from 'react'
import { useEnvironmentLabel } from '../api/environment'

/**
 * Dải báo môi trường kiểm thử (quyết định 50): ở UAT, mọi trang (kể cả trang đăng nhập, kiosk, màn hình ký) có dải màu vàng và
 * tiêu đề tab trình duyệt có nhãn, để người dùng không nhầm với hệ thống chạy thật. Môi trường chạy thật không có nhãn nên
 * không hiện gì.
 */
export function EnvironmentBanner() {
  const label = useEnvironmentLabel()

  useEffect(() => {
    if (!label) {
      return undefined
    }
    const original = document.title
    document.title = `[${label}] ${original}`
    return () => {
      document.title = original
    }
  }, [label])

  if (!label) {
    return null
  }
  return (
    <Alert
      banner
      type="warning"
      showIcon
      icon={<ExperimentOutlined />}
      role="status"
      title={`${label}: môi trường kiểm thử. Phiếu, báo cáo lập ở đây không có giá trị thật; không dùng cho người bệnh thật.`}
    />
  )
}
