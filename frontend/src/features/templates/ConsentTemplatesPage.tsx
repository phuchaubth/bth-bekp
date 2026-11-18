import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Form, Input, Modal, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { consentTemplatesApi, templateKeys, useConsentTemplates, type ConsentTemplateListItem, type TemplateInput } from '../../api/consentTemplates'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { signerRoleLabel, signerRoleOptions } from './signerRoles'

/** Màn hình 11: mẫu phiếu và trạng thái phát hành. */
export function ConsentTemplatesPage() {
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.TemplatesManage)
  const { data, isPending, isError, error } = useConsentTemplates(true)
  const [creating, setCreating] = useState(false)

  const columns: TableColumnsType<ConsentTemplateListItem> = [
    {
      title: 'Mẫu phiếu',
      key: 'name',
      render: (_, template) => (
        <Link to={`/mau-phieu/${template.id}`}>
          <Typography.Text strong>{template.code}</Typography.Text> – {template.name}
        </Link>
      ),
    },
    { title: 'Xác nhận phía bệnh viện', dataIndex: 'hospitalSignerRoleCode', width: 190, render: signerRoleLabel },
    { title: 'Phạm vi', dataIndex: 'departmentId', width: 110, render: (value: string | null) => (value ? 'Một khoa' : 'Toàn viện') },
    {
      title: 'Đang phát hành',
      key: 'published',
      width: 200,
      render: (_, template) =>
        template.publishedVersionNo ? (
          <>
            <Tag color="green">Phiên bản {template.publishedVersionNo}</Tag>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {formatDateTime(template.publishedAtUtc)}
            </Typography.Text>
          </>
        ) : (
          <Tag color="orange">Chưa phát hành</Tag>
        ),
    },
    {
      title: 'Nháp',
      dataIndex: 'draftVersionNo',
      width: 120,
      render: (value: number | null) => (value ? <Tag>Phiên bản {value}</Tag> : '—'),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      width: 120,
      render: (active: boolean) => (active ? <Tag color="blue">Đang dùng</Tag> : <Tag>Ngừng sử dụng</Tag>),
    },
  ]

  return (
    <>
      <PageHeader
        title="Mẫu phiếu"
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              Thêm mẫu phiếu
            </Button>
          )
        }
      />
      <Card>
        {isError && <Alert type="error" showIcon title={errorMessage(error)} style={{ marginBottom: 16 }} />}
        <Table<ConsentTemplateListItem>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={isPending}
          pagination={false}
          scroll={{ x: 900 }}
          locale={{ emptyText: 'Chưa có mẫu phiếu.' }}
        />
      </Card>
      {creating && <CreateTemplateModal onClose={() => setCreating(false)} />}
    </>
  )
}

function CreateTemplateModal({ onClose }: { onClose: () => void }) {
  const [form] = Form.useForm<TemplateInput>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const create = useMutation({
    mutationFn: (values: TemplateInput) => consentTemplatesApi.create({ ...values, departmentId: null }),
    onSuccess: async (template) => {
      await queryClient.invalidateQueries({ queryKey: templateKeys.all })
      message.success(`Đã tạo mẫu ${template.code} (phiên bản nháp 1).`)
      navigate(`/mau-phieu/${template.id}`)
    },
    onError: (e) => {
      if (!applyServerFieldErrors(form, e)) {
        message.error(errorMessage(e))
      }
    },
  })

  return (
    <Modal open title="Thêm mẫu phiếu" okText="Tạo" cancelText="Hủy" onOk={() => form.submit()} onCancel={onClose} confirmLoading={create.isPending}>
      <Form<TemplateInput> form={form} layout="vertical" onFinish={(values) => create.mutate(values)} initialValues={{ hospitalSignerRoleCode: 'NURSE' }}>
        <Form.Item label="Mã mẫu" name="code" rules={[{ required: true, message: 'Vui lòng nhập mã mẫu.' }]} extra="Ví dụ: PT-01. In ở góc phiếu.">
          <Input maxLength={20} />
        </Form.Item>
        <Form.Item label="Tên mẫu" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên mẫu.' }]}>
          <Input maxLength={200} />
        </Form.Item>
        <Form.Item label="Vai trò xác nhận phía bệnh viện" name="hospitalSignerRoleCode" rules={[{ required: true }]}>
          <Select options={signerRoleOptions} />
        </Form.Item>
      </Form>
    </Modal>
  )
}
