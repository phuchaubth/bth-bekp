import { Alert, Button, Card, Form, Input } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useAuth } from '../../auth/authContext'
import { errorMessage } from '../../utils/formErrors'
import { signingSession, type SigningSession } from './signingSession'

/** Thoát chế độ ký: nhân viên đăng nhập lại (máy chủ kiểm tra mật khẩu, khóa tạm nếu sai nhiều lần). */
export function StaffUnlock({ session }: { session: SigningSession }) {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const onFinish = async (values: { userName: string; password: string }) => {
    setBusy(true)
    setError(null)
    try {
      await login(values.userName.trim(), values.password)
      signingSession.clear()
      if (document.fullscreenElement) {
        void document.exitFullscreen().catch(() => {})
      }
      navigate(`/phieu-dong-thuan/${session.form.id}`, { replace: true })
    } catch (e) {
      setError(errorMessage(e))
      setBusy(false)
    }
  }

  return (
    <Card title="Nhân viên đăng nhập để thoát chế độ ký" style={{ maxWidth: 480, width: '100%', alignSelf: 'center' }}>
      <Form layout="vertical" initialValues={{ userName: session.staffUserName }} onFinish={(values) => void onFinish(values)} requiredMark={false}>
        <Form.Item label="Tên đăng nhập" name="userName" rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập.' }]}>
          <Input autoComplete="username" />
        </Form.Item>
        <Form.Item label="Mật khẩu" name="password" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu.' }]}>
          <Input.Password autoComplete="current-password" autoFocus />
        </Form.Item>
        {error && <Alert type="error" showIcon title={error} style={{ marginBottom: 16 }} />}
        <Button type="primary" htmlType="submit" block loading={busy}>
          Đăng nhập và thoát chế độ ký
        </Button>
      </Form>
    </Card>
  )
}
