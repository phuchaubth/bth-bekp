import {
  ArrowDownOutlined,
  ArrowUpOutlined,
  CloudDownloadOutlined,
  FileExcelOutlined,
  FilePdfOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  Flex,
  Input,
  InputNumber,
  Modal,
  Row,
  Select,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
  type TableColumnsType,
} from 'antd'
import dayjs from 'dayjs'
import { useMemo, useState } from 'react'
import {
  WAITING_ADMISSION,
  nursingKeys,
  nursingReportsApi,
  useNursingReportDepartments,
  useNursingSummary,
  type NursingHisEntryStatus,
  type NursingSummary,
  type NursingSummaryDelegation,
  type NursingSummaryLine,
  type NursingSummaryRow,
} from '../../api/nursingReports'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { DepartmentCodeLink, type OpenedDepartment } from './DepartmentReportDrawer'
import { PdfModal } from './NursingReportFields'
import { delegationContents, delegationPeriod, saveBlob, statusTag } from './nursingReportUtils'

const number = (value: number | null | undefined) => (value === null || value === undefined ? '' : value)

/**
 * Bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" (quyết định 31) của ngày `reportDate`: mục A–F như bản giấy, lấy từ báo
 * cáo các khoa đã gửi và số liệu HIS. Nằm ở thẻ "Bảng tổng hợp" của Dashboard điều dưỡng (quyết định 58; trước là trang Tổng hợp
 * hằng ngày riêng), ngày chọn ở đầu Dashboard. Phòng Điều dưỡng in PDF, xuất Excel, xem báo cáo từng khoa (`onOpen`: ngăn kéo của
 * Dashboard, trả lại khoa ở đó), cài khoa báo cáo, số giường và khoa nhập từng chỉ số, lấy số liệu HIS. Khi HIS lỗi, các khoa
 * nhập số liệu của khoa; Phòng Điều dưỡng chỉ tổng hợp (quyết định 35). Số khoa đã gửi ở ô chỉ số đầu Dashboard.
 */
export function NursingSummaryPanel({ reportDate, onOpen }: { reportDate: string; onOpen: (department: OpenedDepartment) => void }) {
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.NursingReportsManage)
  const summary = useNursingSummary(reportDate)
  const [pdfOpen, setPdfOpen] = useState(false)
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

  if (summary.isError) {
    return <Alert type="error" showIcon title={errorMessage(summary.error)} />
  }
  if (!summary.data) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }
  const data = summary.data

  return (
    <>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex justify="space-between" align="center" wrap gap={8}>
          <Flex vertical gap={2}>
            <Typography.Text type="secondary">{statisticsText(data)}</Typography.Text>
            <HisEntryStatusLine summary={data} />
          </Flex>
          <Flex gap={8} wrap>
            <Button icon={<FilePdfOutlined />} onClick={() => setPdfOpen(true)}>
              In PDF
            </Button>
            <Button icon={<FileExcelOutlined />} loading={workbook.isPending} onClick={() => workbook.mutate()}>
              Xuất Excel
            </Button>
            {canManage && (
              <>
                <Button icon={<CloudDownloadOutlined />} loading={refresh.isPending} onClick={() => refresh.mutate()}>
                  Lấy số liệu HIS
                </Button>
                <Button icon={<SettingOutlined />} onClick={() => setDepartmentsOpen(true)}>
                  Khoa báo cáo và số giường
                </Button>
              </>
            )}
          </Flex>
        </Flex>
      </Card>
      {data.delegations && data.delegations.length > 0 && <DelegationsAlert delegations={data.delegations} />}
      <SummaryView
        summary={data}
        onOpen={(row) => onOpen({ departmentId: row.departmentId, departmentName: row.departmentName, reportDate: data.reportDate })}
      />

      <PdfModal
        open={pdfOpen}
        title={`Tổng hợp báo cáo điều dưỡng ngày ${dayjs(reportDate).format('DD/MM/YYYY')}`}
        fileName={`tong-hop-dieu-duong-${reportDate}.pdf`}
        queryKey={[...nursingKeys.summary(reportDate), summary.dataUpdatedAt]}
        load={() => nursingReportsApi.summaryPdf(reportDate)}
        onClose={() => setPdfOpen(false)}
      />
      {departmentsOpen && <DepartmentsModal lines={[...data.outpatient, ...data.imaging]} onClose={() => setDepartmentsOpen(false)} />}
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
    e.enteredAtUtc ? `đã nhập lúc ${formatDateTime(e.enteredAtUtc)}${e.enteredBy ? ` (${enteredBy(e)})` : ''}` : 'chưa nhập'
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

/** Người nhập số liệu HIS của khoa, kèm người ủy quyền khi nhập theo ủy quyền (quyết định 48). */
const enteredBy = (e: NursingHisEntryStatus) =>
  e.enteredByDelegator ? `${e.enteredBy}, được ${e.enteredByDelegator} ủy quyền` : e.enteredBy

/**
 * Ủy quyền báo cáo có hiệu lực trong ngày tổng hợp (quyết định 48): Phòng Điều dưỡng biết khoa nào do người được ủy quyền
 * làm. Báo cáo gửi theo ủy quyền ghi "được … ủy quyền" ở cột Người báo cáo (cả PDF và Excel).
 */
function DelegationsAlert({ delegations }: { delegations: NursingSummaryDelegation[] }) {
  return (
    <Alert
      type="info"
      showIcon
      style={{ marginBottom: 16 }}
      title={`Ủy quyền báo cáo trong ngày (${delegations.length})`}
      description={
        <ul style={{ margin: 0, paddingInlineStart: 20 }}>
          {delegations.map((d, index) => (
            // Ủy quyền trong bảng tổng hợp không kèm mã: khóa ghép từ các trường, thêm vị trí cho chắc chắn không trùng.
            <li key={`${d.departmentId}-${d.delegateName}-${d.startDate}-${index}`}>
              <b>{d.departmentCode}</b>: {d.delegateName} được {d.delegatorName} ủy quyền, {delegationPeriod(d)} (
              {delegationContents(d.contents)})
            </li>
          ))}
        </ul>
      }
    />
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
      // Mã khoa (tên viết tắt) ngắn; tên dài thì xuống dòng.
      width: 96,
      render: (code: string, row) => (
        <Flex vertical gap={2}>
          <DepartmentCodeLink code={code} name={row.departmentName} onClick={() => onOpen(row)} />
          <span>{statusTag(row.status)}</span>
        </Flex>
      ),
    },
    ...summary.columns.map((c) => ({
      title: c.header,
      key: c.key,
      align: 'right' as const,
      // 16 cột số (thêm "Số NB ngày hôm trước", quyết định 46; "Bao phòng", quyết định 54) và cột người báo cáo vẫn vừa màn hình
      // rộng 1366 điểm ảnh, không phải cuộn ngang.
      width: 63,
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

interface DepartmentRow {
  departmentId: string
  code: string
  name: string
  shortName: string | null
  isActive: boolean
  isReporting: boolean
  bedCount: number | null
}

/** Phần đang sửa của một khoa: số giường, tên viết tắt (chữ đang gõ). */
interface DepartmentEdit {
  bedCount?: number | null
  shortName?: string
}

/**
 * Khoa ghi trên bảng tổng hợp bằng tên viết tắt (bỏ khoảng trắng hai đầu), để trống thì bằng mã khoa (quyết định 45). Dạng so
 * trùng: Unicode dựng sẵn, chữ hoa, như máy chủ (gõ kiểu tổ hợp vẫn nhận ra trùng).
 */
const departmentLabelKey = (row: Pick<DepartmentRow, 'code' | 'shortName'>) =>
  (row.shortName?.trim() || row.code).normalize('NFC').toUpperCase()

/**
 * Phòng Điều dưỡng chọn khoa phải gửi báo cáo, cài số giường thực kê (không tính băng ca) của từng khoa, và giao khoa nhập tay
 * từng chỉ số mục B, D khi HIS lỗi (quyết định 35). Khoa được chọn đứng đầu danh sách theo thứ tự in ở mục A của bảng tổng hợp,
 * Phòng Điều dưỡng đổi chỗ được; mỗi khoa có tên viết tắt in thay mã khoa (quyết định 45). Khoa đã ngừng sử dụng (ví dụ khoa nhập
 * tay trước đây không còn trong danh mục khoa lấy từ HIS) có nhãn "Ngừng sử dụng", không chọn được và được bỏ khi lưu, kể cả phần
 * giao chỉ số; nếu không máy chủ từ chối cả lần lưu ("Dữ liệu không hợp lệ").
 */
function DepartmentsModal({ lines, onClose }: { lines: NursingSummaryLine[]; onClose: () => void }) {
  const departments = useNursingReportDepartments(true)
  const [edits, setEdits] = useState<Record<string, DepartmentEdit>>({})
  // Khoa phải báo cáo theo thứ tự trên bảng tổng hợp; null là chưa đổi, theo máy chủ (khoa phải báo cáo đứng đầu, đúng thứ tự).
  const [order, setOrder] = useState<string[] | null>(null)
  const [assignmentEdits, setAssignmentEdits] = useState<Record<string, string | null>>({})
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const saved = departments.data
  const reporting = useMemo(
    () => order ?? (saved ?? []).filter((d) => d.isActive && d.isReporting).map((d) => d.departmentId),
    [order, saved],
  )
  // Khoa phải báo cáo theo thứ tự, rồi các khoa khác (theo tên như máy chủ), khoa ngừng sử dụng xuống cuối.
  const { rows, ordered } = useMemo(() => {
    const all = (saved ?? []).map(
      (d): DepartmentRow => ({
        ...d,
        ...edits[d.departmentId],
        isReporting: d.isActive ? reporting.includes(d.departmentId) : d.isReporting,
      }),
    )
    const byId = new Map(all.map((r) => [r.departmentId, r]))
    const reportingRows = reporting.flatMap((id) => {
      const row = byId.get(id)
      return row?.isActive ? [row] : []
    })
    return {
      ordered: reportingRows.map((r) => r.departmentId),
      rows: [...reportingRows, ...all.filter((r) => r.isActive && !r.isReporting), ...all.filter((r) => !r.isActive)],
    }
  }, [saved, edits, reporting])
  const stopped = useMemo(() => (saved ?? []).filter((d) => !d.isActive), [saved])
  // Tên viết tắt (hoặc mã khoa khi để trống) trùng với khoa đang dùng khác: bảng tổng hợp lẫn khoa, máy chủ không nhận khi lưu.
  const duplicates = useMemo(() => {
    const active = rows.filter((r) => r.isActive)
    return Object.fromEntries(
      active.flatMap((r) => {
        const label = departmentLabelKey(r)
        const other = active.find((o) => o.departmentId !== r.departmentId && departmentLabelKey(o) === label)
        return other ? [[r.departmentId, other] as const] : []
      }),
    ) as Record<string, DepartmentRow>
  }, [rows])
  const current = useMemo(
    () =>
      Object.fromEntries(
        (saved ?? []).flatMap((d) => d.hisIndicators.map((key) => [key, d.departmentId] as const)),
      ) as Record<string, string>,
    [saved],
  )
  // Chỉ số đang giao cho khoa ngừng sử dụng: coi như chưa giao (khi lưu gửi null) cho tới khi chọn khoa khác.
  const assignments = useMemo(() => {
    const usable = (departmentId: string | null | undefined) =>
      departmentId && rows.some((r) => r.departmentId === departmentId && r.isActive) ? departmentId : null
    return Object.fromEntries(lines.map((l) => [l.key, usable(l.key in assignmentEdits ? assignmentEdits[l.key] : current[l.key])]))
  }, [rows, current, assignmentEdits, lines])
  const save = useMutation({
    mutationFn: () =>
      nursingReportsApi.updateDepartments(
        rows.filter((r) => r.isActive && r.isReporting).map((r) => ({ departmentId: r.departmentId, bedCount: r.bedCount ?? 0 })),
        assignments,
        // Chỉ gửi tên viết tắt đã đổi; để trống là bỏ tên viết tắt, in lại mã khoa.
        Object.fromEntries(
          (saved ?? []).flatMap((d) => {
            const typed = edits[d.departmentId]?.shortName
            const value = typed?.trim() || null
            return d.isActive && typed !== undefined && value !== d.shortName ? [[d.departmentId, value] as const] : []
          }),
        ),
      ),
    onSuccess: (saved) => {
      queryClient.setQueryData(nursingKeys.departments, saved)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.all })
      message.success('Đã lưu danh sách khoa báo cáo.')
      onClose()
    },
    // Không trả về kết quả của message.error: React Query sẽ chờ thông báo đóng (3 giây) rồi mới hết trạng thái đang lưu.
    onError: (e) => {
      message.error(errorMessageWithDetails(e))
    },
  })
  const update = (row: DepartmentRow, change: DepartmentEdit) =>
    setEdits((edited) => ({ ...edited, [row.departmentId]: { ...edited[row.departmentId], ...change } }))
  // Khoa mới chọn xếp cuối các khoa phải báo cáo; khoa bỏ chọn ra khỏi thứ tự.
  const setReporting = (row: DepartmentRow, checked: boolean) => {
    const others = ordered.filter((id) => id !== row.departmentId)
    setOrder(checked ? [...others, row.departmentId] : others)
  }
  // Chuyển khoa tới vị trí index (tính từ 0); ngoài khoảng thì về đầu hoặc cuối.
  const move = (row: DepartmentRow, index: number) => {
    const next = ordered.filter((id) => id !== row.departmentId)
    next.splice(Math.min(Math.max(index, 0), next.length), 0, row.departmentId)
    setOrder(next)
  }

  return (
    <Modal
      open
      width="min(980px, 96vw)"
      title="Khoa báo cáo và số giường thực kê"
      okText="Lưu"
      cancelText="Đóng"
      okButtonProps={{ loading: save.isPending, disabled: !saved }}
      onOk={() => save.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph type="secondary">
        Khoa được chọn mới lập được báo cáo và có dòng trên bảng tổng hợp. Số giường thực kê không tính băng ca.
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary">
        Các khoa được chọn đứng đầu danh sách, theo thứ tự in ở mục A của bảng tổng hợp: gõ số thứ tự rồi bấm Enter, hoặc bấm mũi tên
        để đổi chỗ; khoa mới chọn xếp cuối. Tên viết tắt in ở cột khoa phòng của bảng tổng hợp thay cho mã khoa (để trống thì in mã
        khoa), không trùng nhau.
      </Typography.Paragraph>
      {stopped.length > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          title="Có khoa đã ngừng sử dụng"
          description={`${stopped.map((d) => `${d.name} (${d.code})`).join(', ')}: không còn trong danh mục khoa/phòng. Khi lưu, các khoa này được bỏ khỏi danh sách khoa báo cáo và không còn được giao nhập số liệu HIS. Chọn khoa thay thế trong danh sách nếu cần.`}
        />
      )}
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
              width: 84,
              render: (_, row) => (
                <Checkbox
                  aria-label={`${row.name} phải báo cáo`}
                  checked={row.isActive && row.isReporting}
                  disabled={!row.isActive}
                  onChange={(e) => setReporting(row, e.target.checked)}
                />
              ),
            },
            {
              title: 'Thứ tự',
              width: 128,
              render: (_, row) => {
                const index = ordered.indexOf(row.departmentId)
                return (
                  index >= 0 && (
                    <Flex align="center" gap={2}>
                      <PositionInput
                        label={`Thứ tự ${row.name}`}
                        position={index + 1}
                        onCommit={(position) => move(row, position - 1)}
                      />
                      <Button
                        size="small"
                        type="text"
                        icon={<ArrowUpOutlined />}
                        aria-label={`Đưa ${row.name} lên`}
                        disabled={index === 0}
                        onClick={() => move(row, index - 1)}
                      />
                      <Button
                        size="small"
                        type="text"
                        icon={<ArrowDownOutlined />}
                        aria-label={`Đưa ${row.name} xuống`}
                        disabled={index === ordered.length - 1}
                        onClick={() => move(row, index + 1)}
                      />
                    </Flex>
                  )
                )
              },
            },
            {
              title: 'Khoa/phòng',
              dataIndex: 'name',
              render: (name: string, row) => (
                <>
                  {`${name} (${row.code})`}
                  {!row.isActive && <Tag style={{ marginInlineStart: 8 }}>Ngừng sử dụng</Tag>}
                </>
              ),
            },
            {
              title: 'Tên viết tắt',
              width: 170,
              render: (_, row) => {
                const other = duplicates[row.departmentId]
                return (
                  <Flex vertical gap={2}>
                    <Input
                      aria-label={`Tên viết tắt ${row.name}`}
                      placeholder={row.code}
                      maxLength={20}
                      disabled={!row.isActive}
                      status={other ? 'error' : undefined}
                      value={row.shortName ?? ''}
                      onChange={(e) => update(row, { shortName: e.target.value })}
                    />
                    {other && (
                      <Typography.Text type="danger" style={{ fontSize: 12 }}>
                        Trùng với {other.name} ({other.code})
                      </Typography.Text>
                    )}
                  </Flex>
                )
              },
            },
            {
              title: 'Số giường',
              width: 130,
              render: (_, row) => (
                <InputNumber
                  aria-label={`Số giường ${row.name}`}
                  min={0}
                  max={10000}
                  precision={0}
                  disabled={!row.isActive || !row.isReporting}
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
      {lines.map((line) => {
        // Đang giao cho khoa ngừng sử dụng (chưa chọn lại): nhắc chọn khoa khác.
        const stale = line.key in assignmentEdits ? undefined : stopped.find((d) => d.departmentId === current[line.key])
        return (
          <Flex key={line.key} align="center" gap={12} wrap style={{ marginBottom: 8 }}>
            <span style={{ flex: '1 1 200px' }}>{line.label}</span>
            <Select
              aria-label={`Khoa nhập ${line.label}`}
              style={{ flex: '1 1 260px' }}
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Chưa giao khoa nào"
              disabled={!saved}
              status={stale ? 'warning' : undefined}
              value={assignments[line.key] ?? undefined}
              options={rows.filter((r) => r.isActive).map((r) => ({ value: r.departmentId, label: r.name }))}
              onChange={(value: string | undefined) => setAssignmentEdits((edited) => ({ ...edited, [line.key]: value ?? null }))}
            />
            {stale && (
              <Typography.Text type="warning" style={{ flexBasis: '100%' }}>
                Đang giao {stale.name} ({stale.code}), khoa này đã ngừng sử dụng. Chọn khoa khác; để trống thì khi lưu chỉ số này không
                giao khoa nào.
              </Typography.Text>
            )}
          </Flex>
        )
      })}
    </Modal>
  )
}

/**
 * Ô số thứ tự của khoa trên bảng tổng hợp: gõ số rồi bấm Enter (hoặc rời ô) để chuyển khoa tới vị trí đó, các khoa khác dịch
 * xuống. Chỉ nhận chữ số; để trống thì giữ thứ tự cũ.
 */
function PositionInput({ label, position, onCommit }: { label: string; position: number; onCommit: (position: number) => void }) {
  const [draft, setDraft] = useState<string | null>(null)
  const commit = () => {
    const value = Number.parseInt(draft ?? '', 10)
    setDraft(null)
    if (Number.isInteger(value) && value !== position) {
      onCommit(value)
    }
  }
  return (
    <Input
      aria-label={label}
      size="small"
      inputMode="numeric"
      maxLength={4}
      style={{ width: 44, textAlign: 'center' }}
      value={draft ?? String(position)}
      onChange={(e) => setDraft(e.target.value.replace(/\D/g, ''))}
      onBlur={commit}
      onPressEnter={commit}
    />
  )
}
