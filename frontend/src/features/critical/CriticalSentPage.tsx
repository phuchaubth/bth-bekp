import { PhoneOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Col, Empty, Flex, Input, Row, Select, Space, Spin, Table, Tag, Typography, type TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useDepartments } from '../../api/admin'
import { accountDepartments } from '../../api/auth'
import { GENDER_LABELS, useCriticalSent, useCriticalSettings, type CriticalResult } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { StatTile } from '../../components/DashboardCards'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { CallPendingTag, CriticalSectionNav, CriticalStatusTag, DeadlineClock, OnTimeTag, ResultValue } from './CriticalParts'
import { ConfirmCallModal, NotifyModal } from './CriticalForms'
import { dateOf, minutesBetween, secondsLeft, timeOf, useServerNow } from './criticalUtils'

const isClosed = (r: CriticalResult) => r.status === 'Completed' || r.status === 'Cancelled'

function matches(r: CriticalResult, keyword: string) {
  const k = keyword.trim().toLocaleLowerCase('vi')
  return !k || [r.patientName, r.patientCode, r.number, r.testName].some((v) => v.toLocaleLowerCase('vi').includes(k))
}

function median(values: number[]): number | null {
  if (values.length === 0) return null
  const sorted = [...values].sort((a, b) => a - b)
  const middle = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2
}

/** Dòng mô tả bước đang chờ của hồ sơ đang mở; báo trên phần mềm thì nhắc gọi xác nhận người nhận (quyết định 68). */
function stepLine(r: CriticalResult): string {
  const rejected = r.notifications.filter((n) => n.rejectedAtUtc).at(-1)
  const call = r.callPending ? ' · chưa gọi xác nhận người nhận' : ''
  switch (r.status) {
    case 'AwaitingNotification':
      return rejected
        ? `${rejected.receivingDepartmentName} trả lại lúc ${timeOf(rejected.rejectedAtUtc)}: ${rejected.rejectReason}. Gọi lại đúng khoa.`
        : `Có kết quả ${timeOf(r.resultAtUtc)} · chưa báo khoa điều trị`
    case 'AwaitingAcknowledgement':
      return r.callPending
        ? `Đã báo ${r.receivingDepartmentName} trên phần mềm lúc ${timeOf(r.notifiedAtUtc)}${call} · chờ khoa xác nhận`
        : `Đã báo ${r.receivingDepartmentName} lúc ${timeOf(r.notifiedAtUtc)} · ${r.notifications.at(-1)?.receiverName ?? ''} nghe máy · chờ khoa xác nhận`
    case 'AwaitingAction':
      return `${r.acknowledgedByName} (${r.receivingDepartmentName}) đã nhận lúc ${timeOf(r.acknowledgedAtUtc)}${call} · chờ bác sĩ xử trí`
    default:
      return ''
  }
}

/** Nút ghi cuộc gọi xác nhận cho hồ sơ đã báo trên phần mềm (khoa báo). */
function CallButton({ record, onCall }: { record: CriticalResult; onCall: (record: CriticalResult) => void }) {
  if (!record.can.confirmCall) return null
  return (
    <Button size="small" icon={<PhoneOutlined />} onClick={() => onCall(record)}>
      Ghi cuộc gọi
    </Button>
  )
}

function OpenRecord({
  record,
  nowMs,
  warnMinutes,
  onNotify,
  onCall,
}: {
  record: CriticalResult
  nowMs: number
  warnMinutes: number
  onNotify: (record: CriticalResult) => void
  onCall: (record: CriticalResult) => void
}) {
  const waiting = record.status === 'AwaitingNotification'
  const due = waiting ? record.notifyDueAtUtc : (record.actionDueAtUtc ?? record.notifyDueAtUtc)
  return (
    <Card size="small" role="article" aria-label={`${record.patientName} · ${record.testName}`}>
      <Row gutter={[16, 8]} align="middle">
        <Col xs={24} md={6}>
          <DeadlineClock dueUtc={due} nowMs={nowMs} warnMinutes={warnMinutes} label={waiting ? 'Hạn báo' : 'BS xử trí'} />
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              hạn {timeOf(due)}
            </Typography.Text>
          </div>
        </Col>
        <Col xs={24} md={12}>
          <Typography.Text strong>{record.patientName}</Typography.Text>
          <Typography.Text type="secondary">
            {' '}
            · {record.patientCode} · {dateOf(record.patientDateOfBirth)}
            {record.patientGender ? ` · ${GENDER_LABELS[record.patientGender]}` : ''}
          </Typography.Text>
          <div>
            <ResultValue testName={record.testName} value={record.value} unit={record.unit} />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {' '}
              · ngưỡng nguy cấp {record.testAdultRange}
            </Typography.Text>
          </div>
          <Typography.Text type="secondary" style={{ fontSize: 13 }}>
            {stepLine(record)}
          </Typography.Text>
        </Col>
        <Col xs={24} md={6}>
          <Flex vertical gap={6} align="flex-end">
            <Space size={4} wrap style={{ justifyContent: 'flex-end' }}>
              <CriticalStatusTag status={record.status} />
              <CallPendingTag pending={record.callPending} />
            </Space>
            <Space wrap style={{ justifyContent: 'flex-end' }}>
              {record.can.notify && (
                <Button type="primary" size="small" onClick={() => onNotify(record)}>
                  Ghi lần báo
                </Button>
              )}
              <CallButton record={record} onCall={onCall} />
              <Link to={`/ket-qua-nguy-cap/ho-so/${record.id}`}>Xem hồ sơ</Link>
            </Space>
          </Flex>
        </Col>
      </Row>
    </Card>
  )
}

/**
 * Khoa tôi báo (quyết định 60): bàn làm việc của khoa cận lâm sàng: hồ sơ chờ gọi báo, đã báo khoa điều trị chưa xong (hạn
 * gần nhất trước), hồ sơ vừa xong. Hồ sơ đã báo trên phần mềm mà chưa gọi xác nhận người nhận có nhãn và nút Ghi cuộc gọi, ở lại
 * đây tới khi ghi cuộc gọi (quyết định 68). Tự cập nhật theo chu kỳ cấu hình; đồng hồ theo giờ máy chủ.
 */
export function CriticalSentPage() {
  const { user, hasPermission } = useAuth()
  const navigate = useNavigate()
  const settings = useCriticalSettings()
  const values = settings.data?.values
  const poll = values?.pollSeconds ?? 15
  const warn = values?.warnMinutes ?? 3
  const canRecord = hasPermission(Permissions.CriticalRecord)
  const monitor = hasPermission(Permissions.CriticalMonitor)
  const own = accountDepartments(user)
  const departments = useDepartments(false)
  const [departmentId, setDepartmentId] = useState<string | undefined>(undefined)
  const [keyword, setKeyword] = useState('')
  const [notifying, setNotifying] = useState<CriticalResult | null>(null)
  const [calling, setCalling] = useState<CriticalResult | null>(null)
  const list = useCriticalSent(departmentId, poll)
  const nowMs = useServerNow(list.data?.serverTimeUtc, list.dataUpdatedAt)

  const items = useMemo(() => (list.data?.items ?? []).filter((r) => matches(r, keyword)), [list.data, keyword])
  const open = items.filter((r) => !isClosed(r))
  const closed = items.filter(isClosed)
  const all = list.data?.items ?? []
  const waiting = all.filter((r) => r.status === 'AwaitingNotification')
  const inWard = all.filter((r) => r.status === 'AwaitingAcknowledgement' || r.status === 'AwaitingAction')
  const callPending = all.filter((r) => r.callPending)
  const soon = waiting.filter((r) => {
    const left = secondsLeft(r.notifyDueAtUtc, nowMs)
    return left >= 0 && left < warn * 60
  }).length
  const overdue = waiting.filter((r) => secondsLeft(r.notifyDueAtUtc, nowMs) < 0).length
  const doctorLate = inWard.filter((r) => r.actionDueAtUtc && secondsLeft(r.actionDueAtUtc, nowMs) < 0).length
  const today = dayjs(nowMs)
  const notifiedToday = all.filter((r) => r.notifiedAtUtc && dayjs(r.notifiedAtUtc).isSame(today, 'day') && r.status !== 'Cancelled')
  const medianMinutes = median(notifiedToday.map((r) => minutesBetween(r.resultAtUtc, r.notifiedAtUtc!)))
  const departmentOptions = monitor
    ? (departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))
    : own.map((d) => ({ value: d.id, label: d.name }))

  const closedColumns: TableColumnsType<CriticalResult> = [
    {
      title: 'Người bệnh',
      key: 'patient',
      render: (_, r) => (
        <>
          <Typography.Text strong>{r.patientName}</Typography.Text>
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {r.patientCode} · {r.number}
            </Typography.Text>
          </div>
        </>
      ),
    },
    { title: 'Kết quả', key: 'test', render: (_, r) => <ResultValue testName={r.testName} value={r.value} unit={r.unit} /> },
    { title: 'Khoa nhận', key: 'to', render: (_, r) => r.receivingDepartmentName ?? '—' },
    {
      title: 'Trạng thái',
      key: 'status',
      render: (_, r) => (
        <>
          <Space size={4} wrap>
            <CriticalStatusTag status={r.status} />
            <CallPendingTag pending={r.callPending} />
          </Space>
          {r.cancelReason && (
            <div>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {r.cancelReason}
              </Typography.Text>
            </div>
          )}
        </>
      ),
    },
    {
      title: 'Báo',
      key: 'notify',
      render: (_, r) =>
        r.notifiedAtUtc ? <OnTimeTag onTime={r.notifiedOnTime} minutes={minutesBetween(r.resultAtUtc, r.notifiedAtUtc)} label="sau" /> : '—',
    },
    {
      title: 'BS xử trí',
      key: 'act',
      render: (_, r) =>
        r.actionAtUtc && r.notifiedAtUtc ? (
          <OnTimeTag onTime={r.actedOnTime} minutes={minutesBetween(r.notifiedAtUtc, r.actionAtUtc)} label="sau" />
        ) : (
          '—'
        ),
    },
    {
      title: '',
      key: 'view',
      render: (_, r) => (
        <Space wrap>
          <CallButton record={r} onCall={setCalling} />
          <Link to={`/ket-qua-nguy-cap/ho-so/${r.id}`}>Xem hồ sơ</Link>
        </Space>
      ),
    },
  ]

  return (
    <>
      <CriticalSectionNav />
      <PageHeader
        title="Khoa tôi báo"
        extra={
          canRecord && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/ket-qua-nguy-cap/ghi')}>
              Ghi kết quả nguy cấp
            </Button>
          )
        }
      />
      <Typography.Paragraph type="secondary">
        {own.map((d) => d.name).join(', ') || 'Toàn viện'} · gọi điện cho khoa điều trị, rồi ghi một lần: sổ ST01 và sổ ST02 của khoa nhận
        cùng lấy từ đây. Chưa gọi được thì Lưu, gọi sau: khoa nhận được báo ngay trên phần mềm, gọi sau để ghi người nhận.
      </Typography.Paragraph>
      {list.isError && <Alert type="error" showIcon title={errorMessage(list.error)} style={{ marginBottom: 16 }} />}
      <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
        <Col xs={24} sm={12} xl={6}>
          <StatTile label="Chờ gọi báo" value={waiting.length}>
            {overdue > 0 ? `${overdue} hồ sơ đã quá hạn báo` : soon > 0 ? `${soon} hồ sơ còn dưới ${warn} phút` : 'Không có hồ sơ sắp quá hạn'}
          </StatTile>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <StatTile label="Chưa gọi xác nhận" value={callPending.length}>
            {callPending.length > 0 ? 'Đã báo trên phần mềm: gọi để ghi người nhận' : 'Không có hồ sơ chờ gọi xác nhận'}
          </StatTile>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <StatTile label="Đã báo, khoa điều trị chưa xong" value={inWard.length}>
            {doctorLate > 0 ? `${doctorLate} hồ sơ bác sĩ quá hạn xử trí` : 'Không có hồ sơ quá hạn xử trí'}
          </StatTile>
        </Col>
        <Col xs={24} sm={12} xl={6}>
          <StatTile label="Hôm nay đã báo" value={notifiedToday.length}>
            {medianMinutes === null ? 'Chưa có lần báo hôm nay' : `trung vị ${medianMinutes} phút từ lúc có kết quả`}
          </StatTile>
        </Col>
      </Row>
      <Flex gap={12} wrap style={{ marginBottom: 12 }}>
        <Input
          allowClear
          prefix={<SearchOutlined />}
          placeholder="Tìm tên, mã người bệnh, số hồ sơ, xét nghiệm"
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
          style={{ maxWidth: 360 }}
          aria-label="Tìm hồ sơ"
        />
        {departmentOptions.length > 1 && (
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={monitor ? 'Mọi khoa' : 'Mọi khoa của tôi'}
            options={departmentOptions}
            value={departmentId}
            onChange={setDepartmentId}
            style={{ minWidth: 240 }}
            aria-label="Khoa báo"
          />
        )}
      </Flex>
      {list.isLoading ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <>
          <Typography.Title level={5}>Đang chờ ({open.length})</Typography.Title>
          {open.length === 0 ? (
            <Empty description={keyword ? 'Không có hồ sơ khớp ô tìm kiếm.' : 'Không có hồ sơ đang chờ.'} style={{ margin: '16px 0 24px' }} />
          ) : (
            <Flex vertical gap={8} style={{ marginBottom: 24 }}>
              {open.map((r) => (
                <OpenRecord key={r.id} record={r} nowMs={nowMs} warnMinutes={warn} onNotify={setNotifying} onCall={setCalling} />
              ))}
            </Flex>
          )}
          <Typography.Title level={5}>
            Vừa xong <Tag>{closed.length}</Tag>
          </Typography.Title>
          <Table<CriticalResult>
            rowKey="id"
            size="small"
            columns={closedColumns}
            dataSource={closed}
            pagination={{ pageSize: 20, hideOnSinglePage: true }}
            scroll={{ x: 900 }}
            locale={{ emptyText: 'Chưa có hồ sơ hoàn tất hay đã hủy.' }}
          />
          <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 12 }}>
            Đồng hồ chạy từ giờ có kết quả. Còn dưới {warn} phút thì chuyển màu cam, quá hạn thì chuyển màu đỏ. Mốc {values?.notifyMinutes ?? 10} phút
            và {warn} phút lấy từ trang Danh mục và cấu hình; hồ sơ hoàn tất, đã hủy còn ở đây {values?.keepHours ?? 24} giờ, sau đó xem trong Sổ kết quả nguy cấp
            (hồ sơ chưa gọi xác nhận ở lại tới khi ghi cuộc gọi).
          </Typography.Paragraph>
        </>
      )}
      {notifying && <NotifyModal open result={notifying} departments={departments.data ?? []} onClose={() => setNotifying(null)} />}
      {calling && <ConfirmCallModal open result={calling} onClose={() => setCalling(null)} />}
    </>
  )
}
