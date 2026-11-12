import { Alert, Card, Col, DatePicker, Flex, Progress, Row, Select, Statistic, Table, Typography, type TableColumnsType } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { useDepartments } from '../../api/admin'
import { useConsentTemplates } from '../../api/consentTemplates'
import { useReportSummary, type GroupStats, type ReportQuery } from '../../api/reports'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { BarChart } from '../../components/BarChart'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDate } from '../../utils/format'

const percent = (value: number | null) => (value === null ? '—' : `${(value * 100).toFixed(1)}%`)
const minutes = (value: number | null) => (value === null ? '—' : value >= 120 ? `${(value / 60).toFixed(1)} giờ` : `${value} phút`)

/** Màn hình 14: thống kê phiếu theo khoa, mẫu, trạng thái, tỉ lệ từ chối, người ký, thời gian hoàn tất, theo ngày. */
export function ReportsPage() {
  const { hasPermission } = useAuth()
  const scopeAll = hasPermission(Permissions.ScopeAllDepartments)
  const [query, setQuery] = useState<ReportQuery>({})
  const report = useReportSummary(query)
  const templates = useConsentTemplates(true, hasPermission(Permissions.TemplatesRead))
  const departments = useDepartments(false)
  const data = report.data

  const groupColumns: TableColumnsType<GroupStats> = [
    { title: 'Mã', dataIndex: 'code', width: 90 },
    { title: 'Tên', dataIndex: 'name' },
    { title: 'Tổng', dataIndex: ['counts', 'total'], align: 'right', width: 80 },
    { title: 'Nháp', dataIndex: ['counts', 'draft'], align: 'right', width: 80 },
    { title: 'Chờ ký', dataIndex: ['counts', 'pendingSignature'], align: 'right', width: 90 },
    { title: 'Hoàn tất', dataIndex: ['counts', 'completed'], align: 'right', width: 90 },
    { title: 'Đã hủy', dataIndex: ['counts', 'cancelled'], align: 'right', width: 80 },
    { title: 'Thu hồi', dataIndex: ['counts', 'revoked'], align: 'right', width: 80 },
    { title: 'Từ chối', dataIndex: ['decisions', 'refused'], align: 'right', width: 80 },
    { title: 'Tỉ lệ từ chối', key: 'rate', align: 'right', width: 110, render: (_, g) => percent(g.decisions.refusalRate) },
  ]

  return (
    <>
      <PageHeader title="Báo cáo thống kê" />
      <Card style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap>
          <DatePicker.RangePicker
            format="DD/MM/YYYY"
            placeholder={['Lập từ ngày', 'Đến ngày']}
            value={data ? [dayjs(data.from), dayjs(data.to)] : undefined}
            allowClear={false}
            onChange={(range) => {
              const [from, to] = (range ?? []) as (Dayjs | null)[]
              setQuery((q) => ({ ...q, from: from?.format('YYYY-MM-DD'), to: to?.format('YYYY-MM-DD') }))
            }}
          />
          {templates.data && (
            <Select
              allowClear
              placeholder="Mẫu phiếu (tất cả)"
              aria-label="Mẫu phiếu"
              options={templates.data.map((t) => ({ value: t.id, label: `${t.code} – ${t.name}` }))}
              onChange={(templateId?: string) => setQuery((q) => ({ ...q, templateId }))}
              style={{ width: 300, maxWidth: '100%' }}
              popupMatchSelectWidth={false}
            />
          )}
          {scopeAll && departments.data && (
            <Select
              allowClear
              placeholder="Khoa/phòng (toàn viện)"
              aria-label="Khoa/phòng"
              options={departments.data.map((d) => ({ value: d.id, label: d.name }))}
              onChange={(departmentId?: string) => setQuery((q) => ({ ...q, departmentId }))}
              style={{ width: 240 }}
            />
          )}
        </Flex>
        {!scopeAll && (
          <Typography.Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            Số liệu trong phạm vi khoa của bạn.
          </Typography.Text>
        )}
      </Card>
      {report.isError && <Alert type="error" showIcon title={errorMessage(report.error)} style={{ marginBottom: 16 }} />}
      {data && (
        <Row gutter={[16, 16]}>
          <Col xs={24}>
            <Typography.Text type="secondary">
              Phiếu lập từ {formatDate(data.from)} đến {formatDate(data.to)} (theo trạng thái hiện tại).
            </Typography.Text>
          </Col>
          {[
            ['Tổng số phiếu', data.totals.total, undefined],
            ['Hoàn tất', data.totals.completed, '#389e0d'],
            ['Chờ ký', data.totals.pendingSignature, '#d48806'],
            ['Nháp', data.totals.draft, '#8c96a3'],
            ['Đã hủy', data.totals.cancelled, '#cf1322'],
            ['Đã thu hồi', data.totals.revoked, '#d4380d'],
          ].map(([title, value, color]) => (
            <Col key={title as string} xs={12} md={8} xl={4}>
              <Card size="small">
                <Statistic title={title as string} value={value as number} styles={{ content: { color: color as string | undefined } }} />
              </Card>
            </Col>
          ))}
          <Col xs={24} md={12} xl={8}>
            <Card size="small" title="Ý kiến của người bệnh (phiếu hoàn tất)">
              <Flex gap={24} wrap>
                <Statistic title="Đồng ý" value={data.decisions.agreed} styles={{ content: { color: '#389e0d' } }} />
                <Statistic title="Từ chối" value={data.decisions.refused} styles={{ content: { color: '#cf1322' } }} />
                <Statistic title="Tỉ lệ từ chối" value={percent(data.decisions.refusalRate)} />
              </Flex>
              {data.decisions.refusalRate !== null && (
                <Progress percent={Math.round(data.decisions.refusalRate * 1000) / 10} status="normal" strokeColor="#cf1322" showInfo={false} />
              )}
            </Card>
          </Col>
          <Col xs={24} md={12} xl={8}>
            <Card size="small" title="Người ký phía người bệnh (phiếu hoàn tất)">
              <Flex gap={24} wrap>
                <Statistic title="Người bệnh tự ký" value={data.signers.patient} />
                <Statistic title="Người đại diện ký" value={data.signers.representative} />
              </Flex>
              <Typography.Text type="secondary">Tất cả ký tay trên máy tính bảng (chưa tích hợp chữ ký số).</Typography.Text>
            </Card>
          </Col>
          <Col xs={24} xl={8}>
            <Card size="small" title="Thời gian từ lập đến hoàn tất">
              <Flex gap={24} wrap>
                <Statistic title="Trung bình" value={minutes(data.completionTime.averageMinutes)} />
                <Statistic title="Trung vị" value={minutes(data.completionTime.medianMinutes)} />
                <Statistic title="90% phiếu dưới" value={minutes(data.completionTime.percentile90Minutes)} />
              </Flex>
              <Typography.Text type="secondary">
                Đến 15 phút: {data.completionTime.upTo15Minutes} · đến 1 giờ: {data.completionTime.upTo1Hour} · đến 1 ngày:{' '}
                {data.completionTime.upTo1Day} · trên 1 ngày: {data.completionTime.over1Day}
              </Typography.Text>
            </Card>
          </Col>
          <Col xs={24}>
            <Card size="small" title="Số phiếu lập theo ngày">
              <BarChart
                title="Số phiếu lập theo ngày, theo trạng thái hiện tại"
                labels={data.daily.map((d) => dayjs(d.date).format('DD/MM'))}
                series={[
                  { name: 'Hoàn tất', color: '#52c41a', values: data.daily.map((d) => d.counts.completed) },
                  { name: 'Chờ ký', color: '#faad14', values: data.daily.map((d) => d.counts.pendingSignature) },
                  { name: 'Nháp', color: '#bfbfbf', values: data.daily.map((d) => d.counts.draft) },
                  { name: 'Hủy/thu hồi', color: '#ff4d4f', values: data.daily.map((d) => d.counts.cancelled + d.counts.revoked) },
                ]}
              />
            </Card>
          </Col>
          <Col xs={24}>
            <Card size="small" title="Theo khoa/phòng">
              <Table<GroupStats> rowKey="id" size="small" columns={groupColumns} dataSource={data.byDepartment} pagination={false} scroll={{ x: 900 }} />
            </Card>
          </Col>
          <Col xs={24}>
            <Card size="small" title="Theo mẫu phiếu">
              <Table<GroupStats> rowKey="id" size="small" columns={groupColumns} dataSource={data.byTemplate} pagination={false} scroll={{ x: 900 }} />
            </Card>
          </Col>
        </Row>
      )}
    </>
  )
}
