import { Button, Result } from 'antd'
import { useNavigate } from 'react-router'

export function NotFoundPage() {
  const navigate = useNavigate()

  return (
    <Result
      status="404"
      title="Không tìm thấy trang"
      subTitle="Đường dẫn không tồn tại hoặc đã được thay đổi."
      extra={
        <Button type="primary" onClick={() => navigate('/')}>
          Về trang tổng quan
        </Button>
      }
    />
  )
}
