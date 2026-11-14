import { Button, Result } from 'antd'
import { useNavigate } from 'react-router'

export function ForbiddenPage() {
  const navigate = useNavigate()
  return (
    <Result
      status="403"
      title="Bạn không có quyền truy cập"
      subTitle="Tài khoản của bạn chưa được cấp quyền cho chức năng này. Vui lòng liên hệ quản trị viên nếu cần."
      extra={
        <Button type="primary" onClick={() => navigate('/')}>
          Về trang tổng quan
        </Button>
      }
    />
  )
}
