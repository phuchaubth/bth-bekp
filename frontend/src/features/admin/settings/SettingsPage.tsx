import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, Descriptions, Form, Input, Row, Tag, Typography } from 'antd'
import { settingsApi, settingsKeys, useSystemSettings, type HospitalSettingsInput } from '../../../api/settings'
import { PageHeader } from '../../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

/** Màn hình 19: tên đơn vị in trên PDF (sửa được) và cấu hình triển khai đang có hiệu lực (chỉ đọc, không có bí mật). */
export function SettingsPage() {
  const settings = useSystemSettings()
  const [form] = Form.useForm<HospitalSettingsInput>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const save = useMutation({
    mutationFn: (input: HospitalSettingsInput) => settingsApi.updateHospital(input),
    onSuccess: (updated) => {
      queryClient.setQueryData(settingsKeys.all, updated)
      message.success('Đã lưu tên đơn vị. Phiếu in từ giờ dùng tên mới; PDF đã ký giữ nguyên.')
    },
    onError: (error) => {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  return (
    <>
      <PageHeader title="Cấu hình hệ thống" />
      {settings.isError && <Alert type="error" showIcon title={errorMessage(settings.error)} style={{ marginBottom: 16 }} />}
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={12}>
          <Card title="Tên đơn vị in trên phiếu" loading={settings.isPending}>
            {settings.data && (
              <Form<HospitalSettingsInput>
                form={form}
                layout="vertical"
                initialValues={settings.data.hospital}
                onFinish={(values) => save.mutate(values)}
              >
                <Form.Item
                  label="Cơ quan chủ quản"
                  name="parentAgency"
                  extra="In ở góc trái đầu phiếu, ví dụ: SỞ Y TẾ TỈNH …"
                  rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập cơ quan chủ quản.' }, { max: 500 }]}
                >
                  <Input maxLength={500} />
                </Form.Item>
                <Form.Item
                  label="Tên bệnh viện"
                  name="name"
                  extra="In đậm dưới cơ quan chủ quản, ví dụ: BỆNH VIỆN ĐA KHOA …"
                  rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập tên bệnh viện.' }, { max: 500 }]}
                >
                  <Input maxLength={500} />
                </Form.Item>
                <Form.Item
                  label="Địa danh"
                  name="place"
                  extra="Ghi ở dòng ngày tháng, ví dụ: Hà Nội → “Hà Nội, ngày … tháng … năm …”"
                  rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập địa danh.' }, { max: 500 }]}
                >
                  <Input maxLength={500} />
                </Form.Item>
                <Button type="primary" htmlType="submit" loading={save.isPending}>
                  Lưu
                </Button>
                <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
                  {settings.data.hospital.updatedAtUtc
                    ? `Cập nhật lần cuối: ${formatDateTime(settings.data.hospital.updatedAtUtc)}.`
                    : 'Đang dùng giá trị mẫu từ cấu hình triển khai; hãy nhập tên đơn vị thật trước khi đưa vào sử dụng.'}{' '}
                  Mọi thay đổi được ghi nhật ký hệ thống.
                </Typography.Paragraph>
              </Form>
            )}
          </Card>
        </Col>
        <Col xs={24} xl={12}>
          <Card title="Cấu hình triển khai (chỉ đọc)" loading={settings.isPending}>
            {settings.data && (
              <Descriptions column={1} size="small" bordered>
                <Descriptions.Item label="Kết nối HIS">
                  {settings.data.hisEnabled ? <Tag color="green">Đang bật</Tag> : <Tag>Chưa kết nối</Tag>}
                </Descriptions.Item>
                <Descriptions.Item label="Ký số VNPT SmartCA">
                  {settings.data.smartCaEnabled ? <Tag color="green">Đang bật</Tag> : <Tag>Tạm hoãn – ký tay trên điện thoại</Tag>}
                </Descriptions.Item>
                <Descriptions.Item label="Giới hạn mỗi lần xuất dữ liệu">{settings.data.exportMaxRows.toLocaleString('vi-VN')} phiếu</Descriptions.Item>
                <Descriptions.Item label="Lưu trữ">{settings.data.retentionPolicy}</Descriptions.Item>
              </Descriptions>
            )}
            <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
              Khóa ký, khóa mã hóa, chuỗi kết nối và thời hạn phiên được đặt bằng biến môi trường trên máy chủ (xem hướng dẫn triển khai),
              không hiển thị và không sửa được ở đây.
            </Typography.Paragraph>
          </Card>
        </Col>
      </Row>
    </>
  )
}
