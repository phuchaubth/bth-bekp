import { SaveOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Form, InputNumber, Typography } from 'antd'
import { nursingKeys, nursingReportsApi, type NursingHisEntry, type NursingReport } from '../../api/nursingReports'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'

/**
 * Số liệu HIS khoa nhập khi HIS lỗi (quyết định 35): "NB PK chờ nhập viện" của khoa và các chỉ số Phòng Điều dưỡng giao cho
 * khoa (tổng số người bệnh đến khám, số liệu chẩn đoán hình ảnh). Lưu riêng, không khóa theo báo cáo của khoa. Phòng Điều
 * dưỡng chỉ tổng hợp: bảng tổng hợp dùng số lấy từ HIS trước, ô HIS không có số mới dùng số khoa nhập.
 */
export function HisEntryCard({
  report,
  entry,
  maxValue,
  queryDepartmentId,
}: {
  report: NursingReport
  entry: NursingHisEntry
  maxValue: number
  /** Khoa đã chọn ở đầu trang (khóa bộ nhớ đệm); chưa chọn là khoa chính. */
  queryDepartmentId?: string
}) {
  const [form] = Form.useForm<Record<string, number | null>>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: () => {
      const values = form.getFieldsValue(true)
      return nursingReportsApi.saveHisEntry({
        departmentId: report.departmentId,
        reportDate: report.reportDate,
        values: Object.fromEntries(entry.lines.map((l) => [l.key, values[l.key] ?? null])),
      })
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(nursingKeys.daily(report.reportDate, queryDepartmentId), saved)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.summary(report.reportDate) })
      message.success('Đã lưu số liệu HIS của khoa.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  return (
    <Card title="Số liệu HIS khoa nhập khi HIS lỗi" style={{ marginTop: 16 }}>
      <Typography.Paragraph type="secondary">
        Chỉ nhập khi HIS lỗi, không lấy được số. Bảng tổng hợp dùng số lấy từ HIS trước; ô HIS không có số mới dùng số khoa
        nhập.
      </Typography.Paragraph>
      {entry.hisHasValues && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title={`Ngày này đã có số liệu HIS${entry.hisUpdatedAtUtc ? ` (lấy lúc ${formatDateTime(entry.hisUpdatedAtUtc)})` : ''}. Số khoa nhập chỉ dùng cho ô HIS không có số.`}
        />
      )}
      <Form
        form={form}
        initialValues={Object.fromEntries(entry.lines.map((l) => [l.key, l.value]))}
        disabled={!entry.canEdit}
        layout="horizontal"
        labelWrap
        requiredMark={false}
      >
        {entry.lines.map((line) => (
          <Form.Item
            key={line.key}
            name={line.key}
            label={line.label}
            extra={line.hisValue !== null ? `Số lấy từ HIS: ${line.hisValue}` : undefined}
            labelCol={{ flex: '220px' }}
            wrapperCol={{ flex: '160px' }}
            labelAlign="start"
            colon={false}
            style={{ marginBottom: 12 }}
          >
            <InputNumber aria-label={line.label} min={0} max={maxValue} precision={0} style={{ width: '100%' }} />
          </Form.Item>
        ))}
      </Form>
      <Flex gap={8} wrap justify="space-between" align="center">
        <Typography.Text type="secondary">
          {entry.updatedAtUtc
            ? `Đã nhập lúc ${formatDateTime(entry.updatedAtUtc)}${entry.updatedBy ? ` bởi ${entry.updatedBy}` : ''}.`
            : 'Khoa chưa nhập số liệu HIS ngày này.'}
        </Typography.Text>
        {entry.canEdit && (
          <Button icon={<SaveOutlined />} loading={save.isPending} onClick={() => save.mutate()}>
            Lưu số liệu HIS
          </Button>
        )}
      </Flex>
    </Card>
  )
}
