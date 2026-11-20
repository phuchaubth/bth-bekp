import { CloudDownloadOutlined, FileExcelOutlined, FilePdfOutlined, RollbackOutlined, SettingOutlined } from '@ant-design/icons'
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
  Select,
  Spin,
  Table,
  Tooltip,
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
  type NursingHisEntryStatus,
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
 * và số liệu HIS. Phòng Điều dưỡng xem báo cáo từng khoa, trả lại khoa, cài khoa báo cáo, số giường và khoa nhập từng chỉ số, lấy
 * số liệu HIS. Khi HIS lỗi, các khoa nhập số liệu của khoa; Phòng Điều dưỡng chỉ tổng hợp (quyết định 35).
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
                <HisEntryStatusLine summary={summary.data} />
              </Flex>
              {canManage && (
                <Flex gap={8} wrap>
                  <Button icon={<CloudDownloadOutlined />} loading={refresh.isPending} onClick={() => refresh.mutate()}>
                    Lấy số liệu HIS
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
      {departmentsOpen && summary.data && (
        <DepartmentsModal lines={[...summary.data.outpatient, ...summary.data.imaging]} onClose={() => setDepartmentsOpen(false)} />
      )}
    </>
  )
}

function statisticsText(summary: NursingSummary) {
  const info = summary.statistics
  if (!info.hasValues) {
    return info.hisAvailable
      ? 'Chưa có số liệu HIS của ngày này: bấm "Lấy số liệu HIS". HIS lỗi thì các khoa nhập tay số liệu của khoa.'
      : 'Chưa kết nối HIS: các khoa nhập tay số liệu HIS của khoa.'
  }
  const at = info.updatedAtUtc ? formatDateTime(info.updatedAtUtc) : ''
  const by = info.updatedBy ? ` bởi ${info.updatedBy}` : ''
  return info.source === 'Manual' ? `Số liệu HIS nhập tay lúc ${at}${by}.` : `Số liệu HIS lấy lúc ${at}${by}.`
}

/**
 * Khoa nào đã nhập tay số liệu HIS (quyết định 35), để Phòng Điều dưỡng nhắc: từng khoa được giao chỉ số mục B, D, và số khoa
 * đã nhập "NB PK chờ nhập viện". Chỉ hiện khi chưa có số liệu HIS hoặc bảng có số do khoa nhập.
 */
function HisEntryStatusLine({ summary }: { summary: NursingSummary }) {
  const info = summary.statistics
  if (info.hasValues && !info.usesDepartmentEntries) {
    return null
  }
  const labels = new Map([...summary.outpatient, ...summary.imaging].map((l) => [l.key, l.label]))
  const assigned = info.entries.filter((e) => e.keys.some((k) => k !== WAITING_ADMISSION))
  const waiting = info.entries.filter((e) => e.keys.includes(WAITING_ADMISSION))
  const missingWaiting = waiting.filter((e) => !e.enteredAtUtc)
  const status = (e: NursingHisEntryStatus) =>
    e.enteredAtUtc ? `đã nhập lúc ${formatDateTime(e.enteredAtUtc)}${e.enteredBy ? ` (${e.enteredBy})` : ''}` : 'chưa nhập'
  return (
    <Typography.Text type="secondary">
      Khoa nhập tay khi HIS lỗi:{' '}
      {assigned.length === 0
        ? 'chưa giao khoa nhập mục B, D. '
        : assigned
            .map((e) => `${e.departmentName} (${e.keys.filter((k) => k !== WAITING_ADMISSION).map((k) => labels.get(k) ?? k).join(', ')}): ${status(e)}`)
            .join('; ') + '. '}
      NB PK chờ nhập viện: {waiting.length - missingWaiting.length}/{waiting.length} khoa đã nhập
      {missingWaiting.length > 0 ? ` (chưa nhập: ${missingWaiting.map((e) => e.departmentName).join(', ')})` : ''}.
    </Typography.Text>
  )
}

/** Người báo cáo của một mục (quyết định 44); mục chưa có số liệu ghi "chưa có". */
function Reporters({ text }: { text: string | null }) {
  return (
    <Typography.Paragraph type="secondary" style={{ marginTop: -8, marginBottom: 16 }}>
      Người báo cáo: {text ?? 'chưa có'}
    </Typography.Paragraph>
  )
}

/** Mục A–F như bản giấy; khoa ghi bằng mã khoa (tên viết tắt), mỗi mục có người báo cáo (quyết định 44). */
export function SummaryView({ summary, onOpen }: { summary: NursingSummary; onOpen: (row: NursingSummaryRow) => void }) {
  const columns: TableColumnsType<NursingSummaryRow> = [
    {
      title: 'Khoa phòng',
      dataIndex: 'departmentCode',
      fixed: 'left',
      width: 110,
      render: (code: string, row) => (
        <Flex vertical gap={2}>
          <Tooltip title={row.departmentName}>
            <Button
              type="link"
              aria-label={`${code} – ${row.departmentName}`}
              style={{ padding: 0, height: 'auto', alignSelf: 'flex-start', textAlign: 'start', whiteSpace: 'normal' }}
              onClick={() => onOpen(row)}
            >
              {code}
            </Button>
          </Tooltip>
          <span>{statusTag(row.status)}</span>
        </Flex>
      ),
    },
    ...summary.columns.map((c) => ({
      title: c.header,
      key: c.key,
      align: 'right' as const,
      // 14 cột số và cột người báo cáo (quyết định 44) vẫn vừa màn hình rộng 1366 điểm ảnh, không phải cuộn ngang.
      width: 72,
      render: (_: unknown, row: NursingSummaryRow) => number(row.cells[c.key]),
    })),
    { title: 'Người báo cáo', key: 'submittedBy', dataIndex: 'submittedBy', width: 150 },
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
              <Table.Summary.Cell index={summary.columns.length + 1} />
            </Table.Summary.Row>
          </Table.Summary>
        )}
        style={{ marginBottom: 16 }}
      />
      <Row gutter={16}>
        <Col xs={24} lg={8}>
          <LineTable title="B. Số liệu khoa khám bệnh" lines={summary.outpatient} reporters={summary.reporters.outpatient} />
        </Col>
        <Col xs={24} lg={8}>
          <LineTable title="C. Số liệu PTTT" lines={summary.procedures} reporters={summary.reporters.procedures} />
        </Col>
        <Col xs={24} lg={8}>
          <LineTable title="D. Số liệu CĐHA" lines={summary.imaging} reporters={summary.reporters.imaging} />
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
      <Reporters text={summary.reporters.situation} />
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
            {
              title: 'Khoa',
              dataIndex: 'departmentCode',
              width: '15%',
              render: (code: string, note) => <Tooltip title={note.departmentName}>{code}</Tooltip>,
            },
            {
              title: 'Nội dung báo cáo sự cố',
              dataIndex: 'text',
              render: (text: string) => <span style={{ whiteSpace: 'pre-wrap' }}>{text}</span>,
            },
          ]}
          style={{ marginBottom: 16 }}
        />
      )}
      <Reporters text={summary.reporters.incidents} />
    </>
  )
}

function LineTable({ title, lines, reporters }: { title: string; lines: NursingSummaryLine[]; reporters: string | null }) {
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
          {
            title: 'Nội dung',
            dataIndex: 'label',
            render: (label: string, line) =>
              line.enteredBy ? (
                <Flex vertical>
                  <span>{label}</span>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {line.enteredBy} nhập tay
                  </Typography.Text>
                </Flex>
              ) : (
                label
              ),
          },
          { title: 'Số liệu', dataIndex: 'value', width: 90, align: 'right', render: number },
        ]}
        style={{ marginBottom: 16 }}
      />
      <Reporters text={reporters} />
    </>
  )
}

/** Báo cáo của một khoa (chỉ đọc) trong bảng tổng hợp: in PDF, Phòng Điều dưỡng trả lại khoa để sửa khi cần. */
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
              <Button icon={<RollbackOutlined />} onClick={() => setReopenOpen(true)}>
                Trả lại khoa
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
              title={`Đã trả lại khoa lúc ${data.reopenedAtUtc ? formatDateTime(data.reopenedAtUtc) : ''}${data.reopenedBy ? ` bởi ${data.reopenedBy}` : ''}`}
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
      message.success('Đã trả lại báo cáo cho khoa. Khoa sửa và gửi lại.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  return (
    <Modal
      open
      title="Trả lại báo cáo cho khoa sửa"
      okText="Trả lại khoa"
      cancelText="Đóng"
      okButtonProps={{ disabled: reason.trim().length === 0, loading: reopen.isPending }}
      onOk={() => reopen.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph>Báo cáo trở lại dạng nháp; khoa sửa rồi gửi lại. Lý do được ghi vào nhật ký.</Typography.Paragraph>
      <Input.TextArea
        aria-label="Lý do trả lại"
        placeholder="Lý do trả lại (bắt buộc)"
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

/**
 * Phòng Điều dưỡng chọn khoa phải gửi báo cáo, cài số giường thực kê (không tính băng ca) của từng khoa, và giao khoa nhập tay
 * từng chỉ số mục B, D khi HIS lỗi (quyết định 35).
 */
function DepartmentsModal({ lines, onClose }: { lines: NursingSummaryLine[]; onClose: () => void }) {
  const departments = useNursingReportDepartments(true)
  const [edits, setEdits] = useState<Record<string, Pick<DepartmentRow, 'isReporting' | 'bedCount'>>>({})
  const [assignmentEdits, setAssignmentEdits] = useState<Record<string, string | null>>({})
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const rows: DepartmentRow[] = useMemo(
    () => (departments.data ?? []).map((d) => ({ ...d, ...edits[d.departmentId] })),
    [departments.data, edits],
  )
  const assignments = useMemo(() => {
    const current = Object.fromEntries(
      (departments.data ?? []).flatMap((d) => d.hisIndicators.map((key) => [key, d.departmentId] as const)),
    ) as Record<string, string>
    return Object.fromEntries(lines.map((l) => [l.key, l.key in assignmentEdits ? assignmentEdits[l.key] : (current[l.key] ?? null)]))
  }, [departments.data, assignmentEdits, lines])
  const save = useMutation({
    mutationFn: () =>
      nursingReportsApi.updateDepartments(
        rows.filter((r) => r.isReporting).map((r) => ({ departmentId: r.departmentId, bedCount: r.bedCount ?? 0 })),
        assignments,
      ),
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
      <Typography.Title level={5} style={{ marginTop: 20 }}>
        Khoa nhập số liệu HIS khi HIS lỗi
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        Khi không lấy được số liệu từ HIS, mỗi khoa nhập số thuộc nhiệm vụ của khoa ở trang Báo cáo của khoa; Phòng Điều dưỡng chỉ
        tổng hợp. Khoa phải báo cáo tự nhập "NB PK chờ nhập viện" của khoa mình. Chọn khoa nhập từng chỉ số dưới đây.
      </Typography.Paragraph>
      {lines.map((line) => (
        <Flex key={line.key} align="center" gap={12} wrap style={{ marginBottom: 8 }}>
          <span style={{ flex: '1 1 200px' }}>{line.label}</span>
          <Select
            aria-label={`Khoa nhập ${line.label}`}
            style={{ flex: '1 1 260px' }}
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Chưa giao khoa nào"
            disabled={!departments.data}
            value={assignments[line.key] ?? undefined}
            options={rows.map((r) => ({ value: r.departmentId, label: r.name }))}
            onChange={(value: string | undefined) => setAssignmentEdits((current) => ({ ...current, [line.key]: value ?? null }))}
          />
        </Flex>
      ))}
    </Modal>
  )
}
