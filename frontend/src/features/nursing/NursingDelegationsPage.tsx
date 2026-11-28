import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  DatePicker,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  type TableColumnsType,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router'
import { accountDepartments } from '../../api/auth'
import { ApiError } from '../../api/http'
import {
  DELEGATION_CONTENT_LABELS,
  nursingKeys,
  nursingReportsApi,
  useNursingDelegationCandidates,
  useNursingDelegations,
  type NursingDelegation,
  type NursingDelegationContent,
  type NursingDelegationStatus,
} from '../../api/nursingReports'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { delegationPeriod } from './nursingReportUtils'

const DATE = 'YYYY-MM-DD'

const STATUS: Record<NursingDelegationStatus, { label: string; color: string }> = {
  Upcoming: { label: 'Sắp tới', color: 'blue' },
  Active: { label: 'Đang hiệu lực', color: 'green' },
  Expired: { label: 'Đã hết hạn', color: 'default' },
  Revoked: { label: 'Đã hủy', color: 'red' },
}

const CONTENTS: NursingDelegationContent[] = ['report', 'hisEntry']

interface CreateValues {
  departmentId: string
  delegateUserId: string
  range: [Dayjs, Dayjs]
  contents: NursingDelegationContent[]
  reason?: string
}

/**
 * Ủy quyền báo cáo điều dưỡng khi người báo cáo vắng (quyết định 48). Người lập báo cáo của khoa ủy quyền cho người khác làm
 * báo cáo của khoa và/hoặc nhập số liệu HIS, từ ngày đến ngày; xem ủy quyền mình đã tạo và được giao, hủy ủy quyền. Phòng Điều
 * dưỡng xem mọi ủy quyền.
 */
export function NursingDelegationsPage() {
  const { user, hasPermission } = useAuth()
  const canSubmit = hasPermission(Permissions.NursingReportsSubmit)
  const canReadAll = hasPermission(Permissions.NursingReportsRead) || hasPermission(Permissions.NursingReportsManage)
  const mine = useNursingDelegations(false, canSubmit)
  const all = useNursingDelegations(true, canReadAll)
  const [creating, setCreating] = useState(false)

  return (
    <>
      <PageHeader
        title="Ủy quyền báo cáo điều dưỡng"
        extra={
          canSubmit && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              Ủy quyền mới
            </Button>
          )
        }
      />
      <Typography.Paragraph type="secondary">
        Khi vắng, người lập báo cáo của khoa ủy quyền cho người khác làm báo cáo của khoa và/hoặc nhập số liệu HIS của khoa trong một
        khoảng ngày. Người được ủy quyền chỉ làm được trong thời gian đó, với báo cáo của các ngày đó. Bảng tổng hợp ghi rõ người được
        ủy quyền.
      </Typography.Paragraph>
      {canSubmit &&
        (mine.isError ? (
          <Alert type="error" showIcon title={errorMessage(mine.error)} style={{ marginBottom: 16 }} />
        ) : (
          <>
            <DelegationTable
              title="Tôi đã ủy quyền"
              rows={mine.data?.filter((d) => d.delegatorUserId === user?.id)}
              loading={mine.isPending}
              people="delegate"
              empty="Bạn chưa ủy quyền cho ai."
            />
            <DelegationTable
              title="Tôi được ủy quyền"
              rows={mine.data?.filter((d) => d.delegateUserId === user?.id)}
              loading={mine.isPending}
              people="delegator"
              empty="Bạn chưa được ai ủy quyền."
              reportLink
            />
          </>
        ))}
      {canReadAll &&
        (all.isError ? (
          <Alert type="error" showIcon title={errorMessage(all.error)} />
        ) : (
          <DelegationTable title="Mọi ủy quyền" rows={all.data} loading={all.isPending} people="both" empty="Chưa có ủy quyền nào." />
        ))}
      {creating && <CreateDelegationModal onClose={() => setCreating(false)} />}
    </>
  )
}

function DelegationTable({
  title,
  rows,
  loading,
  people,
  empty,
  reportLink = false,
}: {
  title: string
  rows: NursingDelegation[] | undefined
  loading: boolean
  /** Cột người: người được ủy quyền, người ủy quyền hay cả hai. */
  people: 'delegate' | 'delegator' | 'both'
  empty: string
  /** Ủy quyền đang hiệu lực có liên kết mở báo cáo của khoa (người được ủy quyền). */
  reportLink?: boolean
}) {
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const revoke = useMutation({
    mutationFn: (id: string) => nursingReportsApi.revokeDelegation(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: nursingKeys.all })
      message.success('Đã hủy ủy quyền.')
    },
    onError: (e) => {
      message.error(errorMessageWithDetails(e))
    },
  })

  const columns: TableColumnsType<NursingDelegation> = [
    {
      title: 'Khoa',
      key: 'department',
      render: (_, d) => (
        <Tooltip title={d.departmentName}>
          <span>{d.departmentCode}</span>
        </Tooltip>
      ),
    },
    ...(people !== 'delegate' ? [{ title: 'Người ủy quyền', dataIndex: 'delegatorName' }] : []),
    ...(people !== 'delegator' ? [{ title: 'Người được ủy quyền', dataIndex: 'delegateName' }] : []),
    { title: 'Thời gian', key: 'period', render: (_, d) => delegationPeriod(d) },
    {
      title: 'Nội dung',
      key: 'contents',
      render: (_, d) => (
        <Space size={4} wrap>
          {d.contents.map((c) => (
            <Tag key={c} style={{ marginInlineEnd: 0 }}>
              {DELEGATION_CONTENT_LABELS[c]}
            </Tag>
          ))}
        </Space>
      ),
    },
    { title: 'Lý do', dataIndex: 'reason', render: (reason: string | null) => reason ?? '' },
    {
      title: 'Trạng thái',
      key: 'status',
      render: (_, d) => {
        const tag = <Tag color={STATUS[d.status].color}>{STATUS[d.status].label}</Tag>
        return d.status === 'Revoked' && d.revokedAtUtc ? (
          <Tooltip title={`Hủy lúc ${formatDateTime(d.revokedAtUtc)}${d.revokedBy ? ` bởi ${d.revokedBy}` : ''}`}>{tag}</Tooltip>
        ) : (
          tag
        )
      },
    },
    {
      title: '',
      key: 'actions',
      align: 'right',
      render: (_, d) => (
        <Space size={4}>
          {reportLink && d.status === 'Active' && (
            <Link to={`/dieu-duong/bao-cao-khoa?khoa=${encodeURIComponent(d.departmentId)}`}>Mở báo cáo của khoa</Link>
          )}
          {d.canRevoke && (
            <Popconfirm
              title="Hủy ủy quyền này?"
              description="Người được ủy quyền không làm được nữa. Báo cáo đã gửi theo ủy quyền vẫn giữ."
              okText="Hủy ủy quyền"
              cancelText="Không"
              onConfirm={() => revoke.mutate(d.id)}
            >
              <Button size="small" danger loading={revoke.isPending && revoke.variables === d.id}>
                Hủy ủy quyền
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ]

  return (
    <Card size="small" title={title} style={{ marginBottom: 16 }}>
      <Table
        size="small"
        rowKey="id"
        loading={loading}
        pagination={false}
        dataSource={rows ?? []}
        columns={columns}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: empty }}
      />
    </Card>
  )
}

function CreateDelegationModal({ onClose }: { onClose: () => void }) {
  const { user } = useAuth()
  const departments = accountDepartments(user)
  const candidates = useNursingDelegationCandidates(true)
  const [form] = Form.useForm<CreateValues>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const create = useMutation({
    mutationFn: (values: CreateValues) =>
      nursingReportsApi.createDelegation({
        departmentId: values.departmentId,
        delegateUserId: values.delegateUserId,
        startDate: values.range[0].format(DATE),
        endDate: values.range[1].format(DATE),
        report: values.contents.includes('report'),
        hisEntry: values.contents.includes('hisEntry'),
        reason: values.reason?.trim() || null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: nursingKeys.all })
      message.success('Đã ủy quyền.')
      onClose()
    },
    onError: (e) => {
      // Lỗi ngày của máy chủ (startDate, endDate) gắn vào ô thời gian.
      const dateErrors = e instanceof ApiError ? [...(e.fieldErrors?.startDate ?? []), ...(e.fieldErrors?.endDate ?? [])] : []
      if (dateErrors.length > 0) {
        form.setFields([{ name: 'range', errors: dateErrors }])
      }
      if (!applyServerFieldErrors(form, e) && dateErrors.length === 0) {
        message.error(errorMessageWithDetails(e))
      }
    },
  })

  return (
    <Modal
      open
      title="Ủy quyền báo cáo điều dưỡng"
      okText="Ủy quyền"
      cancelText="Hủy"
      confirmLoading={create.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form<CreateValues>
        form={form}
        layout="vertical"
        initialValues={{ departmentId: departments[0]?.id, contents: ['report'], range: [dayjs(), dayjs().add(1, 'day')] }}
        onFinish={(values) => create.mutate(values)}
      >
        <Form.Item name="departmentId" label="Khoa" rules={[{ required: true, message: 'Chọn khoa.' }]}>
          <Select aria-label="Khoa" options={departments.map((d) => ({ value: d.id, label: d.name }))} />
        </Form.Item>
        <Form.Item name="delegateUserId" label="Người được ủy quyền" rules={[{ required: true, message: 'Chọn người được ủy quyền.' }]}>
          <Select
            aria-label="Người được ủy quyền"
            showSearch
            optionFilterProp="label"
            loading={candidates.isPending}
            placeholder="Gõ tên để tìm"
            options={candidates.data?.map((c) => ({ value: c.userId, label: `${c.fullName} – ${c.departmentName}` }))}
            notFoundContent={candidates.isError ? errorMessage(candidates.error) : 'Không có người phù hợp.'}
          />
        </Form.Item>
        <Form.Item
          name="range"
          label="Thời gian ủy quyền (tính cả hai ngày)"
          rules={[{ required: true, message: 'Chọn từ ngày, đến ngày.' }]}
        >
          <DatePicker.RangePicker
            format="DD/MM/YYYY"
            allowClear={false}
            disabledDate={(d) => d.isBefore(dayjs(), 'day')}
            placeholder={['Từ ngày', 'Đến ngày']}
          />
        </Form.Item>
        <Form.Item
          name="contents"
          label="Nội dung ủy quyền"
          rules={[{ required: true, type: 'array', min: 1, message: 'Chọn ít nhất một nội dung ủy quyền.' }]}
        >
          <Checkbox.Group options={CONTENTS.map((c) => ({ value: c, label: DELEGATION_CONTENT_LABELS[c] }))} />
        </Form.Item>
        <Form.Item name="reason" label="Lý do (không bắt buộc)">
          <Input maxLength={200} placeholder="Ví dụ: nghỉ phép" />
        </Form.Item>
      </Form>
    </Modal>
  )
}
