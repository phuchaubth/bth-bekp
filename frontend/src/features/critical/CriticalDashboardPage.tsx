import { CheckCircleFilled, WarningFilled } from '@ant-design/icons'
import { Alert, Card, Col, DatePicker, Flex, Row, Segmented, Spin, Table, Typography, type TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import { Link, useSearchParams } from 'react-router'
import {
  useCriticalDashboard,
  type CriticalDashboard,
  type CriticalDashboardDepartment,
  type CriticalDashboardLate,
  type CriticalRate,
} from '../../api/critical'
import { Busy, ChartCard, StatTile } from '../../components/DashboardCards'
import type { LineChartProps } from '../../components/LineChart'
import { SERIES_COLORS, formatNumber } from '../../components/chartUtils'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { CriticalSectionNav } from './CriticalParts'
import { dateTimeOf } from './criticalUtils'

const MONTH = 'YYYY-MM'

/** Xem được dashboard của tối đa bấy nhiêu tháng trước (như máy chủ). */
const MAX_MONTHS_BACK = 24

const GOOD = '#1d7a3a'
const WARNING = '#b26a00'

const showPercent = (value: number | null) => (value === null ? '—' : `${formatNumber(value)}%`)
const showMinutes = (value: number | null) => (value === null ? '—' : `${formatNumber(value)} phút`)

/** Đạt mục tiêu hay chưa (kèm biểu tượng và chữ, không chỉ dựa vào màu). */
function TargetLine({ rate, target }: { rate: CriticalRate; target: number }) {
  if (rate.percent === null) return <>Chưa có hồ sơ tới hạn · mục tiêu ≥ {target}%</>
  const met = rate.percent >= target
  return (
    <span style={{ color: met ? GOOD : WARNING }}>
      {met ? <CheckCircleFilled /> : <WarningFilled />} {formatNumber(rate.numerator)}/{formatNumber(rate.denominator)} ·{' '}
      {met ? 'đạt' : 'chưa đạt'} mục tiêu ≥ {target}%
    </span>
  )
}

function TrendCharts({ data }: { data: CriticalDashboard }) {
  const labels = data.trend.map((m) => dayjs(`${m.month}-01`).format('M/YY'))
  const fullLabels = data.trend.map((m) => dayjs(`${m.month}-01`).format('MM/YYYY'))
  const details = (i: number) => [`${formatNumber(data.trend[i].total)} kết quả nguy cấp`]
  const charts: { title: string; note: string; chart: LineChartProps }[] = [
    {
      title: `Báo trong ${data.targets.notifyMinutes} phút · 12 tháng`,
      note: 'Hồ sơ báo đúng hạn trên hồ sơ đã báo hoặc đã quá hạn báo (không tính hồ sơ đã hủy).',
      chart: {
        title: 'Tỷ lệ báo đúng hạn theo tháng',
        labels,
        fullLabels,
        pointLabel: 'Tháng',
        series: [{ name: 'Báo đúng hạn (%)', color: SERIES_COLORS[0], values: data.trend.map((m) => m.notifyOnTimePercent) }],
        reference: { value: data.targets.notifyPercent, label: `Mục tiêu ${data.targets.notifyPercent}%` },
        details,
      },
    },
    {
      title: `Bác sĩ xử trí trong ${data.targets.actionMinutes} phút · 12 tháng`,
      note: 'Hồ sơ bác sĩ xử trí đúng hạn trên hồ sơ đã xử trí hoặc đã quá hạn xử trí.',
      chart: {
        title: 'Tỷ lệ bác sĩ xử trí đúng hạn theo tháng',
        labels,
        fullLabels,
        pointLabel: 'Tháng',
        series: [{ name: 'BS xử trí đúng hạn (%)', color: SERIES_COLORS[2], values: data.trend.map((m) => m.actionOnTimePercent) }],
        reference: { value: data.targets.actionPercent, label: `Mục tiêu ${data.targets.actionPercent}%` },
        details,
      },
    },
  ]
  return (
    <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
      {charts.map((c) => (
        <Col key={c.title} xs={24} lg={12}>
          <ChartCard title={c.title} note={c.note} chart={c.chart} />
        </Col>
      ))}
    </Row>
  )
}

function departmentColumns(rateTitle: string): TableColumnsType<CriticalDashboardDepartment> {
  return [
    { title: 'Khoa', dataIndex: 'name', key: 'name' },
    { title: 'KQNC', dataIndex: 'total', key: 'total', align: 'right', render: (v: number) => formatNumber(v) },
    {
      title: rateTitle,
      key: 'rate',
      render: (_, d) => (
        <span style={{ fontVariantNumeric: 'tabular-nums' }}>
          {showPercent(d.onTime.percent)}
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {' '}
            ({d.onTime.numerator}/{d.onTime.denominator})
          </Typography.Text>
        </span>
      ),
    },
    { title: 'Trung vị', key: 'median', render: (_, d) => showMinutes(d.medianMinutes) },
    { title: 'Đang chờ', dataIndex: 'open', key: 'open', align: 'right' },
  ]
}

const lateColumns: TableColumnsType<CriticalDashboardLate> = [
  { title: 'Thời điểm', key: 'at', render: (_, l) => dateTimeOf(l.resultAtUtc) },
  {
    title: 'Người bệnh',
    key: 'who',
    render: (_, l) => (
      <>
        {l.patientInitials}
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {' '}
          · {l.patientCode}
        </Typography.Text>
      </>
    ),
  },
  { title: 'Kết quả', key: 'test', render: (_, l) => `${l.testName} ${l.value}${l.unit ? ` ${l.unit}` : ''}` },
  { title: 'Khoa báo → khoa nhận', key: 'route', render: (_, l) => `${l.sendingDepartmentName} → ${l.receivingDepartmentName ?? 'chưa báo'}` },
  { title: 'Mốc quá hạn', dataIndex: 'miss', key: 'miss' },
  { title: '', key: 'view', render: (_, l) => <Link to={`/ket-qua-nguy-cap/ho-so/${l.id}`}>Xem hồ sơ</Link> },
]

/**
 * Dashboard kết quả nguy cấp cho QLCL, KHTH (quyết định 60): tháng hoặc quý; tỷ lệ báo đúng hạn, bác sĩ xử trí đúng hạn, đọc lại
 * chính xác lần đầu, đủ các bước, so với mục tiêu ở trang cấu hình; trung vị; xu hướng 12 tháng; theo khoa báo, khoa nhận; các
 * trường hợp quá hạn (tên viết tắt) để phân tích nguyên nhân.
 */
export function CriticalDashboardPage() {
  const [params, setParams] = useSearchParams()
  const raw = params.get('thang')
  const month = raw && /^\d{4}-\d{2}$/.test(raw) ? raw : dayjs().format(MONTH)
  const period = params.get('ky') === 'quy' ? 'quarter' : 'month'
  const dashboard = useCriticalDashboard(month, period)
  const data = dashboard.data

  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) next.set(key, value)
    setParams(next, { replace: true })
  }

  return (
    <>
      <CriticalSectionNav />
      <PageHeader
        title="Dashboard kết quả nguy cấp"
        extra={
          <Flex gap={8} wrap>
            <Segmented
              value={period}
              onChange={(value) => update({ ky: value === 'quarter' ? 'quy' : 'thang' })}
              options={[
                { value: 'month', label: 'Tháng' },
                { value: 'quarter', label: 'Quý' },
              ]}
            />
            <DatePicker
              picker={period === 'quarter' ? 'quarter' : 'month'}
              format={period === 'quarter' ? '[Quý] Q/YYYY' : 'MM/YYYY'}
              allowClear={false}
              value={dayjs(`${month}-01`)}
              disabledDate={(d) => d.isAfter(dayjs(), 'month') || d.isBefore(dayjs().subtract(MAX_MONTHS_BACK, 'month'), 'month')}
              onChange={(value) => value && update({ thang: (value.isAfter(dayjs(), 'month') ? dayjs() : value).format(MONTH) })}
              aria-label={period === 'quarter' ? 'Quý' : 'Tháng'}
            />
          </Flex>
        }
      />
      {dashboard.isError && <Alert type="error" showIcon title={errorMessage(dashboard.error)} style={{ marginBottom: 16 }} />}
      {!data ? (
        dashboard.isLoading && <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <Busy busy={dashboard.isFetching}>
          <Typography.Paragraph type="secondary">
            {data.label} ({dayjs(data.from).format('DD/MM')} – {dayjs(data.to).format('DD/MM/YYYY')}) · mọi khoa · hồ sơ đã hủy đếm riêng,
            không tính vào tỷ lệ. Mục tiêu và các mốc thời hạn lấy từ trang Danh mục và cấu hình.
          </Typography.Paragraph>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12} lg={8} xl={5}>
              <StatTile label="Kết quả nguy cấp" value={formatNumber(data.kpis.total)}>
                {data.kpis.cancelled > 0 ? `${data.kpis.cancelled} hồ sơ đã hủy (đếm riêng)` : 'Không có hồ sơ hủy'}
              </StatTile>
            </Col>
            <Col xs={24} sm={12} lg={8} xl={5}>
              <StatTile label={`Báo trong ${data.targets.notifyMinutes} phút`} value={showPercent(data.kpis.notifyOnTime.percent)}>
                <TargetLine rate={data.kpis.notifyOnTime} target={data.targets.notifyPercent} />
              </StatTile>
            </Col>
            <Col xs={24} sm={12} lg={8} xl={5}>
              <StatTile label={`BS xử trí trong ${data.targets.actionMinutes} phút`} value={showPercent(data.kpis.actionOnTime.percent)}>
                <TargetLine rate={data.kpis.actionOnTime} target={data.targets.actionPercent} />
              </StatTile>
            </Col>
            <Col xs={24} sm={12} lg={12} xl={5}>
              <StatTile label="Đọc lại chính xác lần đầu" value={showPercent(data.kpis.readBackFirstTime.percent)}>
                <TargetLine rate={data.kpis.readBackFirstTime} target={data.targets.readBackPercent} />
              </StatTile>
            </Col>
            <Col xs={24} sm={12} lg={12} xl={4}>
              <StatTile label="Hồ sơ đủ các bước" value={showPercent(data.kpis.complete.percent)}>
                <TargetLine rate={data.kpis.complete} target={data.targets.completePercent} />
              </StatTile>
            </Col>
          </Row>
          <Card size="small" title="Thời gian trung vị" style={{ marginTop: 16 }}>
            <Flex gap={32} wrap>
              <span>
                Có kết quả → báo: <Typography.Text strong>{showMinutes(data.kpis.medianNotifyMinutes)}</Typography.Text>
                {data.kpis.notifyQ1Minutes !== null && (
                  <Typography.Text type="secondary">
                    {' '}
                    (Q1–Q3: {formatNumber(data.kpis.notifyQ1Minutes)}–{formatNumber(data.kpis.notifyQ3Minutes!)} phút)
                  </Typography.Text>
                )}
              </span>
              <span>
                Khoa nhận → bác sĩ xử trí: <Typography.Text strong>{showMinutes(data.kpis.medianActionMinutes)}</Typography.Text>
                {data.kpis.actionQ1Minutes !== null && (
                  <Typography.Text type="secondary">
                    {' '}
                    (Q1–Q3: {formatNumber(data.kpis.actionQ1Minutes)}–{formatNumber(data.kpis.actionQ3Minutes!)} phút)
                  </Typography.Text>
                )}
              </span>
            </Flex>
          </Card>
          <TrendCharts data={data} />
          <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
            <Col xs={24} xl={12}>
              <Card size="small" title="Theo khoa báo (sổ ST01, sổ Vi sinh)">
                <Table<CriticalDashboardDepartment>
                  rowKey="departmentId"
                  size="small"
                  pagination={false}
                  columns={departmentColumns(`Báo trong ${data.targets.notifyMinutes} phút (mục tiêu ${data.targets.notifyPercent}%)`)}
                  dataSource={data.senders}
                  locale={{ emptyText: 'Chưa có hồ sơ trong kỳ.' }}
                  scroll={{ x: 560 }}
                />
              </Card>
            </Col>
            <Col xs={24} xl={12}>
              <Card size="small" title="Theo khoa nhận (sổ ST02)">
                <Table<CriticalDashboardDepartment>
                  rowKey="departmentId"
                  size="small"
                  pagination={false}
                  columns={departmentColumns(`BS xử trí trong ${data.targets.actionMinutes} phút (mục tiêu ${data.targets.actionPercent}%)`)}
                  dataSource={data.receivers}
                  locale={{ emptyText: 'Chưa có hồ sơ trong kỳ.' }}
                  scroll={{ x: 560 }}
                />
              </Card>
            </Col>
          </Row>
          <Card size="small" title="Trường hợp quá hạn trong kỳ" style={{ marginTop: 16 }}>
            <Typography.Paragraph type="secondary">
              Tên người bệnh viết tắt; bấm để mở hồ sơ. Dùng cho phân tích nguyên nhân hằng quý (tối đa 50 trường hợp gần nhất).
            </Typography.Paragraph>
            <Table<CriticalDashboardLate>
              rowKey="id"
              size="small"
              columns={lateColumns}
              dataSource={data.late}
              pagination={{ pageSize: 10, hideOnSinglePage: true }}
              locale={{ emptyText: 'Không có trường hợp quá hạn.' }}
              scroll={{ x: 900 }}
            />
          </Card>
        </Busy>
      )}
    </>
  )
}
