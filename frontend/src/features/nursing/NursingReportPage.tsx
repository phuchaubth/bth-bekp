import { FilePdfOutlined, SaveOutlined, SendOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Flex, Form, Popconfirm, Spin, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  nursingKeys,
  nursingReportsApi,
  useNursingReport,
  useNursingReportForm,
  type NursingReport,
  type NursingReportFormDefinition,
} from '../../api/nursingReports'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { HisEntryCard } from './HisEntryCard'
import { NursingReportFields, PdfModal, ReportStatus } from './NursingReportFields'
import { toSaveInput, type NursingReportFormValues } from './nursingReportUtils'

const DATE = 'YYYY-MM-DD'

/**
 * Báo cáo công tác điều dưỡng hằng ngày của khoa (quyết định 31): điều dưỡng chọn ngày, nhập theo bản giấy, lưu nháp hoặc
 * gửi Phòng Điều dưỡng. Gửi xong thì khóa; cần sửa thì đề nghị Phòng Điều dưỡng trả lại khoa. Khi HIS lỗi, khoa nhập số liệu HIS
 * thuộc nhiệm vụ của khoa ở thẻ riêng (quyết định 35); khoa không phải báo cáo nhưng được giao chỉ số thì chỉ thấy thẻ này.
 */
export function NursingReportPage() {
  const [date, setDate] = useState<Dayjs>(() => dayjs())
  const reportDate = date.format(DATE)
  const definition = useNursingReportForm()
  const report = useNursingReport(reportDate)

  return (
    <>
      <PageHeader
        title="Báo cáo điều dưỡng của khoa"
        extra={
          <DatePicker
            value={date}
            format="DD/MM/YYYY"
            allowClear={false}
            aria-label="Ngày báo cáo"
            disabledDate={(d) => d.isAfter(dayjs(), 'day')}
            onChange={(d) => d && setDate(d)}
          />
        }
      />
      {definition.isError || report.isError ? (
        <Alert type="error" showIcon title={errorMessage(definition.error ?? report.error)} />
      ) : !definition.data || !report.data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : !report.data.isReportingDepartment && report.data.hisEntry ? (
        <>
          <Alert
            type="info"
            showIcon
            title={`${report.data.departmentName} không phải gửi báo cáo công tác điều dưỡng.`}
            description="Khoa chỉ nhập các số liệu HIS được Phòng Điều dưỡng giao, khi HIS lỗi."
          />
          <HisEntryCard
            key={`${reportDate}-${report.data.hisEntry.updatedAtUtc ?? 'new'}`}
            report={report.data}
            entry={report.data.hisEntry}
            maxValue={definition.data.maxValue}
          />
        </>
      ) : (
        <>
          <ReportEditor key={`${reportDate}-${report.data.id ?? 'new'}`} definition={definition.data} report={report.data} />
          {report.data.hisEntry && (
            <HisEntryCard
              key={`${reportDate}-${report.data.hisEntry.updatedAtUtc ?? 'new'}`}
              report={report.data}
              entry={report.data.hisEntry}
              maxValue={definition.data.maxValue}
            />
          )}
        </>
      )}
    </>
  )
}

function ReportEditor({ definition, report }: { definition: NursingReportFormDefinition; report: NursingReport }) {
  const [form] = Form.useForm<NursingReportFormValues>()
  const [pdfOpen, setPdfOpen] = useState(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (submit: boolean) => nursingReportsApi.save(toSaveInput(definition, form.getFieldsValue(true), report.reportDate, submit)),
    onSuccess: (saved, submit) => {
      queryClient.setQueryData(nursingKeys.daily(report.reportDate), saved)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.all })
      message.success(submit ? 'Đã gửi báo cáo cho Phòng Điều dưỡng.' : 'Đã lưu nháp.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const submit = () => {
    const checks = form.getFieldValue('checks') as Record<string, string | null | undefined> | undefined
    const missing = definition.checks.filter((c) => !checks?.[c.key])
    if (missing.length > 0) {
      form.setFields(missing.map((c) => ({ name: ['checks', c.key], errors: ['Chọn một trong hai ô.'] })))
      message.error('Chọn "Ổn" hoặc "Không ổn" cho mọi dòng của phần tick trước khi gửi.')
      return
    }
    save.mutate(true)
  }

  const title = `${definition.title} – ${report.departmentName}`
  return (
    <Card
      title={
        <Flex vertical>
          <span>{title}</span>
          <Typography.Text type="secondary" style={{ fontWeight: 'normal' }}>
            Ngày {dayjs(report.reportDate).format('DD/MM/YYYY')}
          </Typography.Text>
        </Flex>
      }
      extra={
        report.id && (
          <Button icon={<FilePdfOutlined />} onClick={() => setPdfOpen(true)}>
            In PDF
          </Button>
        )
      }
    >
      <ReportStatus report={report} />
      {!report.isReportingDepartment && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          title="Khoa chưa có trong danh sách khoa báo cáo điều dưỡng."
          description="Vui lòng liên hệ Phòng Điều dưỡng để được thêm vào danh sách."
        />
      )}
      {report.status === 'Draft' && report.reopenReason && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title={`Phòng Điều dưỡng đã trả lại báo cáo cho khoa${report.reopenedBy ? ` (${report.reopenedBy})` : ''}.`}
          description={`Lý do: ${report.reopenReason}. Sửa rồi bấm "Gửi báo cáo" lại.`}
        />
      )}
      {report.status === 'New' && report.carriedOverKeys.length > 0 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title="Đã điền sẵn nhân sự và chỉ tiêu theo báo cáo đã gửi gần nhất của khoa. Kiểm tra lại trước khi gửi."
        />
      )}
      {report.status === 'Submitted' && (
        <Alert
          type="success"
          showIcon
          style={{ marginBottom: 12 }}
          title="Báo cáo đã gửi Phòng Điều dưỡng."
          description="Cần sửa thì đề nghị Phòng Điều dưỡng trả lại khoa."
        />
      )}

      <NursingReportFields definition={definition} report={report} form={form} editable={report.canEdit} />

      {report.canEdit && (
        <Flex gap={8} wrap justify="end">
          <Button icon={<SaveOutlined />} loading={save.isPending && save.variables === false} onClick={() => save.mutate(false)}>
            Lưu nháp
          </Button>
          <Popconfirm
            title="Gửi báo cáo cho Phòng Điều dưỡng?"
            description="Sau khi gửi, khoa không sửa được nữa; cần sửa thì đề nghị Phòng Điều dưỡng trả lại khoa."
            okText="Gửi"
            cancelText="Xem lại"
            onConfirm={submit}
          >
            <Button type="primary" icon={<SendOutlined />} loading={save.isPending && save.variables === true}>
              Gửi báo cáo
            </Button>
          </Popconfirm>
        </Flex>
      )}

      {report.id && (
        <PdfModal
          open={pdfOpen}
          title={`PDF ${title}`}
          fileName={`bao-cao-dieu-duong-${report.reportDate}.pdf`}
          queryKey={[...nursingKeys.daily(report.reportDate), report.id, report.updatedAtUtc]}
          load={() => nursingReportsApi.pdf(report.id!)}
          onClose={() => setPdfOpen(false)}
        />
      )}
    </Card>
  )
}
