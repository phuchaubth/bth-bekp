import { FilePdfOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Col, ConfigProvider, Form, Input, InputNumber, Modal, Radio, Row, Spin, Typography, type FormInstance } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, type ReactNode } from 'react'
import type { NursingReport, NursingReportFormDefinition, NursingReportFormula, NursingReportItem } from '../../api/nursingReports'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { evaluateFormula, statusTag, toFormValues, type NursingReportFormValues } from './nursingReportUtils'

/** Dòng trạng thái: ai gửi lúc nào, ai lưu nháp lúc nào. */
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
 * không sửa được (đã gửi, không phải khoa mình), chữ vẫn đậm để dễ đọc. Dòng có công thức (Số NB hiện tại, quyết định 46) không
 * nhập được: đang sửa thì tính ngay theo các ô khác như máy chủ sẽ tính khi lưu; xem báo cáo đã lưu thì hiện số đã lưu.
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
  const labels = Object.fromEntries(definition.sections.flatMap((s) => s.items.map((i) => [i.key, i.label] as const)))
  const computed = (item: NursingReportItem & { formula: NursingReportFormula }) => {
    const { base, add, subtract } = item.formula
    const formulaText = `Tự tính: ${[base, ...add].map((k) => labels[k] ?? k).join(' + ')}${subtract.map((k) => ` − ${labels[k] ?? k}`).join('')}`
    return (
      <>
        <Form.Item
          label={item.label}
          labelCol={{ flex: 'auto' }}
          wrapperCol={{ flex: '120px' }}
          labelAlign="start"
          colon={false}
          style={{ marginBottom: 2 }}
        >
          <Form.Item noStyle shouldUpdate>
            {() => {
              const value = editable
                ? evaluateFormula(item.formula, form.getFieldValue('values') as NursingReportFormValues['values'] | undefined)
                : (report.values[item.key] ?? null)
              return (
                <InputNumber
                  aria-label={item.label}
                  disabled
                  status={value !== null && value < 0 ? 'error' : undefined}
                  value={value}
                  style={{ width: '100%' }}
                />
              )
            }}
          </Form.Item>
        </Form.Item>
        {/* Công thức ghi dưới ô, rộng cả cột (để trong nhãn thì nhãn cao hơn ô, đè lên ô số). */}
        <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 8 }}>
          {formulaText}
        </Typography.Paragraph>
      </>
    )
  }

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
                  {item.formula ? computed({ ...item, formula: item.formula }) : number(item.key, item.label)}
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
