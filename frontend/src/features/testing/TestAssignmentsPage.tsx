import { DeleteOutlined, FileExcelOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  AutoComplete,
  Button,
  Card,
  DatePicker,
  Descriptions,
  Drawer,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Typography,
  type TableColumnsType,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  testingApi,
  testingKeys,
  useTestAssignments,
  useTestCandidates,
  type TestAssignment,
} from '../../api/testing'
import { ApiError } from '../../api/http'
import { PageHeader } from '../../components/PageHeader'
import { saveBlob } from '../../utils/download'
import { applyServerFieldErrors, errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { formatDateRange, formatDateTime } from '../../utils/format'
import { TestResultTag, TestStatusTag } from './testingTags'

const DATE = 'YYYY-MM-DD'
const ALL = '__all__'

/** Tổng hợp theo tính năng: số người, kết quả, chưa báo cáo (quá hạn) — để quyết định đưa lên chạy thật hay sửa tiếp. */
interface FeatureSummary {
  key: string
  version: string
  feature: string
  total: number
  passed: number
  partial: number
  failed: number
  waiting: number
  overdue: number
}

function summarize(items: TestAssignment[]): FeatureSummary[] {
  const groups = new Map<string, FeatureSummary>()
  for (const a of items.filter((x) => x.status !== 'Cancelled')) {
    const key = `${a.version}\u0000${a.feature}`
    const g = groups.get(key) ?? { key, version: a.version, feature: a.feature, total: 0, passed: 0, partial: 0, failed: 0, waiting: 0, overdue: 0 }
    g.total += 1
    if (a.result === 'Passed') g.passed += 1
    else if (a.result === 'PartiallyPassed') g.partial += 1
    else if (a.result === 'Failed') g.failed += 1
    else g.waiting += 1
    if (a.status === 'Overdue') g.overdue += 1
    groups.set(key, g)
  }
  return [...groups.values()]
}

/**
 * Phân công kiểm thử (quyết định 49): người quản lý kiểm thử (quyền testing.manage) phân công từng tính năng mới cho từng người,
 * mỗi người một khoảng thời gian; theo dõi ai đã báo cáo, quá hạn; xem báo cáo, ghi hướng xử lý; xuất Excel gửi nhóm phát triển.
 */
export function TestAssignmentsPage() {
  const [version, setVersion] = useState<string | undefined>(undefined)
  const list = useTestAssignments(version)
  const [creating, setCreating] = useState(false)
  const [editing, setEditing] = useState<TestAssignment | null>(null)
  const [viewing, setViewing] = useState<TestAssignment | null>(null)
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const cancel = useMutation({
    mutationFn: (id: string) => testingApi.cancel(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: testingKeys.all })
      message.success('Đã hủy phân công.')
    },
    onError: (e) => {
      message.error(errorMessageWithDetails(e))
    },
  })
  const workbook = useMutation({
    mutationFn: () => testingApi.workbook(version),
    onSuccess: (blob) => saveBlob(blob, `bao-cao-kiem-thu-${dayjs().format('YYYYMMDD-HHmm')}.xlsx`),
    onError: (e) => {
      message.error(errorMessage(e))
    },
  })

  const items = list.data?.items ?? []
  const columns: TableColumnsType<TestAssignment> = [
    {
      title: 'Tính năng',
      key: 'feature',
      render: (_, a) => (
        <Flex vertical gap={2}>
          <span>{a.feature}</span>
          {version === undefined && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {a.version}
            </Typography.Text>
          )}
        </Flex>
      ),
    },
    {
      title: 'Người kiểm thử',
      key: 'assignee',
      render: (_, a) => (
        <Flex vertical gap={2}>
          <span>{a.assigneeName}</span>
          {a.assigneeDepartment && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {a.assigneeDepartment}
            </Typography.Text>
          )}
        </Flex>
      ),
    },
    { title: 'Thời gian', key: 'period', render: (_, a) => formatDateRange(a.startDate, a.dueDate) },
    { title: 'Trạng thái', key: 'status', render: (_, a) => <TestStatusTag assignment={a} /> },
    { title: 'Kết quả', key: 'result', render: (_, a) => <TestResultTag result={a.result} /> },
    {
      title: '',
      key: 'actions',
      align: 'right',
      render: (_, a) => (
        <Space size={4} wrap>
          {a.result && (
            <Button size="small" onClick={() => setViewing(a)}>
              Xem báo cáo
            </Button>
          )}
          {a.canEdit && (
            <Button size="small" onClick={() => setEditing(a)}>
              Sửa
            </Button>
          )}
          {a.canEdit && (
            <Popconfirm
              title="Hủy phân công này?"
              okText="Hủy phân công"
              cancelText="Không"
              onConfirm={() => cancel.mutate(a.id)}
            >
              <Button size="small" danger loading={cancel.isPending && cancel.variables === a.id}>
                Hủy
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Phân công kiểm thử"
        extra={
          <Flex gap={8} wrap>
            <Select
              aria-label="Đợt kiểm thử"
              value={version ?? ALL}
              onChange={(v: string) => setVersion(v === ALL ? undefined : v)}
              options={[{ value: ALL, label: 'Mọi đợt kiểm thử' }, ...(list.data?.versions ?? []).map((v) => ({ value: v, label: v }))]}
              style={{ minWidth: 220 }}
              popupMatchSelectWidth={false}
            />
            <Button icon={<FileExcelOutlined />} loading={workbook.isPending} onClick={() => workbook.mutate()}>
              Xuất Excel
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              Phân công mới
            </Button>
          </Flex>
        }
      />
      <Typography.Paragraph type="secondary">
        Phân công từng tính năng mới cho từng người, mỗi người một khoảng thời gian. Người được phân công kiểm thử (thường trên môi
        trường UAT) và bắt buộc gửi báo cáo; xem báo cáo, ghi hướng xử lý, xuất Excel để nâng cấp phần mềm. Đạt hết mới đưa bản lên
        chạy thật.
      </Typography.Paragraph>
      {list.isError ? (
        <Alert type="error" showIcon title={errorMessage(list.error)} />
      ) : (
        <div aria-busy={list.isFetching} style={{ opacity: list.isPlaceholderData ? 0.6 : 1 }}>
          <Card size="small" title="Tổng hợp theo tính năng" style={{ marginBottom: 16 }}>
            <Table
              size="small"
              rowKey="key"
              loading={list.isPending}
              pagination={false}
              dataSource={summarize(items)}
              scroll={{ x: 'max-content' }}
              locale={{ emptyText: 'Chưa có phân công.' }}
              columns={[
                {
                  title: 'Tính năng',
                  key: 'feature',
                  render: (_, s) => (
                    <Flex vertical gap={2}>
                      <span>{s.feature}</span>
                      {version === undefined && (
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {s.version}
                        </Typography.Text>
                      )}
                    </Flex>
                  ),
                },
                { title: 'Số người', dataIndex: 'total', align: 'right' },
                { title: 'Đạt', dataIndex: 'passed', align: 'right' },
                { title: 'Đạt một phần', dataIndex: 'partial', align: 'right' },
                { title: 'Không đạt', dataIndex: 'failed', align: 'right' },
                {
                  title: 'Chưa báo cáo',
                  key: 'waiting',
                  align: 'right',
                  render: (_, s) => (s.overdue > 0 ? `${s.waiting} (${s.overdue} quá hạn)` : s.waiting),
                },
              ]}
            />
          </Card>
          <Card size="small" title="Phân công">
            <Table
              size="small"
              rowKey="id"
              loading={list.isPending}
              pagination={{ pageSize: 50, hideOnSinglePage: true }}
              dataSource={items}
              columns={columns}
              scroll={{ x: 'max-content' }}
              locale={{ emptyText: 'Chưa có phân công. Bấm "Phân công mới".' }}
            />
          </Card>
        </div>
      )}
      {creating && <CreateAssignmentsModal versions={list.data?.versions ?? []} defaultVersion={version} onClose={() => setCreating(false)} />}
      {editing && <EditAssignmentModal assignment={editing} versions={list.data?.versions ?? []} onClose={() => setEditing(null)} />}
      {viewing && <ReportDrawer assignment={viewing} onClose={() => setViewing(null)} />}
    </>
  )
}

interface CreateValues {
  version: string
  feature: string
  instructions?: string
  assignees: { userId?: string; range?: [Dayjs, Dayjs] }[]
}

function CreateAssignmentsModal({ versions, defaultVersion, onClose }: { versions: string[]; defaultVersion?: string; onClose: () => void }) {
  const candidates = useTestCandidates()
  const [form] = Form.useForm<CreateValues>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const create = useMutation({
    mutationFn: (values: CreateValues) =>
      testingApi.create({
        version: values.version,
        feature: values.feature,
        instructions: values.instructions?.trim() || null,
        assignees: values.assignees.map((a) => ({
          userId: a.userId!,
          startDate: a.range![0].format(DATE),
          dueDate: a.range![1].format(DATE),
        })),
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: testingKeys.all })
      message.success(`Đã phân công ${created.length} người.`)
      onClose()
    },
    onError: (e) => {
      // Lỗi từng người (assignees[i].userId, assignees[i].dueDate) gắn vào đúng dòng.
      if (e instanceof ApiError && e.fieldErrors) {
        const fields = Object.entries(e.fieldErrors).flatMap(([name, errors]): { name: (string | number)[]; errors: string[] }[] => {
          const row = /^assignees\[(\d+)\]\.(userId|dueDate|startDate)$/.exec(name)
          if (row) {
            return [{ name: ['assignees', Number(row[1]), row[2] === 'userId' ? 'userId' : 'range'], errors }]
          }
          return ['version', 'feature', 'instructions'].includes(name) ? [{ name: [name], errors }] : []
        })
        form.setFields(fields as Parameters<typeof form.setFields>[0])
        if (fields.length > 0 && Object.keys(e.fieldErrors).every((k) => k !== 'assignees')) {
          return
        }
      }
      message.error(errorMessageWithDetails(e))
    },
  })

  return (
    <Modal
      open
      title="Phân công kiểm thử"
      okText="Phân công"
      cancelText="Hủy"
      confirmLoading={create.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
      width={720}
    >
      <Form<CreateValues>
        form={form}
        layout="vertical"
        initialValues={{ version: defaultVersion, assignees: [{ range: [dayjs(), dayjs().add(2, 'day')] }] }}
        onFinish={(values) => create.mutate(values)}
      >
        <Form.Item name="version" label="Đợt kiểm thử" rules={[{ required: true, whitespace: true, message: 'Nhập đợt kiểm thử.' }]}>
          <AutoComplete
            options={versions.map((v) => ({ value: v }))}
            placeholder="Ví dụ: Gói 71 – 21/11/2026"
            maxLength={100}
            filterOption={(input, option) => (option?.value ?? '').toLowerCase().includes(input.toLowerCase())}
          />
        </Form.Item>
        <Form.Item name="feature" label="Tính năng cần kiểm thử" rules={[{ required: true, whitespace: true, message: 'Nhập tính năng.' }]}>
          <Input maxLength={200} placeholder="Ví dụ: Ủy quyền báo cáo điều dưỡng" />
        </Form.Item>
        <Form.Item name="instructions" label="Nội dung, cách kiểm thử (không bắt buộc)">
          <Input.TextArea rows={3} maxLength={4000} />
        </Form.Item>
        <Typography.Text strong>Người kiểm thử và thời gian của từng người</Typography.Text>
        <Form.List name="assignees">
          {(rows, { add, remove }) => (
            <>
              {rows.map((row, index) => (
                <Flex key={row.key} gap={8} wrap align="start" style={{ marginTop: 8 }}>
                  <Form.Item
                    name={[row.name, 'userId']}
                    rules={[{ required: true, message: 'Chọn người kiểm thử.' }]}
                    // minWidth 0: tên dài thì rút gọn (…), không đẩy nút Bỏ xuống dòng dưới.
                    style={{ flex: '1 1 240px', minWidth: 0, marginBottom: 0 }}
                  >
                    <Select
                      aria-label={`Người kiểm thử ${index + 1}`}
                      showSearch
                      optionFilterProp="label"
                      placeholder="Gõ tên để tìm"
                      loading={candidates.isPending}
                      options={candidates.data?.map((c) => ({
                        value: c.userId,
                        label: c.departmentName ? `${c.fullName} – ${c.departmentName}` : c.fullName,
                      }))}
                    />
                  </Form.Item>
                  <Form.Item
                    name={[row.name, 'range']}
                    rules={[{ required: true, message: 'Chọn từ ngày, hạn báo cáo.' }]}
                    style={{ flex: '0 1 280px', marginBottom: 0 }}
                  >
                    <DatePicker.RangePicker
                      aria-label={`Thời gian kiểm thử ${index + 1}`}
                      format="DD/MM/YYYY"
                      allowClear={false}
                      placeholder={['Từ ngày', 'Hạn báo cáo']}
                    />
                  </Form.Item>
                  {rows.length > 1 && (
                    <Button aria-label={`Bỏ người kiểm thử ${index + 1}`} icon={<DeleteOutlined />} onClick={() => remove(row.name)} />
                  )}
                </Flex>
              ))}
              <Button
                type="dashed"
                icon={<PlusOutlined />}
                style={{ marginTop: 8 }}
                onClick={() => add({ range: form.getFieldValue(['assignees', rows.length - 1, 'range']) ?? [dayjs(), dayjs().add(2, 'day')] })}
              >
                Thêm người kiểm thử
              </Button>
            </>
          )}
        </Form.List>
      </Form>
    </Modal>
  )
}

interface EditValues {
  version: string
  feature: string
  instructions?: string
  range: [Dayjs, Dayjs]
}

function EditAssignmentModal({ assignment, versions, onClose }: { assignment: TestAssignment; versions: string[]; onClose: () => void }) {
  const [form] = Form.useForm<EditValues>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (values: EditValues) =>
      testingApi.update(assignment.id, {
        version: values.version,
        feature: values.feature,
        instructions: values.instructions?.trim() || null,
        startDate: values.range[0].format(DATE),
        dueDate: values.range[1].format(DATE),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: testingKeys.all })
      message.success('Đã lưu phân công.')
      onClose()
    },
    onError: (e) => {
      const dateErrors = e instanceof ApiError ? [...(e.fieldErrors?.dueDate ?? []), ...(e.fieldErrors?.startDate ?? [])] : []
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
      title={`Sửa phân công: ${assignment.assigneeName}`}
      okText="Lưu"
      cancelText="Hủy"
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Form<EditValues>
        form={form}
        layout="vertical"
        initialValues={{
          version: assignment.version,
          feature: assignment.feature,
          instructions: assignment.instructions ?? undefined,
          range: [dayjs(assignment.startDate), dayjs(assignment.dueDate)],
        }}
        onFinish={(values) => save.mutate(values)}
      >
        <Form.Item name="version" label="Đợt kiểm thử" rules={[{ required: true, whitespace: true, message: 'Nhập đợt kiểm thử.' }]}>
          <AutoComplete options={versions.map((v) => ({ value: v }))} maxLength={100} />
        </Form.Item>
        <Form.Item name="feature" label="Tính năng cần kiểm thử" rules={[{ required: true, whitespace: true, message: 'Nhập tính năng.' }]}>
          <Input maxLength={200} />
        </Form.Item>
        <Form.Item name="instructions" label="Nội dung, cách kiểm thử (không bắt buộc)">
          <Input.TextArea rows={3} maxLength={4000} />
        </Form.Item>
        <Form.Item name="range" label="Thời gian (từ ngày, hạn báo cáo)" rules={[{ required: true, message: 'Chọn từ ngày, hạn báo cáo.' }]}>
          <DatePicker.RangePicker format="DD/MM/YYYY" allowClear={false} placeholder={['Từ ngày', 'Hạn báo cáo']} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** Báo cáo của một người và hướng xử lý (ghi xong thì người kiểm thử không sửa báo cáo được nữa). */
function ReportDrawer({ assignment, onClose }: { assignment: TestAssignment; onClose: () => void }) {
  const [form] = Form.useForm<{ note: string }>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const review = useMutation({
    mutationFn: (note: string) => testingApi.review(assignment.id, note),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: testingKeys.all })
      message.success('Đã lưu hướng xử lý.')
      onClose()
    },
    onError: (e) => {
      if (!applyServerFieldErrors(form, e)) {
        message.error(errorMessageWithDetails(e))
      }
    },
  })

  return (
    <Drawer open title={`Báo cáo kiểm thử: ${assignment.feature}`} onClose={onClose} size={Math.min(720, window.innerWidth)}>
      <Descriptions
        size="small"
        column={1}
        bordered
        items={[
          { key: 'version', label: 'Đợt kiểm thử', children: assignment.version },
          { key: 'assignee', label: 'Người kiểm thử', children: assignment.assigneeName },
          { key: 'period', label: 'Thời gian', children: formatDateRange(assignment.startDate, assignment.dueDate) },
          { key: 'status', label: 'Trạng thái', children: <TestStatusTag assignment={assignment} /> },
          { key: 'result', label: 'Kết quả', children: <TestResultTag result={assignment.result} /> },
          { key: 'at', label: 'Gửi lúc', children: formatDateTime(assignment.reportedAtUtc) },
          { key: 'findings', label: 'Lỗi, vấn đề phát hiện', children: <span style={{ whiteSpace: 'pre-line' }}>{assignment.findings ?? '—'}</span> },
          { key: 'suggestions', label: 'Đề xuất', children: <span style={{ whiteSpace: 'pre-line' }}>{assignment.suggestions ?? '—'}</span> },
        ]}
      />
      <Form
        form={form}
        layout="vertical"
        style={{ marginTop: 16 }}
        initialValues={{ note: assignment.reviewNote ?? '' }}
        onFinish={(values) => review.mutate(values.note)}
      >
        <Form.Item
          name="note"
          label="Hướng xử lý"
          extra={assignment.reviewedBy ? `Ghi bởi ${assignment.reviewedBy} lúc ${formatDateTime(assignment.reviewedAtUtc)}.` : 'Ví dụ: chuyển nhóm phát triển sửa trong gói sau.'}
          rules={[{ required: true, whitespace: true, message: 'Ghi hướng xử lý.' }]}
        >
          <Input.TextArea rows={3} maxLength={2000} />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={review.isPending}>
          Lưu hướng xử lý
        </Button>
      </Form>
    </Drawer>
  )
}
