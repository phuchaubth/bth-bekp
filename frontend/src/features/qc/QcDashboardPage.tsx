import { CheckCircleFilled, WarningFilled } from '@ant-design/icons'
import { Alert, Card, Col, DatePicker, Flex, Row, Spin, Table, Tag, Tooltip, Typography, type TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import {
  useQcDashboard,
  type QcDashboard,
  type QcDashboardBookCounts,
  type QcDashboardFailure,
  type QcDashboardFigures,
  type QcDashboardLotAlert,
  type QcDashboardMeter,
  type QcDashboardPendingBook,
} from '../../api/qc'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { Busy, ChartCard, StatTile } from '../../components/DashboardCards'
import type { LineChartProps } from '../../components/LineChart'
import { SERIES_COLORS, formatNumber } from '../../components/chartUtils'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { QcStatusTag } from './qcTags'

const MONTH = 'YYYY-MM'
const SHOW_DATE = 'DD/MM/YYYY'

/** Xem được dashboard của tối đa bấy nhiêu tháng trước (như máy chủ). */
const MAX_MONTHS_BACK = 24

/** Mỗi cảnh báo dạng danh sách hiện tối đa bấy nhiêu dòng. */
const MAX_LISTED = 8

/** Màu trạng thái (luôn kèm biểu tượng và chữ, không dùng màu riêng lẻ). */
const GOOD = '#1d7a3a'
const WARNING = '#b26a00'

/** Rãnh của thanh tỷ lệ: nhạt hơn cùng tông với màu thanh. */
const TRACK = '#d6eeeb'

const KIND_NAMES = { TestStrip: 'que thử', ControlSolution: 'dung dịch chứng' } as const

/** Tỷ lệ phần trăm, một chữ số thập phân; `null` khi mẫu số bằng 0. */
const rate = (part: number, whole: number) => (whole === 0 ? null : Math.round((part * 1000) / whole) / 10)
const showRate = (value: number | null) => (value === null ? '—' : `${formatNumber(value)}%`)
const monthLabel = (month: string) => dayjs(`${month}-01`).format('MM/YYYY')
const compliance = (figures: QcDashboardFigures) => rate(figures.recordedDays, figures.requiredDays)

/**
 * Dashboard giám sát nội kiểm đường huyết tại giường (quyết định 59): Khoa HSH và người có quyền xem dashboard thấy mọi khoa, khoa
 * thấy khoa của mình. Chọn tháng (mặc định tháng này): ô chỉ số, cảnh báo cần xử lý (tính đến hôm nay), xu hướng 6 tháng, bảng
 * theo khoa (bấm mở từng máy). Tỷ lệ thực hiện tính trên mọi ngày trong tháng như sổ giấy.
 */
export function QcDashboardPage() {
  const [params, setParams] = useSearchParams()
  const raw = params.get('thang')
  const month = raw && /^\d{4}-\d{2}$/.test(raw) ? raw : dayjs().format(MONTH)
  const dashboard = useQcDashboard(month)
  const data = dashboard.data
  const { hasPermission } = useAuth()
  const canUseBooks = hasPermission(Permissions.QcRecord) || hasPermission(Permissions.QcReview)

  const setMonth = (value: string) => {
    const next = new URLSearchParams(params)
    next.set('thang', value)
    setParams(next, { replace: true })
  }

  return (
    <>
      <PageHeader
        title="Dashboard nội kiểm đường huyết"
        extra={
          <DatePicker
            picker="month"
            format="MM/YYYY"
            allowClear={false}
            value={dayjs(`${month}-01`)}
            disabledDate={(d) => d.isAfter(dayjs(), 'month') || d.isBefore(dayjs().subtract(MAX_MONTHS_BACK, 'month'), 'month')}
            onChange={(value) => value && setMonth(value.format(MONTH))}
            aria-label="Tháng"
          />
        }
      />
      {dashboard.isError ? (
        <Alert type="error" showIcon title={errorMessage(dashboard.error)} />
      ) : !data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <Busy busy={dashboard.isPlaceholderData}>
          <Typography.Title level={4} style={{ marginTop: 0 }}>
            Tháng {monthLabel(data.month)}
          </Typography.Title>
          <Typography.Paragraph type="secondary">
            {periodText(data)} Máy khai báo giữa tháng tính từ ngày khai báo; máy đã ngưng sử dụng hoặc đã chuyển khoa tính đến
            ngày có kết quả cuối cùng của sổ.
          </Typography.Paragraph>
          <KpiRow data={data} />
          <AlertsCard data={data} canUseBooks={canUseBooks} />
          <TrendCharts data={data} />
          <DepartmentTable data={data} />
        </Busy>
      )}
    </>
  )
}

/** Kỳ tính của tháng đang xem: tháng này đến hôm qua (hôm nay tính khi đã có kết quả); tháng đã qua cả tháng. */
function periodText(data: QcDashboard) {
  const today = dayjs(data.today)
  const first = dayjs(`${data.month}-01`)
  if (!first.isSame(today, 'month')) {
    return `Tính mọi ngày trong tháng (${first.daysInMonth()} ngày).`
  }
  return today.date() === 1
    ? 'Hôm nay là ngày đầu tháng: chỉ tính hôm nay khi khoa đã ghi kết quả.'
    : `Tính mọi ngày từ ${first.format('DD/MM')} đến hôm qua (${today.subtract(1, 'day').format('DD/MM')}); hôm nay (${today.format('DD/MM')}) tính khi khoa đã ghi kết quả.`
}

const good = (text: string) => (
  <>
    <CheckCircleFilled style={{ color: GOOD }} /> {text}
  </>
)

const warning = (text: string) => (
  <>
    <WarningFilled style={{ color: WARNING }} /> {text}
  </>
)

/** Tám ô chỉ số của tháng đang xem (các khoa người xem thấy). */
function KpiRow({ data }: { data: QcDashboard }) {
  const f = data.totals.figures
  const b = data.totals.books
  const books = b.open + b.returned + b.submitted + b.confirmed + b.notOpened
  const tiles: { label: string; value: ReactNode; detail: ReactNode }[] = [
    {
      label: 'Tỷ lệ thực hiện',
      value: showRate(compliance(f)),
      detail:
        f.requiredDays === 0
          ? 'Chưa có ngày phải thực hiện'
          : `${formatNumber(f.recordedDays)}/${formatNumber(f.requiredDays)} ngày phải thực hiện có kết quả`,
    },
    {
      label: 'Ngày chưa ghi',
      value: formatNumber(f.missingDays),
      detail: f.missingDays === 0 ? good('Không thiếu ngày nào') : 'Ngày đã qua chưa có kết quả, kể cả máy chưa mở sổ',
    },
    {
      label: 'Ghi muộn',
      value: formatNumber(f.lateDays),
      detail: `Ghi sau ngày thực hiện · ${showRate(rate(f.lateDays, f.recordedDays))} số ngày đã ghi`,
    },
    {
      label: 'Không đạt',
      value: formatNumber(f.failedDays),
      detail: `${showRate(rate(f.failedDays, f.recordedDays))} số ngày đã ghi · chạy lại đạt ${formatNumber(f.repeatPassedDays)}, vẫn không đạt ${formatNumber(f.repeatFailedDays)}`,
    },
    {
      label: 'Không đạt, chưa chạy lại',
      value: formatNumber(f.notRepeatedDays),
      detail: f.notRepeatedDays === 0 ? good('Không có') : warning('Cần nhắc khoa chạy lại QC'),
    },
    {
      label: 'Chờ Khoa HSH xem xét',
      value: formatNumber(f.daysToReview),
      detail: `Ngày đến hôm nay chưa xem xét · đã xem xét ${formatNumber(f.reviewedDays)} ngày`,
    },
    {
      label: 'Sổ đã xác nhận',
      value: `${formatNumber(b.confirmed)}/${formatNumber(books)}`,
      detail: `Chờ xác nhận ${b.submitted} · bị trả lại ${b.returned} · đang ghi ${b.open} · chưa mở sổ ${b.notOpened}`,
    },
    {
      label: 'Máy đo',
      value: formatNumber(data.totals.meters),
      detail: `Của ${formatNumber(data.totals.departments)} khoa`,
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

function lotText(lot: QcDashboardLotAlert) {
  const date = dayjs(lot.expiryDate).format(SHOW_DATE)
  const expiry =
    lot.daysLeft < 0
      ? `đã hết hạn ngày ${date} nhưng chưa ngưng sử dụng`
      : lot.daysLeft === 0
        ? `hết hạn hôm nay (${date})`
        : `hết hạn ngày ${date}, còn ${lot.daysLeft} ngày`
  const name = `Lô ${KIND_NAMES[lot.kind]} ${lot.lotNumber} (${lot.productName}${lot.level === null ? '' : `, Level ${lot.level}`})`
  return `${name}: ${expiry}.${lot.usedBy.length > 0 ? ` Đang dùng ở: ${lot.usedBy.join(', ')}.` : ''}`
}

function pendingText(book: QcDashboardPendingBook) {
  return book.status === 'Submitted' ? 'chờ Khoa HSH xác nhận' : book.returned ? 'bị trả lại, khoa chưa gửi lại' : 'khoa chưa gửi sổ'
}

/** Các dòng đầu của danh sách, còn lại ghi "và N … khác". */
function Listed<T>({ items, unit, render }: { items: T[]; unit: string; render: (item: T) => ReactNode }) {
  return (
    <ul style={{ margin: '4px 0 0', paddingInlineStart: 20 }}>
      {items.slice(0, MAX_LISTED).map((item, index) => (
        <li key={index}>{render(item)}</li>
      ))}
      {items.length > MAX_LISTED && (
        <li>
          <Typography.Text type="secondary">
            và {items.length - MAX_LISTED} {unit} khác
          </Typography.Text>
        </li>
      )}
    </ul>
  )
}

/** Cảnh báo tính đến hôm nay: lô hết hạn hoặc sắp hết hạn, thiếu mức dung dịch chứng, máy chưa mở sổ, sổ tháng trước chưa xác nhận. */
function AlertsCard({ data, canUseBooks }: { data: QcDashboard; canUseBooks: boolean }) {
  const alerts = data.alerts
  const current = monthLabel(alerts.currentMonth)
  const items: { key: string; content: ReactNode }[] = alerts.lots.map((lot) => ({ key: lot.lotId, content: lotText(lot) }))
  if (alerts.controlLevels.length < 2) {
    items.push({
      key: 'levels',
      content: `${
        alerts.controlLevels.length === 0
          ? 'Danh mục không còn lô dung dịch chứng nào còn hạn'
          : `Danh mục chỉ còn dung dịch chứng Level ${alerts.controlLevels.join(', ')} còn hạn`
      }: khoa không ghi được kết quả hai mức. Khoa HSH thêm lô ở mục Máy đo và lô vật tư.`,
    })
  }
  if (alerts.stripLots === 0) {
    items.push({ key: 'strips', content: 'Danh mục không còn lô que thử nào còn hạn: khoa không ghi được kết quả.' })
  }
  if (alerts.metersWithoutBook.length > 0) {
    items.push({
      key: 'no-book',
      content: (
        <>
          {alerts.metersWithoutBook.length} máy chưa mở sổ tháng {current}
          {canUseBooks && (
            <>
              {' '}
              (<Link to={`/noi-kiem/so?thang=${alerts.currentMonth}`}>mở sổ ở trang Sổ nội kiểm</Link>)
            </>
          )}
          :
          <Listed items={alerts.metersWithoutBook} unit="máy" render={(m) => `${m.departmentName} – ${m.meterLabel}`} />
        </>
      ),
    })
  }
  if (alerts.pendingBooks.length > 0) {
    items.push({
      key: 'pending',
      content: (
        <>
          {alerts.pendingBooks.length} sổ của các tháng trước chưa được xác nhận:
          <Listed
            items={alerts.pendingBooks}
            unit="sổ"
            render={(book) => (
              <>
                Tháng {monthLabel(book.month)} · {book.departmentName} – {book.meterLabel}: {pendingText(book)}
                {book.canOpen && (
                  <>
                    {' '}
                    · <Link to={`/noi-kiem/so/${book.bookId}`}>Xem sổ</Link>
                  </>
                )}
              </>
            )}
          />
        </>
      ),
    })
  }

  return (
    <Card size="small" title={`Cần xử lý – tính đến hôm nay ${dayjs(data.today).format(SHOW_DATE)}`} style={{ marginTop: 16 }}>
      {items.length === 0 ? (
        good('Không có cảnh báo: lô đang dùng còn hạn trên 30 ngày, đủ hai mức dung dịch chứng, máy đã mở sổ, sổ các tháng trước đã xác nhận.')
      ) : (
        <Flex vertical gap={8} component="ul" style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {items.map((item) => (
            <li key={item.key}>
              <Flex gap={8} align="start">
                <WarningFilled style={{ color: WARNING, marginTop: 4 }} aria-label="Cảnh báo" />
                <div>{item.content}</div>
              </Flex>
            </li>
          ))}
        </Flex>
      )}
    </Card>
  )
}

/** Hai biểu đồ 6 tháng (tháng đang xem ở cuối): tỷ lệ thực hiện, tỷ lệ không đạt; mỗi biểu đồ một trục. */
function TrendCharts({ data }: { data: QcDashboard }) {
  const labels = data.trend.map((m) => monthLabel(m.month))
  const charts: { title: string; note: string; chart: LineChartProps }[] = [
    {
      title: 'Tỷ lệ thực hiện 6 tháng',
      note: 'Số ngày có kết quả trên số ngày phải thực hiện (mọi ngày trong tháng).',
      chart: {
        title: 'Tỷ lệ thực hiện nội kiểm theo tháng',
        labels,
        pointLabel: 'Tháng',
        series: [{ name: 'Tỷ lệ thực hiện (%)', color: SERIES_COLORS[0], values: data.trend.map((m) => rate(m.recordedDays, m.requiredDays)) }],
        details: (i) => [`${formatNumber(data.trend[i].recordedDays)}/${formatNumber(data.trend[i].requiredDays)} ngày phải thực hiện`],
      },
    },
    {
      title: 'Tỷ lệ không đạt 6 tháng',
      note: 'Số ngày không đạt (lần chạy đầu) trên số ngày đã ghi kết quả.',
      chart: {
        title: 'Tỷ lệ không đạt nội kiểm theo tháng',
        labels,
        pointLabel: 'Tháng',
        series: [{ name: 'Tỷ lệ không đạt (%)', color: SERIES_COLORS[1], values: data.trend.map((m) => rate(m.failedDays, m.recordedDays)) }],
        details: (i) => [`${formatNumber(data.trend[i].failedDays)}/${formatNumber(data.trend[i].recordedDays)} ngày đã ghi`],
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

interface TableRow {
  key: string
  name: string
  figures: QcDashboardFigures
  latestFailure: QcDashboardFailure | null
  /** Dòng khoa. */
  books?: QcDashboardBookCounts
  /** Dòng máy. */
  meter?: QcDashboardMeter
  children?: TableRow[]
}

/** Thanh tỷ lệ thực hiện kèm số (không chỉ dựa vào màu). */
function RateBar({ figures }: { figures: QcDashboardFigures }) {
  const value = compliance(figures)
  return (
    <Flex align="center" gap={8}>
      <div aria-hidden style={{ width: 56, flexShrink: 0, height: 8, background: TRACK, borderRadius: 4, overflow: 'hidden' }}>
        <div style={{ width: `${value ?? 0}%`, height: '100%', background: SERIES_COLORS[0], borderRadius: 4 }} />
      </div>
      <span style={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
        {showRate(value)}
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {' '}
          ({figures.recordedDays}/{figures.requiredDays})
        </Typography.Text>
      </span>
    </Flex>
  )
}

function failureText(failure: QcDashboardFailure | null, withMeter: boolean) {
  if (!failure) {
    return ''
  }
  const where = withMeter ? ` (${failure.meterLabel})` : ''
  return `${dayjs(failure.date).format('DD/MM')}${where}: ${failure.reason ?? 'khoa chưa ghi nguyên nhân'}`
}

/** Trạng thái sổ của máy, kèm "Xem sổ" khi người xem mở được sổ. */
function bookStatus(meter: QcDashboardMeter) {
  if (!meter.bookId || !meter.status) {
    return <Tag>Chưa mở sổ</Tag>
  }
  return (
    <Flex vertical gap={2} align="start">
      {meter.returned ? <Tag color="error">Bị trả lại</Tag> : <QcStatusTag status={meter.status} />}
      {meter.canOpen && <Link to={`/noi-kiem/so/${meter.bookId}`}>Xem sổ</Link>}
    </Flex>
  )
}

function departmentBooks(books: QcDashboardBookCounts) {
  const total = books.open + books.returned + books.submitted + books.confirmed + books.notOpened
  const parts = [
    books.submitted > 0 && `${books.submitted} chờ xác nhận`,
    books.returned > 0 && `${books.returned} bị trả lại`,
    books.notOpened > 0 && `${books.notOpened} chưa mở sổ`,
  ].filter(Boolean)
  return (
    <Flex vertical style={{ fontSize: 12 }}>
      <span>
        {books.confirmed}/{total} đã xác nhận
      </span>
      {parts.map((part) => (
        <Typography.Text key={String(part)} type="secondary" style={{ fontSize: 12 }}>
          {part}
        </Typography.Text>
      ))}
    </Flex>
  )
}

const count = (value: number, flag?: 'danger' | 'warning') =>
  value > 0 && flag ? <Typography.Text type={flag}>{formatNumber(value)}</Typography.Text> : formatNumber(value)

/** Theo khoa (bấm dấu + để xem từng máy), theo tên khoa; sắp xếp được theo tỷ lệ thực hiện. */
function DepartmentTable({ data }: { data: QcDashboard }) {
  const rows: TableRow[] = data.departments.map((d) => ({
    key: d.departmentId,
    name: d.departmentName,
    figures: d.figures,
    latestFailure: d.latestFailure,
    books: d.books,
    children: d.meters.map((m) => ({
      key: `${d.departmentId}:${m.meterId}`,
      name: m.meterLabel,
      figures: m.figures,
      latestFailure: m.latestFailure,
      meter: m,
    })),
  }))

  const columns: TableColumnsType<TableRow> = [
    {
      title: 'Khoa / máy',
      key: 'name',
      fixed: 'left',
      width: 280,
      render: (_, row) =>
        row.meter ? (
          <Flex vertical gap={2} align="start">
            <span>{row.name}</span>
            {!row.meter.meterActive && <Tag style={{ marginInlineEnd: 0 }}>Ngưng sử dụng</Tag>}
          </Flex>
        ) : (
          <>
            <Typography.Text strong>{row.name}</Typography.Text>
            <Typography.Text type="secondary"> · {row.children?.length ?? 0} máy</Typography.Text>
          </>
        ),
    },
    {
      title: 'Thực hiện',
      key: 'rate',
      width: 160,
      sorter: (a, b) => (compliance(a.figures) ?? -1) - (compliance(b.figures) ?? -1),
      render: (_, row) => <RateBar figures={row.figures} />,
    },
    {
      title: 'Chưa ghi',
      key: 'missing',
      align: 'right',
      width: 100,
      render: (_, row) => (
        <Flex vertical align="end">
          {count(row.figures.missingDays, 'danger')}
          {row.meter && row.meter.missingDates.length > 0 && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              ngày {row.meter.missingDates.map((d) => dayjs(d).date()).join(', ')}
            </Typography.Text>
          )}
        </Flex>
      ),
    },
    { title: 'Ghi muộn', key: 'late', align: 'right', width: 80, render: (_, row) => count(row.figures.lateDays) },
    { title: 'Không đạt', key: 'failed', align: 'right', width: 80, render: (_, row) => count(row.figures.failedDays, 'danger') },
    {
      title: <Tooltip title="Không đạt mà khoa chưa chạy lại QC: cần nhắc khoa">Chưa chạy lại</Tooltip>,
      key: 'notRepeated',
      align: 'right',
      width: 90,
      render: (_, row) =>
        row.figures.notRepeatedDays > 0 ? (
          <>
            <WarningFilled style={{ color: WARNING, marginRight: 4 }} aria-label="Cần nhắc" />
            {formatNumber(row.figures.notRepeatedDays)}
          </>
        ) : (
          0
        ),
    },
    {
      title: 'Không đạt gần nhất',
      key: 'latestFailure',
      width: 190,
      render: (_, row) => <span style={{ fontSize: 13 }}>{failureText(row.latestFailure, !row.meter)}</span>,
    },
    {
      title: <Tooltip title="Ngày đến hôm nay Khoa HSH chưa xem xét">Chờ xem xét</Tooltip>,
      key: 'review',
      align: 'right',
      width: 90,
      render: (_, row) => (
        <Flex vertical align="end">
          {count(row.figures.daysToReview, 'warning')}
          <Typography.Text type="secondary" style={{ fontSize: 12, whiteSpace: 'nowrap' }}>
            đã xem {row.figures.reviewedDays}
          </Typography.Text>
        </Flex>
      ),
    },
    {
      title: 'Sổ tháng',
      key: 'book',
      width: 170,
      render: (_, row) => (row.meter ? bookStatus(row.meter) : row.books ? departmentBooks(row.books) : null),
    },
  ]

  return (
    <Card size="small" title={`Theo khoa – tháng ${monthLabel(data.month)}`} style={{ marginTop: 16 }}>
      <Table
        // Đổi tháng thì mở lại theo số khoa của tháng mới.
        key={data.month}
        size="small"
        pagination={false}
        dataSource={rows}
        columns={columns}
        expandable={{ defaultExpandAllRows: rows.length <= 3 }}
        scroll={{ x: 1240 }}
        locale={{ emptyText: 'Tháng này chưa có máy đo nào. Khoa HSH khai báo máy đo ở mục "Máy đo và lô vật tư".' }}
      />
    </Card>
  )
}
