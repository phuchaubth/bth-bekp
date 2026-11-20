import { FilePdfOutlined, SaveOutlined, SendOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Flex, Form, Popconfirm, Select, Spin, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { accountDepartments } from '../../api/auth'
import {
  nursingKeys,
  nursingReportsApi,
  useNursingReport,
  useNursingReportForm,
  type NursingReport,
  type NursingReportFormDefinition,
} from '../../api/nursingReports'
import { useAuth } from '../../auth/authContext'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { HisEntryCard } from './HisEntryCard'
import { NursingReportFields, PdfModal, ReportStatus } from './NursingReportFields'
import { formulaItems, toSaveInput, type NursingReportFormValues } from './nursingReportUtils'

const DATE = 'YYYY-MM-DD'

/**
 * Báo cáo công tác điều dưỡng hằng ngày của khoa (quyết định 31): điều dưỡng chọn ngày, nhập theo bản giấy, lưu nháp hoặc
 * gửi Phòng Điều dưỡng. Gửi xong thì khóa; cần sửa thì đề nghị Phòng Điều dưỡng trả lại khoa. Khi HIS lỗi, khoa nhập số liệu HIS
 * thuộc nhiệm vụ của khoa ở thẻ riêng (quyết định 35); khoa không phải báo cáo nhưng được giao chỉ số thì chỉ thấy thẻ này.
 * Tài khoản thuộc nhiều khoa (quyết định 41) chọn khoa; mặc định là khoa chính.
 */
export function NursingReportPage() {
  const { user } = useAuth()
  const myDepartments = accountDepartments(user)
  const [date, setDate] = useState<Dayjs>(() => dayjs())
  // Chưa chọn: khoa chính (máy chủ tự lấy theo tài khoản).
  const [departmentId, setDepartmentId] = useState<string | undefined>(undefined)
  const reportDate = date.format(DATE)
  const definition = useNursingReportForm()
  const report = useNursingReport(reportDate, departmentId)

  return (
    <>
      <PageHeader
        title="Báo cáo điều dưỡng của khoa"
        extra={
          <Flex gap={8} wrap>
            {myDepartments.length > 1 && (
              <Select
                aria-label="Khoa"
                value={departmentId ?? myDepartments[0].id}
                options={myDepartments.map((d) => ({ value: d.id, label: d.name }))}
                onChange={(id: string) => setDepartmentId(id)}
                style={{ minWidth: 220 }}
                popupMatchSelectWidth={false}
              />
            )}
            <DatePicker
              value={date}
              format="DD/MM/YYYY"
              allowClear={false}
              aria-label="Ngày báo cáo"
              disabledDate={(d) => d.isAfter(dayjs(), 'day')}
              onChange={(d) => d && setDate(d)}
            />
          </Flex>
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
            key={`${report.data.departmentId}-${reportDate}-${report.data.hisEntry.updatedAtUtc ?? 'new'}`}
            report={report.data}
            entry={report.data.hisEntry}
            maxValue={definition.data.maxValue}
            queryDepartmentId={departmentId}
          />
        </>
      ) : (
        <>
          <ReportEditor
            key={`${report.data.departmentId}-${reportDate}-${report.data.id ?? 'new'}`}
            definition={definition.data}
            report={report.data}
            queryDepartmentId={departmentId}
          />
          {report.data.hisEntry && (
            <HisEntryCard
              key={`${report.data.departmentId}-${reportDate}-${report.data.hisEntry.updatedAtUtc ?? 'new'}`}
              report={report.data}
              entry={report.data.hisEntry}
              maxValue={definition.data.maxValue}
              queryDepartmentId={departmentId}
            />
          )}
        </>
      )}
    </>
  )
}

function ReportEditor({
  definition,
  report,
  queryDepartmentId,
}: {
  definition: NursingReportFormDefinition
  report: NursingReport
  /** Khoa đã chọn ở đầu trang (khóa bộ nhớ đệm); chưa chọn là khoa chính. */
  queryDepartmentId?: string
}) {
  const [form] = Form.useForm<NursingReportFormValues>()
  const [pdfOpen, setPdfOpen] = useState(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (submit: boolean) =>
      nursingReportsApi.save({
        ...toSaveInput(definition, form.getFieldsValue(true), report.reportDate, submit),
        departmentId: report.departmentId,
      }),
    onSuccess: (saved, submit) => {
      queryClient.setQueryData(nursingKeys.daily(report.reportDate, queryDepartmentId), saved)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.all })
      message.success(submit ? 'Đã gửi báo cáo cho Phòng Điều dưỡng.' : 'Đã lưu nháp.')
    },
    // Không trả về kết quả của message.error: React Query sẽ chờ thông báo đóng (3 giây) rồi mới hết trạng thái đang lưu.
    onError: (e) => {
      message.error(errorMessageWithDetails(e))
    },
  })
  const labels = Object.fromEntries(definition.sections.flatMap((s) => s.items.map((i) => [i.key, i.label] as const)))

  const submit = () => {
    const checks = form.getFieldValue('checks') as Record<string, string | null | undefined> | undefined
    const missing = definition.checks.filter((c) => !checks?.[c.key])
    if (missing.length > 0) {
      form.setFields(missing.map((c) => ({ name: ['checks', c.key], errors: ['Chọn một trong hai ô.'] })))
      message.error('Chọn "Ổn" hoặc "Không ổn" cho mọi dòng của phần tick trước khi gửi.')
      return
    }
    // Dòng tính tự động (quyết định 46) cần số gốc, ví dụ Số NB ngày hôm trước để tính Số NB hiện tại.
    const values = form.getFieldValue('values') as NursingReportFormValues['values'] | undefined
    const noBase = formulaItems(definition).filter((i) => typeof values?.[i.formula.base] !== 'number')
    if (noBase.length > 0) {
      const texts = noBase.map((i) => `Nhập ${labels[i.formula.base] ?? i.formula.base} để tính ${i.label}.`)
      form.setFields(noBase.map((i, index) => ({ name: ['values', i.formula.base], errors: [texts[index]] })))
      message.error(texts.join(' '))
      return
    }
    save.mutate(true)
  }
  const bases = new Set(formulaItems(definition).map((i) => i.formula.base))
  const prefilled = [
    ...(report.carriedOverKeys.some((k) => !bases.has(k)) ? ['nhân sự và chỉ tiêu theo báo cáo đã gửi gần nhất của khoa'] : []),
    ...report.carriedOverKeys
      .filter((k) => bases.has(k))
      .map((k) => `${(labels[k] ?? k).toLowerCase().replace(/^số nb/, 'số NB')} theo báo cáo đã gửi hôm trước`),
  ]

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
      {report.status === 'New' && prefilled.length > 0 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          title={`Đã điền sẵn ${prefilled.join('; ')}. Kiểm tra lại trước khi gửi.`}
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
