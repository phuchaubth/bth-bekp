import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Col, Form, Input, Modal, Row, Select, Spin } from 'antd'
import {
  adminApi,
  adminKeys,
  useDepartments,
  useRoles,
  useUser,
  type CreateUserInput,
  type UserProfileInput,
} from '../../../api/admin'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'
import { passwordHint, passwordRules } from '../../../utils/passwordRules'

type UserFormValues = CreateUserInput

interface Props {
  /** null: thêm mới. */
  userId: string | null
  onClose: () => void
}

/** Thêm/sửa người dùng (màn hình 16). */
export function UserFormModal({ userId, onClose }: Props) {
  const isEdit = userId !== null
  const [form] = Form.useForm<UserFormValues>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const detail = useUser(userId)
  const roles = useRoles()
  const departments = useDepartments(false)

  const save = useMutation({
    mutationFn: (values: UserFormValues) => {
      const profile: UserProfileInput = {
        fullName: values.fullName,
        email: values.email || null,
        phoneNumber: values.phoneNumber || null,
        title: values.title || null,
        departmentId: values.departmentId ?? null,
        smartCaUserId: values.smartCaUserId || null,
        roleIds: values.roleIds,
      }
      return isEdit
        ? adminApi.updateUser(userId, profile)
        : adminApi.createUser({ ...profile, userName: values.userName, password: values.password })
    },
    onSuccess: async () => {
      message.success(isEdit ? 'Đã cập nhật người dùng.' : 'Đã thêm người dùng.')
      await queryClient.invalidateQueries({ queryKey: adminKeys.allUsers })
      onClose()
    },
    onError: (error) => {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  const loading = (isEdit && detail.isPending) || roles.isPending || departments.isPending
  const loadError = detail.error ?? roles.error ?? departments.error

  const initialValues: Partial<UserFormValues> = detail.data
    ? {
        fullName: detail.data.fullName,
        email: detail.data.email,
        phoneNumber: detail.data.phoneNumber,
        title: detail.data.title,
        departmentId: detail.data.departmentId,
        smartCaUserId: detail.data.smartCaUserId,
        roleIds: detail.data.roles.map((r) => r.id),
      }
    : { roleIds: [] }

  return (
    <Modal
      open
      width={720}
      title={isEdit ? `Sửa người dùng${detail.data ? `: ${detail.data.userName}` : ''}` : 'Thêm người dùng'}
      okText="Lưu"
      cancelText="Hủy"
      confirmLoading={save.isPending}
      okButtonProps={{ disabled: loading || !!loadError }}
      onOk={() => form.submit()}
      onCancel={onClose}
    >
      {loadError ? (
        <Alert type="error" showIcon title={errorMessage(loadError)} />
      ) : loading ? (
        <Spin style={{ display: 'block', padding: 32 }} />
      ) : (
        <Form<UserFormValues>
          form={form}
          layout="vertical"
          initialValues={initialValues}
          onFinish={(values) => save.mutate(values)}
        >
          <Row gutter={16}>
            {!isEdit && (
              <Col xs={24} md={12}>
                <Form.Item
                  label="Tên đăng nhập"
                  name="userName"
                  rules={[
                    { required: true, message: 'Vui lòng nhập tên đăng nhập.' },
                    { min: 3, max: 50, message: 'Tên đăng nhập phải có từ 3 đến 50 ký tự.' },
                    {
                      pattern: /^[A-Za-z0-9._-]+$/,
                      message: 'Tên đăng nhập chỉ gồm chữ không dấu, số, dấu chấm, gạch ngang hoặc gạch dưới.',
                    },
                  ]}
                >
                  <Input autoComplete="off" placeholder="Ví dụ: nguyenvana" />
                </Form.Item>
              </Col>
            )}
            <Col xs={24} md={12}>
              <Form.Item
                label="Họ tên"
                name="fullName"
                rules={[
                  { required: true, message: 'Vui lòng nhập họ tên.' },
                  { max: 200, message: 'Họ tên tối đa 200 ký tự.' },
                ]}
              >
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item label="Chức danh" name="title" rules={[{ max: 100, message: 'Chức danh tối đa 100 ký tự.' }]}>
                <Input placeholder="Ví dụ: Điều dưỡng" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item label="Khoa/phòng" name="departmentId">
                <Select
                  allowClear
                  showSearch
                  optionFilterProp="label"
                  placeholder="Chọn khoa/phòng"
                  options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24}>
              <Form.Item
                label="Vai trò"
                name="roleIds"
                rules={[{ required: true, type: 'array', min: 1, message: 'Vui lòng chọn ít nhất một vai trò.' }]}
              >
                <Select
                  mode="multiple"
                  optionFilterProp="label"
                  placeholder="Chọn vai trò"
                  options={(roles.data ?? []).map((r) => ({ value: r.id, label: r.name }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item label="Email" name="email" rules={[{ type: 'email', message: 'Email không hợp lệ.' }]}>
                <Input type="email" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                label="Số điện thoại"
                name="phoneNumber"
                rules={[{ pattern: /^\+?[0-9 .-]{8,20}$/, message: 'Số điện thoại không hợp lệ.' }]}
              >
                <Input inputMode="tel" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                label="Mã tài khoản SmartCA"
                name="smartCaUserId"
                extra="Thường là số CCCD của nhân viên, dùng để ký số."
                rules={[{ pattern: /^[A-Za-z0-9]{1,50}$/, message: 'Mã SmartCA chỉ gồm chữ không dấu và số.' }]}
              >
                <Input />
              </Form.Item>
            </Col>
            {!isEdit && (
              <Col xs={24} md={12}>
                <Form.Item
                  label="Mật khẩu ban đầu"
                  name="password"
                  rules={passwordRules}
                  extra={`${passwordHint} Người dùng phải đổi mật khẩu ở lần đăng nhập đầu tiên.`}
                >
                  <Input.Password autoComplete="new-password" />
                </Form.Item>
              </Col>
            )}
          </Row>
        </Form>
      )}
    </Modal>
  )
}
