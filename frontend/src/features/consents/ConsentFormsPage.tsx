import { PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, DatePicker, Flex, Input, Select, Table, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { formStatusLabels, useConsentForms, type ConsentFormListItem, type ConsentFormQuery, type ConsentFormStatus } from '../../api/consentForms'
import { useConsentTemplates } from '../../api/consentTemplates'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { FormStatusTag } from './FormStatusTag'

const statusOptions = (Object.keys(formStatusLabels) as ConsentFormStatus[]).map((value) => ({ value, label: formStatusLabels[value] }))

/** Màn hình 3: danh sách phiếu (lọc, phân trang ở máy chủ; chỉ thấy phiếu trong phạm vi khoa). */
export function ConsentFormsPage() {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const [query, setQuery] = useState<ConsentFormQuery>({ page: 1, pageSize: 20 })
  const { data, isPending, isFetching, isError, error } = useConsentForms(query)
  const templates = useConsentTemplates(true, hasPermission(Permissions.TemplatesRead))
  const update = (patch: Partial<ConsentFormQuery>) => setQuery((current) => ({ ...current, ...patch, page: 1 }))

  const columns: TableColumnsType<ConsentFormListItem> = [
    {
      title: 'Số phiếu',
      dataIndex: 'formNo',
      width: 170,
      fixed: 'left',
      render: (formNo: string, form) => (
        <Link to={`/phieu-dong-thuan/${form.id}`} style={{ fontVariantNumeric: 'tabular-nums' }}>
          {formNo}
        </Link>
      ),
    },
    {
      title: 'Người bệnh',
      key: 'patient',
      width: 220,
      render: (_, form) => (
        <Flex vertical>
          <Typography.Text strong>{form.patientName}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {form.patientCode ?? 'Chưa có mã người bệnh'}
          </Typography.Text>
        </Flex>
      ),
    },
    {
      title: 'Mẫu phiếu',
      key: 'template',
      width: 280,
      render: (_, form) => (
        <Flex vertical>
          <Typography.Text>{form.templateName}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {form.templateCode} · phiên bản {form.templateVersionNo}
          </Typography.Text>
        </Flex>
      ),
    },
    { title: 'Khoa/phòng', dataIndex: 'departmentName', width: 170 },
    { title: 'Trạng thái', dataIndex: 'status', width: 120, render: (status: ConsentFormStatus) => <FormStatusTag status={status} /> },
    { title: 'Người lập', dataIndex: 'createdByName', width: 170 },
    { title: 'Ngày lập', dataIndex: 'createdAtUtc', width: 150, render: (value: string) => formatDateTime(value) },
  ]

  return (
    <>
      <PageHeader
        title="Phiếu đồng thuận"
        extra={
          hasPermission(Permissions.ConsentsCreate) && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/phieu-dong-thuan/tao-moi')}>
              Lập phiếu
            </Button>
          )
        }
      />
      <Card>
        <Flex gap={12} wrap style={{ marginBottom: 16 }}>
          <Input.Search
            allowClear
            placeholder="Tìm theo số phiếu, họ tên, mã người bệnh"
            onSearch={(keyword) => update({ keyword: keyword.trim() || undefined })}
            style={{ width: 320, maxWidth: '100%' }}
          />
          <Select
            allowClear
            placeholder="Trạng thái"
            aria-label="Lọc theo trạng thái"
            options={statusOptions}
            onChange={(status?: ConsentFormStatus) => update({ status })}
            style={{ width: 160 }}
          />
          {templates.data && (
            <Select
              allowClear
              placeholder="Mẫu phiếu"
              aria-label="Lọc theo mẫu phiếu"
              options={templates.data.map((t) => ({ value: t.id, label: `${t.code} – ${t.name}` }))}
              onChange={(templateId?: string) => update({ templateId })}
              style={{ width: 280, maxWidth: '100%' }}
              popupMatchSelectWidth={false}
            />
          )}
          <DatePicker.RangePicker
            format="DD/MM/YYYY"
            placeholder={['Từ ngày', 'Đến ngày']}
            onChange={(range) => {
              const [from, to] = (range ?? []) as (Dayjs | null)[]
              update({ from: from?.format('YYYY-MM-DD'), to: to?.format('YYYY-MM-DD') })
            }}
          />
        </Flex>
        {isError && <Alert type="error" showIcon title={errorMessage(error)} style={{ marginBottom: 16 }} />}
        <Table<ConsentFormListItem>
          rowKey="id"
          columns={columns}
          dataSource={data?.items}
          loading={isPending || isFetching}
          scroll={{ x: 1100 }}
          locale={{ emptyText: 'Chưa có phiếu nào.' }}
          pagination={{
            current: query.page,
            pageSize: query.pageSize,
            total: data?.totalCount,
            showSizeChanger: true,
            showTotal: (total) => `Tổng ${total} phiếu`,
            onChange: (page, pageSize) => setQuery((current) => ({ ...current, page, pageSize })),
          }}
          onRow={(form) => ({ onDoubleClick: () => navigate(`/phieu-dong-thuan/${form.id}`) })}
        />
      </Card>
    </>
  )
}
