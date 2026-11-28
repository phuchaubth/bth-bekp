import { CheckOutlined, CloseOutlined, PhoneOutlined } from '@ant-design/icons'
import { useMutation } from '@tanstack/react-query'
import { Alert, App, AutoComplete, Button, Checkbox, DatePicker, Form, Input, InputNumber, Modal, Radio, Select, Space, Typography } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { CRITICAL_CALL_METHODS, criticalApi, CRITICAL_METHOD_LABELS, type CriticalCallMethod, type CriticalResult } from '../../api/critical'
import type { Department } from '../../api/admin'
import { useAuth } from '../../auth/authContext'
import { errorMessageWithDetails } from '../../utils/formErrors'
import { useCriticalSaved } from './criticalHooks'
import {
  clearCallErrors,
  emptyNotification,
  nowToMinute,
  REJECT_REASONS,
  toAppNotificationInput,
  toCallInput,
  toNotificationInput,
  type NotificationFormValues,
} from './criticalInputs'
import { dateTimeOf, timeOf } from './criticalUtils'

const DATE_TIME = 'DD/MM/YYYY HH:mm'

/** Người nhận đọc lại: "Chính xác" mới lưu được; mỗi lần bấm "Không chính xác" đếm một lần đọc sai. */
function ReadBackPicker({ value, onChange, onWrong }: { value?: boolean; onChange?: (value: boolean) => void; onWrong: () => void }) {
  return (
    <Space wrap>
      <Button type={value === true ? 'primary' : 'default'} icon={<CheckOutlined />} aria-pressed={value === true} onClick={() => onChange?.(true)}>
        Chính xác
      </Button>
      <Button
        danger={value === false}
        icon={<CloseOutlined />}
        aria-pressed={value === false}
        onClick={() => {
          onWrong()
          onChange?.(false)
        }}
      >
        Không chính xác
      </Button>
    </Space>
  )
}

/** Ô giờ kèm nút "Bây giờ". */
function TimeWithNow({ value, onChange, label }: { value?: Dayjs; onChange?: (value: Dayjs | null) => void; label: string }) {
  return (
    <Space.Compact style={{ width: '100%' }}>
      <DatePicker showTime format={DATE_TIME} value={value} onChange={onChange} allowClear={false} aria-label={label} style={{ width: '100%' }} />
      <Button onClick={() => onChange?.(nowToMinute())}>Bây giờ</Button>
    </Space.Compact>
  )
}

/**
 * Các ô "Đã báo cho khoa điều trị" (form có trường `notification`): khoa nhận, cách báo, giờ báo, người nhận, người nhận đọc lại
 * chính xác; sổ Vi sinh thêm "người nhận nhắc lại 2 lần". Người báo là tài khoản đang đăng nhập. Ghi cuộc gọi xác nhận (quyết
 * định 68) dùng lại các ô này, không có ô khoa nhận (`departments` bỏ trống), giờ báo đổi thành giờ gọi.
 */
export function NotificationFields({
  departments,
  sendingDepartmentId,
  microbiology,
  timeLabel = 'Giờ báo',
  actorLabel = 'Người báo',
}: {
  /** Bỏ trống: không có ô khoa nhận (đã báo khoa nhận trên phần mềm). */
  departments?: Department[]
  sendingDepartmentId?: string
  microbiology: boolean
  timeLabel?: string
  actorLabel?: string
}) {
  const form = Form.useFormInstance()
  const { user } = useAuth()
  const method = Form.useWatch(['notification', 'method'], form) as CriticalCallMethod | undefined
  const incorrect = (Form.useWatch(['notification', 'incorrectReadBacks'], form) as number | undefined) ?? 0
  const readBack = Form.useWatch(['notification', 'readBackCorrect'], form) as boolean | undefined
  return (
    <>
      <Form.Item name={['notification', 'method']} label="Cách báo">
        <Radio.Group optionType="button" options={CRITICAL_CALL_METHODS.map((m) => ({ value: m, label: CRITICAL_METHOD_LABELS[m] }))} />
      </Form.Item>
      {departments && (
        <Form.Item
          name={['notification', 'receivingDepartmentId']}
          label="Khoa nhận"
          rules={[{ required: true, message: 'Chọn khoa nhận.' }]}
        >
          <Select
            showSearch
            optionFilterProp="label"
            options={departments.filter((d) => d.isActive && d.id !== sendingDepartmentId).map((d) => ({ value: d.id, label: d.name }))}
            placeholder="Khoa đang điều trị người bệnh"
          />
        </Form.Item>
      )}
      <Form.Item
        name={['notification', 'receiverName']}
        label={method === 'InPerson' ? 'Người nhận tại khoa' : 'Người nghe máy'}
        rules={[{ required: true, whitespace: true, message: 'Nhập họ tên người nhận.' }]}
      >
        <Input maxLength={200} placeholder="Họ tên, chức danh" />
      </Form.Item>
      <Form.Item name={['notification', 'notifiedAt']} label={timeLabel} rules={[{ required: true, message: `Nhập ${timeLabel.toLowerCase()}.` }]}>
        <TimeWithNow label={timeLabel} />
      </Form.Item>
      <Form.Item
        name={['notification', 'readBackCorrect']}
        label="Người nhận đọc lại tên kết quả, giá trị, đơn vị"
        rules={[
          {
            validator: (_, value) =>
              value === true
                ? Promise.resolve()
                : Promise.reject(new Error(value === false ? 'Chỉ lưu khi người nhận đã đọc lại chính xác.' : 'Chọn kết quả người nhận đọc lại.')),
          },
        ]}
      >
        <ReadBackPicker
          onWrong={() => form.setFieldValue(['notification', 'incorrectReadBacks'], (form.getFieldValue(['notification', 'incorrectReadBacks']) ?? 0) + 1)}
        />
      </Form.Item>
      <Form.Item name={['notification', 'incorrectReadBacks']} hidden>
        <InputNumber />
      </Form.Item>
      {readBack === false && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={`Đọc lại chưa chính xác (lần ${incorrect}).`}
          description="Đọc lại kết quả, nghe người nhận đọc lại lần nữa rồi chọn Chính xác. Lần đọc sai vẫn được lưu trong lịch sử hồ sơ."
        />
      )}
      {microbiology && (
        <Form.Item
          name={['notification', 'repeatedTwice']}
          valuePropName="checked"
          rules={[{ validator: (_, value) => (value ? Promise.resolve() : Promise.reject(new Error('Sổ Vi sinh: người nhận phải nhắc lại 2 lần.'))) }]}
        >
          <Checkbox>Người nhận đã nhắc lại 2 lần (sổ Vi sinh)</Checkbox>
        </Form.Item>
      )}
      <Typography.Text type="secondary">
        {actorLabel}: <Typography.Text strong>{user?.fullName}</Typography.Text> (tài khoản đang đăng nhập)
      </Typography.Text>
    </>
  )
}

/**
 * Khoa báo ghi lần báo cho hồ sơ chưa báo hoặc bị khoa nhận trả lại: gọi điện xong thì "Lưu, đã báo"; chưa gọi được thì "Lưu,
 * gọi sau" (quyết định 68): chỉ cần khoa nhận, khoa nhận được báo ngay trên phần mềm, gọi sau để ghi người nhận.
 */
export function NotifyModal({
  result,
  departments,
  open,
  onClose,
}: {
  result: CriticalResult
  departments: Department[]
  open: boolean
  onClose: () => void
}) {
  const [form] = Form.useForm<{ notification: NotificationFormValues }>()
  const { message } = App.useApp()
  const saved = useCriticalSaved()
  const mutation = useMutation({
    mutationFn: ({ values, byApp }: { values: NotificationFormValues; byApp: boolean }) =>
      criticalApi.notify(result.id, byApp ? toAppNotificationInput(values) : toNotificationInput(values)),
    onSuccess: (next, { byApp }) => {
      saved(next)
      void message.success(
        byApp
          ? `Đã báo ${next.receivingDepartmentName} trên phần mềm. Gọi cho khoa rồi bấm Ghi cuộc gọi để ghi người nhận.`
          : `Đã ghi lần báo cho ${next.receivingDepartmentName}.`,
      )
      onClose()
    },
  })

  async function save(byApp: boolean) {
    if (byApp) {
      clearCallErrors(form)
    }
    try {
      await (byApp ? form.validateFields([['notification', 'receivingDepartmentId']]) : form.validateFields())
    } catch {
      return
    }
    const values = form.getFieldsValue(true) as { notification: NotificationFormValues }
    mutation.mutate({ values: values.notification, byApp })
  }

  return (
    <Modal
      open={open}
      title={`Ghi lần báo · ${result.patientName}`}
      onCancel={onClose}
      destroyOnHidden
      footer={[
        <Button key="cancel" onClick={onClose}>
          Thôi
        </Button>,
        <Button key="later" onClick={() => void save(true)} loading={mutation.isPending && mutation.variables?.byApp === true}>
          Lưu, gọi sau
        </Button>,
        <Button key="called" type="primary" onClick={() => void save(false)} loading={mutation.isPending && mutation.variables?.byApp === false}>
          Lưu, đã báo
        </Button>,
      ]}
    >
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Form form={form} layout="vertical" initialValues={{ notification: emptyNotification() }}>
        <NotificationFields departments={departments} sendingDepartmentId={result.sendingDepartmentId} microbiology={result.logbook === 'Microbiology'} />
      </Form>
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
        Chưa gọi được thì chọn khoa nhận rồi bấm Lưu, gọi sau: khoa nhận được báo ngay trên phần mềm (dải cảnh báo đỏ, chuông); gọi sau
        để ghi người nhận thông tin.
      </Typography.Paragraph>
    </Modal>
  )
}

/**
 * Ghi cuộc gọi xác nhận người nhận thông tin (quyết định 68): hồ sơ đã báo khoa nhận trên phần mềm lúc lưu; khoa báo gọi cho
 * khoa nhận, đọc kết quả, nghe đọc lại rồi ghi người nhận. Giờ báo của hồ sơ giữ nguyên.
 */
export function ConfirmCallModal({
  result,
  open,
  onClose,
  onSaved,
}: {
  result: CriticalResult
  open: boolean
  onClose: () => void
  onSaved?: (result: CriticalResult) => void
}) {
  const [form] = Form.useForm<{ notification: NotificationFormValues }>()
  const { message } = App.useApp()
  const saved = useCriticalSaved()
  const mutation = useMutation({
    mutationFn: (values: { notification: NotificationFormValues }) => criticalApi.confirmCall(result.id, toCallInput(values.notification)),
    onSuccess: (next) => {
      saved(next)
      onSaved?.(next)
      void message.success(`Đã ghi người nhận thông tin của ${next.receivingDepartmentName}.`)
      onClose()
    },
  })
  return (
    <Modal
      open={open}
      title={`Ghi cuộc gọi · ${result.patientName}`}
      okText="Lưu cuộc gọi"
      okButtonProps={{ icon: <PhoneOutlined /> }}
      cancelText="Thôi"
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={mutation.isPending}
      destroyOnHidden
    >
      <Typography.Paragraph>
        Đã báo <Typography.Text strong>{result.receivingDepartmentName}</Typography.Text> trên phần mềm lúc {timeOf(result.notifiedAtUtc)}. Gọi
        cho khoa, đọc kết quả, nghe người nhận đọc lại rồi ghi người nhận thông tin. Giờ báo của hồ sơ giữ nguyên.
      </Typography.Paragraph>
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Form form={form} layout="vertical" initialValues={{ notification: emptyNotification() }} onFinish={(values) => mutation.mutate(values)}>
        <NotificationFields microbiology={result.logbook === 'Microbiology'} timeLabel="Giờ gọi" actorLabel="Người gọi" />
      </Form>
    </Modal>
  )
}

/** Khoa nhận trả lại lần báo không đúng khoa (khoa báo thấy lý do ngay). */
export function RejectModal({ result, open, onClose }: { result: CriticalResult; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<{ reason: string }>()
  const { message } = App.useApp()
  const saved = useCriticalSaved()
  const mutation = useMutation({
    mutationFn: (reason: string) => criticalApi.reject(result.id, reason),
    onSuccess: (next) => {
      saved(next)
      void message.success(`Đã trả hồ sơ ${result.patientName} về ${result.sendingDepartmentName}.`)
      onClose()
    },
  })
  return (
    <Modal
      open={open}
      title="Không phải khoa tôi"
      okText="Gửi trả khoa báo"
      okButtonProps={{ danger: true }}
      cancelText="Thôi"
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={mutation.isPending}
      destroyOnHidden
    >
      <Typography.Paragraph>
        Trả hồ sơ <Typography.Text strong>{result.patientName}</Typography.Text> về {result.sendingDepartmentName}. Khoa báo gọi lại
        đúng khoa và ghi lần báo mới.
      </Typography.Paragraph>
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Form form={form} layout="vertical" onFinish={(values) => mutation.mutate(values.reason.trim())}>
        <Form.Item name="reason" label="Lý do (khoa báo thấy ngay)" rules={[{ required: true, whitespace: true, message: 'Nhập lý do.' }]}>
          <AutoComplete options={REJECT_REASONS.map((value) => ({ value }))} maxLength={500} placeholder="Chọn hoặc nhập lý do" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** Khoa báo hủy hồ sơ báo nhầm: bắt buộc lý do; đã báo khoa nhận thì phải gọi báo khoa nhận về việc hủy trước. */
export function CancelModal({ result, open, onClose }: { result: CriticalResult; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<{ reason: string; informed: boolean }>()
  const { message } = App.useApp()
  const saved = useCriticalSaved()
  const notified = result.receivingDepartmentId !== null
  const mutation = useMutation({
    mutationFn: (values: { reason: string; informed: boolean }) => criticalApi.cancel(result.id, values.reason.trim(), notified && values.informed),
    onSuccess: (next) => {
      saved(next)
      void message.success('Đã hủy hồ sơ. Sổ in dòng này gạch ngang kèm lý do.')
      onClose()
    },
  })
  return (
    <Modal
      open={open}
      title="Hủy hồ sơ báo nhầm"
      okText="Hủy hồ sơ"
      okButtonProps={{ danger: true }}
      cancelText="Thôi"
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={mutation.isPending}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">
        Hồ sơ không bị xóa: sổ in dòng này gạch ngang kèm lý do; dashboard đếm riêng, không tính vào tỷ lệ đúng hạn. Nếu đã báo nhầm
        người bệnh hoặc kết quả, gọi ngay khoa nhận để hủy thông tin đã báo, rồi ghi hồ sơ đúng.
      </Typography.Paragraph>
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Form form={form} layout="vertical" initialValues={{ informed: false }} onFinish={(values) => mutation.mutate(values)}>
        <Form.Item name="reason" label="Lý do hủy" rules={[{ required: true, whitespace: true, message: 'Nhập lý do hủy.' }]}>
          <Input.TextArea rows={3} maxLength={500} showCount />
        </Form.Item>
        {notified && (
          <Form.Item
            name="informed"
            valuePropName="checked"
            rules={[{ validator: (_, value) => (value ? Promise.resolve() : Promise.reject(new Error('Gọi báo khoa nhận về việc hủy trước khi hủy.'))) }]}
          >
            <Checkbox>Đã gọi báo {result.receivingDepartmentName} về việc hủy</Checkbox>
          </Form.Item>
        )}
      </Form>
    </Modal>
  )
}

interface ActionFormValues {
  summary: string
  actionAt: Dayjs
  recordedInMedicalRecord: boolean
}

/** Bác sĩ khoa nhận xác nhận đã nhận và ghi hướng xử trí ban đầu bằng tài khoản của mình (thay chữ ký cột 9 sổ ST02). */
export function ActionForm({ result, onSaved }: { result: CriticalResult; onSaved?: (result: CriticalResult) => void }) {
  const [form] = Form.useForm<ActionFormValues>()
  const { user } = useAuth()
  const saved = useCriticalSaved()
  const [mode, setMode] = useState<'now' | 'earlier'>('now')
  const mutation = useMutation({
    mutationFn: (values: ActionFormValues) =>
      criticalApi.act(result.id, {
        summary: values.summary.trim(),
        actionAtUtc: mode === 'now' ? null : values.actionAt.toISOString(),
        recordedInMedicalRecord: values.recordedInMedicalRecord,
      }),
    onSuccess: (next) => {
      saved(next)
      onSaved?.(next)
    },
  })
  return (
    <Form<ActionFormValues>
      form={form}
      layout="vertical"
      initialValues={{ summary: '', actionAt: nowToMinute(), recordedInMedicalRecord: true }}
      onFinish={(values) => mutation.mutate(values)}
    >
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Form.Item name="summary" label="Hướng xử trí ban đầu" rules={[{ required: true, whitespace: true, message: 'Nhập hướng xử trí ban đầu.' }]}>
        <Input.TextArea rows={4} maxLength={1000} showCount placeholder="Ví dụ: ngưng thuốc, làm lại xét nghiệm, báo trưởng khoa" />
      </Form.Item>
      <Form.Item label="Giờ xử trí">
        <Radio.Group value={mode} onChange={(e) => setMode(e.target.value as 'now' | 'earlier')} optionType="button">
          <Radio.Button value="now">Bây giờ</Radio.Button>
          <Radio.Button value="earlier">Đã xử trí lúc khác</Radio.Button>
        </Radio.Group>
      </Form.Item>
      {mode === 'earlier' && (
        <Form.Item name="actionAt" label={`Giờ xử trí (sau giờ báo ${timeOf(result.notifiedAtUtc)})`} rules={[{ required: true }]}>
          <DatePicker showTime format={DATE_TIME} allowClear={false} style={{ width: '100%' }} />
        </Form.Item>
      )}
      <Form.Item name="recordedInMedicalRecord" valuePropName="checked">
        <Checkbox>Đã ghi vào hồ sơ bệnh án</Checkbox>
      </Form.Item>
      <Typography.Paragraph type="secondary">
        Bác sĩ: <Typography.Text strong>{user?.fullName}</Typography.Text> (tài khoản đang đăng nhập, thay chữ ký cột 9 sổ ST02)
      </Typography.Paragraph>
      <Button type="primary" htmlType="submit" size="large" block loading={mutation.isPending}>
        {result.status === 'AwaitingAcknowledgement' ? 'Xác nhận đã nhận và lưu' : 'Lưu hướng xử trí'}
      </Button>
      {result.acknowledgedByName && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
          {result.acknowledgedByName} đã xác nhận đã nhận lúc {dateTimeOf(result.acknowledgedAtUtc)}.
        </Typography.Paragraph>
      )}
    </Form>
  )
}
