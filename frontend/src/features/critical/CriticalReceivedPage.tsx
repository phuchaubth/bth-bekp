import { DesktopOutlined } from '@ant-design/icons'
import { useMutation } from '@tanstack/react-query'
import { Alert, App, Badge, Button, Card, Col, Empty, Flex, Row, Select, Space, Spin, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useDepartments } from '../../api/admin'
import { accountDepartments } from '../../api/auth'
import { criticalApi, GENDER_LABELS, useCriticalReceived, useCriticalSettings, type CriticalResult } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import { hasAnyPermission, Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { CriticalSectionNav, CriticalStatusTag, DeadlineClock, OnTimeTag, PatientDepartmentTag, ResultValue } from './CriticalParts'
import { RejectModal } from './CriticalForms'
import { useCriticalSaved } from './criticalHooks'
import { dateOf, minutesBetween, SCREEN_PATH, SCREEN_PERMISSIONS, secondsLeft, seesManyDepartments, timeOf, useServerNow } from './criticalUtils'

function PatientLine({ record }: { record: CriticalResult }) {
  return (
    <>
      <Typography.Text strong style={{ fontSize: 16 }}>
        {record.patientName}
      </Typography.Text>
      <div>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {record.patientCode} · {dateOf(record.patientDateOfBirth)}
          {record.patientGender ? ` · ${GENDER_LABELS[record.patientGender]}` : ''}
        </Typography.Text>
      </div>
    </>
  )
}

function ColumnTitle({ title, count, color }: { title: string; count: number; color: string }) {
  return (
    <Space>
      <Typography.Text strong>{title}</Typography.Text>
      <Badge count={count} showZero color={color} />
    </Space>
  )
}

/**
 * Gửi đến khoa tôi (quyết định 60): người nghe máy bấm "Đã nhận cuộc gọi" bằng tài khoản của mình (hoặc trả lại khi không phải
 * khoa của người bệnh), báo trực tiếp bác sĩ; bác sĩ ghi hướng xử trí trong thời hạn tính từ giờ báo.
 */
export function CriticalReceivedPage() {
  const { user, hasPermission } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const settings = useCriticalSettings()
  const values = settings.data?.values
  const poll = values?.pollSeconds ?? 15
  const warn = values?.warnMinutes ?? 3
  const monitor = hasPermission(Permissions.CriticalMonitor)
  // QLCL, xem toàn viện, thuộc nhiều khoa: mỗi thẻ ghi khoa của người bệnh (quyết định 63).
  const many = seesManyDepartments(user, hasPermission)
  const own = accountDepartments(user)
  const departments = useDepartments(false)
  const [departmentId, setDepartmentId] = useState<string | undefined>(undefined)
  const [rejecting, setRejecting] = useState<CriticalResult | null>(null)
  const list = useCriticalReceived(departmentId, poll)
  const nowMs = useServerNow(list.data?.serverTimeUtc, list.dataUpdatedAt)
  const saved = useCriticalSaved()
  const acknowledge = useMutation({
    mutationFn: (record: CriticalResult) => criticalApi.acknowledge(record.id),
    onSuccess: (next) => {
      saved(next)
      void message.success(`Đã xác nhận nhận kết quả nguy cấp của ${next.patientName}. Báo ngay bác sĩ điều trị.`)
    },
    onError: (error) => void message.error(errorMessageWithDetails(error)),
  })

  const items = useMemo(() => list.data?.items ?? [], [list.data])
  const waiting = items.filter((r) => r.status === 'AwaitingAcknowledgement')
  const doctor = items.filter((r) => r.status === 'AwaitingAction')
  const done = items.filter((r) => r.status === 'Completed' || r.status === 'Cancelled')
  const departmentOptions = monitor
    ? (departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))
    : own.map((d) => ({ value: d.id, label: d.name }))

  return (
    <>
      <CriticalSectionNav />
      <PageHeader
        title="Gửi đến khoa tôi"
        extra={
          // Chỉ hiện với tài khoản mở được màn hình (QLCL chỉ giám sát thì không: bấm vào sẽ ra trang không có quyền).
          hasAnyPermission(SCREEN_PERMISSIONS, hasPermission) && (
            <Button icon={<DesktopOutlined />} onClick={() => navigate(SCREEN_PATH)}>
              Mở màn hình theo dõi (TV)
            </Button>
          )
        }
      />
      <Typography.Paragraph type="secondary">
        {own.map((d) => d.name).join(', ') || 'Toàn viện'} · người nghe máy bấm Đã nhận cuộc gọi, báo trực tiếp bác sĩ; bác sĩ ghi hướng xử trí
        trong {values?.actionMinutes ?? 10} phút kể từ giờ báo.
      </Typography.Paragraph>
      {departmentOptions.length > 1 && (
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={monitor ? 'Mọi khoa' : 'Mọi khoa của tôi'}
          options={departmentOptions}
          value={departmentId}
          onChange={setDepartmentId}
          style={{ minWidth: 240, marginBottom: 12 }}
          aria-label="Khoa nhận"
        />
      )}
      {list.isError && <Alert type="error" showIcon title={errorMessage(list.error)} style={{ marginBottom: 16 }} />}
      {list.isLoading ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <Row gutter={[16, 16]}>
          <Col xs={24} lg={9}>
            <Card size="small" title={<ColumnTitle title="Chờ khoa xác nhận" count={waiting.length} color="#c62828" />}>
              {waiting.length === 0 ? (
                <Empty description="Không còn hồ sơ chờ khoa xác nhận." />
              ) : (
                <Flex vertical gap={12}>
                  {waiting.map((r) => (
                    <Card key={r.id} size="small" style={{ borderColor: '#f1b5b5', background: '#fff6f6' }} role="article" aria-label={r.patientName}>
                      <Flex justify="space-between" align="start" gap={8} wrap>
                        <Space size={4} wrap>
                          <Tag color="error">MỚI · {timeOf(r.notifiedAtUtc)}</Tag>
                          {many && <PatientDepartmentTag name={r.receivingDepartmentName} />}
                        </Space>
                        {r.actionDueAtUtc && <DeadlineClock dueUtc={r.actionDueAtUtc} nowMs={nowMs} warnMinutes={warn} label="BS xử trí" />}
                      </Flex>
                      <PatientLine record={r} />
                      <div style={{ margin: '6px 0' }}>
                        <ResultValue testName={r.testName} value={r.value} unit={r.unit} />
                        <div>
                          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                            ngưỡng {r.testAdultRange} · chỉ định {dateOf(r.orderDate)}
                          </Typography.Text>
                        </div>
                      </div>
                      <Typography.Paragraph style={{ fontSize: 13, marginBottom: 8 }}>
                        Khoa báo: {r.sendingDepartmentName} · {r.notifications.at(-1)?.notifiedByName}
                        <br />
                        Giờ có KQ {timeOf(r.resultAtUtc)} · báo lúc {timeOf(r.notifiedAtUtc)} (sau {minutesBetween(r.resultAtUtc, r.notifiedAtUtc!)} phút)
                        <br />
                        Người nghe máy: {r.notifications.at(-1)?.receiverName} · đọc lại chính xác
                      </Typography.Paragraph>
                      <Space wrap>
                        {r.can.acknowledge && (
                          <Button type="primary" loading={acknowledge.isPending && acknowledge.variables?.id === r.id} onClick={() => acknowledge.mutate(r)}>
                            Đã nhận cuộc gọi
                          </Button>
                        )}
                        {r.can.act && <Button onClick={() => navigate(`/ket-qua-nguy-cap/ho-so/${r.id}`)}>Bác sĩ ghi hướng xử trí</Button>}
                        {r.can.reject && (
                          <Button danger type="text" onClick={() => setRejecting(r)}>
                            Không phải khoa tôi
                          </Button>
                        )}
                      </Space>
                    </Card>
                  ))}
                </Flex>
              )}
            </Card>
          </Col>
          <Col xs={24} lg={9}>
            <Card size="small" title={<ColumnTitle title="Chờ bác sĩ xử trí" count={doctor.length} color="#b26a00" />}>
              {doctor.length === 0 ? (
                <Empty description="Không có hồ sơ chờ bác sĩ." />
              ) : (
                <Flex vertical gap={12}>
                  {doctor.map((r) => {
                    const late = r.actionDueAtUtc !== null && secondsLeft(r.actionDueAtUtc, nowMs) < 0
                    return (
                      <Card key={r.id} size="small" role="article" aria-label={r.patientName}>
                        {(r.actionDueAtUtc || many) && (
                          <Flex justify="space-between" align="start" gap={8} wrap style={{ marginBottom: 4 }}>
                            {many && <PatientDepartmentTag name={r.receivingDepartmentName} />}
                            {r.actionDueAtUtc && <DeadlineClock dueUtc={r.actionDueAtUtc} nowMs={nowMs} warnMinutes={warn} label="BS xử trí" />}
                          </Flex>
                        )}
                        <PatientLine record={r} />
                        <div style={{ margin: '6px 0' }}>
                          <ResultValue testName={r.testName} value={r.value} unit={r.unit} />
                        </div>
                        <Typography.Paragraph type="secondary" style={{ fontSize: 13, marginBottom: 8 }}>
                          {r.acknowledgedByName} nhận lúc {timeOf(r.acknowledgedAtUtc)} · {r.sendingDepartmentName} báo lúc {timeOf(r.notifiedAtUtc)}
                        </Typography.Paragraph>
                        {late && (
                          <Alert type="error" showIcon style={{ marginBottom: 8 }} title="Quá hạn: nhắc lại và báo trực tiếp bác sĩ." />
                        )}
                        <Space wrap>
                          {r.can.act && (
                            <Button type="primary" onClick={() => navigate(`/ket-qua-nguy-cap/ho-so/${r.id}`)}>
                              Bác sĩ ghi hướng xử trí
                            </Button>
                          )}
                          <Link to={`/ket-qua-nguy-cap/ho-so/${r.id}`}>Xem hồ sơ</Link>
                        </Space>
                      </Card>
                    )
                  })}
                </Flex>
              )}
            </Card>
          </Col>
          <Col xs={24} lg={6}>
            <Card size="small" title={<ColumnTitle title="Vừa xong" count={done.length} color="#1d7a3a" />}>
              {done.length === 0 ? (
                <Empty description="Chưa có hồ sơ hoàn tất." />
              ) : (
                <Flex vertical gap={10}>
                  {done.map((r) => (
                    <div key={r.id}>
                      <Flex justify="space-between" gap={8}>
                        <Link to={`/ket-qua-nguy-cap/ho-so/${r.id}`}>
                          <Typography.Text strong>{r.patientName}</Typography.Text>
                        </Link>
                        <Typography.Text type="secondary">{timeOf(r.actionAtUtc ?? r.cancelledAtUtc)}</Typography.Text>
                      </Flex>
                      {many && (
                        <div>
                          <PatientDepartmentTag name={r.receivingDepartmentName} />
                        </div>
                      )}
                      <ResultValue testName={r.testName} value={r.value} unit={r.unit} />
                      <div>
                        {r.status === 'Cancelled' ? (
                          <>
                            <CriticalStatusTag status={r.status} />
                            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                              {r.cancelReason}
                            </Typography.Text>
                          </>
                        ) : (
                          r.actionAtUtc &&
                          r.notifiedAtUtc && (
                            <OnTimeTag onTime={r.actedOnTime} minutes={minutesBetween(r.notifiedAtUtc, r.actionAtUtc)} label="BS xử trí sau" />
                          )
                        )}
                      </div>
                    </div>
                  ))}
                </Flex>
              )}
              <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 12, marginBottom: 0 }}>
                Hồ sơ hoàn tất ở đây {values?.keepHours ?? 24} giờ (thông số cấu hình), sau đó xem trong Sổ kết quả nguy cấp.
              </Typography.Paragraph>
            </Card>
          </Col>
        </Row>
      )}
      {rejecting && <RejectModal open result={rejecting} onClose={() => setRejecting(null)} />}
    </>
  )
}
