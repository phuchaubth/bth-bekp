import { FilePdfOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Col, ConfigProvider, Form, Input, InputNumber, Modal, Radio, Row, Spin, Typography, type FormInstance } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, type ReactNode } from 'react'
import type { NursingReport, NursingReportFormDefinition } from '../../api/nursingReports'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { statusTag, toFormValues, type NursingReportFormValues } from './nursingReportUtils'

/** Dòng trạng thái: ai gửi lúc nào; lần mở lại gần nhất và lý do. */
export function ReportStatus({ report }: { report: NursingReport }) {
  return (
    <Typography.Paragraph style={{ marginBottom: 12 }}>
      {statusTag(report.status)}
      {report.status === 'Submitted' && report.submittedAtUtc && (
        <Typography.Text type="secondary">
          Gửi lúc {formatDateTime(report.submittedAtUtc)}
          {report.submittedBy ? ` bởi ${report.submittedBy}` : ''}
        </Typography.Text>
      )}
      {report.status === 'Draft' && report.updatedAtUtc && (
        <Typography.Text type="secondary">
          Lưu lúc {formatDateTime(report.updatedAtUtc)}
          {report.updatedBy ? ` bởi ${report.updatedBy}` : ''}, chưa gửi Phòng Điều dưỡng
        </Typography.Text>
      )}
    </Typography.Paragraph>
  )
}

/**
 * Các ô nhập của báo cáo khoa theo bản giấy: phần số liệu (mục I–VI), phần tick, phần báo cáo nhập thêm. Khóa cả form khi
 * không sửa được (đã gửi, không phải khoa mình), chữ vẫn đậm để dễ đọc.
 */
export function NursingReportFields({
  definition,
  report,
  form,
  editable,
}: {
  definition: NursingReportFormDefinition
  report: NursingReport
  form: FormInstance<NursingReportFormValues>
  editable: boolean
}) {
  const initialValues = useMemo(() => toFormValues(report), [report])
  useEffect(() => {
    form.resetFields()
    form.setFieldsValue(initialValues)
  }, [form, initialValues])
  const number = (key: string, label: ReactNode) => (
    <Form.Item
      key={key}
      name={['values', key]}
      label={label}
      labelCol={{ flex: 'auto' }}
      wrapperCol={{ flex: '120px' }}
      labelAlign="start"
      colon={false}
      style={{ marginBottom: 8 }}
    >
      <InputNumber min={0} max={definition.maxValue} precision={0} style={{ width: '100%' }} aria-label={typeof label === 'string' ? label : key} />
    </Form.Item>
  )

  return (
    <ConfigProvider theme={{ token: { colorTextDisabled: 'rgba(0, 0, 0, 0.82)' } }}>
      <Form form={form} initialValues={initialValues} disabled={!editable} layout="horizontal" labelWrap requiredMark={false}>
        <Typography.Title level={5}>PHẦN SỐ LIỆU</Typography.Title>
        {definition.sections.map((section) => (
          <Card key={section.title} size="small" title={section.title} style={{ marginBottom: 12 }}>
            {section.valueKey && number(section.valueKey, 'Số ca')}
            <Row gutter={24}>
              {section.items.map((item) => (
                <Col key={item.key} xs={24} md={12}>
                  {number(item.key, item.label)}
                </Col>
              ))}
            </Row>
          </Card>
        ))}

        <Typography.Title level={5}>PHẦN TICK</Typography.Title>
        <Card size="small" style={{ marginBottom: 12 }}>
          {definition.checks.map((check) => (
            <Form.Item
              key={check.key}
              name={['checks', check.key]}
              label={check.label}
              labelCol={{ flex: '260px' }}
              labelAlign="start"
              colon={false}
              style={{ marginBottom: 8 }}
            >
              <Radio.Group
                aria-label={check.label}
                options={[
                  { value: 'issue', label: definition.issueLabel },
                  { value: 'ok', label: definition.okLabel },
                ]}
              />
            </Form.Item>
          ))}
        </Card>

        <Typography.Title level={5}>PHẦN BÁO CÁO (NHẬP THÊM)</Typography.Title>
        <Form.Item name="notes" label={definition.notesLabel} layout="vertical">
          <Input.TextArea autoSize={{ minRows: 3, maxRows: 16 }} maxLength={definition.notesMaxLength} showCount />
        </Form.Item>
      </Form>
    </ConfigProvider>
  )
}

/** Tạo đường dẫn tạm cho file tải về, tự thu hồi khi đóng. */
function useObjectUrl(blob: Blob | undefined) {
  const url = useMemo(() => (blob ? URL.createObjectURL(blob) : null), [blob])
  useEffect(
    () => () => {
      if (url) {
        URL.revokeObjectURL(url)
      }
    },
    [url],
  )
  return url
}

/** Xem PDF (tải có xác thực khi mở), có nút tải về. */
export function PdfModal({
  open,
  title,
  fileName,
  queryKey,
  load,
  onClose,
}: {
  open: boolean
  title: string
  fileName: string
  queryKey: readonly unknown[]
  load: () => Promise<Blob>
  onClose: () => void
}) {
  const pdf = useQuery({ queryKey: [...queryKey, 'pdf'], queryFn: load, enabled: open, gcTime: 0 })
  const url = useObjectUrl(open ? pdf.data : undefined)
  return (
    <Modal
      open={open}
      onCancel={onClose}
      title={title}
      width="min(1000px, 96vw)"
      footer={
        url && (
          <a href={url} download={fileName}>
            <Button icon={<FilePdfOutlined />}>Tải PDF</Button>
          </a>
        )
      }
      destroyOnHidden
    >
      {pdf.isError ? (
        <Alert type="error" showIcon title={errorMessage(pdf.error)} />
      ) : url ? (
        <iframe title={title} src={url} style={{ width: '100%', height: '75vh', border: 0 }} />
      ) : (
        <Spin style={{ display: 'block', padding: 48 }} />
      )}
    </Modal>
  )
}
