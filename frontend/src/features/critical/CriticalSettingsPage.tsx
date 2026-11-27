import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Radio,
  Row,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Typography,
  type TableColumnsType,
} from 'antd'
import { useEffect, useState } from 'react'
import {
  CRITICAL_LOGBOOK_LABELS,
  criticalApi,
  criticalKeys,
  useCriticalSettings,
  useCriticalTests,
  type CriticalLogbook,
  type CriticalSettingsValues,
  type CriticalTest,
  type SaveCriticalTestInput,
} from '../../api/critical'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage, errorMessageWithDetails } from '../../utils/formErrors'
import { CriticalSectionNav } from './CriticalParts'
import { dateTimeOf } from './criticalUtils'

type Key = keyof CriticalSettingsValues

type Field =
  | { key: Key; kind: 'number'; label: string; help: string; unit: string; min: number; max: number }
  | { key: Key; kind: 'switch'; label: string; help: string; on: string; off: string }
  | { key: Key; kind: 'text'; label: string; help: string }

/** Các nhóm thông số, giới hạn như máy chủ (CriticalSettings.Ranges). */
const GROUPS: { title: string; note: string; fields: Field[] }[] = [
  {
    title: 'Thời hạn',
    note: 'Áp dụng cho hồ sơ ghi từ lúc lưu; hồ sơ đã có giữ thời hạn áp dụng lúc ghi.',
    fields: [
      { key: 'notifyMinutes', kind: 'number', label: 'Thời hạn báo kết quả', help: 'Tính từ giờ có kết quả.', unit: 'phút', min: 1, max: 120 },
      {
        key: 'actionMinutes',
        kind: 'number',
        label: 'Thời hạn bác sĩ xử trí ban đầu',
        help: 'Tính từ giờ khoa nhận cuộc gọi.',
        unit: 'phút',
        min: 1,
        max: 120,
      },
      {
        key: 'lateEntryMinutes',
        kind: 'number',
        label: 'Ngưỡng ghi bù',
        help: 'Bước ghi sau giờ sự việc quá bấy nhiêu phút thì hồ sơ có nhãn Ghi bù.',
        unit: 'phút',
        min: 5,
        max: 1440,
      },
    ],
  },
  {
    title: 'Cảnh báo tại khoa và màn hình theo dõi',
    note: 'Dải cảnh báo trên mọi trang của khoa nhận và màn hình đặt tại khoa.',
    fields: [
      {
        key: 'warnMinutes',
        kind: 'number',
        label: 'Báo sắp quá hạn khi còn',
        help: 'Đồng hồ chuyển màu cam; phải ít hơn cả hai thời hạn.',
        unit: 'phút',
        min: 0,
        max: 30,
      },
      {
        key: 'pollSeconds',
        kind: 'number',
        label: 'Chu kỳ tự kiểm tra hồ sơ mới',
        help: 'Dải cảnh báo, bàn làm việc, màn hình theo dõi tự cập nhật theo chu kỳ này.',
        unit: 'giây',
        min: 5,
        max: 120,
      },
      { key: 'soundEnabled', kind: 'switch', label: 'Chuông báo', help: 'Kêu khi có kết quả nguy cấp mới gửi đến khoa.', on: 'Bật', off: 'Tắt' },
      {
        key: 'soundRepeatSeconds',
        kind: 'number',
        label: 'Nhắc lại chuông',
        help: 'Nhắc lại khi còn hồ sơ chờ khoa xác nhận; 0 là không nhắc lại.',
        unit: 'giây',
        min: 0,
        max: 600,
      },
      { key: 'flashEnabled', kind: 'switch', label: 'Chớp tắt dải cảnh báo', help: 'Dải đỏ chớp tắt khi có hồ sơ chờ khoa xác nhận.', on: 'Bật', off: 'Tắt' },
      {
        key: 'flashPeriodMs',
        kind: 'number',
        label: 'Nhịp chớp tắt',
        help: 'Một lần sáng rồi tối; từ 1.000 ms trở lên (không quá 1 lần mỗi giây), an toàn cho người nhạy cảm với ánh sáng nhấp nháy.',
        unit: 'ms',
        min: 1000,
        max: 3000,
      },
      {
        key: 'keepHours',
        kind: 'number',
        label: 'Giữ hồ sơ hoàn tất trên bàn làm việc',
        help: 'Sau thời gian này xem hồ sơ trong Sổ kết quả nguy cấp.',
        unit: 'giờ',
        min: 1,
        max: 168,
      },
      {
        key: 'displayFullName',
        kind: 'switch',
        label: 'Màn hình theo dõi hiện họ tên đầy đủ',
        help: 'Mặc định chỉ hiện tên viết tắt để giữ bí mật thông tin người bệnh.',
        on: 'Họ tên đầy đủ',
        off: 'Tên viết tắt',
      },
    ],
  },
  {
    title: 'Ghi hồ sơ',
    note: 'Khoa cận lâm sàng ghi khi gọi điện.',
    fields: [
      {
        key: 'requireAddress',
        kind: 'switch',
        label: 'Bắt buộc địa chỉ người bệnh',
        help: 'Thông tin đối chiếu thứ ba khi gọi điện (cùng họ tên, ngày sinh).',
        on: 'Bắt buộc',
        off: 'Không bắt buộc',
      },
    ],
  },
  {
    title: 'Mục tiêu dashboard',
    note: 'Dashboard so tỷ lệ của tháng, quý với các mục tiêu này.',
    fields: [
      { key: 'targetNotifyPercent', kind: 'number', label: 'Báo đúng hạn', help: '', unit: '%', min: 50, max: 100 },
      { key: 'targetActionPercent', kind: 'number', label: 'Bác sĩ xử trí đúng hạn', help: '', unit: '%', min: 50, max: 100 },
      { key: 'targetReadBackPercent', kind: 'number', label: 'Đọc lại chính xác lần đầu', help: '', unit: '%', min: 50, max: 100 },
      { key: 'targetCompletePercent', kind: 'number', label: 'Hồ sơ đủ các bước', help: '', unit: '%', min: 50, max: 100 },
    ],
  },
  {
    title: 'Mã số sổ (in trên PDF)',
    note: 'Theo mã số sổ giấy bệnh viện đang dùng.',
    fields: [
      { key: 'codeLaboratory', kind: 'text', label: 'Sổ khoa cận lâm sàng (ST01)', help: '' },
      { key: 'codeClinical', kind: 'text', label: 'Sổ khoa điều trị (ST02)', help: '' },
      { key: 'codeMicrobiology', kind: 'text', label: 'Sổ Khoa Vi sinh', help: '' },
    ],
  },
]

function SettingsTab() {
  const settings = useCriticalSettings()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [form] = Form.useForm<CriticalSettingsValues>()
  const current = Form.useWatch([], form) as CriticalSettingsValues | undefined
  const saved = settings.data?.values
  useEffect(() => {
    if (saved) form.setFieldsValue(saved)
  }, [saved, form])
  const mutation = useMutation({
    mutationFn: (values: CriticalSettingsValues) => criticalApi.updateSettings(values),
    onSuccess: (next) => {
      queryClient.setQueryData(criticalKeys.settings, next)
      void queryClient.invalidateQueries({ queryKey: criticalKeys.records })
      void message.success('Đã lưu. Hồ sơ ghi từ bây giờ dùng thông số mới; nhật ký hệ thống ghi giá trị cũ và mới.')
    },
    onError: (error) => {
      applyServerFieldErrors(form, error)
    },
  })

  if (settings.isLoading || !saved) {
    return settings.isError ? <Alert type="error" showIcon title={errorMessage(settings.error)} /> : <Spin style={{ display: 'block', padding: 48 }} />
  }

  const changed = (key: Key) => current !== undefined && current[key] !== undefined && current[key] !== saved[key]
  const dirty = GROUPS.flatMap((g) => g.fields).filter((f) => changed(f.key)).length

  return (
    <Form<CriticalSettingsValues> form={form} layout="vertical" initialValues={saved} onFinish={(values) => mutation.mutate({ ...saved, ...values })}>
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Row gutter={[16, 16]}>
        {GROUPS.map((group) => (
          <Col key={group.title} xs={24} lg={12}>
            <Card size="small" title={group.title} style={{ height: '100%' }}>
              <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
                {group.note}
              </Typography.Paragraph>
              {group.fields.map((field) => (
                <Form.Item
                  key={field.key}
                  name={field.key}
                  valuePropName={field.kind === 'switch' ? 'checked' : 'value'}
                  label={
                    <span>
                      {field.label}
                      {changed(field.key) && (
                        <Tag color="warning" style={{ marginInlineStart: 8 }}>
                          đã sửa
                        </Tag>
                      )}
                    </span>
                  }
                  extra={field.help || undefined}
                  rules={
                    field.kind === 'number'
                      ? [{ required: true, type: 'number', min: field.min, max: field.max, message: `Từ ${field.min} đến ${field.max}.` }]
                      : field.kind === 'text'
                        ? [{ required: true, whitespace: true, max: 50, message: 'Mã số sổ: 1 đến 50 ký tự.' }]
                        : []
                  }
                  style={{ marginBottom: 16 }}
                >
                  {field.kind === 'number' ? (
                    <InputNumber min={field.min} max={field.max} precision={0} suffix={field.unit} style={{ width: 180 }} />
                  ) : field.kind === 'switch' ? (
                    <Switch checkedChildren={field.on} unCheckedChildren={field.off} />
                  ) : (
                    <Input maxLength={50} style={{ maxWidth: 260 }} />
                  )}
                </Form.Item>
              ))}
            </Card>
          </Col>
        ))}
      </Row>
      <Flex justify="space-between" align="center" wrap gap={12} style={{ marginTop: 16 }}>
        <Typography.Text type="secondary">
          {settings.data?.updatedAtUtc
            ? `Sửa lần cuối ${dateTimeOf(settings.data.updatedAtUtc)}${settings.data.updatedBy ? ` · ${settings.data.updatedBy}` : ''}`
            : 'Đang dùng thông số mặc định.'}
        </Typography.Text>
        <Flex gap={8}>
          <Typography.Text type={dirty ? 'warning' : 'secondary'} style={{ alignSelf: 'center' }}>
            {dirty ? `${dirty} thông số chưa lưu` : 'Không có thay đổi'}
          </Typography.Text>
          <Button disabled={!dirty} onClick={() => form.setFieldsValue(saved)}>
            Hoàn tác
          </Button>
          <Button type="primary" htmlType="submit" disabled={!dirty} loading={mutation.isPending}>
            Lưu thay đổi
          </Button>
        </Flex>
      </Flex>
    </Form>
  )
}

const LOGBOOKS: CriticalLogbook[] = ['Laboratory', 'Microbiology']

function TestModal({ test, open, onClose }: { test: CriticalTest | null; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<SaveCriticalTestInput>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const textResult = Form.useWatch('textResult', form) as boolean | undefined
  const mutation = useMutation({
    mutationFn: (input: SaveCriticalTestInput) => (test ? criticalApi.updateTest(test.id, input) : criticalApi.createTest(input)),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: criticalKeys.tests })
      void message.success(test ? 'Đã lưu mục trong danh mục.' : 'Đã thêm mục vào danh mục.')
      onClose()
    },
  })
  const initial: SaveCriticalTestInput = test
    ? { ...test }
    : { name: '', adultRange: '', childRange: null, unit: null, section: null, logbook: 'Laboratory', textResult: false, sortOrder: 0, isActive: true }
  return (
    <Modal
      open={open}
      title={test ? `Sửa: ${test.name}` : 'Thêm mục vào danh mục kết quả nguy cấp'}
      okText="Lưu"
      cancelText="Thôi"
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={mutation.isPending}
      destroyOnHidden
      width={640}
    >
      {mutation.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={errorMessageWithDetails(mutation.error)} />}
      <Form<SaveCriticalTestInput>
        form={form}
        layout="vertical"
        initialValues={initial}
        onFinish={(values) =>
          mutation.mutate({
            ...values,
            name: values.name.trim(),
            adultRange: values.adultRange.trim(),
            childRange: values.childRange?.trim() || null,
            unit: values.textResult ? null : values.unit?.trim() || null,
            section: values.section?.trim() || null,
          })
        }
      >
        <Form.Item name="name" label="Xét nghiệm, kỹ thuật" rules={[{ required: true, whitespace: true, message: 'Nhập tên.' }]}>
          <Input maxLength={200} />
        </Form.Item>
        <Form.Item name="adultRange" label="Khoảng nguy cấp: người lớn" rules={[{ required: true, whitespace: true, message: 'Nhập khoảng nguy cấp.' }]}>
          <Input.TextArea rows={2} maxLength={500} />
        </Form.Item>
        <Form.Item name="childRange" label="Khoảng nguy cấp: trẻ em (nếu khác)">
          <Input.TextArea rows={2} maxLength={500} />
        </Form.Item>
        <Row gutter={12}>
          <Col xs={24} sm={12}>
            <Form.Item name="textResult" label="Kiểu kết quả" valuePropName="checked">
              <Switch checkedChildren="Ghi bằng chữ" unCheckedChildren="Giá trị số" />
            </Form.Item>
          </Col>
          {!textResult && (
            <Col xs={24} sm={12}>
              <Form.Item name="unit" label="Đơn vị" rules={[{ required: true, whitespace: true, message: 'Nhập đơn vị.' }]}>
                <Input maxLength={30} />
              </Form.Item>
            </Col>
          )}
          <Col xs={24} sm={12}>
            <Form.Item name="section" label="Khoa thực hiện">
              <Input maxLength={200} placeholder="Ví dụ: Sinh hóa, Vi sinh, Chẩn đoán hình ảnh" />
            </Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="sortOrder" label="Thứ tự" rules={[{ required: true, type: 'number', min: 0, max: 9999 }]}>
              <InputNumber min={0} max={9999} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="logbook" label="Ghi vào sổ của khoa báo">
          <Radio.Group options={LOGBOOKS.map((l) => ({ value: l, label: CRITICAL_LOGBOOK_LABELS[l] }))} />
        </Form.Item>
        <Form.Item name="isActive" label="Trạng thái" valuePropName="checked">
          <Switch checkedChildren="Đang dùng" unCheckedChildren="Ngưng dùng" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function CatalogTab() {
  const tests = useCriticalTests()
  const [editing, setEditing] = useState<CriticalTest | null | undefined>(undefined)
  const columns: TableColumnsType<CriticalTest> = [
    { title: 'Xét nghiệm, kỹ thuật', dataIndex: 'name', key: 'name', render: (name: string) => <Typography.Text strong>{name}</Typography.Text> },
    { title: 'Khoảng nguy cấp: người lớn', dataIndex: 'adultRange', key: 'adult' },
    { title: 'Trẻ em', dataIndex: 'childRange', key: 'child', render: (v: string | null) => v ?? '—' },
    { title: 'Đơn vị', key: 'unit', render: (_, t) => (t.textResult ? 'Ghi bằng chữ' : t.unit) },
    { title: 'Khoa thực hiện', dataIndex: 'section', key: 'section', render: (v: string | null) => v ?? '—' },
    { title: 'Mẫu sổ', dataIndex: 'logbook', key: 'logbook', render: (v: CriticalLogbook) => CRITICAL_LOGBOOK_LABELS[v] },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'active',
      render: (active: boolean) => <Tag color={active ? 'success' : 'default'}>{active ? 'Đang dùng' : 'Ngưng dùng'}</Tag>,
    },
    {
      title: '',
      key: 'edit',
      render: (_, t) => (
        <Button size="small" icon={<EditOutlined />} onClick={() => setEditing(t)} aria-label={`Sửa ${t.name}`}>
          Sửa
        </Button>
      ),
    },
  ]
  return (
    <>
      <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 12 }}>
        <Typography.Text type="secondary">Rà soát định kỳ: sửa hoặc ngưng dùng mục cũ, không xóa để giữ hồ sơ đã ghi.</Typography.Text>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing(null)}>
          Thêm mục
        </Button>
      </Flex>
      {tests.isError && <Alert type="error" showIcon title={errorMessage(tests.error)} style={{ marginBottom: 12 }} />}
      <Table<CriticalTest>
        rowKey="id"
        size="small"
        loading={tests.isLoading}
        columns={columns}
        dataSource={tests.data}
        pagination={false}
        scroll={{ x: 960 }}
      />
      {editing !== undefined && <TestModal open test={editing} onClose={() => setEditing(undefined)} />}
    </>
  )
}

/** Danh mục kết quả nguy cấp và thông số vận hành (quyết định 60): chỉnh khi vận hành, không cần cài bản mới. */
export function CriticalSettingsPage() {
  return (
    <>
      <CriticalSectionNav />
      <PageHeader title="Danh mục và cấu hình" />
      <Typography.Paragraph type="secondary">
        Chỉnh khi vận hành, không cần cài bản mới. Mỗi lần lưu ghi nhật ký hệ thống; hồ sơ đã có giữ thời hạn áp dụng lúc ghi.
      </Typography.Paragraph>
      <Tabs
        items={[
          { key: 'settings', label: 'Thông số vận hành', children: <SettingsTab /> },
          { key: 'catalog', label: 'Danh mục kết quả nguy cấp', children: <CatalogTab /> },
        ]}
      />
    </>
  )
}
