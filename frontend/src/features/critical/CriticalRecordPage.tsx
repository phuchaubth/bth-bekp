import { CheckCircleFilled, SearchOutlined } from '@ant-design/icons'
import { useMutation } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Flex,
  Form,
  Input,
  Radio,
  Result,
  Row,
  Select,
  Space,
  Spin,
  Steps,
  Tag,
  Typography,
} from 'antd'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router'
import { useDepartments } from '../../api/admin'
import { accountDepartments } from '../../api/auth'
import {
  criticalApi,
  GENDER_LABELS,
  useCriticalSettings,
  useCriticalTests,
  type CreateCriticalResultInput,
  type CriticalPatientLookup,
  type CriticalResult,
  type Gender,
} from '../../api/critical'
import { useFeatures } from '../../api/features'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { CriticalSectionNav, DeadlineClock } from './CriticalParts'
import { NotificationFields } from './CriticalForms'
import { useCriticalSaved } from './criticalHooks'
import { emptyNotification, nowToMinute, toNotificationInput, type NotificationFormValues } from './criticalInputs'
import { ageOf, minutesBetween, timeOf } from './criticalUtils'

const DATE = 'DD/MM/YYYY'
const DATE_TIME = 'DD/MM/YYYY HH:mm'

interface PatientValues {
  code?: string
  fullName?: string
  dateOfBirth?: Dayjs
  gender?: Gender
  address?: string
}

interface RecordFormValues {
  sendingDepartmentId?: string
  patient: PatientValues
  testId?: string
  value?: string
  unit?: string
  orderDate?: Dayjs
  resultAt?: Dayjs
  sampleId?: string
  urgent: boolean
  notification: NotificationFormValues
}

/** Các ô không thuộc phần "Đã báo cho khoa điều trị": kiểm tra khi "Lưu, gọi sau". */
const RECORD_FIELDS = [
  ['sendingDepartmentId'],
  ['patient', 'code'],
  ['patient', 'fullName'],
  ['patient', 'dateOfBirth'],
  ['patient', 'gender'],
  ['patient', 'address'],
  ['testId'],
  ['value'],
  ['unit'],
  ['orderDate'],
  ['resultAt'],
  ['sampleId'],
]

const initialValues = (departmentId: string | undefined): RecordFormValues => ({
  sendingDepartmentId: departmentId,
  patient: {},
  urgent: false,
  orderDate: dayjs().startOf('day'),
  resultAt: nowToMinute(),
  notification: emptyNotification(),
})

/** Thông tin người bệnh trùng đúng bản lấy từ HIS (sửa ô nào thì hồ sơ ghi "Nhập tay"). */
function sameAsHis(patient: PatientValues, his: CriticalPatientLookup | null): boolean {
  if (!his || his.status !== 'Found') return false
  return (
    (patient.code ?? '').trim() === (his.code ?? '') &&
    (patient.fullName ?? '').trim() === (his.fullName ?? '') &&
    (patient.dateOfBirth?.format('YYYY-MM-DD') ?? null) === his.dateOfBirth &&
    (patient.gender ?? null) === his.gender &&
    (patient.address ?? '').trim() === (his.address ?? '')
  )
}

function StepTitle({ n, children }: { n: number; children: ReactNode }) {
  return (
    <Space>
      <Tag color="#0d7c74" style={{ borderRadius: 12, marginInlineEnd: 0 }}>
        {n}
      </Tag>
      {children}
    </Space>
  )
}

/**
 * Ghi kết quả nguy cấp (quyết định 60): KTV gọi điện cho khoa điều trị theo kịch bản bên phải rồi ghi ngay một lần (người bệnh,
 * kết quả, lần báo). Người báo là tài khoản đang đăng nhập; giờ hệ thống ghi riêng với giờ nhập.
 */
export function CriticalRecordPage() {
  const { user, hasPermission } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const features = useFeatures()
  const settings = useCriticalSettings()
  const tests = useCriticalTests()
  const departments = useDepartments(false)
  const [form] = Form.useForm<RecordFormValues>()
  const [lookup, setLookup] = useState<CriticalPatientLookup | null>(null)
  const [looking, setLooking] = useState(false)
  const [created, setCreated] = useState<CriticalResult | null>(null)
  const saved = useCriticalSaved()

  const own = accountDepartments(user)
  const allDepartments = hasPermission(Permissions.ScopeAllDepartments)
  const sendingOptions = (allDepartments ? (departments.data ?? []) : own).map((d) => ({ value: d.id, label: d.name }))
  const defaultDepartment = own[0]?.id ?? sendingOptions[0]?.value

  const testId = Form.useWatch('testId', form)
  const values = Form.useWatch([], form) as RecordFormValues | undefined
  const activeTests = useMemo(() => (tests.data ?? []).filter((t) => t.isActive), [tests.data])
  const test = activeTests.find((t) => t.id === testId)
  const microbiology = test?.logbook === 'Microbiology'
  const notifyMinutes = settings.data?.values.notifyMinutes ?? 10
  const warnMinutes = settings.data?.values.warnMinutes ?? 3
  const requireAddress = settings.data?.values.requireAddress ?? true
  const [nowMs, setNowMs] = useState(() => Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setNowMs(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])

  const mutation = useMutation({
    mutationFn: ({ input }: { input: CreateCriticalResultInput }) => criticalApi.create(input),
    onSuccess: (result) => {
      saved(result)
      setCreated(result)
      window.scrollTo?.({ top: 0 })
    },
  })

  const sendingName = sendingOptions.find((o) => o.value === values?.sendingDepartmentId)?.label ?? ''

  async function save(withCall: boolean) {
    try {
      await (withCall ? form.validateFields() : form.validateFields(RECORD_FIELDS))
    } catch {
      return
    }
    const all = form.getFieldsValue(true) as RecordFormValues
    const patient = all.patient
    mutation.mutate({
      input: {
        sendingDepartmentId: all.sendingDepartmentId!,
        testId: all.testId!,
        patient: {
          code: (patient.code ?? '').trim(),
          fullName: (patient.fullName ?? '').trim(),
          dateOfBirth: patient.dateOfBirth?.format('YYYY-MM-DD') ?? null,
          gender: patient.gender ?? null,
          address: patient.address?.trim() || null,
          source: sameAsHis(patient, lookup) ? 'His' : 'Manual',
        },
        value: (all.value ?? '').trim(),
        unit: test?.textResult ? null : all.unit?.trim() || null,
        orderDate: all.orderDate!.format('YYYY-MM-DD'),
        resultAtUtc: all.resultAt!.toISOString(),
        sampleId: all.sampleId?.trim() || null,
        urgent: microbiology && all.urgent,
        notification: withCall ? toNotificationInput(all.notification) : null,
      },
    })
  }

  async function lookUpPatient() {
    const code = (form.getFieldValue(['patient', 'code']) as string | undefined)?.trim()
    if (!code) {
      form.setFields([{ name: ['patient', 'code'], errors: ['Nhập mã người bệnh để tra HIS.'] }])
      return
    }
    setLooking(true)
    try {
      const found = await criticalApi.lookupPatient(code)
      setLookup(found)
      if (found.status === 'Found') {
        form.setFieldsValue({
          patient: {
            code: found.code ?? code,
            fullName: found.fullName ?? undefined,
            dateOfBirth: found.dateOfBirth ? dayjs(found.dateOfBirth) : undefined,
            gender: found.gender ?? undefined,
            address: found.address ?? undefined,
          },
        })
      } else {
        void message.info(found.status === 'NotFound' ? 'Không tìm thấy người bệnh trên HIS: nhập tay.' : 'HIS đang lỗi: nhập tay họ tên, ngày sinh, địa chỉ.')
      }
    } catch (error) {
      void message.error(errorMessage(error))
    } finally {
      setLooking(false)
    }
  }

  function again() {
    setCreated(null)
    setLookup(null)
    mutation.reset()
    form.resetFields()
    form.setFieldsValue(initialValues(defaultDepartment))
  }

  if (tests.isLoading || settings.isLoading) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }

  if (created) {
    const notifiedAfter = created.notifiedAtUtc ? minutesBetween(created.resultAtUtc, created.notifiedAtUtc) : null
    return (
      <>
        <CriticalSectionNav />
        <Result
          status="success"
          icon={<CheckCircleFilled />}
          title={`Đã lưu hồ sơ ${created.number}${notifiedAfter !== null ? `, báo sau ${notifiedAfter} phút` : ''}`}
          subTitle={
            created.notifiedAtUtc
              ? `${created.receivingDepartmentName} đã thấy hồ sơ cùng dải cảnh báo đỏ. Đồng hồ ${created.actionDeadlineMinutes} phút của bác sĩ bắt đầu từ giờ báo ${timeOf(created.notifiedAtUtc)}.`
              : `Hồ sơ chờ gọi báo trong Khoa tôi báo; hạn báo ${timeOf(created.notifyDueAtUtc)}.`
          }
          extra={[
            <Button key="list" type="primary" onClick={() => navigate('/ket-qua-nguy-cap/khoa-bao')}>
              Về Khoa tôi báo
            </Button>,
            <Button key="again" onClick={again}>
              Ghi kết quả khác
            </Button>,
            <Link key="view" to={`/ket-qua-nguy-cap/ho-so/${created.id}`}>
              Xem hồ sơ
            </Link>,
          ]}
        />
      </>
    )
  }

  const resultAt = values?.resultAt
  const dueUtc = resultAt ? resultAt.add(notifyMinutes, 'minute').toISOString() : null
  const patient = values?.patient ?? {}
  const unitText = test?.textResult ? '' : (values?.unit ?? test?.unit ?? '')

  return (
    <>
      <CriticalSectionNav />
      <PageHeader title="Ghi kết quả nguy cấp" />
      <Typography.Paragraph type="secondary">
        Gọi điện theo kịch bản bên phải rồi ghi ngay. Người báo là tài khoản đang đăng nhập; giờ hệ thống ghi riêng với giờ anh chị nhập.
      </Typography.Paragraph>
      {tests.isError && <Alert type="error" showIcon title={errorMessage(tests.error)} style={{ marginBottom: 16 }} />}
      {mutation.isError && <Alert type="error" showIcon title={errorMessageWithDetails(mutation.error)} style={{ marginBottom: 16 }} />}
      <Form<RecordFormValues> form={form} layout="vertical" initialValues={initialValues(defaultDepartment)} requiredMark="optional">
        <Row gutter={[16, 16]}>
          <Col xs={24} lg={15}>
            <Flex vertical gap={16}>
              <Card size="small" title={<StepTitle n={1}>Người bệnh</StepTitle>}>
                {sendingOptions.length > 1 && (
                  <Form.Item name="sendingDepartmentId" label="Khoa báo" rules={[{ required: true, message: 'Chọn khoa báo.' }]}>
                    <Select options={sendingOptions} showSearch optionFilterProp="label" />
                  </Form.Item>
                )}
                {sendingOptions.length <= 1 && (
                  <Form.Item name="sendingDepartmentId" hidden rules={[{ required: true, message: 'Tài khoản chưa thuộc khoa nào.' }]}>
                    <Input />
                  </Form.Item>
                )}
                <Row gutter={12}>
                  <Col xs={24} sm={12}>
                    <Form.Item label="Mã người bệnh" required htmlFor="patient_code">
                      <Space.Compact style={{ width: '100%' }}>
                        <Form.Item name={['patient', 'code']} noStyle rules={[{ required: true, whitespace: true, message: 'Nhập mã người bệnh.' }]}>
                          <Input maxLength={50} placeholder="Mã y tế" />
                        </Form.Item>
                        {features.patientSource && (
                          <Button icon={<SearchOutlined />} loading={looking} onClick={() => void lookUpPatient()}>
                            Tra HIS
                          </Button>
                        )}
                      </Space.Compact>
                    </Form.Item>
                  </Col>
                  <Col xs={24} sm={12}>
                    <Form.Item name={['patient', 'fullName']} label="Họ tên" rules={[{ required: true, whitespace: true, message: 'Nhập họ tên.' }]}>
                      <Input maxLength={200} />
                    </Form.Item>
                  </Col>
                  <Col xs={24} sm={12}>
                    <Form.Item
                      name={['patient', 'dateOfBirth']}
                      label={`Ngày sinh${patient.dateOfBirth ? ` (${ageOf(patient.dateOfBirth.format('YYYY-MM-DD'))} tuổi)` : ''}`}
                      rules={[{ required: true, message: 'Nhập ngày sinh.' }]}
                    >
                      <DatePicker format={DATE} style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
                    </Form.Item>
                  </Col>
                  <Col xs={24} sm={12}>
                    <Form.Item name={['patient', 'gender']} label="Giới tính">
                      <Radio.Group options={(Object.keys(GENDER_LABELS) as Gender[]).map((g) => ({ value: g, label: GENDER_LABELS[g] }))} />
                    </Form.Item>
                  </Col>
                  <Col span={24}>
                    <Form.Item
                      name={['patient', 'address']}
                      label="Địa chỉ"
                      rules={requireAddress ? [{ required: true, whitespace: true, message: 'Nhập địa chỉ (thông tin đối chiếu thứ ba khi gọi điện).' }] : []}
                    >
                      <Input maxLength={500} />
                    </Form.Item>
                  </Col>
                </Row>
                <Typography.Text type="secondary">
                  {lookup?.status === 'Found' && sameAsHis(patient, lookup) ? (
                    <Tag color="success">Lấy từ HIS</Tag>
                  ) : (
                    <Tag>Nhập tay</Tag>
                  )}{' '}
                  HIS lỗi thì nhập tay họ tên, ngày sinh, địa chỉ; hồ sơ có nhãn Nhập tay.
                </Typography.Text>
              </Card>

              <Card size="small" title={<StepTitle n={2}>Kết quả nguy cấp</StepTitle>}>
                <Form.Item
                  name="testId"
                  label="Xét nghiệm, kỹ thuật (danh mục kết quả nguy cấp)"
                  rules={[{ required: true, message: 'Chọn xét nghiệm, kỹ thuật trong danh mục.' }]}
                >
                  <Select
                    showSearch
                    optionFilterProp="label"
                    placeholder="Chọn trong danh mục"
                    options={activeTests.map((t) => ({ value: t.id, label: t.section ? `${t.name} · ${t.section}` : t.name }))}
                    onChange={(id: string) => {
                      const next = activeTests.find((t) => t.id === id)
                      form.setFieldsValue({ unit: next?.textResult ? undefined : (next?.unit ?? undefined), value: undefined })
                    }}
                  />
                </Form.Item>
                {test && (
                  <Alert
                    type="info"
                    style={{ marginBottom: 16 }}
                    title={`Khoảng nguy cấp theo danh mục${test.section ? ` · ${test.section}` : ''}`}
                    description={
                      <>
                        <div>Người lớn: {test.adultRange}</div>
                        {test.childRange && <div>Trẻ em: {test.childRange}</div>}
                        <Typography.Text type="secondary">
                          Người ghi tự đối chiếu như quy trình hiện nay; phần mềm không tự đánh giá giá trị.
                        </Typography.Text>
                      </>
                    }
                  />
                )}
                <Row gutter={12}>
                  {test?.textResult ? (
                    <Col span={24}>
                      <Form.Item name="value" label="Kết quả (ghi bằng chữ)" rules={[{ required: true, whitespace: true, message: 'Nhập kết quả.' }]}>
                        <Input.TextArea rows={2} maxLength={500} showCount />
                      </Form.Item>
                    </Col>
                  ) : (
                    <>
                      <Col xs={14} sm={8}>
                        <Form.Item name="value" label="Giá trị" rules={[{ required: true, whitespace: true, message: 'Nhập giá trị.' }]}>
                          <Input maxLength={50} />
                        </Form.Item>
                      </Col>
                      <Col xs={10} sm={6}>
                        <Form.Item name="unit" label="Đơn vị" rules={[{ required: true, whitespace: true, message: 'Nhập đơn vị.' }]}>
                          <Input maxLength={30} />
                        </Form.Item>
                      </Col>
                    </>
                  )}
                  <Col xs={12} sm={test?.textResult ? 12 : 5}>
                    <Form.Item name="orderDate" label="Ngày chỉ định" rules={[{ required: true, message: 'Nhập ngày chỉ định.' }]}>
                      <DatePicker format={DATE} allowClear={false} style={{ width: '100%' }} disabledDate={(d) => d.isAfter(dayjs(), 'day')} />
                    </Form.Item>
                  </Col>
                  <Col xs={12} sm={test?.textResult ? 12 : 5}>
                    <Form.Item name="resultAt" label="Giờ có kết quả" rules={[{ required: true, message: 'Nhập giờ có kết quả.' }]}>
                      <DatePicker showTime format={DATE_TIME} allowClear={false} style={{ width: '100%' }} />
                    </Form.Item>
                  </Col>
                  {microbiology && (
                    <>
                      <Col xs={14} sm={12}>
                        <Form.Item name="sampleId" label="SID (mã mẫu)" rules={[{ required: true, whitespace: true, message: 'Sổ Vi sinh: nhập SID.' }]}>
                          <Input maxLength={50} />
                        </Form.Item>
                      </Col>
                      <Col xs={10} sm={12}>
                        <Form.Item name="urgent" valuePropName="checked" label=" ">
                          <Checkbox>Mẫu khẩn</Checkbox>
                        </Form.Item>
                      </Col>
                    </>
                  )}
                </Row>
              </Card>

              <Card size="small" title={<StepTitle n={3}>Đã báo cho khoa điều trị</StepTitle>}>
                <NotificationFields departments={departments.data ?? []} sendingDepartmentId={values?.sendingDepartmentId} microbiology={microbiology} />
                <Flex gap={8} wrap justify="flex-end" style={{ marginTop: 16 }}>
                  <Button onClick={() => navigate('/ket-qua-nguy-cap/khoa-bao')}>Hủy</Button>
                  <Button onClick={() => void save(false)} loading={mutation.isPending && !mutation.variables?.input.notification}>
                    Lưu, gọi sau
                  </Button>
                  <Button type="primary" onClick={() => void save(true)} loading={mutation.isPending && Boolean(mutation.variables?.input.notification)}>
                    Lưu, đã báo
                  </Button>
                </Flex>
                <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0, textAlign: 'right' }}>
                  Chưa gọi được thì bấm Lưu, gọi sau: hồ sơ vào Khoa tôi báo, đồng hồ hạn báo vẫn chạy.
                </Typography.Paragraph>
              </Card>
            </Flex>
          </Col>

          <Col xs={24} lg={9}>
            <Flex vertical gap={16} style={{ position: 'sticky', top: 80 }}>
              <Card size="small" title="Thời hạn báo">
                {dueUtc ? (
                  <>
                    <DeadlineClock dueUtc={dueUtc} nowMs={nowMs} warnMinutes={warnMinutes} size="large" />
                    <Typography.Paragraph type="secondary" style={{ margin: '8px 0 0' }}>
                      Có kết quả {timeOf(resultAt!.toISOString())} · hạn báo {timeOf(dueUtc)} ({notifyMinutes} phút theo thông số đang dùng).
                      Không liên lạc được thì cử người đến khoa và chọn cách báo Trực tiếp tại khoa.
                    </Typography.Paragraph>
                  </>
                ) : (
                  <Typography.Text type="secondary">Nhập giờ có kết quả.</Typography.Text>
                )}
              </Card>
              <Card size="small" title="Kịch bản gọi điện" extra={<Typography.Text type="secondary">điền sẵn theo hồ sơ</Typography.Text>}>
                <Steps
                  orientation="vertical"
                  size="small"
                  current={-1}
                  items={[
                    {
                      title: 'Giới thiệu',
                      content: `“Tôi là ${user?.fullName ?? '…'}, ${sendingName || 'khoa báo'}, báo kết quả nguy cấp.”`,
                    },
                    {
                      title: 'Hỏi người nghe máy',
                      content: '“Xin cho biết họ tên, chức danh.” Ghi vào ô Người nghe máy.',
                    },
                    {
                      title: 'Đối chiếu người bệnh',
                      content: `“Người bệnh ${patient.fullName || '…'}, sinh ${patient.dateOfBirth?.format(DATE) ?? '…'}${patient.address ? `, ${patient.address}` : ''}.” Đọc đủ ba thông tin để khoa nhận đối chiếu.`,
                    },
                    {
                      title: 'Đọc kết quả',
                      content: `“${test?.name ?? 'Xét nghiệm'}: ${values?.value || '…'}${unitText ? ` ${unitText}` : ''}, có kết quả lúc ${resultAt ? resultAt.format('HH:mm') : '…'}.”`,
                    },
                    {
                      title: 'Nghe đọc lại',
                      content: `“Đề nghị đọc lại kết quả.” Đúng hết thì chọn Chính xác, đề nghị báo ngay bác sĩ điều trị.${microbiology ? ' Sổ Vi sinh: người nhận nhắc lại 2 lần.' : ''}`,
                    },
                  ]}
                />
              </Card>
            </Flex>
          </Col>
        </Row>
      </Form>
    </>
  )
}
