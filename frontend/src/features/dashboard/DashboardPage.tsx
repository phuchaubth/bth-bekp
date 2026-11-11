import { Card, Col, Row, Statistic, Typography } from 'antd'
import { SystemStatus } from './SystemStatus'

const statusCards = [
  { title: 'Phiếu nháp', color: '#8c96a3' },
  { title: 'Chờ ký', color: '#d48806' },
  { title: 'Hoàn tất hôm nay', color: '#389e0d' },
  { title: 'Đã thu hồi', color: '#cf1322' },
]

export function DashboardPage() {
  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Tổng quan
      </Typography.Title>
      <Row gutter={[16, 16]}>
        {statusCards.map((card) => (
          <Col key={card.title} xs={12} md={6}>
            <Card size="small">
              <Statistic title={card.title} value={0} styles={{ content: { color: card.color } }} />
            </Card>
          </Col>
        ))}
        <Col xs={24}>
          <SystemStatus />
        </Col>
      </Row>
      <Typography.Paragraph type="secondary" style={{ marginTop: 16 }}>
        Số liệu thống kê sẽ được kết nối với dữ liệu thật từ Sprint 3.
      </Typography.Paragraph>
    </>
  )
}
