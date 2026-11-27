import { useMutation } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, Descriptions, Flex, Result, Row, Space, Spin, Tag, Timeline, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { useDepartments } from '../../api/admin'
import { ApiError } from '../../api/http'
import { CRITICAL_METHOD_LABELS, criticalApi, GENDER_LABELS, useCriticalResult, useCriticalSettings, type CriticalResult } from '../../api/critical'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { ActionForm, CancelModal, NotifyModal, RejectModal } from './CriticalForms'
import { CriticalSectionNav, CriticalStatusTag, DeadlineClock, OnTimeTag, ResultValue } from './CriticalParts'
import { useCriticalSaved } from './criticalHooks'
import { dateOf, dateTimeOf, minutesBetween, timeOf, useServerNow } from './criticalUtils'

interface TimelineEvent {
  at: string
  title: string
  who?: string | null
  text?: ReactNode
  color: string
  badge?: ReactNode
}

/** Diễn biến hồ sơ theo thời gian: ghi kết quả, các lần báo (kể cả bị trả lại), xác nhận, xử trí, hủy. */
function eventsOf(r: CriticalResult): TimelineEvent[] {
  const events: TimelineEvent[] = [
    {
      at: r.resultAtUtc,
      title: 'Có kết quả nguy cấp',
      who: `${r.sendingDepartmentName} · ghi bởi ${r.recordedByName} lúc ${dateTimeOf(r.recordedAtUtc)}`,
      color: 'gray',
    },
  ]
  for (const n of r.notifications) {
    events.push({
      at: n.notifiedAtUtc,
      title: `Lần báo ${n.attemptNo}: ${CRITICAL_METHOD_LABELS[n.method].toLowerCase()} cho ${n.receivingDepartmentName}`,
      who: n.notifiedByName,
      text: `Người nhận ${n.receiverName}; đọc lại chính xác${n.incorrectReadBacks > 0 ? ` sau ${n.incorrectReadBacks} lần đọc sai` : ''}${
        n.repeatedTwice ? '; đã nhắc lại 2 lần' : ''
      }.`,
      color: 'blue',
    })
    if (n.rejectedAtUtc) {
      events.push({
        at: n.rejectedAtUtc,
        title: `${n.receivingDepartmentName} trả lại lần báo ${n.attemptNo}`,
        who: n.rejectedByName,
        text: `Lý do: ${n.rejectReason}`,
        color: 'orange',
      })
    }
  }
  if (r.acknowledgedAtUtc) {
    events.push({ at: r.acknowledgedAtUtc, title: 'Khoa xác nhận đã nhận cuộc gọi', who: r.acknowledgedByName, color: 'blue' })
  }
  if (r.actionAtUtc) {
    events.push({
      at: r.actionAtUtc,
      title: 'Bác sĩ ghi hướng xử trí ban đầu',
      who: r.actionByName,
      text: (
        <>
          {r.actionSummary}
          <br />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {r.recordedInMedicalRecord ? 'Đã ghi vào hồ sơ bệnh án · ' : ''}ghi lúc {dateTimeOf(r.actionRecordedAtUtc)}
          </Typography.Text>
        </>
      ),
      color: 'green',
      badge: <OnTimeTag onTime={r.actedOnTime} />,
    })
  }
  if (r.cancelledAtUtc) {
    events.push({ at: r.cancelledAtUtc, title: 'Hủy hồ sơ báo nhầm', who: r.cancelledByName, text: `Lý do: ${r.cancelReason}`, color: 'red' })
  }
  return events.sort((a, b) => Date.parse(a.at) - Date.parse(b.at))
}

/** Đường dẫn sổ chứa hồ sơ: sổ của khoa báo (ST01 hoặc Vi sinh) và ST02 của khoa nhận, tháng theo giờ có kết quả. */
function logbookLinks(r: CriticalResult) {
  const month = dayjs(r.resultAtUtc).format('YYYY-MM')
  const sendingForm = r.logbook === 'Microbiology' ? 'Microbiology' : 'Laboratory'
  const links = [
    {
      label: `${r.logbook === 'Microbiology' ? 'Sổ Vi sinh' : 'ST01'} · ${r.sendingDepartmentName}`,
      to: `/ket-qua-nguy-cap/so?mau=${sendingForm}&khoa=${r.sendingDepartmentId}&thang=${month}`,
    },
  ]
  if (r.receivingDepartmentId) {
    links.push({
      label: `ST02 · ${r.receivingDepartmentName}`,
      to: `/ket-qua-nguy-cap/so?mau=Clinical&khoa=${r.receivingDepartmentId}&thang=${month}`,
    })
  }
  return links
}

/** Chi tiết hồ sơ kết quả nguy cấp (quyết định 60); bác sĩ khoa nhận ghi hướng xử trí ngay trên trang (dùng được trên điện thoại). */
export function CriticalDetailPage() {
  const { id = '' } = useParams()
  const { message } = App.useApp()
  const settings = useCriticalSettings()
  const warn = settings.data?.values.warnMinutes ?? 3
  const record = useCriticalResult(id, settings.data?.values.pollSeconds ?? 15)
  const departments = useDepartments(false)
  const nowMs = useServerNow(undefined, 0)
  const [dialog, setDialog] = useState<'notify' | 'reject' | 'cancel' | null>(null)
  const [justActed, setJustActed] = useState<CriticalResult | null>(null)
  const saved = useCriticalSaved()
  const acknowledge = useMutation({
    mutationFn: () => criticalApi.acknowledge(id),
    onSuccess: (next) => {
      saved(next)
      void message.success('Đã xác nhận nhận cuộc gọi. Báo ngay bác sĩ điều trị.')
    },
    onError: (error) => void message.error(errorMessageWithDetails(error)),
  })

  if (record.isLoading) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }
  if (record.isError || !record.data) {
    const notFound = record.error instanceof ApiError && record.error.status === 404
    return (
      <Result
        status={notFound ? '404' : 'error'}
        title={notFound ? 'Không tìm thấy hồ sơ' : 'Không tải được hồ sơ'}
        subTitle={notFound ? 'Hồ sơ không có hoặc không thuộc khoa của anh chị.' : errorMessage(record.error)}
        extra={<Link to="/">Về Tổng quan</Link>}
      />
    )
  }

  const r = record.data
  const notifyMinutes = r.notifiedAtUtc ? minutesBetween(r.resultAtUtc, r.notifiedAtUtc) : null
  const actionMinutes = r.actionAtUtc && r.notifiedAtUtc ? minutesBetween(r.notifiedAtUtc, r.actionAtUtc) : null
  const open = r.status !== 'Completed' && r.status !== 'Cancelled'

  return (
    <>
      <CriticalSectionNav />
      <PageHeader
        title={`Hồ sơ ${r.number}`}
        extra={
          <Space wrap>
            <CriticalStatusTag status={r.status} />
            {r.lateEntry && <Tag color="purple">Ghi bù</Tag>}
            {r.can.notify && (
              <Button type="primary" onClick={() => setDialog('notify')}>
                Ghi lần báo
              </Button>
            )}
            {r.can.cancel && (
              <Button danger onClick={() => setDialog('cancel')}>
                Hủy hồ sơ báo nhầm
              </Button>
            )}
          </Space>
        }
      />
      {r.status === 'Cancelled' && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={`Đã hủy lúc ${dateTimeOf(r.cancelledAtUtc)} bởi ${r.cancelledByName}`}
          description={`Lý do: ${r.cancelReason}. Sổ in dòng này gạch ngang kèm lý do; dashboard đếm riêng, không tính vào tỷ lệ đúng hạn.`}
        />
      )}
      {justActed && (
        <Alert
          type="success"
          showIcon
          style={{ marginBottom: 16 }}
          title={`Đã lưu hướng xử trí lúc ${timeOf(justActed.actionAtUtc)}, sau ${minutesBetween(justActed.notifiedAtUtc!, justActed.actionAtUtc!)} phút kể từ khi khoa nhận.`}
          description={`Sổ ST02 của khoa đã đủ cột hướng xử trí. ${justActed.sendingDepartmentName} thấy hồ sơ hoàn tất.`}
        />
      )}
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={14}>
          <Flex vertical gap={16}>
            {(r.can.act || r.can.acknowledge) && (
              <Card
                size="small"
                title={r.can.act ? 'Bác sĩ ghi hướng xử trí' : 'Khoa điều trị xác nhận đã nhận'}
                extra={r.actionDueAtUtc && <DeadlineClock dueUtc={r.actionDueAtUtc} nowMs={nowMs} warnMinutes={warn} label="BS xử trí" />}
                style={{ borderColor: '#f1b5b5' }}
              >
                <Typography.Paragraph type="secondary">
                  Hạn xử trí {timeOf(r.actionDueAtUtc)}: {r.actionDeadlineMinutes} phút từ giờ khoa nhận cuộc gọi ({timeOf(r.notifiedAtUtc)}).
                </Typography.Paragraph>
                {r.can.act ? (
                  <ActionForm result={r} onSaved={setJustActed} />
                ) : (
                  <Space wrap>
                    <Button type="primary" loading={acknowledge.isPending} onClick={() => acknowledge.mutate()}>
                      Đã nhận cuộc gọi
                    </Button>
                    {r.can.reject && (
                      <Button danger type="text" onClick={() => setDialog('reject')}>
                        Không phải khoa tôi
                      </Button>
                    )}
                  </Space>
                )}
                {r.can.act && r.can.reject && (
                  <Button danger type="link" style={{ paddingInline: 0, marginTop: 8 }} onClick={() => setDialog('reject')}>
                    Không phải khoa tôi
                  </Button>
                )}
              </Card>
            )}
            <Card size="small" title="Kết quả nguy cấp">
              <ResultValue testName={r.testName} value={r.value} unit={r.unit} />
              <Descriptions
                size="small"
                column={{ xs: 1, sm: 2 }}
                style={{ marginTop: 8 }}
                items={[
                  { key: 'adult', label: 'Ngưỡng người lớn', children: r.testAdultRange },
                  ...(r.testChildRange ? [{ key: 'child', label: 'Ngưỡng trẻ em', children: r.testChildRange }] : []),
                  { key: 'order', label: 'Ngày chỉ định', children: dateOf(r.orderDate) },
                  { key: 'result', label: 'Giờ có kết quả', children: dateTimeOf(r.resultAtUtc) },
                  ...(r.sampleId ? [{ key: 'sid', label: 'SID', children: r.sampleId }] : []),
                  ...(r.logbook === 'Microbiology' ? [{ key: 'urgent', label: 'Mẫu khẩn', children: r.urgent ? 'Có' : 'Không' }] : []),
                  {
                    key: 'route',
                    label: 'Khoa báo → khoa nhận',
                    children: `${r.sendingDepartmentName} → ${r.receivingDepartmentName ?? 'chưa báo'}`,
                  },
                ]}
              />
            </Card>
            <Card size="small" title="Diễn biến">
              <Timeline
                items={eventsOf(r).map((e) => ({
                  color: e.color,
                  title: (
                    <Space wrap size={6}>
                      <Typography.Text strong>{dateTimeOf(e.at)}</Typography.Text>
                      <Typography.Text>{e.title}</Typography.Text>
                      {e.badge}
                    </Space>
                  ),
                  content: (
                    <>
                      {e.who && (
                        <div>
                          <Typography.Text type="secondary">{e.who}</Typography.Text>
                        </div>
                      )}
                      {e.text && <div>{e.text}</div>}
                    </>
                  ),
                }))}
              />
            </Card>
          </Flex>
        </Col>
        <Col xs={24} lg={10}>
          <Flex vertical gap={16}>
            <Card size="small" title="Người bệnh">
              <Typography.Text strong style={{ fontSize: 16 }}>
                {r.patientName}
              </Typography.Text>
              <div>
                <Typography.Text type="secondary">
                  {dateOf(r.patientDateOfBirth)}
                  {r.patientGender ? ` · ${GENDER_LABELS[r.patientGender]}` : ''} · {r.patientCode}
                </Typography.Text>
              </div>
              {r.patientAddress && <div>{r.patientAddress}</div>}
              <Tag style={{ marginTop: 8 }} color={r.patientSource === 'His' ? 'success' : 'default'}>
                {r.patientSource === 'His' ? 'Lấy từ HIS' : 'Nhập tay'}
              </Tag>
            </Card>
            <Card size="small" title={`Hai mốc ${r.notifyDeadlineMinutes === r.actionDeadlineMinutes ? `${r.notifyDeadlineMinutes} phút` : 'thời hạn'}`}>
              <Flex vertical gap={8}>
                <div>
                  <Typography.Text type="secondary">Có kết quả → báo: </Typography.Text>
                  {notifyMinutes !== null ? (
                    <OnTimeTag onTime={r.notifiedOnTime} minutes={notifyMinutes} />
                  ) : open ? (
                    <DeadlineClock dueUtc={r.notifyDueAtUtc} nowMs={nowMs} warnMinutes={warn} label="Hạn báo" />
                  ) : (
                    '—'
                  )}
                </div>
                <div>
                  <Typography.Text type="secondary">Khoa nhận → BS xử trí: </Typography.Text>
                  {actionMinutes !== null ? (
                    <OnTimeTag onTime={r.actedOnTime} minutes={actionMinutes} />
                  ) : open && r.actionDueAtUtc ? (
                    <DeadlineClock dueUtc={r.actionDueAtUtc} nowMs={nowMs} warnMinutes={warn} label="BS xử trí" />
                  ) : (
                    '—'
                  )}
                </div>
              </Flex>
            </Card>
            <Card size="small" title="Ghi sổ">
              <Flex vertical gap={4}>
                {logbookLinks(r).map((link) => (
                  <Link key={link.to} to={link.to}>
                    {link.label} · tháng {dayjs(r.resultAtUtc).format('MM/YYYY')}
                  </Link>
                ))}
              </Flex>
            </Card>
            <Card size="small" title="Thông số áp dụng cho hồ sơ này">
              <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                Hạn báo {r.notifyDeadlineMinutes} phút, hạn bác sĩ xử trí {r.actionDeadlineMinutes} phút, theo cấu hình lúc ghi. Sửa cấu hình
                sau này không làm đổi hạn của hồ sơ đã có.
              </Typography.Paragraph>
              <Typography.Text type="secondary">Tạo lúc {dateTimeOf(r.recordedAtUtc)}</Typography.Text>
            </Card>
          </Flex>
        </Col>
      </Row>
      {dialog === 'notify' && <NotifyModal open result={r} departments={departments.data ?? []} onClose={() => setDialog(null)} />}
      {dialog === 'reject' && <RejectModal open result={r} onClose={() => setDialog(null)} />}
      {dialog === 'cancel' && <CancelModal open result={r} onClose={() => setDialog(null)} />}
    </>
  )
}
