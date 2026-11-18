import { Alert, Card, DatePicker, Flex, Input, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router'
import { auditActionLabels, auditEntityLabels, useAuditLogs, type AuditLogEntry, type AuditLogQuery } from '../../api/reports'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'

const actionOptions = Object.entries(auditActionLabels).map(([value, label]) => ({ value, label: `${label} (${value})` }))
const entityOptions = Object.entries(auditEntityLabels).map(([value, label]) => ({ value, label }))

function entityLink(entry: AuditLogEntry) {
  if (!entry.entityId) return null
  switch (entry.entityType) {
    case 'ConsentForm':
      return <Link to={`/phieu-dong-thuan/${entry.entityId}`}>{entry.entityId}</Link>
    case 'Patient':
      return <Link to={`/nguoi-benh/${entry.entityId}`}>{entry.entityId}</Link>
    default:
      return <Typography.Text code>{entry.entityId}</Typography.Text>
  }
}

function prettyDetails(details: string | null): string {
  if (!details) return ''
  try {
    return JSON.stringify(JSON.parse(details), null, 2)
  } catch {
    return details
  }
}

/** Màn hình 15: tra cứu nhật ký hệ thống (chỉ đọc). Dữ liệu nhạy cảm đã được che khi ghi. */
export function AuditLogsPage() {
  const [query, setQuery] = useState<AuditLogQuery>({ page: 1, pageSize: 50 })
  const logs = useAuditLogs(query)
  const update = (patch: Partial<AuditLogQuery>) => setQuery((q) => ({ ...q, ...patch, page: 1 }))

  const columns: TableColumnsType<AuditLogEntry> = [
    { title: 'Thời điểm', dataIndex: 'occurredAtUtc', width: 150, render: (v: string) => formatDateTime(v) },
    { title: 'Người dùng', dataIndex: 'userName', width: 160, render: (v: string | null) => v ?? <Typography.Text type="secondary">hệ thống</Typography.Text> },
    {
      title: 'Hành động',
      dataIndex: 'action',
      width: 220,
      render: (action: string) => (
        <Flex vertical>
          <Typography.Text>{auditActionLabels[action] ?? action}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {action}
          </Typography.Text>
        </Flex>
      ),
    },
    {
      title: 'Đối tượng',
      key: 'entity',
      render: (_, entry) => (
        <Flex vertical>
          {entry.entityType && <Tag style={{ width: 'fit-content' }}>{auditEntityLabels[entry.entityType] ?? entry.entityType}</Tag>}
          <Typography.Text style={{ fontSize: 12 }}>{entityLink(entry)}</Typography.Text>
        </Flex>
      ),
    },
    { title: 'IP', dataIndex: 'ipAddress', width: 130 },
  ]

  return (
    <>
      <PageHeader title="Nhật ký hệ thống" />
      <Card>
        <Flex gap={12} wrap style={{ marginBottom: 16 }}>
          <Select
            allowClear
            showSearch
            placeholder="Hành động"
            aria-label="Hành động"
            options={actionOptions}
            optionFilterProp="label"
            onChange={(action?: string) => update({ action })}
            style={{ width: 300, maxWidth: '100%' }}
          />
          <Select
            allowClear
            placeholder="Loại đối tượng"
            aria-label="Loại đối tượng"
            options={entityOptions}
            onChange={(entityType?: string) => update({ entityType })}
            style={{ width: 200 }}
          />
          <Input.Search
            allowClear
            placeholder="Mã đối tượng (Id)"
            aria-label="Mã đối tượng"
            onSearch={(value) => update({ entityId: value.trim() || undefined })}
            style={{ width: 320, maxWidth: '100%' }}
          />
          <DatePicker.RangePicker
            showTime={{ format: 'HH:mm' }}
            format="DD/MM/YYYY HH:mm"
            placeholder={['Từ thời điểm', 'Đến thời điểm']}
            onChange={(range) => {
              const [from, to] = (range ?? []) as (Dayjs | null)[]
              update({ fromUtc: from?.toISOString(), toUtc: to?.toISOString() })
            }}
          />
        </Flex>
        {logs.isError && <Alert type="error" showIcon title={errorMessage(logs.error)} style={{ marginBottom: 16 }} />}
        <Table<AuditLogEntry>
          rowKey="id"
          size="small"
          columns={columns}
          dataSource={logs.data?.items}
          loading={logs.isPending || logs.isFetching}
          scroll={{ x: 900 }}
          locale={{ emptyText: 'Không có bản ghi nào.' }}
          expandable={{
            rowExpandable: (entry) => !!entry.details || !!entry.correlationId,
            expandedRowRender: (entry) => (
              <Flex vertical gap={4}>
                {entry.correlationId && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    Mã yêu cầu: <Typography.Text code>{entry.correlationId}</Typography.Text>
                  </Typography.Text>
                )}
                {entry.details && (
                  <pre style={{ margin: 0, fontSize: 12, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{prettyDetails(entry.details)}</pre>
                )}
              </Flex>
            ),
          }}
          pagination={{
            current: query.page,
            pageSize: query.pageSize,
            total: logs.data?.totalCount,
            showSizeChanger: true,
            pageSizeOptions: [20, 50, 100],
            showTotal: (total) => `Tổng ${total} bản ghi`,
            onChange: (page, pageSize) => setQuery((q) => ({ ...q, page, pageSize })),
          }}
        />
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          Nhật ký chỉ ghi thêm, không sửa, không xóa. Giá trị nhạy cảm (số CCCD, số điện thoại, nội dung phiếu, mật khẩu) đã được che khi ghi.
        </Typography.Paragraph>
      </Card>
    </>
  )
}
