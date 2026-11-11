import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Col, Flex, List, Row, Space, Spin, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import {
  adminApi,
  adminKeys,
  usePermissionCatalog,
  useRoles,
  type PermissionDefinition,
  type Role,
} from '../../../api/admin'
import { PageHeader } from '../../../components/PageHeader'
import { errorMessage } from '../../../utils/formErrors'

function groupPermissions(catalog: PermissionDefinition[]): [string, PermissionDefinition[]][] {
  const groups = new Map<string, PermissionDefinition[]>()
  for (const permission of catalog) {
    groups.set(permission.group, [...(groups.get(permission.group) ?? []), permission])
  }
  return [...groups.entries()]
}

function RolePermissionsEditor({ role, catalog }: { role: Role; catalog: PermissionDefinition[] }) {
  const [selected, setSelected] = useState(() => new Set(role.permissions))
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const groups = useMemo(() => groupPermissions(catalog), [catalog])
  const dirty = selected.size !== role.permissions.length || role.permissions.some((p) => !selected.has(p))

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
      title={role.name}
      extra={
        !role.isAdministrator && (
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
      {role.isAdministrator && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message="Vai trò Quản trị hệ thống luôn có toàn bộ quyền và không thể thay đổi."
        />
      )}
      {groups.map(([group, permissions]) => (
        <section key={group} style={{ marginBottom: 16 }}>
          <Typography.Title level={5} style={{ marginTop: 0 }}>
            {group}
          </Typography.Title>
          <Row gutter={[16, 8]}>
            {permissions.map((permission) => (
              <Col xs={24} sm={12} key={permission.code}>
                <Checkbox
                  checked={selected.has(permission.code)}
                  disabled={role.isAdministrator}
                  onChange={(e) => toggle(permission.code, e.target.checked)}
                >
                  {permission.name}{' '}
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    ({permission.code})
                  </Typography.Text>
                </Checkbox>
              </Col>
            ))}
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

  if (roles.isPending || catalog.isPending) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }

  if (roles.isError || catalog.isError) {
    return <Alert type="error" showIcon message={errorMessage(roles.error ?? catalog.error)} />
  }

  const selectedRole = roles.data.find((r) => r.id === selectedId) ?? roles.data[0]

  return (
    <>
      <PageHeader title="Vai trò và quyền" />
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={8}>
          <Card title="Vai trò" styles={{ body: { padding: 0 } }}>
            <List<Role>
              dataSource={roles.data}
              renderItem={(role) => (
                <List.Item
                  onClick={() => setSelectedId(role.id)}
                  style={{
                    cursor: 'pointer',
                    padding: '12px 16px',
                    background: role.id === selectedRole?.id ? '#e6f4ff' : undefined,
                  }}
                  aria-current={role.id === selectedRole?.id}
                >
                  <Flex vertical style={{ width: '100%' }}>
                    <Flex justify="space-between" align="center" gap={8}>
                      <Typography.Text strong>{role.name}</Typography.Text>
                      <Tag>{role.isAdministrator ? 'Toàn quyền' : `${role.permissions.length} quyền`}</Tag>
                    </Flex>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {role.code}
                    </Typography.Text>
                  </Flex>
                </List.Item>
              )}
            />
          </Card>
        </Col>
        <Col xs={24} lg={16}>
          {selectedRole && <RolePermissionsEditor key={selectedRole.id} role={selectedRole} catalog={catalog.data} />}
        </Col>
      </Row>
    </>
  )
}
