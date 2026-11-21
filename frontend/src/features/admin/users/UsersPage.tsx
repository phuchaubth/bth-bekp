import {
  DeleteOutlined,
  EditOutlined,
  KeyOutlined,
  MoreOutlined,
  PlusOutlined,
  StopOutlined,
  UndoOutlined,
  UnlockOutlined,
} from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Dropdown, Flex, Input, Select, Space, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { adminApi, adminKeys, useDepartments, useUsers, type UserListItem } from '../../../api/admin'
import { useAuth } from '../../../auth/authContext'
import { Permissions } from '../../../auth/permissions'
import { PageHeader } from '../../../components/PageHeader'
import { formatDateTime } from '../../../utils/format'
import { errorMessage } from '../../../utils/formErrors'
import { ResetPasswordModal } from './ResetPasswordModal'
import { UserFormModal } from './UserFormModal'

/** Thẻ của trang (quyết định 51): tài khoản đang sử dụng và tài khoản đã ngưng sử dụng. */
type UserTab = 'active' | 'inactive'

/** Ngưng sử dụng, sử dụng lại tài khoản đã ngưng, mở khóa tạm thời (đăng nhập sai, tra cứu sai trên kiosk), xóa. */
type UserAction = 'stop' | 'reuse' | 'unlock' | 'delete'

const DONE: Record<UserAction, string> = {
  stop: 'Đã ngưng sử dụng tài khoản.',
  reuse: 'Đã sử dụng lại tài khoản.',
  unlock: 'Đã mở khóa tài khoản.',
  delete: 'Đã xóa tài khoản.',
}

function UserStatus({ user }: { user: UserListItem }) {
  if (!user.isActive) {
    return <Tag color="red">Ngưng sử dụng</Tag>
  }
  if (user.isLockedOut) {
    return <Tag color="orange">Tạm khóa</Tag>
  }
  if (user.isKioskLookupLocked) {
    return <Tag color="orange">Tạm khóa tra cứu</Tag>
  }
  return <Tag color="green">Hoạt động</Tag>
}

/** Màn hình 16: quản lý người dùng; tài khoản ngưng sử dụng ở thẻ riêng, xóa được ở đó (quyết định 51). */
export function UsersPage() {
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.UsersManage)
  const [tab, setTab] = useState<UserTab>('active')
  const [filters, setFilters] = useState<{ keyword?: string; departmentId?: string }>({})
  const [paging, setPaging] = useState<Record<UserTab, { page: number; pageSize: number }>>({
    active: { page: 1, pageSize: 20 },
    inactive: { page: 1, pageSize: 20 },
  })
  const activeUsers = useUsers({ ...filters, ...paging.active, isActive: true })
  const inactiveUsers = useUsers({ ...filters, ...paging.inactive, isActive: false })
  const { data, isPending, isFetching, isError, error } = tab === 'active' ? activeUsers : inactiveUsers
  const departments = useDepartments(true)
  const [editingUserId, setEditingUserId] = useState<string | 'new' | null>(null)
  const [resettingUser, setResettingUser] = useState<UserListItem | null>(null)
  const queryClient = useQueryClient()
  const { message, modal } = App.useApp()

  const change = useMutation({
    mutationFn: ({ user, action }: { user: UserListItem; action: UserAction }) =>
      action === 'stop'
        ? adminApi.lockUser(user.id)
        : action === 'delete'
          ? adminApi.deleteUser(user.id)
          : adminApi.unlockUser(user.id),
    onSuccess: (_, { action }) => {
      message.success(DONE[action])
    },
    onError: (e) => {
      message.error(errorMessage(e))
    },
    // Tải lại cả khi máy chủ từ chối: trạng thái có thể vừa đổi (ví dụ quản trị khác vừa sử dụng lại tài khoản).
    onSettled: () => queryClient.invalidateQueries({ queryKey: adminKeys.allUsers }),
  })

  const confirm = (user: UserListItem, action: UserAction) => {
    const texts: Record<UserAction, { title: string; content: string; okText: string }> = {
      stop: {
        title: `Ngưng sử dụng tài khoản ${user.userName}?`,
        content:
          'Người dùng sẽ bị đăng xuất khỏi mọi thiết bị và không đăng nhập được. Tài khoản chuyển sang thẻ Ngưng sử dụng, sử dụng lại được khi cần.',
        okText: 'Ngưng sử dụng',
      },
      reuse: {
        title: `Sử dụng lại tài khoản ${user.userName}?`,
        content: 'Người dùng đăng nhập lại được ngay với mật khẩu hiện tại.',
        okText: 'Sử dụng lại',
      },
      unlock: {
        title: `Mở khóa tài khoản ${user.userName}?`,
        content:
          user.isKioskLookupLocked && !user.isLockedOut
            ? 'Máy kiosk tra cứu lại được ngay (người bệnh nhập mã hoặc CCCD kèm ngày sinh).'
            : 'Người dùng có thể đăng nhập lại ngay.',
        okText: 'Mở khóa',
      },
      delete: {
        title: `Xóa tài khoản ${user.userName}?`,
        content:
          'Tài khoản bị xóa khỏi danh sách, không đăng nhập và không sử dụng lại được; tên đăng nhập này không tạo lại được. Phiếu, báo cáo, nhật ký đã có vẫn giữ tên người này. Không hoàn tác được.',
        okText: 'Xóa',
      },
    }
    modal.confirm({
      ...texts[action],
      okButtonProps: { danger: action === 'stop' || action === 'delete' },
      cancelText: 'Hủy',
      // Lỗi đã báo ở onError; đóng hộp xác nhận cả khi máy chủ từ chối.
      onOk: () => change.mutateAsync({ user, action }).catch(() => undefined),
    })
  }

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
    {
      title: 'Khoa/phòng',
      key: 'department',
      width: 200,
      render: (_, user) => (
        <Flex vertical>
          <span>{user.departmentName ?? '—'}</span>
          {user.additionalDepartmentNames && user.additionalDepartmentNames.length > 0 && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Khoa khác: {user.additionalDepartmentNames.join(', ')}
            </Typography.Text>
          )}
        </Flex>
      ),
    },
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
    ...(tab === 'active'
      ? [{ title: 'Trạng thái', key: 'status', width: 120, render: (_: unknown, user: UserListItem) => <UserStatus user={user} /> }]
      : []),
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
      width: tab === 'active' ? 130 : 260,
      fixed: 'right',
      render: (_, user) =>
        tab === 'active' ? (
          <Space size={4}>
            <Button size="small" icon={<EditOutlined />} onClick={() => setEditingUserId(user.id)}>
              Sửa
            </Button>
            <Dropdown
              trigger={['click']}
              menu={{
                items: [
                  { key: 'stop', icon: <StopOutlined />, label: 'Ngưng sử dụng', danger: true },
                  ...(user.isLockedOut || user.isKioskLookupLocked
                    ? [{ key: 'unlock', icon: <UnlockOutlined />, label: 'Mở khóa' }]
                    : []),
                  { key: 'reset', icon: <KeyOutlined />, label: 'Đặt lại mật khẩu' },
                ],
                onClick: ({ key }) => {
                  if (key === 'reset') {
                    setResettingUser(user)
                  } else {
                    confirm(user, key as UserAction)
                  }
                },
              }}
            >
              <Button size="small" icon={<MoreOutlined />} aria-label={`Thao tác khác cho ${user.userName}`} />
            </Dropdown>
          </Space>
        ) : (
          <Space size={4}>
            <Button size="small" icon={<EditOutlined />} onClick={() => setEditingUserId(user.id)}>
              Sửa
            </Button>
            <Button size="small" icon={<UndoOutlined />} onClick={() => confirm(user, 'reuse')}>
              Sử dụng lại
            </Button>
            <Button size="small" danger icon={<DeleteOutlined />} onClick={() => confirm(user, 'delete')}>
              Xóa
            </Button>
          </Space>
        ),
    })
  }

  const filter = (patch: { keyword?: string; departmentId?: string }) => {
    setFilters((f) => ({ ...f, ...patch }))
    setPaging((p) => ({ active: { ...p.active, page: 1 }, inactive: { ...p.inactive, page: 1 } }))
  }
  const count = (total: number | undefined) => (total === undefined ? '' : ` (${total})`)

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
      <Card
        activeTabKey={tab}
        onTabChange={(key) => setTab(key as UserTab)}
        tabList={[
          { key: 'active', label: `Đang sử dụng${count(activeUsers.data?.totalCount)}` },
          { key: 'inactive', label: `Ngưng sử dụng${count(inactiveUsers.data?.totalCount)}` },
        ]}
      >
        {tab === 'inactive' && (
          <Typography.Paragraph type="secondary">
            Tài khoản ngưng sử dụng không đăng nhập được. Bấm <strong>Sử dụng lại</strong> khi người dùng quay lại làm việc;{' '}
            <strong>Xóa</strong> khi không còn dùng nữa. Phiếu, báo cáo, nhật ký đã có vẫn giữ tên người này.
          </Typography.Paragraph>
        )}
        <Flex wrap gap={12} style={{ marginBottom: 16 }}>
          <Input.Search
            placeholder="Tìm theo họ tên, tên đăng nhập, chức danh"
            allowClear
            style={{ width: 320, maxWidth: '100%' }}
            onSearch={(keyword) => filter({ keyword: keyword.trim() || undefined })}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Khoa/phòng"
            style={{ width: 240, maxWidth: '100%' }}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
            onChange={(departmentId?: string) => filter({ departmentId })}
          />
        </Flex>
        {isError && <Alert type="error" showIcon title={errorMessage(error)} style={{ marginBottom: 16 }} />}
        <Table<UserListItem>
          rowKey="id"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={isPending || isFetching}
          scroll={{ x: 1150 }}
          locale={{ emptyText: tab === 'active' ? 'Không có người dùng phù hợp.' : 'Không có tài khoản ngưng sử dụng.' }}
          pagination={{
            current: paging[tab].page,
            pageSize: paging[tab].pageSize,
            total: data?.totalCount ?? 0,
            showSizeChanger: true,
            showTotal: (total) => `Tổng ${total} người dùng`,
            onChange: (page, pageSize) => setPaging((p) => ({ ...p, [tab]: { page, pageSize } })),
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
