import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Flex, Form, Input, Row, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import {
  MAINTENANCE_MAX_DAYS,
  MAINTENANCE_REASON_MAX_LENGTH,
  maintenanceApi,
  maintenanceKeys,
  useMaintenanceSettings,
  type MaintenanceInput,
  type MaintenanceSettings,
  type MaintenanceStatus,
} from '../../../api/maintenance'
import { PageHeader } from '../../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

interface MaintenanceFormValues {
  reason: string
  expectedEnd: Dayjs | null
}

const TIME_FORMAT = 'HH:mm DD/MM/YYYY'

/**
 * Bảo trì hệ thống (quyết định 53): người có quyền bảo trì bật chế độ bảo trì, ghi lý do và giờ dự kiến xong (hiện cho người
 * dùng), sửa khi đang bật, tắt khi xong. Có hiệu lực ngay.
 */
export function MaintenanceSettingsPage() {
  const settings = useMaintenanceSettings()
  const queryClient = useQueryClient()
  const { message, modal } = App.useApp()
  const [form] = Form.useForm<MaintenanceFormValues>()
  const data = settings.data

  const save = useMutation({
    mutationFn: (input: MaintenanceInput) => maintenanceApi.update(input),
    onSuccess: (saved, input) => {
      queryClient.setQueryData<MaintenanceSettings>(maintenanceKeys.settings, saved)
      queryClient.setQueryData<MaintenanceStatus>(maintenanceKeys.status, {
        enabled: saved.enabled,
        reason: saved.reason,
        expectedEndAtUtc: saved.expectedEndAtUtc,
      })
      message.success(
        !saved.enabled
          ? 'Đã tắt chế độ bảo trì: mọi người làm việc lại được ngay.'
          : data?.enabled && input.enabled
            ? 'Đã lưu lý do và giờ dự kiến xong.'
            : 'Đã bật chế độ bảo trì.',
      )
    },
    onError: (error) => {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  const toInput = (values: MaintenanceFormValues): MaintenanceInput => ({
    enabled: true,
    reason: values.reason.trim(),
    expectedEndAtUtc: values.expectedEnd ? values.expectedEnd.second(0).millisecond(0).toISOString() : null,
  })

  const onFinish = (values: MaintenanceFormValues) => {
    if (data?.enabled) {
      save.mutate(toInput(values))
      return
    }
    modal.confirm({
      title: 'Bật chế độ bảo trì?',
      content: (
        <ul style={{ paddingInlineStart: 20, margin: 0 }}>
          <li>Người không có quyền bảo trì (kể cả kiosk, người bệnh đang ký) không đăng nhập, không thao tác được cho tới khi tắt.</li>
          <li>Trang họ đang mở và dữ liệu đang nhập vẫn giữ trên màn hình; tắt bảo trì là làm tiếp.</li>
          <li>Tài khoản quản trị vẫn làm việc như thường.</li>
        </ul>
      ),
      okText: 'Bật bảo trì',
      okButtonProps: { danger: true },
      cancelText: 'Không bật',
      onOk: () => save.mutateAsync(toInput(values)),
    })
  }

  return (
    <>
      <PageHeader title="Bảo trì hệ thống" />
      {settings.isError && <Alert type="error" showIcon title={errorMessage(settings.error)} style={{ marginBottom: 16 }} />}
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={14}>
          <Card title="Chế độ bảo trì" loading={settings.isPending}>
            {data && (
              <Flex vertical gap={16}>
                {data.enabled ? (
                  <Alert
                    type="warning"
                    showIcon
                    title="Đang bảo trì"
                    description={`Bật lúc ${formatDateTime(data.startedAtUtc)}${data.startedBy ? `, người bật: ${data.startedBy}` : ''}. Người dùng không có quyền bảo trì đang thấy màn hình "Hệ thống đang bảo trì".`}
                  />
                ) : (
                  <Alert type="success" showIcon title="Hệ thống đang hoạt động bình thường." />
                )}
                <Form<MaintenanceFormValues>
                  // Dựng lại form theo dữ liệu vừa lưu.
                  key={`${data.enabled}|${data.reason ?? ''}|${data.expectedEndAtUtc ?? ''}`}
                  form={form}
                  layout="vertical"
                  initialValues={{ reason: data.reason ?? '', expectedEnd: data.expectedEndAtUtc ? dayjs(data.expectedEndAtUtc) : null }}
                  onFinish={onFinish}
                  disabled={save.isPending}
                >
                  <Form.Item
                    label="Lý do (hiện cho người dùng)"
                    name="reason"
                    extra="Ví dụ: Nâng cấp phần mềm, cập nhật mẫu phiếu."
                    rules={[
                      { required: true, whitespace: true, message: 'Vui lòng nhập lý do bảo trì (hiện cho người dùng).' },
                      { max: MAINTENANCE_REASON_MAX_LENGTH, message: `Lý do bảo trì tối đa ${MAINTENANCE_REASON_MAX_LENGTH} ký tự.` },
                    ]}
                  >
                    <Input.TextArea rows={2} maxLength={MAINTENANCE_REASON_MAX_LENGTH} showCount />
                  </Form.Item>
                  <Form.Item
                    label="Dự kiến xong lúc"
                    name="expectedEnd"
                    extra="Để trống nếu chưa rõ. Chỉ để thông báo: hết giờ hệ thống không tự tắt bảo trì."
                    rules={[
                      {
                        validator: (_, value: Dayjs | null) =>
                          !value || value.isAfter(dayjs())
                            ? Promise.resolve()
                            : Promise.reject(new Error('Giờ dự kiến xong phải sau thời điểm hiện tại.')),
                      },
                    ]}
                  >
                    <DatePicker
                      showTime={{ format: 'HH:mm' }}
                      format={TIME_FORMAT}
                      placeholder="Chưa rõ"
                      disabledDate={(d) => d.isBefore(dayjs(), 'day') || d.isAfter(dayjs().add(MAINTENANCE_MAX_DAYS, 'day'), 'day')}
                      style={{ width: '100%', maxWidth: 280 }}
                    />
                  </Form.Item>
                  <Flex gap={8} wrap>
                    {data.enabled ? (
                      <>
                        <Button htmlType="submit" loading={save.isPending && save.variables?.enabled === true}>
                          Lưu lý do, giờ dự kiến
                        </Button>
                        <Button
                          type="primary"
                          loading={save.isPending && save.variables?.enabled === false}
                          onClick={() => save.mutate({ enabled: false })}
                        >
                          Tắt chế độ bảo trì
                        </Button>
                      </>
                    ) : (
                      <Button type="primary" danger htmlType="submit" loading={save.isPending}>
                        Bật chế độ bảo trì
                      </Button>
                    )}
                  </Flex>
                </Form>
              </Flex>
            )}
          </Card>
        </Col>
        <Col xs={24} xl={10}>
          <Card title="Trong lúc bảo trì">
            <ul style={{ paddingInlineStart: 20, margin: 0 }}>
              <li>
                Người không có quyền <strong>Bảo trì hệ thống</strong> (kể cả kiosk, người bệnh ký trên máy tính bảng hoặc qua mã QR) thấy
                màn hình "Hệ thống đang bảo trì" kèm lý do và giờ dự kiến xong; không đăng nhập, không thao tác được.
              </li>
              <li>
                Phiên đăng nhập của họ không bị thu hồi; trang đang mở và dữ liệu đang nhập vẫn giữ trên màn hình. Tắt bảo trì là họ làm
                tiếp (thao tác lúc đang bảo trì chưa được lưu thì bấm lưu lại).
              </li>
              <li>
                Tài khoản có quyền Bảo trì hệ thống (vai trò Quản trị hệ thống) vẫn đăng nhập, điều chỉnh cấu hình, mẫu phiếu, dữ liệu như
                thường. Đăng nhập trong lúc bảo trì: ở màn hình bảo trì chọn <em>Đăng nhập bằng tài khoản quản trị</em>.
              </li>
              <li>Mọi lần bật, sửa, tắt đều được ghi nhật ký hệ thống.</li>
            </ul>
            <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
              Không còn tài khoản quản trị nào đăng nhập được thì phòng CNTT tắt bảo trì bằng SQL theo hướng dẫn triển khai (mục 5.12).
            </Typography.Paragraph>
          </Card>
        </Col>
      </Row>
    </>
  )
}
