import { DeleteOutlined, EditOutlined, PlusOutlined, SyncOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Input, Popconfirm, Space, Table, Tag, type TableColumnsType } from 'antd'
import { useMemo, useState } from 'react'
import { adminApi, adminKeys, useDepartments, type Department, type DepartmentSyncResult } from '../../../api/admin'
import { useFeatures } from '../../../api/features'
import { PageHeader } from '../../../components/PageHeader'
import { errorMessage } from '../../../utils/formErrors'
import { normalizeForSearch } from '../../../utils/text'
import { DepartmentFormModal } from './DepartmentFormModal'

/** Tóm tắt một lần lấy danh mục từ HIS. */
function syncSummary(result: DepartmentSyncResult): string {
  return `Đã lấy ${result.total} khoa/phòng từ HIS: thêm ${result.added}, cập nhật ${result.updated}, giữ nguyên ${result.unchanged}.`
}

function SyncResultAlert({ result, onClose }: { result: DepartmentSyncResult; onClose: () => void }) {
  const warnings = result.deactivated.length > 0 || result.skipped.length > 0
  return (
    <Alert
      type={warnings ? 'warning' : 'success'}
      showIcon
      closable={{ onClose }}
      style={{ marginBottom: 16 }}
      title={syncSummary(result)}
      description={
        warnings && (
          <>
            {result.deactivated.length > 0 && (
              <div>
                Chuyển sang ngừng sử dụng vì không còn trên HIS: {result.deactivated.join(', ')}. Người dùng thuộc các khoa/phòng
                này cần chuyển sang khoa/phòng khác (Quản trị → Người dùng).
              </div>
            )}
            {result.skipped.length > 0 && (
              <div>
                Bỏ qua {result.skipped.length} dòng có mã rỗng hoặc dài quá 20 ký tự
                {result.skipped.some((code) => code) && `: ${result.skipped.filter((code) => code).join(', ')}`}.
              </div>
            )}
          </>
        )
      }
    />
  )
}

/** Màn hình 18: danh mục khoa/phòng. Khi đã kết nối API thông tin người bệnh, danh mục lấy từ HIS (quyết định 42). */
export function DepartmentsPage() {
  const { data, isPending, isError, error } = useDepartments(true)
  const { departmentsFromHis } = useFeatures()
  const [keyword, setKeyword] = useState('')
  const [editing, setEditing] = useState<Department | 'new' | null>(null)
  const [lastSync, setLastSync] = useState<DepartmentSyncResult | null>(null)
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

  const sync = useMutation({
    mutationFn: () => adminApi.syncDepartmentsFromHis(),
    onSuccess: async (result) => {
      setLastSync(result)
      message.success(syncSummary(result))
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
  ]
  if (!departmentsFromHis) {
    columns.push({
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
    })
  }

  return (
    <>
      <PageHeader
        title="Khoa/phòng"
        extra={
          departmentsFromHis ? (
            <Button type="primary" icon={<SyncOutlined />} loading={sync.isPending} onClick={() => sync.mutate()}>
              Lấy danh mục từ HIS
            </Button>
          ) : (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>
              Thêm khoa/phòng
            </Button>
          )
        }
      />
      <Card>
        {departmentsFromHis && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            title="Danh mục khoa/phòng lấy từ HIS: không thêm, sửa, xóa ở đây."
            description="Khi HIS thêm, đổi tên hoặc ngừng dùng khoa/phòng, bấm Lấy danh mục từ HIS. Khoa/phòng không còn trên HIS chuyển sang ngừng sử dụng; người dùng, phiếu đã gắn vẫn giữ."
          />
        )}
        {lastSync && <SyncResultAlert result={lastSync} onClose={() => setLastSync(null)} />}
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
          locale={{ emptyText: departmentsFromHis ? 'Chưa có khoa/phòng nào. Bấm Lấy danh mục từ HIS.' : 'Chưa có khoa/phòng nào.' }}
        />
      </Card>
      {editing !== null && (
        <DepartmentFormModal department={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      )}
    </>
  )
}
