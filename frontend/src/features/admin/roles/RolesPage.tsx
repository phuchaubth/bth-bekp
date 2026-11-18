import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Col, Flex, Menu, Popconfirm, Row, Space, Spin, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import {
  adminApi,
  adminKeys,
  usePermissionCatalog,
  useRoles,
  type PermissionDefinition,
  type Role,
} from '../../../api/admin'
import { Permissions } from '../../../auth/permissions'
import { PageHeader } from '../../../components/PageHeader'
import { errorMessage } from '../../../utils/formErrors'
import { RoleFormModal } from './RoleFormModal'

function groupPermissions(catalog: PermissionDefinition[]): [string, PermissionDefinition[]][] {
  const groups = new Map<string, PermissionDefinition[]>()
  for (const permission of catalog) {
    groups.set(permission.group, [...(groups.get(permission.group) ?? []), permission])
  }
  return [...groups.entries()]
}

interface EditorProps {
  role: Role
  catalog: PermissionDefinition[]
  onEdit: () => void
}

function RolePermissionsEditor({ role, catalog, onEdit }: EditorProps) {
  const [selected, setSelected] = useState(() => new Set(role.permissions))
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const groups = useMemo(() => groupPermissions(catalog), [catalog])
  const dirty = selected.size !== role.permissions.length || role.permissions.some((p) => !selected.has(p))
  const locked = role.isAdministrator || !!role.isLocked

  const save = useMutation({
    mutationFn: () => adminApi.updateRolePermissions(role.id, [...selected]),
    onSuccess: async () => {
      message.success(
        `Đã cập nhật quyền cho vai trò "${role.name}". Người dùng nhận quyền mới khi phiên được làm mới (tối đa 15 phút).`,
      )
      await queryClient.invalidateQueries({ queryKey: adminKeys.roles })
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  // Quyết định 40: chỉ xóa được vai trò tự tạo; máy chủ chặn khi vai trò còn gán cho tài khoản hay đang dùng trong mẫu phiếu.
  // Xóa xong, trang tự chọn vai trò đầu danh sách (vai trò đang chọn không còn trong danh sách).
  const remove = useMutation({
    mutationFn: () => adminApi.deleteRole(role.id),
    onSuccess: async () => {
      message.success(`Đã xóa vai trò "${role.name}".`)
      await queryClient.invalidateQueries({ queryKey: adminKeys.roles })
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const toggle = (code: string, checked: boolean) =>
    setSelected((current) => {
      const next = new Set(current)
      if (checked) {
        next.add(code)
      } else {
        next.delete(code)
      }
      return next
    })

  return (
    <Card
      title={
        <Space wrap>
          {role.name}
          <Tag color={role.isSystem ? undefined : 'blue'}>{role.isSystem ? 'Có sẵn' : 'Tự tạo'}</Tag>
        </Space>
      }
      extra={
        !locked && (
          <Space wrap>
            <Button disabled={!dirty || save.isPending} onClick={() => setSelected(new Set(role.permissions))}>
              Hủy thay đổi
            </Button>
            <Button type="primary" disabled={!dirty} loading={save.isPending} onClick={() => save.mutate()}>
              Lưu thay đổi
            </Button>
          </Space>
        )
      }
    >
      {role.description && <Typography.Paragraph type="secondary">{role.description}</Typography.Paragraph>}
      <Flex wrap gap={8} style={{ marginBottom: 16 }}>
        <Button icon={<EditOutlined />} onClick={onEdit}>
          Sửa tên, mô tả
        </Button>
        {!role.isSystem && (
          <Popconfirm
            title={`Xóa vai trò "${role.name}"?`}
            description="Chỉ xóa được vai trò không còn gán cho tài khoản nào và không dùng trong cấu hình ký của mẫu phiếu."
            okText="Xóa"
            cancelText="Hủy"
            okButtonProps={{ danger: true }}
            onConfirm={() => remove.mutate()}
          >
            <Button danger icon={<DeleteOutlined />} loading={remove.isPending}>
              Xóa vai trò
            </Button>
          </Popconfirm>
        )}
      </Flex>
      {role.isAdministrator && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title="Vai trò Quản trị hệ thống luôn có toàn bộ quyền và không thể thay đổi."
        />
      )}
      {locked && !role.isAdministrator && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title="Vai trò này có quyền cố định và không thể thay đổi (máy kiosk đặt ở nơi công cộng chỉ dùng được màn hình kiosk)."
        />
      )}
      {groups.map(([group, permissions]) => (
        <section key={group} style={{ marginBottom: 16 }}>
          <Typography.Title level={5} style={{ marginTop: 0 }}>
            {group}
          </Typography.Title>
          <Row gutter={[16, 8]}>
            {permissions.map((permission) => {
              // Quyền dùng kiosk chỉ thuộc vai trò Kiosk (quyết định 24): vai trò khác không đánh dấu được, chỉ bỏ dấu được.
              const kioskOnly = !locked && permission.code === Permissions.KioskUse
              return (
                <Col xs={24} sm={12} key={permission.code}>
                  <Checkbox
                    checked={selected.has(permission.code)}
                    disabled={locked || (kioskOnly && !selected.has(permission.code))}
                    onChange={(e) => toggle(permission.code, e.target.checked)}
                  >
                    {permission.name}{' '}
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      ({permission.code}
                      {kioskOnly && ', chỉ dành cho vai trò Kiosk'})
                    </Typography.Text>
                  </Checkbox>
                </Col>
              )
            })}
          </Row>
        </section>
      ))}
    </Card>
  )
}

/** Màn hình 17: vai trò và ma trận quyền. */
export function RolesPage() {
  const roles = useRoles()
  const catalog = usePermissionCatalog()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [editing, setEditing] = useState<Role | 'new' | null>(null)

  if (roles.isPending || catalog.isPending) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }

  if (roles.isError || catalog.isError) {
    return <Alert type="error" showIcon title={errorMessage(roles.error ?? catalog.error)} />
  }

  const selectedRole = roles.data.find((r) => r.id === selectedId) ?? roles.data[0]

  return (
    <>
      <PageHeader
        title="Vai trò và quyền"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>
            Thêm vai trò
          </Button>
        }
      />
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={8}>
          <Card title="Vai trò" styles={{ body: { padding: 8 } }}>
            {/* Menu: chọn được bằng bàn phím (phím mũi tên, Enter) và đánh dấu vai trò đang chọn. */}
            <Menu
              mode="inline"
              aria-label="Danh sách vai trò"
              selectedKeys={selectedRole ? [selectedRole.id] : []}
              onClick={({ key }) => setSelectedId(key)}
              style={{ borderInlineEnd: 'none' }}
              items={roles.data.map((role) => ({
                key: role.id,
                style: { height: 'auto', lineHeight: 1.5, paddingBlock: 8 },
                label: (
                  <Flex vertical>
                    <Flex justify="space-between" align="center" gap={8}>
                      <Typography.Text strong>{role.name}</Typography.Text>
                      <Tag style={{ marginInlineEnd: 0 }}>
                        {role.isAdministrator ? 'Toàn quyền' : `${role.permissions.length} quyền`}
                      </Tag>
                    </Flex>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {role.code}
                    </Typography.Text>
                  </Flex>
                ),
              }))}
            />
          </Card>
        </Col>
        <Col xs={24} lg={16}>
          {selectedRole && (
            <RolePermissionsEditor
              key={selectedRole.id}
              role={selectedRole}
              catalog={catalog.data}
              onEdit={() => setEditing(selectedRole)}
            />
          )}
        </Col>
      </Row>
      {editing && (
        <RoleFormModal
          role={editing === 'new' ? null : editing}
          roles={roles.data}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            setSelectedId(saved.id)
            setEditing(null)
          }}
        />
      )}
    </>
  )
}
