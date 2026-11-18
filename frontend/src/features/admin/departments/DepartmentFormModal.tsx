import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Form, Input, Modal, Switch } from 'antd'
import { adminApi, adminKeys, type Department, type DepartmentInput } from '../../../api/admin'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'

interface Props {
  /** null: thêm mới. */
  department: Department | null
  onClose: () => void
}

export function DepartmentFormModal({ department, onClose }: Props) {
  const [form] = Form.useForm<DepartmentInput>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const save = useMutation({
    mutationFn: (input: DepartmentInput) =>
      department ? adminApi.updateDepartment(department.id, input) : adminApi.createDepartment(input),
    onSuccess: async () => {
      message.success(department ? 'Đã cập nhật khoa/phòng.' : 'Đã thêm khoa/phòng.')
      await queryClient.invalidateQueries({ queryKey: adminKeys.allDepartments })
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
      title={department ? 'Sửa khoa/phòng' : 'Thêm khoa/phòng'}
      okText="Lưu"
      cancelText="Hủy"
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
    >
      <Form<DepartmentInput>
        form={form}
        layout="vertical"
        initialValues={department ?? { code: '', name: '', isActive: true }}
        onFinish={(values) => save.mutate(values)}
      >
        <Form.Item
          label="Mã khoa"
          name="code"
          normalize={(value: string) => value?.toUpperCase()}
          rules={[
            { required: true, message: 'Vui lòng nhập mã khoa.' },
            { max: 20, message: 'Mã khoa tối đa 20 ký tự.' },
            { pattern: /^[A-Za-z0-9_-]+$/, message: 'Mã khoa chỉ gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới.' },
          ]}
        >
          <Input placeholder="Ví dụ: KKB" maxLength={20} />
        </Form.Item>
        <Form.Item
          label="Tên khoa/phòng"
          name="name"
          rules={[
            { required: true, message: 'Vui lòng nhập tên khoa.' },
            { max: 200, message: 'Tên khoa tối đa 200 ký tự.' },
          ]}
        >
          <Input placeholder="Ví dụ: Khoa Khám bệnh" maxLength={200} />
        </Form.Item>
        <Form.Item label="Trạng thái" name="isActive" valuePropName="checked">
          <Switch checkedChildren="Đang sử dụng" unCheckedChildren="Ngừng sử dụng" />
        </Form.Item>
      </Form>
    </Modal>
  )
}
