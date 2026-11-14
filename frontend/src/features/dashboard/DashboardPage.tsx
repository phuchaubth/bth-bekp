import { Alert, Card, Col, Empty, Flex, Row, Statistic, Typography } from 'antd'
import dayjs from 'dayjs'
import { Link } from 'react-router'
import { useDashboard, type DashboardFormItem } from '../../api/reports'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { BarChart } from '../../components/BarChart'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { FormStatusTag } from '../consents/FormStatusTag'
import { SystemStatus } from './SystemStatus'

/** Màn hình 2: số liệu trong phạm vi khoa của người dùng, việc chờ xử lý và biểu đồ 14 ngày gần nhất. */
export function DashboardPage() {
  const { user, hasPermission } = useAuth()
  const canSeeForms = hasPermission(Permissions.ConsentsRead)
  const dashboard = useDashboard(canSeeForms)
  const data = dashboard.data

  const statusCards = [
    { title: 'Phiếu nháp', value: data?.drafts, color: '#8c96a3' },
    { title: 'Chờ ký', value: data?.pendingSignature, color: '#d48806' },
    { title: 'Hoàn tất hôm nay', value: data?.completedToday, color: '#389e0d' },
    { title: 'Thu hồi 30 ngày qua', value: data?.revokedLast30Days, color: '#cf1322' },
  ]

  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Tổng quan
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        Xin chào {user?.fullName}
        {user?.departmentName ? ` – ${user.departmentName}` : ''}.
      </Typography.Paragraph>
      {dashboard.isError && <Alert type="error" showIcon title={errorMessage(dashboard.error)} style={{ marginBottom: 16 }} />}
      <Row gutter={[16, 16]}>
        {canSeeForms &&
          statusCards.map((card) => (
            <Col key={card.title} xs={12} md={6}>
              <Card size="small">
                <Statistic title={card.title} value={card.value ?? '—'} loading={dashboard.isPending} styles={{ content: { color: card.color } }} />
              </Card>
            </Col>
          ))}
        {data && (
          <>
            <Col xs={24} lg={12}>
              <FormList
                title="Chờ tôi xác nhận"
                empty="Không có phiếu nào chờ bạn xác nhận."
                total={data.awaitingMyConfirmation.total}
                items={data.awaitingMyConfirmation.items}
                when="Người bệnh ký lúc"
              />
            </Col>
            <Col xs={24} lg={12}>
              <FormList
                title="Phiếu tôi lập chưa hoàn tất"
                empty="Bạn không có phiếu nháp hay chờ ký nào."
                total={data.myOpenForms.total}
                items={data.myOpenForms.items}
                when="Cập nhật lúc"
              />
            </Col>
            <Col xs={24}>
              <Card size="small" title="14 ngày gần nhất">
                <BarChart
                  title="Số phiếu lập và hoàn tất theo ngày trong 14 ngày gần nhất"
                  labels={data.last14Days.map((d) => dayjs(d.date).format('DD/MM'))}
                  series={[
                    { name: 'Phiếu lập', color: '#0d7c74', values: data.last14Days.map((d) => d.created) },
                    { name: 'Hoàn tất', color: '#d9822b', values: data.last14Days.map((d) => d.completed) },
                  ]}
                />
              </Card>
            </Col>
          </>
        )}
        <Col xs={24}>
          <SystemStatus />
        </Col>
      </Row>
    </>
  )
}

function FormList({
  title,
  empty,
  total,
  items,
  when,
}: {
  title: string
  empty: string
  total: number
  items: DashboardFormItem[]
  when: string
}) {
  return (
    <Card size="small" title={`${title}${total > 0 ? ` (${total})` : ''}`}>
      {items.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={empty} />
      ) : (
        <Flex vertical gap={10} component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Typography.Text>
                <Link to={`/phieu-dong-thuan/${item.id}`}>{item.formNo}</Link> · {item.patientName} <FormStatusTag status={item.status} />
              </Typography.Text>
              <br />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {item.templateCode} – {item.templateName} · {item.departmentName} · {when} {formatDateTime(item.atUtc)}
              </Typography.Text>
            </li>
          ))}
        </Flex>
      )}
      {total > items.length && (
        <Typography.Paragraph style={{ marginTop: 12, marginBottom: 0 }}>
          <Link to="/phieu-dong-thuan">Xem tất cả {total} phiếu trong danh sách</Link>
        </Typography.Paragraph>
      )}
    </Card>
  )
}
