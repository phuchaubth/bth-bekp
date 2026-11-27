import { ArrowDownOutlined, ArrowUpOutlined, CheckCircleFilled, WarningFilled } from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Flex,
  Row,
  Segmented,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
  type TableColumnsType,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState, type ReactNode } from 'react'
import { useNursingDashboard, type NursingDashboard, type NursingDashboardDepartment } from '../../api/nursingReports'
import { LineChart, SeriesTable, type LineChartProps } from '../../components/LineChart'
import { SERIES_COLORS, formatNumber } from '../../components/chartUtils'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { DepartmentCodeLink, DepartmentReportDrawer } from './DepartmentReportDrawer'

const DATE = 'YYYY-MM-DD'

/** Khoảng thời gian chọn nhanh (ngày, tính cả ngày cuối). */
const PERIODS = [7, 30, 90]

/** Màu trạng thái (luôn kèm biểu tượng và chữ, không dùng màu riêng lẻ). */
const GOOD = '#1d7a3a'
const WARNING = '#b26a00'

/** Rãnh của thanh công suất: nhạt hơn cùng tông với màu thanh. */
const TRACK = '#d6eeeb'

const MAX_MISSING_SHOWN = 6

const show = (value: number | null | undefined) => (value === null || value === undefined ? '—' : formatNumber(value))

/** Công suất giường (%), làm tròn; `null` khi chưa có số giường hoặc số NB. */
const occupancy = (patients: number | null | undefined, beds: number | null | undefined) =>
  patients === null || patients === undefined || !beds ? null : Math.round((patients / beds) * 100)

/**
 * Dashboard điều dưỡng của Phòng Điều dưỡng (quyết định 47): số liệu các khoa đã gửi báo cáo trong 7, 30 hoặc 90 ngày đến
 * ngày đã chọn. Ô chỉ số và bảng theo khoa là của ngày cuối kỳ; biểu đồ theo từng ngày báo cáo. Khoa chưa gửi không được tính,
 * như bảng tổng hợp.
 */
export function NursingDashboardPage() {
  const [to, setTo] = useState<Dayjs>(() => dayjs())
  const [days, setDays] = useState(30)
  const dashboard = useNursingDashboard(to.format(DATE), days)

  return (
    <>
      <PageHeader
        title="Dashboard điều dưỡng"
        extra={
          <Flex gap={8} wrap align="center">
            <Segmented
              value={days}
              onChange={(value) => setDays(Number(value))}
              options={PERIODS.map((period) => ({ value: period, label: `${period} ngày` }))}
            />
            <DatePicker
              value={to}
              format="DD/MM/YYYY"
              allowClear={false}
              aria-label="Đến ngày"
              disabledDate={(d) => d.isAfter(dayjs(), 'day')}
              onChange={(d) => d && setTo(d)}
            />
          </Flex>
        }
      />
      {dashboard.isError ? (
        <Alert type="error" showIcon title={errorMessage(dashboard.error)} />
      ) : !dashboard.data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <DashboardBody data={dashboard.data} busy={dashboard.isPlaceholderData} />
      )}
    </>
  )
}

function DashboardBody({ data, busy }: { data: NursingDashboard; busy: boolean }) {
  const lastDate = dayjs(data.to).format('DD/MM/YYYY')
  const hasReports = data.days.some((d) => d.submitted > 0)
  // Khoa đang xem báo cáo (quyết định 55). Ngăn kéo nằm ngoài bảng: trả lại báo cáo duy nhất của kỳ thì bảng ẩn đi, ngăn kéo vẫn mở.
  const [selected, setSelected] = useState<NursingDashboardDepartment | null>(null)
  return (
    // Đang tải khoảng ngày mới: giữ số cũ, mờ đi, không nháy trang.
    <div aria-busy={busy} style={{ opacity: busy ? 0.6 : 1, transition: 'opacity 0.2s' }}>
      <Typography.Paragraph type="secondary">
        Từ {dayjs(data.from).format('DD/MM/YYYY')} đến {lastDate}. Chỉ tính báo cáo khoa đã gửi (như bảng tổng hợp). Số ca nhập
        khoa, xuất viện, chuyển khoa là của ngày trước ngày báo cáo.
      </Typography.Paragraph>
      <Typography.Title level={4} style={{ marginTop: 0 }}>
        Ngày {lastDate}
      </Typography.Title>
      <KpiRow data={data} />
      {!hasReports ? (
        <Card style={{ marginTop: 16 }}>
          <Empty description={`Chưa có khoa nào gửi báo cáo trong ${data.days.length} ngày này.`} />
        </Card>
      ) : (
        <>
          <Typography.Title level={4}>Theo ngày</Typography.Title>
          <Charts data={data} />
          <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
            <Col xs={24} lg={12}>
              <OccupancyCard data={data} />
            </Col>
            <Col xs={24} lg={12}>
              <IssuesCard data={data} />
            </Col>
          </Row>
          <DepartmentTable data={data} onOpen={setSelected} />
        </>
      )}
      {selected && (
        <DepartmentReportDrawer
          departmentId={selected.departmentId}
          departmentName={selected.name}
          reportDate={data.to}
          onClose={() => setSelected(null)}
        />
      )}
    </div>
  )
}

function StatTile({ label, value, children }: { label: string; value: ReactNode; children?: ReactNode }) {
  return (
    <Card size="small" role="group" aria-label={label} style={{ height: '100%' }}>
      <Typography.Text type="secondary">{label}</Typography.Text>
      <div style={{ fontSize: 28, fontWeight: 600, lineHeight: 1.3, margin: '2px 0 4px' }}>{value}</div>
      {children && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {children}
        </Typography.Text>
      )}
    </Card>
  )
}

/** Tám ô chỉ số của ngày cuối kỳ. */
function KpiRow({ data }: { data: NursingDashboard }) {
  const last = data.days[data.days.length - 1]
  const v = (key: string) => last.values[key] ?? null
  const missing = data.departments.filter((d) => d.isReporting && !d.submitted).map((d) => d.code)
  const percent = occupancy(last.bedPatients, last.beds)
  const noBed = v('noBed')
  const change = last.patientChange
  const none = last.submitted === 0 ? 'Chưa khoa nào gửi báo cáo' : null

  const tiles: { label: string; value: ReactNode; detail?: ReactNode }[] = [
    {
      label: 'Khoa đã gửi báo cáo',
      value: `${last.submitted}/${data.reportingCount}`,
      detail:
        missing.length === 0 ? (
          <>
            <CheckCircleFilled style={{ color: GOOD }} /> Đủ các khoa
          </>
        ) : (
          `Chưa gửi: ${missing.slice(0, MAX_MISSING_SHOWN).join(', ')}${
            missing.length > MAX_MISSING_SHOWN ? ` và ${missing.length - MAX_MISSING_SHOWN} khoa khác` : ''
          }`
        ),
    },
    {
      label: 'Số NB hiện tại',
      value: show(v('currentPatients')),
      detail:
        change === null ? none
        : change === 0 ? 'Không đổi so với ngày hôm trước'
        : (
          <>
            {change > 0 ? <ArrowUpOutlined /> : <ArrowDownOutlined />} {change > 0 ? 'Tăng' : 'Giảm'} {formatNumber(Math.abs(change))} so
            với ngày hôm trước
          </>
        ),
    },
    {
      label: 'Công suất giường',
      value: percent === null ? '—' : `${percent}%`,
      detail:
        percent === null ? none ?? 'Chưa có khoa đã gửi nào được cài số giường'
        : (
          <>
            {percent > 100 && (
              <>
                <WarningFilled style={{ color: WARNING }} /> Vượt số giường ·{' '}
              </>
            )}
            {formatNumber(last.bedPatients ?? 0)} NB / {formatNumber(last.beds ?? 0)} giường của khoa đã gửi
            {noBed ? ` · ${formatNumber(noBed)} NB chưa có giường` : ''}
          </>
        ),
    },
    {
      label: 'NB chăm sóc cấp 1',
      value: show(v('careLevel1')),
      detail: none ?? `Thở máy ${show(v('ventilated'))} · Monitor ${show(v('monitored'))} · Thở oxy ${show(v('oxygen'))}`,
    },
    {
      label: 'Nhập khoa (hôm trước)',
      value: show(v('admissions')),
      detail: none ?? `Từ PK ${show(v('admissionsFromClinic'))} · Do CK ${show(v('admissionsByTransfer'))}`,
    },
    { label: 'Xuất viện (hôm trước)', value: show(v('dischargesYesterday')), detail: none },
    { label: 'Chuyển khoa (hôm trước)', value: show(v('transfersOutYesterday')), detail: none },
    {
      label: 'Khoa báo "Không ổn"',
      value: last.submitted === 0 ? '—' : formatNumber(last.issueDepartments),
      detail: none ?? `ADR ${show(v('adr'))} · ${formatNumber(last.incidents)} báo cáo có nội dung bất thường`,
    },
  ]

  return (
    <Row gutter={[16, 16]}>
      {tiles.map((tile) => (
        <Col key={tile.label} xs={12} lg={6}>
          <StatTile label={tile.label} value={tile.value}>
            {tile.detail}
          </StatTile>
        </Col>
      ))}
    </Row>
  )
}

/** Thẻ biểu đồ; bấm "Xem bảng" để đọc số chính xác từng ngày. */
function ChartCard({ title, note, chart }: { title: string; note?: string; chart: LineChartProps }) {
  const [asTable, setAsTable] = useState(false)
  return (
    <Card
      size="small"
      title={title}
      style={{ height: '100%' }}
      extra={
        <Button type="link" size="small" onClick={() => setAsTable((value) => !value)} aria-label={`${asTable ? 'Xem biểu đồ' : 'Xem bảng'}: ${title}`}>
          {asTable ? 'Xem biểu đồ' : 'Xem bảng'}
        </Button>
      }
    >
      {note && (
        <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 8 }}>
          {note}
        </Typography.Paragraph>
      )}
      {asTable ? <SeriesTable {...chart} /> : <LineChart {...chart} />}
    </Card>
  )
}

function Charts({ data }: { data: NursingDashboard }) {
  const labels = data.days.map((d) => dayjs(d.date).format('DD/MM'))
  const fullLabels = data.days.map((d) => dayjs(d.date).format('DD/MM/YYYY'))
  const values = (key: string) => data.days.map((d) => d.values[key] ?? null)
  const [first, second, third] = SERIES_COLORS

  const charts: { title: string; note?: string; chart: LineChartProps }[] = [
    {
      title: 'Số NB hiện tại',
      note: 'Tổng của các khoa đã gửi báo cáo; ngày ít khoa gửi thì số thấp hơn thực tế.',
      chart: {
        title: 'Số người bệnh hiện tại theo ngày',
        labels,
        fullLabels,
        series: [{ name: 'Số NB hiện tại', color: first, values: values('currentPatients') }],
        details: (i) => {
          const day = data.days[i]
          const percent = occupancy(day.bedPatients, day.beds)
          return [`${day.submitted}/${data.reportingCount} khoa đã gửi`, ...(percent === null ? [] : [`Công suất giường ${percent}%`])]
        },
      },
    },
    {
      title: 'Nhập khoa, xuất viện, chuyển khoa',
      note: 'Số ca của ngày hôm trước, ghi theo ngày báo cáo.',
      chart: {
        title: 'Số ca nhập khoa, xuất viện, chuyển khoa theo ngày',
        labels,
        fullLabels,
        series: [
          { name: 'Nhập khoa', color: first, values: values('admissions') },
          { name: 'Xuất viện', color: second, values: values('dischargesYesterday') },
          { name: 'Chuyển khoa', color: third, values: values('transfersOutYesterday') },
        ],
      },
    },
    {
      title: 'NB chăm sóc cấp 1, thở máy, monitor',
      chart: {
        title: 'Số người bệnh chăm sóc cấp 1, thở máy, theo dõi monitor theo ngày',
        labels,
        fullLabels,
        series: [
          { name: 'Chăm sóc cấp 1', color: first, values: values('careLevel1') },
          { name: 'Thở máy', color: second, values: values('ventilated') },
          { name: 'Monitor', color: third, values: values('monitored') },
        ],
      },
    },
    {
      title: 'Khoa đã gửi báo cáo',
      note: `Đường đứt nét: ${data.reportingCount} khoa phải báo cáo.`,
      chart: {
        title: 'Số khoa đã gửi báo cáo theo ngày',
        labels,
        fullLabels,
        series: [{ name: 'Khoa đã gửi', color: first, values: data.days.map((d) => d.submitted) }],
        reference: { value: data.reportingCount, label: `Phải báo cáo: ${data.reportingCount}` },
        details: (i) => [
          `${data.days[i].issueDepartments} khoa báo "Không ổn"`,
          `${data.days[i].incidents} báo cáo có nội dung bất thường`,
        ],
      },
    },
  ]

  return (
    <Row gutter={[16, 16]}>
      {charts.map((c) => (
        <Col key={c.title} xs={24} xl={12}>
          <ChartCard title={c.title} note={c.note} chart={c.chart} />
        </Col>
      ))}
    </Row>
  )
}

/** Công suất giường từng khoa ngày cuối kỳ, cao nhất trước; khoa chưa gửi ở cuối. */
function OccupancyCard({ data }: { data: NursingDashboard }) {
  const rows = data.departments
    .filter((d) => (d.bedCount ?? 0) > 0)
    .map((d) => {
      const patients = d.submitted ? (d.values.currentPatients ?? null) : null
      return { department: d, patients, percent: occupancy(patients, d.bedCount) }
    })
    .sort((a, b) => (b.percent ?? -1) - (a.percent ?? -1))

  return (
    <Card size="small" title={`Công suất giường theo khoa – ngày ${dayjs(data.to).format('DD/MM/YYYY')}`} style={{ height: '100%' }}>
      {rows.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa cài số giường cho khoa nào (hộp Khoa báo cáo và số giường)." />
      ) : (
        <Flex vertical gap={10} component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {rows.map(({ department, patients, percent }) => (
            <li key={department.departmentId}>
              <Flex align="center" gap={12}>
                <Tooltip title={department.name}>
                  <span style={{ width: 96, flexShrink: 0, fontWeight: 500, overflowWrap: 'anywhere' }}>{department.code}</span>
                </Tooltip>
                <div aria-hidden style={{ flex: 1, minWidth: 60, height: 8, background: TRACK, borderRadius: 4, overflow: 'hidden' }}>
                  <div
                    style={{
                      width: `${Math.min(100, percent ?? 0)}%`,
                      height: '100%',
                      background: percent !== null && percent > 100 ? WARNING : SERIES_COLORS[0],
                      borderRadius: 4,
                    }}
                  />
                </div>
                <span style={{ width: 128, flexShrink: 0, textAlign: 'right', fontVariantNumeric: 'tabular-nums', fontSize: 13 }}>
                  {percent === null ? (
                    <Typography.Text type="secondary">Chưa gửi</Typography.Text>
                  ) : (
                    <>
                      {percent > 100 && <WarningFilled style={{ color: WARNING, marginRight: 4 }} aria-label="Vượt số giường" />}
                      {formatNumber(patients ?? 0)}/{formatNumber(department.bedCount ?? 0)} · {percent}%
                    </>
                  )}
                </span>
              </Flex>
            </li>
          ))}
        </Flex>
      )}
    </Card>
  )
}

/** Số lượt khoa báo "Không ổn" theo từng dòng tick trong kỳ, nhiều nhất trước. */
function IssuesCard({ data }: { data: NursingDashboard }) {
  const ranked = [...data.checks].sort((a, b) => b.issueCount - a.issueCount)
  const max = Math.max(1, ...ranked.map((c) => c.issueCount))
  const total = ranked.reduce((sum, c) => sum + c.issueCount, 0)
  const incidents = data.days.reduce((sum, d) => sum + d.incidents, 0)
  const lastDate = dayjs(data.to).format('DD/MM')

  return (
    <Card size="small" title={`"Không ổn" theo nội dung – ${data.days.length} ngày`} style={{ height: '100%' }}>
      <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 8 }}>
        {total === 0 ? 'Không khoa nào báo "Không ổn" trong kỳ.' : 'Mỗi khoa báo "Không ổn" một dòng trong một ngày tính một lượt.'}{' '}
        {formatNumber(incidents)} báo cáo có nội dung bất thường (phần nhập thêm) trong kỳ.
      </Typography.Paragraph>
      <Flex vertical gap={8} component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {ranked.map((check) => (
          <li key={check.key}>
            <Flex align="center" gap={12}>
              <span style={{ width: 190, flexShrink: 0, fontSize: 13 }}>{check.label}</span>
              <div aria-hidden style={{ flex: 1, minWidth: 40, height: 8 }}>
                {check.issueCount > 0 && (
                  <div style={{ width: `${(check.issueCount / max) * 100}%`, height: '100%', background: SERIES_COLORS[0], borderRadius: 4 }} />
                )}
              </div>
              <span style={{ width: 40, flexShrink: 0, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                {formatNumber(check.issueCount)}
              </span>
            </Flex>
            {check.lastDayDepartments.length > 0 && (
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                Ngày {lastDate}: {check.lastDayDepartments.join(', ')}
              </Typography.Text>
            )}
          </li>
        ))}
      </Flex>
    </Card>
  )
}

/**
 * Bảng theo khoa (thứ tự bảng tổng hợp), số liệu ngày cuối kỳ và số ngày đã gửi báo cáo trong kỳ. Bấm mã khoa mở báo cáo của khoa
 * ngày cuối kỳ như ở bảng tổng hợp: in PDF, Phòng Điều dưỡng trả lại khoa (quyết định 55).
 */
function DepartmentTable({ data, onOpen }: { data: NursingDashboard; onOpen: (department: NursingDashboardDepartment) => void }) {
  const checkLabels = Object.fromEntries(data.checks.map((c) => [c.key, c.label]))
  const value = (d: NursingDashboardDepartment, key: string) => (d.submitted ? show(d.values[key]) : '')
  const percentOf = (d: NursingDashboardDepartment) => (d.submitted ? occupancy(d.values.currentPatients, d.bedCount) : null)
  const number = (title: string, key: string): TableColumnsType<NursingDashboardDepartment>[number] => ({
    title,
    key,
    align: 'right',
    render: (_, d) => value(d, key),
  })

  const columns: TableColumnsType<NursingDashboardDepartment> = [
    {
      title: 'Khoa',
      key: 'code',
      fixed: 'left',
      width: 120,
      render: (_, d) => (
        <Flex vertical gap={2} align="start">
          <DepartmentCodeLink code={d.code} name={d.name} onClick={() => onOpen(d)} />
          {!d.submitted && <Tag style={{ marginInlineEnd: 0 }}>Chưa gửi</Tag>}
          {!d.isReporting && <Tag style={{ marginInlineEnd: 0 }}>Không trong danh sách</Tag>}
        </Flex>
      ),
    },
    { title: 'Giường', key: 'beds', align: 'right', render: (_, d) => show(d.bedCount) },
    {
      ...number('NB hiện tại', 'currentPatients'),
      sorter: (a, b) => (a.submitted ? (a.values.currentPatients ?? -1) : -1) - (b.submitted ? (b.values.currentPatients ?? -1) : -1),
    },
    {
      title: 'Công suất',
      key: 'occupancy',
      align: 'right',
      sorter: (a, b) => (percentOf(a) ?? -1) - (percentOf(b) ?? -1),
      render: (_, d) => {
        const percent = percentOf(d)
        return percent === null ? '' : (
          <>
            {percent > 100 && <WarningFilled style={{ color: WARNING, marginRight: 4 }} aria-label="Vượt số giường" />}
            {percent}%
          </>
        )
      },
    },
    number('Nhập khoa', 'admissions'),
    number('Xuất viện', 'dischargesYesterday'),
    number('Chuyển khoa', 'transfersOutYesterday'),
    number('Cấp 1', 'careLevel1'),
    number('Thở máy', 'ventilated'),
    {
      title: 'Không ổn',
      key: 'issues',
      render: (_, d) =>
        !d.submitted ? '' : d.issues.length === 0 ? 'Ổn' : (
          <>
            <WarningFilled style={{ color: WARNING, marginRight: 4 }} />
            {d.issues.map((key) => checkLabels[key] ?? key).join(', ')}
          </>
        ),
    },
    {
      // Số ngày khoa đã gửi báo cáo trên số ngày của kỳ đang xem.
      title: <Tooltip title={`Số ngày đã gửi báo cáo trong ${data.days.length} ngày`}>Ngày đã gửi</Tooltip>,
      key: 'submittedDays',
      align: 'right',
      width: 110,
      render: (_, d) => `${d.submittedDays}/${data.days.length}`,
    },
  ]

  return (
    <Card size="small" title={`Theo khoa – ngày ${dayjs(data.to).format('DD/MM/YYYY')}`} style={{ marginTop: 16 }}>
      <Table
        size="small"
        rowKey="departmentId"
        pagination={false}
        dataSource={data.departments}
        columns={columns}
        scroll={{ x: 1000 }}
        locale={{ emptyText: 'Chưa cài khoa phải báo cáo.' }}
      />
    </Card>
  )
}
