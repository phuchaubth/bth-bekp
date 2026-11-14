import { useMutation } from '@tanstack/react-query'
import { App, Form, Input, Modal, Typography } from 'antd'
import { adminApi, type UserListItem } from '../../../api/admin'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'
import { passwordHint, passwordRules } from '../../../utils/passwordRules'

interface Values {
  newPassword: string
  confirmPassword: string
}

export function ResetPasswordModal({ user, onClose }: { user: UserListItem; onClose: () => void }) {
  const [form] = Form.useForm<Values>()
  const { message } = App.useApp()

  const reset = useMutation({
    mutationFn: (values: Values) => adminApi.resetPassword(user.id, values.newPassword),
    onSuccess: () => {
      message.success('Đã đặt lại mật khẩu. Người dùng phải đổi mật khẩu ở lần đăng nhập tiếp theo.')
      onClose()
    },
    onError: (error) => {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  return (
    <Modal
      open
      title="Đặt lại mật khẩu"
      okText="Đặt lại"
      cancelText="Hủy"
      confirmLoading={reset.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
    >
      <Typography.Paragraph>
        Đặt mật khẩu tạm cho <strong>{user.fullName}</strong> ({user.userName}). Mọi phiên đăng nhập hiện tại của người dùng
        sẽ bị đăng xuất.
      </Typography.Paragraph>
      <Form<Values> form={form} layout="vertical" onFinish={(values) => reset.mutate(values)}>
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
      </Form>
    </Modal>
  )
}
