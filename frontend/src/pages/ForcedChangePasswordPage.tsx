import { Alert, Button, Card, Flex, Typography } from 'antd'
import { useNavigate } from 'react-router'
import { useAuth } from '../auth/authContext'
import { ChangePasswordForm } from '../features/account/ChangePasswordForm'

/** Trang bắt buộc đổi mật khẩu (tài khoản mới hoặc mật khẩu vừa được đặt lại). */
export function ForcedChangePasswordPage() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100vh', padding: 16 }}>
      <Card style={{ width: '100%', maxWidth: 480 }}>
        <Typography.Title level={3} style={{ marginTop: 0 }}>
          Đổi mật khẩu
        </Typography.Title>
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message={`Xin chào ${user?.fullName ?? ''}. Bạn cần đổi mật khẩu trước khi sử dụng hệ thống.`}
        />
        <ChangePasswordForm onSuccess={() => navigate('/', { replace: true })} />
        <Button type="link" style={{ paddingLeft: 0, marginTop: 8 }} onClick={() => void logout()}>
          Đăng xuất
        </Button>
      </Card>
    </Flex>
  )
}
