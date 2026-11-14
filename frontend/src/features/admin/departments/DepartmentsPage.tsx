import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Input, Popconfirm, Space, Table, Tag, type TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { adminApi, adminKeys, useDepartments, type Department } from '../../../api/admin'
import { PageHeader } from '../../../components/PageHeader'
import { errorMessage } from '../../../utils/formErrors'
import { normalizeForSearch } from '../../../utils/text'
import { DepartmentFormModal } from './DepartmentFormModal'

/** Màn hình 18: danh mục khoa/phòng. */
export function DepartmentsPage() {
  const { data, isPending, isError, error } = useDepartments(true)
  const [keyword, setKeyword] = useState('')
  const [editing, setEditing] = useState<Department | 'new' | null>(null)
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const remove = useMutation({
    mutationFn: (id: string) => adminApi.deleteDepartment(id),
    onSuccess: async () => {
      message.success('Đã xóa khoa/phòng.')
      await queryClient.invalidateQueries({ queryKey: adminKeys.allDepartments })
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const rows = useMemo(() => {
    const term = normalizeForSearch(keyword)
    return (data ?? []).filter(
      (d) => !term || normalizeForSearch(d.code).includes(term) || normalizeForSearch(d.name).includes(term),
    )
  }, [data, keyword])

  const columns: TableColumnsType<Department> = [
    { title: 'Mã', dataIndex: 'code', width: 120 },
    { title: 'Tên khoa/phòng', dataIndex: 'name' },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      width: 150,
      render: (isActive: boolean) =>
        isActive ? <Tag color="green">Đang sử dụng</Tag> : <Tag>Ngừng sử dụng</Tag>,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 180,
      render: (_, department) => (
        <Space size={0}>
          <Button type="link" icon={<EditOutlined />} onClick={() => setEditing(department)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xóa khoa/phòng này?"
            description="Chỉ xóa được khoa chưa có người dùng."
            okText="Xóa"
            cancelText="Hủy"
            okButtonProps={{ danger: true }}
            onConfirm={() => remove.mutate(department.id)}
          >
            <Button type="link" danger icon={<DeleteOutlined />}>
              Xóa
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Khoa/phòng"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>
            Thêm khoa/phòng
          </Button>
        }
      />
      <Card>
        <Input.Search
          placeholder="Tìm theo mã hoặc tên khoa"
          allowClear
          style={{ maxWidth: 360, marginBottom: 16 }}
          onChange={(e) => setKeyword(e.target.value)}
        />
        {isError && <Alert type="error" showIcon title={errorMessage(error)} style={{ marginBottom: 16 }} />}
        <Table<Department>
          rowKey="id"
          columns={columns}
          dataSource={rows}
          loading={isPending}
          pagination={{ pageSize: 20, hideOnSinglePage: true }}
          scroll={{ x: 640 }}
          locale={{ emptyText: 'Chưa có khoa/phòng nào.' }}
        />
      </Card>
      {editing !== null && (
        <DepartmentFormModal department={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      )}
    </>
  )
}
