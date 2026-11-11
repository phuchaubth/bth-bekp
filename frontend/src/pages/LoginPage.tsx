import { LockOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Flex, Form, Input, Typography } from 'antd'
import { useState } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router'
import { useAuth } from '../auth/authContext'
import { FullPageSpinner } from '../auth/guards'
import { safeRedirectPath } from '../auth/paths'
import { errorMessage } from '../utils/formErrors'

interface LoginValues {
  userName: string
  password: string
}

export function LoginPage() {
  const { status, user, login } = useAuth()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const redirect = safeRedirectPath(searchParams.get('redirect'))

  if (status === 'loading') {
    return <FullPageSpinner />
  }

  if (status === 'authenticated' && user) {
    return <Navigate to={redirect} replace />
  }

  const onFinish = async (values: LoginValues) => {
    setSubmitting(true)
    setError(null)
    try {
      await login(values.userName, values.password)
      navigate(redirect, { replace: true })
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Flex
      align="center"
      justify="center"
      style={{ minHeight: '100vh', padding: 16, background: 'linear-gradient(135deg, #0a2540 0%, #0b6bcb 100%)' }}
    >
      <Card style={{ width: '100%', maxWidth: 400 }}>
        <Flex vertical align="center" gap={4} style={{ marginBottom: 24 }}>
          <img src="/favicon.svg" alt="" width={48} height={48} />
          <Typography.Title level={3} style={{ margin: '8px 0 0', textAlign: 'center' }}>
            Quản lý Phiếu đồng thuận
          </Typography.Title>
          <Typography.Text type="secondary">Đăng nhập bằng tài khoản được cấp</Typography.Text>
        </Flex>

        {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 16 }} role="alert" />}

        <Form<LoginValues> layout="vertical" onFinish={onFinish} requiredMark={false} disabled={submitting}>
          <Form.Item
            label="Tên đăng nhập"
            name="userName"
            rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập.' }]}
          >
            <Input prefix={<UserOutlined />} autoComplete="username" autoFocus size="large" />
          </Form.Item>
          <Form.Item label="Mật khẩu" name="password" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu.' }]}>
            <Input.Password prefix={<LockOutlined />} autoComplete="current-password" size="large" />
          </Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={submitting}>
            Đăng nhập
          </Button>
        </Form>

        <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0, fontSize: 12, textAlign: 'center' }}>
          Hệ thống chỉ dành cho nhân viên bệnh viện. Quên mật khẩu, vui lòng liên hệ bộ phận CNTT.
        </Typography.Paragraph>
      </Card>
    </Flex>
  )
}
