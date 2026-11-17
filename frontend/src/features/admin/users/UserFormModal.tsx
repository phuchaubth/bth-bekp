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
import { useFeatures } from '../../../api/features'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'
import { passwordHint, passwordRules } from '../../../utils/passwordRules'

type UserFormValues = CreateUserInput

/** Số khoa/phòng khác tối đa của một tài khoản (như máy chủ, quyết định 41). */
const MAX_OTHER_DEPARTMENTS = 20

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
  const departments = useDepartments(true)
  const { smartCa } = useFeatures()
  const primaryDepartmentId = Form.useWatch('departmentId', form)

  const save = useMutation({
    mutationFn: (values: UserFormValues) => {
      const profile: UserProfileInput = {
        fullName: values.fullName,
        email: values.email || null,
        phoneNumber: values.phoneNumber || null,
        title: values.title || null,
        departmentId: values.departmentId ?? null,
        additionalDepartmentIds: values.departmentId ? (values.additionalDepartmentIds ?? []) : [],
        // Chưa tích hợp SmartCA thì ô bị ẩn: giữ nguyên giá trị đã lưu (nếu có), không xóa mất.
        smartCaUserId: smartCa ? values.smartCaUserId || null : (detail.data?.smartCaUserId ?? null),
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

  // Khoa đang ngừng sử dụng chỉ hiện khi tài khoản đang thuộc khoa đó, để thấy tên và đổi, bỏ ra được (lưu phải chọn khoa
  // đang hoạt động).
  const assigned = new Set([detail.data?.departmentId, ...(detail.data?.additionalDepartments ?? []).map((d) => d.id)])
  const shownDepartments = (departments.data ?? []).filter((d) => d.isActive || assigned.has(d.id))
  const optionOf = (d: { id: string; name: string; isActive: boolean }) => ({
    value: d.id,
    label: d.isActive ? d.name : `${d.name} (ngừng sử dụng)`,
  })
  const primaryOptions = shownDepartments.map((d) => ({ ...optionOf(d), disabled: !d.isActive }))
  // Không khóa lựa chọn ở ô nhiều khoa: thẻ của lựa chọn bị khóa không có nút bỏ ra.
  const otherOptions = shownDepartments.filter((d) => d.id !== primaryDepartmentId).map(optionOf)

  const initialValues: Partial<UserFormValues> = detail.data
    ? {
        fullName: detail.data.fullName,
        email: detail.data.email,
        phoneNumber: detail.data.phoneNumber,
        title: detail.data.title,
        departmentId: detail.data.departmentId,
        additionalDepartmentIds: (detail.data.additionalDepartments ?? []).map((d) => d.id),
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
              <Form.Item
                label="Khoa/phòng chính"
                name="departmentId"
                extra="Khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng."
              >
                <Select
                  allowClear
                  showSearch
                  optionFilterProp="label"
                  placeholder="Chọn khoa/phòng"
                  options={primaryOptions}
                  onChange={(departmentId?: string) => {
                    // Khoa chính không nằm trong danh sách khoa khác; bỏ khoa chính thì bỏ luôn các khoa khác.
                    const others = (form.getFieldValue('additionalDepartmentIds') as string[] | undefined) ?? []
                    form.setFieldValue('additionalDepartmentIds', departmentId ? others.filter((id) => id !== departmentId) : [])
                  }}
                />
              </Form.Item>
            </Col>
            <Col xs={24}>
              <Form.Item
                label="Khoa/phòng khác"
                name="additionalDepartmentIds"
                extra="Tài khoản làm việc ở nhiều khoa: thấy phiếu, báo cáo của khoa chính và các khoa này. Người cần xem toàn viện thì gán vai trò có quyền Xem dữ liệu toàn viện."
                rules={[
                  {
                    type: 'array',
                    max: MAX_OTHER_DEPARTMENTS,
                    message: `Một tài khoản có tối đa ${MAX_OTHER_DEPARTMENTS} khoa/phòng khác.`,
                  },
                ]}
              >
                <Select
                  mode="multiple"
                  allowClear
                  optionFilterProp="label"
                  disabled={!primaryDepartmentId}
                  placeholder={primaryDepartmentId ? 'Chọn thêm khoa/phòng (nếu có)' : 'Chọn khoa/phòng chính trước'}
                  options={otherOptions}
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
            {smartCa && (
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
            )}
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
