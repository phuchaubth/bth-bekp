import { App, Button, Form, Input } from 'antd'
import { useState } from 'react'
import { authApi } from '../../api/auth'
import { useAuth } from '../../auth/authContext'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { passwordHint, passwordRules } from '../../utils/passwordRules'

interface ChangePasswordValues {
  currentPassword: string
  newPassword: string
  confirmPassword: string
}

/** Đổi mật khẩu của chính mình. Sau khi đổi, các phiên ở thiết bị khác bị đăng xuất. */
export function ChangePasswordForm({ onSuccess }: { onSuccess?: () => void }) {
  const [form] = Form.useForm<ChangePasswordValues>()
  const { applySession } = useAuth()
  const { message } = App.useApp()
  const [submitting, setSubmitting] = useState(false)

  const onFinish = async (values: ChangePasswordValues) => {
    setSubmitting(true)
    try {
      const session = await authApi.changePassword(values.currentPassword, values.newPassword)
      applySession(session)
      form.resetFields()
      message.success('Đổi mật khẩu thành công.')
      onSuccess?.()
    } catch (error) {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Form<ChangePasswordValues> form={form} layout="vertical" onFinish={onFinish} disabled={submitting} style={{ maxWidth: 420 }}>
      <Form.Item
        label="Mật khẩu hiện tại"
        name="currentPassword"
        rules={[{ required: true, message: 'Vui lòng nhập mật khẩu hiện tại.' }]}
      >
        <Input.Password autoComplete="current-password" />
      </Form.Item>
      <Form.Item label="Mật khẩu mới" name="newPassword" rules={passwordRules} extra={passwordHint}>
        <Input.Password autoComplete="new-password" />
      </Form.Item>
      <Form.Item
        label="Nhập lại mật khẩu mới"
        name="confirmPassword"
        dependencies={['newPassword']}
        rules={[
          { required: true, message: 'Vui lòng nhập lại mật khẩu mới.' },
          ({ getFieldValue }) => ({
            validator: (_, value) =>
              !value || getFieldValue('newPassword') === value
                ? Promise.resolve()
                : Promise.reject(new Error('Mật khẩu nhập lại không khớp.')),
          }),
        ]}
      >
        <Input.Password autoComplete="new-password" />
      </Form.Item>
      <Button type="primary" htmlType="submit" loading={submitting}>
        Đổi mật khẩu
      </Button>
    </Form>
  )
}
