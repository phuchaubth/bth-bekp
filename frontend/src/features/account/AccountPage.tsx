import { Card, Col, Descriptions, Row, Space, Tag, Typography } from 'antd'
import { accountDepartments } from '../../api/auth'
import { useAuth } from '../../auth/authContext'
import { ChangePasswordForm } from './ChangePasswordForm'
import { SignatureSpecimenCard } from './SignatureSpecimen'

export function AccountPage() {
  const { user } = useAuth()
  if (!user) {
    return null
  }

  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Tài khoản của tôi
      </Typography.Title>
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title="Thông tin tài khoản">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="Họ tên">{user.fullName}</Descriptions.Item>
              <Descriptions.Item label="Tên đăng nhập">{user.userName}</Descriptions.Item>
              <Descriptions.Item label="Chức danh">{user.title ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="Khoa/phòng chính">{user.departmentName ?? '—'}</Descriptions.Item>
              {accountDepartments(user).length > 1 && (
                <Descriptions.Item label="Khoa/phòng khác">
                  {accountDepartments(user)
                    .slice(1)
                    .map((d) => d.name)
                    .join(', ')}
                </Descriptions.Item>
              )}
              <Descriptions.Item label="Vai trò">
                <Space size={[4, 4]} wrap>
                  {user.roles.map((role) => (
                    <Tag key={role.id} color="blue">
                      {role.name}
                    </Tag>
                  ))}
                </Space>
              </Descriptions.Item>
            </Descriptions>
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Chữ ký mẫu">
            <SignatureSpecimenCard />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Đổi mật khẩu">
            <ChangePasswordForm />
          </Card>
        </Col>
      </Row>
    </>
  )
}
