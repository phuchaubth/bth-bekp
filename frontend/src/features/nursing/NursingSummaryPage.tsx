import { CloudDownloadOutlined, EditOutlined, FileExcelOutlined, FilePdfOutlined, SettingOutlined, UnlockOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Drawer,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Row,
  Spin,
  Table,
  Typography,
  type TableColumnsType,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import {
  WAITING_ADMISSION,
  nursingKeys,
  nursingReportsApi,
  useNursingReport,
  useNursingReportDepartments,
  useNursingReportForm,
  useNursingSummary,
  type NursingSummary,
  type NursingSummaryLine,
  type NursingSummaryRow,
} from '../../api/nursingReports'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { NursingReportFields, PdfModal, ReportStatus } from './NursingReportFields'
import { saveBlob, statusTag, type NursingReportFormValues } from './nursingReportUtils'

const DATE = 'YYYY-MM-DD'

const number = (value: number | null | undefined) => (value === null || value === undefined ? '' : value)

/**
 * Bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" (quyết định 31): mục A–F như bản giấy, lấy từ báo cáo các khoa đã gửi
 * và số liệu HIS. Phòng Điều dưỡng xem báo cáo từng khoa, mở lại, cài khoa báo cáo và số giường, lấy hoặc nhập số liệu HIS.
 */
export function NursingSummaryPage() {
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.NursingReportsManage)
  const [date, setDate] = useState<Dayjs>(() => dayjs())
  const reportDate = date.format(DATE)
  const summary = useNursingSummary(reportDate)
  const [pdfOpen, setPdfOpen] = useState(false)
  const [selected, setSelected] = useState<NursingSummaryRow | null>(null)
  const [departmentsOpen, setDepartmentsOpen] = useState(false)
  const [statisticsOpen, setStatisticsOpen] = useState(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const workbook = useMutation({
    mutationFn: () => nursingReportsApi.summaryWorkbook(reportDate),
    onSuccess: (blob) => saveBlob(blob, `tong-hop-dieu-duong-${reportDate}.xlsx`),
    onError: (e) => message.error(errorMessage(e)),
  })
  const refresh = useMutation({
    mutationFn: () => nursingReportsApi.refreshStatistics(reportDate),
    onSuccess: (result) => {
      queryClient.setQueryData(nursingKeys.summary(reportDate), result.summary)
      if (result.unmatchedDepartmentCodes.length > 0) {
        message.warning(
          `Đã lấy số liệu HIS. Mã khoa không khớp khoa nào: ${result.unmatchedDepartmentCodes.join(', ')} (báo phòng CNTT kiểm tra câu truy vấn).`,
        )
      } else {
        message.success('Đã lấy số liệu HIS.')
      }
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  return (
    <>
      <PageHeader
        title="Tổng hợp báo cáo điều dưỡng"
        extra={
          <Flex gap={8} wrap>
            <DatePicker
              value={date}
              format="DD/MM/YYYY"
              allowClear={false}
              aria-label="Ngày tổng hợp"
              disabledDate={(d) => d.isAfter(dayjs(), 'day')}
              onChange={(d) => d && setDate(d)}
            />
            <Button icon={<FilePdfOutlined />} onClick={() => setPdfOpen(true)}>
              In PDF
            </Button>
            <Button icon={<FileExcelOutlined />} loading={workbook.isPending} onClick={() => workbook.mutate()}>
              Xuất Excel
            </Button>
          </Flex>
        }
      />
      {summary.isError ? (
        <Alert type="error" showIcon title={errorMessage(summary.error)} />
      ) : !summary.data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <>
          <Card size="small" style={{ marginBottom: 16 }}>
            <Flex justify="space-between" align="center" wrap gap={8}>
              <Flex vertical gap={2}>
                <Typography.Text>
                  Khoa đã gửi: <b>{summary.data.submittedCount}</b>/{summary.data.reportingCount}
                </Typography.Text>
                <Typography.Text type="secondary">{statisticsText(summary.data)}</Typography.Text>
              </Flex>
              {canManage && (
                <Flex gap={8} wrap>
                  <Button icon={<CloudDownloadOutlined />} loading={refresh.isPending} onClick={() => refresh.mutate()}>
                    Lấy số liệu HIS
                  </Button>
                  <Button icon={<EditOutlined />} onClick={() => setStatisticsOpen(true)}>
                    Nhập số liệu HIS
                  </Button>
                  <Button icon={<SettingOutlined />} onClick={() => setDepartmentsOpen(true)}>
                    Khoa báo cáo và số giường
                  </Button>
                </Flex>
              )}
            </Flex>
          </Card>
          <SummaryView summary={summary.data} onOpen={setSelected} />
        </>
      )}

      <PdfModal
        open={pdfOpen}
        title={`Tổng hợp báo cáo điều dưỡng ngày ${date.format('DD/MM/YYYY')}`}
        fileName={`tong-hop-dieu-duong-${reportDate}.pdf`}
        queryKey={[...nursingKeys.summary(reportDate), summary.dataUpdatedAt]}
        load={() => nursingReportsApi.summaryPdf(reportDate)}
        onClose={() => setPdfOpen(false)}
      />
      {selected && <DepartmentReportDrawer row={selected} reportDate={reportDate} onClose={() => setSelected(null)} />}
      {departmentsOpen && <DepartmentsModal onClose={() => setDepartmentsOpen(false)} />}
      {statisticsOpen && summary.data && (
        <StatisticsModal summary={summary.data} onClose={() => setStatisticsOpen(false)} />
      )}
    </>
  )
}

function statisticsText(summary: NursingSummary) {
  const info = summary.statistics
  if (!info.hasValues) {
    return info.hisAvailable
      ? 'Chưa có số liệu HIS của ngày này: bấm "Lấy số liệu HIS".'
      : 'Chưa kết nối HIS: Phòng Điều dưỡng nhập tay số liệu HIS.'
  }
  const at = info.updatedAtUtc ? formatDateTime(info.updatedAtUtc) : ''
  const by = info.updatedBy ? ` bởi ${info.updatedBy}` : ''
  return info.source === 'Manual' ? `Số liệu HIS nhập tay lúc ${at}${by}.` : `Số liệu HIS lấy lúc ${at}${by}.`
}

/** Mục A–F như bản giấy. */
export function SummaryView({ summary, onOpen }: { summary: NursingSummary; onOpen: (row: NursingSummaryRow) => void }) {
  const columns: TableColumnsType<NursingSummaryRow> = [
    {
      title: 'Khoa phòng',
      dataIndex: 'departmentName',
      fixed: 'left',
      width: 220,
      render: (name: string, row) => (
        <Flex vertical gap={2}>
          <Button type="link" style={{ padding: 0, height: 'auto', alignSelf: 'flex-start', textAlign: 'start', whiteSpace: 'normal' }} onClick={() => onOpen(row)}>
            {name}
          </Button>
          <span>{statusTag(row.status)}</span>
        </Flex>
      ),
    },
    ...summary.columns.map((c) => ({
      title: c.header,
      key: c.key,
      align: 'right' as const,
      width: 96,
      render: (_: unknown, row: NursingSummaryRow) => number(row.cells[c.key]),
    })),
  ]

  return (
    <>
      <Typography.Title level={5}>A. Số liệu người bệnh nội trú</Typography.Title>
      <Table
        rowKey="departmentId"
        size="small"
        bordered
        pagination={false}
        columns={columns}
        dataSource={summary.rows}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Chưa có khoa nào trong danh sách khoa báo cáo.' }}
        summary={() => (
          <Table.Summary fixed>
            <Table.Summary.Row>
              <Table.Summary.Cell index={0}>
                <b>Tổng</b>
              </Table.Summary.Cell>
              {summary.columns.map((c, i) => (
                <Table.Summary.Cell key={c.key} index={i + 1} align="right">
                  <b>{number(summary.totals[c.key])}</b>
                </Table.Summary.Cell>
              ))}
            </Table.Summary.Row>
          </Table.Summary>
        )}
        style={{ marginBottom: 16 }}
      />
      <Row gutter={16}>
        <Col xs={24} lg={8}>
          <LineTable title="B. Số liệu khoa khám bệnh" lines={summary.outpatient} />
        </Col>
        <Col xs={24} lg={8}>
          <LineTable title="C. Số liệu PTTT" lines={summary.procedures} />
        </Col>
        <Col xs={24} lg={8}>
          <LineTable title="D. Số liệu CĐHA" lines={summary.imaging} />
        </Col>
      </Row>
      <Typography.Title level={5}>E. Tình hình trong ngày</Typography.Title>
      <Table
        rowKey="key"
        size="small"
        bordered
        pagination={false}
        dataSource={summary.situation}
        columns={[
          { title: 'Nội dung', dataIndex: 'label', width: '40%' },
          {
            title: 'Phần báo cáo',
            dataIndex: 'text',
            render: (text: string | null, row) =>
              row.issueDepartments.length > 0 ? <Typography.Text type="danger">{text}</Typography.Text> : text,
          },
        ]}
        style={{ marginBottom: 16 }}
      />
      <Typography.Title level={5}>F. Báo cáo cụ thể sự cố (nếu có)</Typography.Title>
      {summary.incidents.length === 0 ? (
        <Typography.Paragraph type="secondary">Không có.</Typography.Paragraph>
      ) : (
        <Table
          rowKey="departmentId"
          size="small"
          bordered
          pagination={false}
          dataSource={summary.incidents}
          columns={[
            { title: 'Khoa', dataIndex: 'departmentName', width: '25%' },
            {
              title: 'Nội dung báo cáo sự cố',
              dataIndex: 'text',
              render: (text: string) => <span style={{ whiteSpace: 'pre-wrap' }}>{text}</span>,
            },
          ]}
        />
      )}
    </>
  )
}

function LineTable({ title, lines }: { title: string; lines: NursingSummaryLine[] }) {
  return (
    <>
      <Typography.Title level={5}>{title}</Typography.Title>
      <Table
        rowKey="key"
        size="small"
        bordered
        pagination={false}
        dataSource={lines}
        columns={[
          { title: 'Nội dung', dataIndex: 'label' },
          { title: 'Số liệu', dataIndex: 'value', width: 90, align: 'right', render: number },
        ]}
        style={{ marginBottom: 16 }}
      />
    </>
  )
}

/** Báo cáo của một khoa (chỉ đọc) trong bảng tổng hợp: in PDF, Phòng Điều dưỡng mở lại khi cần. */
function DepartmentReportDrawer({ row, reportDate, onClose }: { row: NursingSummaryRow; reportDate: string; onClose: () => void }) {
  const definition = useNursingReportForm()
  const report = useNursingReport(reportDate, row.departmentId)
  const [form] = Form.useForm<NursingReportFormValues>()
  const [pdfOpen, setPdfOpen] = useState(false)
  const [reopenOpen, setReopenOpen] = useState(false)
  const data = report.data

  return (
    <Drawer
      open
      onClose={onClose}
      size={Math.min(960, window.innerWidth)}
      title={`${row.departmentName} – ngày ${dayjs(reportDate).format('DD/MM/YYYY')}`}
      extra={
        data?.id && (
          <Flex gap={8}>
            {data.canReopen && (
              <Button icon={<UnlockOutlined />} onClick={() => setReopenOpen(true)}>
                Mở lại
              </Button>
            )}
            <Button icon={<FilePdfOutlined />} onClick={() => setPdfOpen(true)}>
              In PDF
            </Button>
          </Flex>
        )
      }
    >
      {report.isError || definition.isError ? (
        <Alert type="error" showIcon title={errorMessage(report.error ?? definition.error)} />
      ) : !data || !definition.data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : data.status === 'New' ? (
        <Alert type="info" showIcon title="Khoa chưa lập báo cáo ngày này." />
      ) : (
        <>
          <ReportStatus report={data} />
          {data.reopenReason && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title={`Đã mở lại lúc ${data.reopenedAtUtc ? formatDateTime(data.reopenedAtUtc) : ''}${data.reopenedBy ? ` bởi ${data.reopenedBy}` : ''}`}
              description={`Lý do: ${data.reopenReason}`}
            />
          )}
          <NursingReportFields definition={definition.data} report={data} form={form} editable={false} />
        </>
      )}
      {data?.id && (
        <PdfModal
          open={pdfOpen}
          title={`Báo cáo điều dưỡng ${row.departmentName}`}
          fileName={`bao-cao-dieu-duong-${reportDate}.pdf`}
          queryKey={[...nursingKeys.daily(reportDate, row.departmentId), data.updatedAtUtc]}
          load={() => nursingReportsApi.pdf(data.id!)}
          onClose={() => setPdfOpen(false)}
        />
      )}
      {reopenOpen && data?.id && (
        <ReopenModal reportId={data.id} departmentId={row.departmentId} reportDate={reportDate} onClose={() => setReopenOpen(false)} />
      )}
    </Drawer>
  )
}

function ReopenModal({ reportId, departmentId, reportDate, onClose }: { reportId: string; departmentId: string; reportDate: string; onClose: () => void }) {
  const [reason, setReason] = useState('')
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const reopen = useMutation({
    mutationFn: () => nursingReportsApi.reopen(reportId, reason.trim()),
    onSuccess: (report) => {
      queryClient.setQueryData(nursingKeys.daily(reportDate, departmentId), report)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.summary(reportDate) })
      message.success('Đã mở lại báo cáo. Khoa sửa và gửi lại.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  return (
    <Modal
      open
      title="Mở lại báo cáo cho khoa sửa"
      okText="Mở lại"
      cancelText="Đóng"
      okButtonProps={{ disabled: reason.trim().length === 0, loading: reopen.isPending }}
      onOk={() => reopen.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph>Báo cáo trở lại dạng nháp; khoa sửa rồi gửi lại. Lý do được ghi vào nhật ký.</Typography.Paragraph>
      <Input.TextArea
        aria-label="Lý do mở lại"
        placeholder="Lý do mở lại (bắt buộc)"
        value={reason}
        maxLength={500}
        autoSize={{ minRows: 2, maxRows: 6 }}
        onChange={(e) => setReason(e.target.value)}
      />
    </Modal>
  )
}

interface DepartmentRow {
  departmentId: string
  code: string
  name: string
  isReporting: boolean
  bedCount: number | null
}

/** Phòng Điều dưỡng chọn khoa phải gửi báo cáo và cài số giường thực kê (không tính băng ca) của từng khoa. */
function DepartmentsModal({ onClose }: { onClose: () => void }) {
  const departments = useNursingReportDepartments(true)
  const [edits, setEdits] = useState<Record<string, Pick<DepartmentRow, 'isReporting' | 'bedCount'>>>({})
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const rows: DepartmentRow[] = useMemo(
    () => (departments.data ?? []).map((d) => ({ ...d, ...edits[d.departmentId] })),
    [departments.data, edits],
  )
  const save = useMutation({
    mutationFn: () =>
      nursingReportsApi.updateDepartments(rows.filter((r) => r.isReporting).map((r) => ({ departmentId: r.departmentId, bedCount: r.bedCount ?? 0 }))),
    onSuccess: (saved) => {
      queryClient.setQueryData(nursingKeys.departments, saved)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.all })
      message.success('Đã lưu danh sách khoa báo cáo.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  const update = (row: DepartmentRow, change: Partial<Pick<DepartmentRow, 'isReporting' | 'bedCount'>>) =>
    setEdits((current) => ({ ...current, [row.departmentId]: { isReporting: row.isReporting, bedCount: row.bedCount, ...change } }))

  return (
    <Modal
      open
      width="min(760px, 96vw)"
      title="Khoa báo cáo và số giường thực kê"
      okText="Lưu"
      cancelText="Đóng"
      okButtonProps={{ loading: save.isPending, disabled: !departments.data }}
      onOk={() => save.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph type="secondary">
        Khoa được chọn mới lập được báo cáo và có dòng trên bảng tổng hợp. Số giường thực kê không tính băng ca.
      </Typography.Paragraph>
      {departments.isError ? (
        <Alert type="error" showIcon title={errorMessage(departments.error)} />
      ) : (
        <Table
          rowKey="departmentId"
          size="small"
          pagination={false}
          loading={departments.isLoading}
          dataSource={rows}
          scroll={{ y: 420 }}
          columns={[
            {
              title: 'Báo cáo',
              width: 80,
              render: (_, row) => (
                <Checkbox aria-label={`${row.name} phải báo cáo`} checked={row.isReporting} onChange={(e) => update(row, { isReporting: e.target.checked })} />
              ),
            },
            { title: 'Khoa/phòng', dataIndex: 'name', render: (name: string, row) => `${name} (${row.code})` },
            {
              title: 'Số giường',
              width: 130,
              render: (_, row) => (
                <InputNumber
                  aria-label={`Số giường ${row.name}`}
                  min={0}
                  max={10000}
                  precision={0}
                  disabled={!row.isReporting}
                  value={row.bedCount}
                  onChange={(value) => update(row, { bedCount: value })}
                />
              ),
            },
          ]}
        />
      )}
    </Modal>
  )
}

/** Phòng Điều dưỡng nhập tay số liệu HIS của ngày (HIS lỗi hoặc chưa có câu truy vấn). */
function StatisticsModal({ summary, onClose }: { summary: NursingSummary; onClose: () => void }) {
  const [form] = Form.useForm<{ hospital: Record<string, number | null>; waiting: Record<string, number | null> }>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const reporting = summary.rows.filter((r) => r.isReporting)
  const save = useMutation({
    mutationFn: () => {
      const values = form.getFieldsValue(true)
      return nursingReportsApi.updateStatistics({
        reportDate: summary.reportDate,
        hospital: Object.fromEntries([...summary.outpatient, ...summary.imaging].map((l) => [l.key, values.hospital?.[l.key] ?? null])),
        waitingAdmission: Object.fromEntries(reporting.map((r) => [r.departmentId, values.waiting?.[r.departmentId] ?? null])),
      })
    },
    onSuccess: (updated) => {
      queryClient.setQueryData(nursingKeys.summary(summary.reportDate), updated)
      message.success('Đã lưu số liệu.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  const initialValues = {
    hospital: Object.fromEntries([...summary.outpatient, ...summary.imaging].map((l) => [l.key, l.value])),
    waiting: Object.fromEntries(reporting.map((r) => [r.departmentId, r.cells[WAITING_ADMISSION]])),
  }
  const field = (name: (string | number)[], label: string) => (
    <Form.Item key={name.join('.')} name={name} label={label} labelCol={{ flex: 'auto' }} wrapperCol={{ flex: '120px' }} labelAlign="start" colon={false} style={{ marginBottom: 8 }}>
      <InputNumber aria-label={label} min={0} max={99999} precision={0} style={{ width: '100%' }} />
    </Form.Item>
  )

  return (
    <Modal
      open
      width="min(760px, 96vw)"
      title={`Nhập số liệu HIS ngày ${dayjs(summary.reportDate).format('DD/MM/YYYY')}`}
      okText="Lưu"
      cancelText="Đóng"
      okButtonProps={{ loading: save.isPending }}
      onOk={() => save.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph type="secondary">
        Dùng khi HIS lỗi hoặc chưa có câu truy vấn số liệu. Số nhập tay thay số đã lấy từ HIS của ngày này.
      </Typography.Paragraph>
      <Form form={form} initialValues={initialValues} layout="horizontal" labelWrap>
        <Row gutter={24}>
          <Col xs={24} md={12}>
            <Typography.Text strong>B. Khoa khám bệnh và D. CĐHA</Typography.Text>
            {[...summary.outpatient, ...summary.imaging].map((l) => field(['hospital', l.key], l.label))}
          </Col>
          <Col xs={24} md={12}>
            <Typography.Text strong>NB PK chờ nhập viện</Typography.Text>
            {reporting.map((r) => field(['waiting', r.departmentId], r.departmentName))}
          </Col>
        </Row>
      </Form>
    </Modal>
  )
}
