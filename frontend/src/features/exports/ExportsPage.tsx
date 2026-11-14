import { DownloadOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Col, DatePicker, Divider, Flex, Radio, Row, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { useDepartments } from '../../api/admin'
import { formStatusLabels, type ConsentFormStatus } from '../../api/consentForms'
import { useConsentTemplates } from '../../api/consentTemplates'
import {
  exportFormatLabels,
  exportKeys,
  exportsApi,
  useExportColumns,
  useExportHistory,
  type ExportFilter,
  type ExportFormat,
  type ExportJob,
  type ExportRequest,
} from '../../api/reports'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDate, formatDateTime } from '../../utils/format'

const statusOptions = (Object.keys(formStatusLabels) as ConsentFormStatus[]).map((value) => ({ value, label: formStatusLabels[value] }))

/** Lưu file tải về bằng thẻ a tạm (trình duyệt hiện hộp thoại lưu hoặc tải thẳng vào thư mục tải xuống). */
function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** Màn hình 13: xuất Excel/CSV theo bộ lọc, chọn cột; dữ liệu nhạy cảm che mặc định; lịch sử xuất. */
export function ExportsPage() {
  const { hasPermission } = useAuth()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const canSensitive = hasPermission(Permissions.ExportsSensitive)
  const scopeAll = hasPermission(Permissions.ScopeAllDepartments)
  const columns = useExportColumns()
  const templates = useConsentTemplates(true, hasPermission(Permissions.TemplatesRead))
  const departments = useDepartments(false)

  const [format, setFormat] = useState<ExportFormat>('Xlsx')
  const [filter, setFilter] = useState<ExportFilter>({})
  // Chưa chọn (null): dùng các cột mặc định của danh mục.
  const [chosen, setChosen] = useState<string[] | null>(null)
  const defaultColumns = useMemo(() => columns.data?.filter((c) => c.default).map((c) => c.key) ?? [], [columns.data])
  const selected = chosen ?? defaultColumns
  const setSelected = (keys: string[]) => setChosen(keys)
  const [includeContent, setIncludeContent] = useState(false)
  const [includeSensitive, setIncludeSensitive] = useState(false)
  const [lastResult, setLastResult] = useState<{ fileName: string; rows: number } | null>(null)

  const groups = useMemo(() => {
    const map = new Map<string, typeof columns.data>()
    for (const column of columns.data ?? []) {
      map.set(column.group, [...(map.get(column.group) ?? []), column])
    }
    return [...map.entries()]
  }, [columns.data])

  const exportMutation = useMutation({
    mutationFn: (request: ExportRequest) => exportsApi.exportConsentForms(request),
    onSuccess: (file) => {
      saveBlob(file.blob, file.fileName)
      const rows = Number(file.headers.get('X-Export-Row-Count') ?? 0)
      setLastResult({ fileName: file.fileName, rows })
      message.success(`Đã xuất ${rows} phiếu: ${file.fileName}`)
      void queryClient.invalidateQueries({ queryKey: exportKeys.allHistory })
    },
  })

  const run = () =>
    exportMutation.mutate({
      format,
      filter,
      columns: selected,
      includeContent: includeContent && !!filter.templateId,
      includeSensitiveData: includeSensitive && canSensitive,
    })

  return (
    <>
      <PageHeader title="Xuất dữ liệu" />
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={10}>
          <Card title="Bộ lọc">
            <Flex vertical gap={12}>
              <Select
                allowClear
                placeholder="Trạng thái (tất cả)"
                aria-label="Trạng thái"
                options={statusOptions}
                value={filter.status}
                onChange={(status?: ConsentFormStatus) => setFilter((f) => ({ ...f, status }))}
              />
              <Select
                allowClear
                placeholder="Mẫu phiếu (tất cả)"
                aria-label="Mẫu phiếu"
                options={(templates.data ?? []).map((t) => ({ value: t.id, label: `${t.code} – ${t.name}` }))}
                value={filter.templateId}
                onChange={(templateId?: string) => {
                  setFilter((f) => ({ ...f, templateId }))
                  if (!templateId) setIncludeContent(false)
                }}
                popupMatchSelectWidth={false}
              />
              {scopeAll && (
                <Select
                  allowClear
                  placeholder="Khoa/phòng (toàn viện)"
                  aria-label="Khoa/phòng"
                  options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
                  value={filter.departmentId}
                  onChange={(departmentId?: string) => setFilter((f) => ({ ...f, departmentId }))}
                />
              )}
              <DatePicker.RangePicker
                format="DD/MM/YYYY"
                placeholder={['Lập từ ngày', 'Đến ngày']}
                onChange={(range) => {
                  const [from, to] = (range ?? []) as (Dayjs | null)[]
                  setFilter((f) => ({ ...f, from: from?.format('YYYY-MM-DD'), to: to?.format('YYYY-MM-DD') }))
                }}
              />
              {!scopeAll && (
                <Typography.Text type="secondary">Bạn chỉ xuất được phiếu của khoa mình và phiếu do mình lập.</Typography.Text>
              )}
            </Flex>
          </Card>
          <Card title="Định dạng và dữ liệu" style={{ marginTop: 16 }}>
            <Flex vertical gap={12}>
              <Radio.Group
                aria-label="Định dạng file"
                value={format}
                onChange={(e) => setFormat(e.target.value as ExportFormat)}
                options={(Object.keys(exportFormatLabels) as ExportFormat[]).map((value) => ({ value, label: exportFormatLabels[value] }))}
              />
              <Checkbox checked={includeContent} disabled={!filter.templateId} onChange={(e) => setIncludeContent(e.target.checked)}>
                Kèm nội dung phiếu theo mẫu{!filter.templateId && ' (chọn một mẫu phiếu ở bộ lọc)'}
              </Checkbox>
              {canSensitive ? (
                <Checkbox checked={includeSensitive} onChange={(e) => setIncludeSensitive(e.target.checked)}>
                  Xuất đầy đủ số CCCD, số điện thoại, số thẻ BHYT (dữ liệu nhạy cảm)
                </Checkbox>
              ) : (
                <Typography.Text type="secondary">Số CCCD, số điện thoại, số thẻ BHYT được che một phần (ví dụ 0790******12).</Typography.Text>
              )}
              {includeSensitive && canSensitive && (
                <Alert
                  type="warning"
                  showIcon
                  title="File chứa dữ liệu cá nhân đầy đủ. Chỉ dùng đúng mục đích, không gửi qua kênh không bảo mật. Lần xuất này được ghi nhật ký."
                />
              )}
            </Flex>
          </Card>
        </Col>
        <Col xs={24} xl={14}>
          <Card
            title="Cột xuất"
            extra={
              <Flex gap={8}>
                <Button size="small" onClick={() => setChosen(null)}>
                  Mặc định
                </Button>
                <Button size="small" onClick={() => setSelected(columns.data?.map((c) => c.key) ?? [])}>
                  Chọn tất cả
                </Button>
              </Flex>
            }
            loading={columns.isPending}
          >
            {columns.isError && <Alert type="error" showIcon title={errorMessage(columns.error)} />}
            {groups.map(([group, items]) => (
              <div key={group} style={{ marginBottom: 12 }}>
                <Typography.Text strong>{group}</Typography.Text>
                <Checkbox.Group
                  style={{ display: 'flex', flexWrap: 'wrap', gap: '4px 16px', marginTop: 4 }}
                  value={selected}
                  onChange={(values) => {
                    // Mỗi nhóm chỉ trả về lựa chọn của nhóm mình: giữ lựa chọn của các nhóm khác, xếp theo thứ tự danh mục.
                    const inGroup = new Set((items ?? []).map((c) => c.key))
                    const next = new Set([...selected.filter((key) => !inGroup.has(key)), ...(values as string[])])
                    setSelected((columns.data ?? []).map((c) => c.key).filter((key) => next.has(key)))
                  }}
                  options={(items ?? []).map((c) => ({
                    value: c.key,
                    label: (
                      <>
                        {c.header}
                        {c.sensitive && (
                          <Tag color="orange" style={{ marginLeft: 6 }}>
                            {canSensitive && includeSensitive ? 'đầy đủ' : 'che'}
                          </Tag>
                        )}
                      </>
                    ),
                  }))}
                />
              </div>
            ))}
            <Divider style={{ margin: '12px 0' }} />
            {exportMutation.isError && <Alert type="error" showIcon title={errorMessage(exportMutation.error)} style={{ marginBottom: 12 }} />}
            {lastResult && !exportMutation.isError && (
              <Alert type="success" showIcon title={`Đã xuất ${lastResult.rows} phiếu: ${lastResult.fileName}`} style={{ marginBottom: 12 }} />
            )}
            <Button
              type="primary"
              icon={<DownloadOutlined />}
              size="large"
              loading={exportMutation.isPending}
              disabled={selected.length === 0}
              onClick={run}
            >
              Xuất file
            </Button>
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              Mỗi lần xuất tối đa 100.000 phiếu và được ghi vào lịch sử xuất kèm mã băm của file. File không lưu lại trên máy chủ.
            </Typography.Paragraph>
          </Card>
        </Col>
        <Col xs={24}>
          <ExportHistory showAll={hasPermission(Permissions.AuditRead)} />
        </Col>
      </Row>
    </>
  )
}

function ExportHistory({ showAll }: { showAll: boolean }) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const history = useExportHistory(page, pageSize)

  const describeFilter = (job: ExportJob) => {
    const parts: string[] = []
    if (job.filter.status) parts.push(formStatusLabels[job.filter.status])
    if (job.filter.templateCode) parts.push(job.filter.templateCode)
    if (job.filter.departmentName) parts.push(job.filter.departmentName)
    if (job.filter.from || job.filter.to) parts.push(`${formatDate(job.filter.from)} – ${formatDate(job.filter.to)}`)
    return parts.length > 0 ? parts.join(' · ') : 'Tất cả'
  }

  const columns: TableColumnsType<ExportJob> = [
    { title: 'Thời điểm', dataIndex: 'requestedAtUtc', width: 150, render: (v: string) => formatDateTime(v) },
    ...(showAll ? [{ title: 'Người xuất', dataIndex: 'requestedByName', width: 180 } satisfies TableColumnsType<ExportJob>[number]] : []),
    { title: 'Định dạng', dataIndex: 'format', width: 110, render: (f: ExportFormat) => exportFormatLabels[f] },
    { title: 'Bộ lọc', key: 'filter', render: (_, job) => describeFilter(job) },
    { title: 'Số phiếu', dataIndex: 'rowCount', width: 100, align: 'right' },
    {
      title: 'Dữ liệu',
      key: 'flags',
      width: 200,
      render: (_, job) => (
        <>
          {job.includesSensitiveData ? <Tag color="orange">Nhạy cảm đầy đủ</Tag> : <Tag>Đã che</Tag>}
          {job.includesContent && <Tag color="blue">Kèm nội dung</Tag>}
        </>
      ),
    },
    {
      title: 'File',
      key: 'file',
      render: (_, job) => (
        <Flex vertical>
          <Typography.Text>{job.fileName}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {formatBytes(job.fileSizeBytes)} · {job.columns.length} cột · {(job.durationMs / 1000).toFixed(1)} giây · SHA-256{' '}
            <Typography.Text code copyable={{ text: job.fileSha256 }}>
              {job.fileSha256.slice(0, 12)}…
            </Typography.Text>
          </Typography.Text>
        </Flex>
      ),
    },
  ]

  return (
    <Card title={showAll ? 'Lịch sử xuất (toàn hệ thống)' : 'Lịch sử xuất của tôi'}>
      {history.isError && <Alert type="error" showIcon title={errorMessage(history.error)} style={{ marginBottom: 12 }} />}
      <Table<ExportJob>
        rowKey="id"
        size="small"
        columns={columns}
        dataSource={history.data?.items}
        loading={history.isPending}
        locale={{ emptyText: 'Chưa có lần xuất nào.' }}
        scroll={{ x: 1000 }}
        pagination={{
          current: page,
          pageSize,
          total: history.data?.totalCount,
          showSizeChanger: true,
          onChange: (p, size) => {
            setPage(p)
            setPageSize(size)
          },
        }}
      />
    </Card>
  )
}
