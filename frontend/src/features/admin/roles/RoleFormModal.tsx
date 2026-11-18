import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Form, Input, Modal, Select, Typography } from 'antd'
import { adminApi, adminKeys, type Role } from '../../../api/admin'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'

interface Values {
  name: string
  description?: string
  /** Chỉ khi thêm: vai trò lấy sẵn quyền. */
  copyFrom?: string
}

interface Props {
  /** null: thêm vai trò mới. */
  role: Role | null
  /** Vai trò hiện có (để lấy sẵn quyền khi thêm). */
  roles: Role[]
  onSaved: (role: Role) => void
  onClose: () => void
}

/** Thêm vai trò, hoặc đổi tên, mô tả (quyết định 40). Mã vai trò do máy chủ sinh từ tên và không đổi. */
export function RoleFormModal({ role, roles, onSaved, onClose }: Props) {
  const [form] = Form.useForm<Values>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  // Vai trò có quyền cố định (Quản trị hệ thống, Kiosk) không dùng làm mẫu quyền.
  const sources = roles.filter((r) => !r.isAdministrator && !r.isLocked)

  const save = useMutation({
    mutationFn: ({ name, description, copyFrom }: Values) => {
      const input = { name, description: description?.trim() ? description : null }
      return role
        ? adminApi.updateRole(role.id, input)
        : adminApi.createRole({ ...input, permissions: sources.find((r) => r.id === copyFrom)?.permissions ?? [] })
    },
    onSuccess: async (saved) => {
      message.success(
        role
          ? `Đã cập nhật vai trò "${saved.name}".`
          : `Đã thêm vai trò "${saved.name}" (mã ${saved.code}). Đánh dấu quyền cho vai trò rồi bấm Lưu thay đổi.`,
      )
      await queryClient.invalidateQueries({ queryKey: adminKeys.roles })
      onSaved(saved)
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
      title={role ? 'Sửa tên, mô tả vai trò' : 'Thêm vai trò'}
      okText="Lưu"
      cancelText="Hủy"
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form<Values>
        form={form}
        layout="vertical"
        initialValues={{ name: role?.name ?? '', description: role?.description ?? '' }}
        onFinish={(values) => save.mutate(values)}
      >
        <Form.Item
          label="Tên vai trò"
          name="name"
          rules={[
            { required: true, whitespace: true, message: 'Vui lòng nhập tên vai trò.' },
            { max: 100, message: 'Tên vai trò tối đa 100 ký tự.' },
          ]}
        >
          <Input placeholder="Ví dụ: Dược sĩ lâm sàng" maxLength={100} />
        </Form.Item>
        <Form.Item label="Mô tả" name="description" rules={[{ max: 500, message: 'Mô tả tối đa 500 ký tự.' }]}>
          <Input.TextArea rows={3} maxLength={500} placeholder="Ví dụ: Khoa Dược, xác nhận phiếu có dùng thuốc" />
        </Form.Item>
        {role ? (
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Mã vai trò <Typography.Text code>{role.code}</Typography.Text> giữ nguyên: quyền của người dùng và cấu hình ký của
            mẫu phiếu dùng mã này.
          </Typography.Paragraph>
        ) : (
          <>
            <Form.Item
              label="Lấy sẵn quyền từ vai trò"
              name="copyFrom"
              extra="Không bắt buộc. Sau khi thêm vẫn đánh dấu, bỏ dấu từng quyền được."
            >
              <Select
                allowClear
                placeholder="Không lấy, tự chọn quyền sau"
                options={sources.map((r) => ({ value: r.id, label: `${r.name} (${r.permissions.length} quyền)` }))}
              />
            </Form.Item>
            <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
              Mã vai trò được tạo tự động từ tên (ví dụ VT_DUOC_SI_LAM_SANG) và không đổi về sau.
            </Typography.Paragraph>
          </>
        )}
      </Form>
    </Modal>
  )
}
