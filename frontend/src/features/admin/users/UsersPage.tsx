import { EditOutlined, KeyOutlined, LockOutlined, MoreOutlined, PlusOutlined, UnlockOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Dropdown,
  Flex,
  Input,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  type TableColumnsType,
} from 'antd'
import { useState } from 'react'
import { adminApi, adminKeys, useDepartments, useUsers, type UserListItem, type UserQuery } from '../../../api/admin'
import { useAuth } from '../../../auth/authContext'
import { Permissions } from '../../../auth/permissions'
import { PageHeader } from '../../../components/PageHeader'
import { formatDateTime } from '../../../utils/format'
import { errorMessage } from '../../../utils/formErrors'
import { ResetPasswordModal } from './ResetPasswordModal'
import { UserFormModal } from './UserFormModal'

function UserStatus({ user }: { user: UserListItem }) {
  if (!user.isActive) {
    return <Tag color="red">Đã khóa</Tag>
  }
  if (user.isLockedOut) {
    return <Tag color="orange">Tạm khóa</Tag>
  }
  return <Tag color="green">Hoạt động</Tag>
}

/** Màn hình 16: quản lý người dùng. */
export function UsersPage() {
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.UsersManage)
  const [query, setQuery] = useState<UserQuery>({ page: 1, pageSize: 20 })
  const { data, isPending, isFetching, isError, error } = useUsers(query)
  const departments = useDepartments(true)
  const [editingUserId, setEditingUserId] = useState<string | 'new' | null>(null)
  const [resettingUser, setResettingUser] = useState<UserListItem | null>(null)
  const queryClient = useQueryClient()
  const { message, modal } = App.useApp()

  const setLock = useMutation({
    mutationFn: ({ user, lock }: { user: UserListItem; lock: boolean }) =>
      lock ? adminApi.lockUser(user.id) : adminApi.unlockUser(user.id),
    onSuccess: async (_, { lock }) => {
      message.success(lock ? 'Đã khóa tài khoản.' : 'Đã mở khóa tài khoản.')
      await queryClient.invalidateQueries({ queryKey: adminKeys.allUsers })
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const confirmLock = (user: UserListItem, lock: boolean) =>
    modal.confirm({
      title: lock ? `Khóa tài khoản ${user.userName}?` : `Mở khóa tài khoản ${user.userName}?`,
      content: lock
        ? 'Người dùng sẽ bị đăng xuất khỏi mọi thiết bị và không đăng nhập được cho tới khi được mở khóa.'
        : 'Người dùng có thể đăng nhập lại ngay.',
      okText: lock ? 'Khóa' : 'Mở khóa',
      okButtonProps: { danger: lock },
      cancelText: 'Hủy',
      onOk: () => setLock.mutateAsync({ user, lock }),
    })

  const columns: TableColumnsType<UserListItem> = [
    {
      title: 'Người dùng',
      key: 'user',
      width: 220,
      fixed: 'left',
      render: (_, user) => (
        <Flex vertical>
          <Typography.Text strong>{user.fullName}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12, whiteSpace: 'nowrap' }}>
            {user.userName}
          </Typography.Text>
        </Flex>
      ),
    },
    { title: 'Chức danh', dataIndex: 'title', width: 150, render: (value: string | null) => value ?? '—' },
    { title: 'Khoa/phòng', dataIndex: 'departmentName', width: 180, render: (value: string | null) => value ?? '—' },
    {
      title: 'Vai trò',
      dataIndex: 'roleNames',
      width: 180,
      render: (names: string[]) => (
        <Space size={[4, 4]} wrap>
          {names.map((name) => (
            <Tag key={name} color="blue">
              {name}
            </Tag>
          ))}
        </Space>
      ),
    },
    { title: 'Trạng thái', key: 'status', width: 120, render: (_, user) => <UserStatus user={user} /> },
    {
      title: 'Đăng nhập gần nhất',
      dataIndex: 'lastLoginAtUtc',
      width: 170,
      render: (value: string | null) => <span style={{ whiteSpace: 'nowrap' }}>{formatDateTime(value)}</span>,
    },
  ]

  if (canManage) {
    columns.push({
      title: 'Thao tác',
      key: 'actions',
      width: 130,
      fixed: 'right',
      render: (_, user) => (
        <Space size={4}>
          <Button size="small" icon={<EditOutlined />} onClick={() => setEditingUserId(user.id)}>
            Sửa
          </Button>
          <Dropdown
            trigger={['click']}
            menu={{
              items: [
                ...(user.isActive
                  ? [{ key: 'lock', icon: <LockOutlined />, label: 'Khóa tài khoản', danger: true }]
                  : []),
                ...(!user.isActive || user.isLockedOut
                  ? [{ key: 'unlock', icon: <UnlockOutlined />, label: 'Mở khóa' }]
                  : []),
                { key: 'reset', icon: <KeyOutlined />, label: 'Đặt lại mật khẩu' },
              ],
              onClick: ({ key }) => {
                if (key === 'reset') {
                  setResettingUser(user)
                } else {
                  confirmLock(user, key === 'lock')
                }
              },
            }}
          >
            <Button size="small" icon={<MoreOutlined />} aria-label={`Thao tác khác cho ${user.userName}`} />
          </Dropdown>
        </Space>
      ),
    })
  }

  return (
    <>
      <PageHeader
        title="Người dùng"
        extra={
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditingUserId('new')}>
              Thêm người dùng
            </Button>
          )
        }
      />
      <Card>
        <Flex wrap gap={12} style={{ marginBottom: 16 }}>
          <Input.Search
            placeholder="Tìm theo họ tên, tên đăng nhập, chức danh"
            allowClear
            style={{ width: 320, maxWidth: '100%' }}
            onSearch={(keyword) => setQuery((q) => ({ ...q, keyword: keyword.trim() || undefined, page: 1 }))}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Khoa/phòng"
            style={{ width: 240, maxWidth: '100%' }}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(departmentId?: string) => setQuery((q) => ({ ...q, departmentId, page: 1 }))}
          />
          <Select
            allowClear
            placeholder="Trạng thái"
            style={{ width: 180, maxWidth: '100%' }}
            options={[
              { value: 'active', label: 'Đang hoạt động' },
              { value: 'locked', label: 'Đã khóa' },
            ]}
            onChange={(status?: string) =>
              setQuery((q) => ({ ...q, isActive: status === undefined ? undefined : status === 'active', page: 1 }))
            }
          />
        </Flex>
        {isError && <Alert type="error" showIcon title={errorMessage(error)} style={{ marginBottom: 16 }} />}
        <Table<UserListItem>
          rowKey="id"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={isPending || isFetching}
          scroll={{ x: 1150 }}
          locale={{ emptyText: 'Không có người dùng phù hợp.' }}
          pagination={{
            current: query.page,
            pageSize: query.pageSize,
            total: data?.totalCount ?? 0,
            showSizeChanger: true,
            showTotal: (total) => `Tổng ${total} người dùng`,
            onChange: (page, pageSize) => setQuery((q) => ({ ...q, page, pageSize })),
          }}
        />
      </Card>
      {editingUserId !== null && (
        <UserFormModal userId={editingUserId === 'new' ? null : editingUserId} onClose={() => setEditingUserId(null)} />
      )}
      {resettingUser && <ResetPasswordModal user={resettingUser} onClose={() => setResettingUser(null)} />}
    </>
  )
}
