import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  DatePicker,
  Flex,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tabs,
  Tag,
  Typography,
  type TableColumnsType,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { useDepartments } from '../../api/admin'
import { ApiError } from '../../api/http'
import {
  QC_UNIT_LABELS,
  formatQcRange,
  qcApi,
  qcKeys,
  useQcCatalog,
  type QcLot,
  type QcLotKind,
  type QcMeter,
  type QcUnit,
} from '../../api/qc'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'

const DATE = 'YYYY-MM-DD'
const SHOW_DATE = 'DD/MM/YYYY'

const ActiveTag = ({ active }: { active: boolean }) => (active ? <Tag color="success">Đang dùng</Tag> : <Tag>Ngưng sử dụng</Tag>)

interface MeterForm {
  departmentId: string
  model: string
  serialNumber: string
  isActive: boolean
}

function MeterModal({ meter, onClose }: { meter: QcMeter | null; onClose: () => void }) {
  const [form] = Form.useForm<MeterForm>()
  const departments = useDepartments(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (values: MeterForm) => (meter ? qcApi.updateMeter(meter.id, values) : qcApi.createMeter(values)),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: qcKeys.all })
      void message.success(meter ? 'Đã lưu máy đo.' : 'Đã thêm máy đo.')
      onClose()
    },
    onError: (error) => applyServerFieldErrors(form, error),
  })
  return (
    <Modal
      open
      title={meter ? 'Sửa máy đo' : 'Thêm máy đo'}
      okText="Lưu"
      onOk={() => form.submit()}
      confirmLoading={save.isPending}
      onCancel={onClose}
      destroyOnHidden
    >
      {save.isError && !(save.error instanceof ApiError && save.error.fieldErrors) ? (
        <Alert type="error" showIcon title={errorMessage(save.error)} style={{ marginBottom: 16 }} />
      ) : null}
      {meter ? (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title="Chuyển máy sang khoa khác: sổ tháng này của khoa cũ giữ nguyên, khoa mới mở sổ mới để ghi kết quả."
        />
      ) : null}
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={meter ? { departmentId: meter.departmentId, model: meter.model, serialNumber: meter.serialNumber, isActive: meter.isActive } : { isActive: true }}
        onFinish={(values) => save.mutate(values)}
      >
        <Form.Item name="departmentId" label="Khoa dùng máy" rules={[{ required: true, message: 'Chọn khoa.' }]}>
          <Select
            showSearch
            optionFilterProp="label"
            loading={departments.isPending}
            options={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))}
          />
        </Form.Item>
        <Form.Item name="model" label="Loại máy (hãng, đời máy)" rules={[{ required: true, whitespace: true, message: 'Nhập loại máy.' }]}>
          <Input maxLength={100} />
        </Form.Item>
        <Form.Item name="serialNumber" label="Số seri máy" rules={[{ required: true, whitespace: true, message: 'Nhập số seri máy.' }]}>
          <Input maxLength={50} />
        </Form.Item>
        {meter ? (
          <Form.Item name="isActive" label="Đang dùng" valuePropName="checked">
            <Switch />
          </Form.Item>
        ) : null}
      </Form>
    </Modal>
  )
}

interface LotForm {
  productName: string
  lotNumber: string
  expiryDate: Dayjs
  level?: number
  rangeLow?: number
  rangeHigh?: number
  unit?: QcUnit
  isActive: boolean
}

/** Lô đang thêm (`lot` null, loại theo nút đã bấm) hoặc đang sửa (loại của lô, không đổi được). */
interface LotEdit {
  lot: QcLot | null
  kind: QcLotKind
}

const KIND_NAMES: Record<QcLotKind, string> = { TestStrip: 'lô que thử', ControlSolution: 'lô dung dịch chứng' }

/**
 * Hộp thêm, sửa lô. Loại vật tư chọn bằng nút mở hộp (Thêm lô que thử, Thêm lô dung dịch chứng): hộp dung dịch chứng hiện ngay ô
 * mức và ngưỡng chấp nhận. Trước đây hộp mở sẵn "Que thử", ô ngưỡng chỉ hiện sau khi đổi loại, bệnh viện không thấy chỗ nhập ngưỡng.
 */
function LotModal({ lot, kind, onClose }: LotEdit & { onClose: () => void }) {
  const [form] = Form.useForm<LotForm>()
  const control = kind === 'ControlSolution'
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (values: LotForm) => {
      const input = {
        kind,
        productName: values.productName,
        lotNumber: values.lotNumber,
        expiryDate: values.expiryDate.format(DATE),
        level: control ? (values.level ?? null) : null,
        rangeLow: control ? (values.rangeLow ?? null) : null,
        rangeHigh: control ? (values.rangeHigh ?? null) : null,
        unit: control ? (values.unit ?? null) : null,
        isActive: values.isActive,
      }
      return lot ? qcApi.updateLot(lot.id, input) : qcApi.createLot(input)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: qcKeys.all })
      void message.success(lot ? 'Đã lưu lô.' : 'Đã thêm lô.')
      onClose()
    },
    onError: (error) => applyServerFieldErrors(form, error),
  })
  const title = `${lot ? 'Sửa' : 'Thêm'} ${KIND_NAMES[kind]}`
  return (
    <Modal open title={title} okText="Lưu" onOk={() => form.submit()} confirmLoading={save.isPending} onCancel={onClose} destroyOnHidden>
      {save.isError && !(save.error instanceof ApiError && save.error.fieldErrors) ? (
        <Alert type="error" showIcon title={errorMessage(save.error)} style={{ marginBottom: 16 }} />
      ) : null}
      {lot ? (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title="Sửa lô chỉ áp dụng cho kết quả ghi sau khi sửa; kết quả đã ghi giữ nguyên số lô, hạn dùng, ngưỡng lúc ghi."
        />
      ) : null}
      <Form
        form={form}
        layout="vertical"
        requiredMark={false}
        initialValues={
          lot
            ? {
                productName: lot.productName,
                lotNumber: lot.lotNumber,
                expiryDate: dayjs(lot.expiryDate),
                level: lot.level ?? undefined,
                rangeLow: lot.rangeLow ?? undefined,
                rangeHigh: lot.rangeHigh ?? undefined,
                unit: lot.unit ?? undefined,
                isActive: lot.isActive,
              }
            : { isActive: true, unit: 'MgPerDl' }
        }
        onFinish={(values) => save.mutate(values)}
      >
        {!control ? (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            title="Que thử không có ngưỡng chấp nhận. Ngưỡng nhập ở lô dung dịch chứng (nút Thêm lô dung dịch chứng), mỗi mức một lô."
          />
        ) : null}
        <Form.Item name="productName" label="Tên vật tư" rules={[{ required: true, whitespace: true, message: 'Nhập tên vật tư.' }]}>
          <Input maxLength={100} />
        </Form.Item>
        <Form.Item name="lotNumber" label="Số lô" rules={[{ required: true, whitespace: true, message: 'Nhập số lô.' }]}>
          <Input maxLength={50} />
        </Form.Item>
        <Form.Item name="expiryDate" label="Hạn dùng" rules={[{ required: true, message: 'Nhập hạn dùng.' }]}>
          <DatePicker format={SHOW_DATE} placeholder="Chọn ngày" />
        </Form.Item>
        {control ? (
          <>
            <Form.Item name="level" label="Mức (Level)" rules={[{ required: true, message: 'Chọn mức.' }]}>
              <Select options={[1, 2, 3].map((l) => ({ value: l, label: `Level ${l}` }))} />
            </Form.Item>
            <Form.Item
              label="Ngưỡng chấp nhận"
              required
              extra="Từ – đến của đúng mức này, theo tờ hướng dẫn của lô (có hãng in trên nhãn lọ que thử). Kết quả trong khoảng, tính cả hai đầu, là Đạt."
            >
              <Space.Compact>
                <Form.Item name="rangeLow" noStyle rules={[{ required: true, message: 'Nhập ngưỡng dưới.' }]}>
                  <InputNumber min={0.1} max={9999.9} step={0.1} precision={1} placeholder="Từ" aria-label="Ngưỡng dưới" />
                </Form.Item>
                <Form.Item name="rangeHigh" noStyle rules={[{ required: true, message: 'Nhập ngưỡng trên.' }]}>
                  <InputNumber min={0.1} max={9999.9} step={0.1} precision={1} placeholder="Đến" aria-label="Ngưỡng trên" />
                </Form.Item>
                <Form.Item name="unit" noStyle rules={[{ required: true, message: 'Chọn đơn vị.' }]}>
                  <Select
                    style={{ width: 110 }}
                    aria-label="Đơn vị"
                    options={(Object.keys(QC_UNIT_LABELS) as QcUnit[]).map((u) => ({ value: u, label: QC_UNIT_LABELS[u] }))}
                  />
                </Form.Item>
              </Space.Compact>
            </Form.Item>
          </>
        ) : null}
        {lot ? (
          <Form.Item name="isActive" label="Đang dùng" valuePropName="checked">
            <Switch />
          </Form.Item>
        ) : null}
      </Form>
    </Modal>
  )
}

/**
 * Danh mục nội kiểm do Khoa HSH khai báo (quyết định 57): máy đo đường huyết của từng khoa; lô que thử, lô dung dịch chứng kèm
 * mức và ngưỡng chấp nhận. Không xóa: hết dùng thì ngưng sử dụng.
 */
export function QcCatalogPage() {
  const catalog = useQcCatalog()
  const [meter, setMeter] = useState<QcMeter | null | undefined>(undefined)
  const [lot, setLot] = useState<LotEdit | null>(null)

  const meterColumns: TableColumnsType<QcMeter> = [
    { title: 'Khoa', dataIndex: 'departmentName' },
    { title: 'Loại máy', dataIndex: 'model' },
    { title: 'Số seri', dataIndex: 'serialNumber' },
    { title: 'Trạng thái', render: (_, row) => <ActiveTag active={row.isActive} /> },
    { title: '', render: (_, row) => <Button size="small" onClick={() => setMeter(row)}>Sửa</Button> },
  ]
  const lotColumns: TableColumnsType<QcLot> = [
    { title: 'Loại', render: (_, row) => (row.kind === 'TestStrip' ? 'Que thử' : `Dung dịch chứng Level ${row.level}`) },
    { title: 'Tên vật tư', dataIndex: 'productName' },
    { title: 'Số lô', dataIndex: 'lotNumber' },
    { title: 'Hạn dùng', render: (_, row) => dayjs(row.expiryDate).format(SHOW_DATE) },
    { title: 'Ngưỡng chấp nhận', render: (_, row) => formatQcRange(row.rangeLow, row.rangeHigh, row.unit) },
    { title: 'Trạng thái', render: (_, row) => <ActiveTag active={row.isActive} /> },
    { title: '', render: (_, row) => <Button size="small" onClick={() => setLot({ lot: row, kind: row.kind })}>Sửa</Button> },
  ]

  return (
    <>
      <PageHeader title="Máy đo và lô vật tư nội kiểm" />
      {catalog.isError ? <Alert type="error" showIcon title={errorMessage(catalog.error)} style={{ marginBottom: 16 }} /> : null}
      <Card>
        <Tabs
          items={[
            {
              key: 'meters',
              label: 'Máy đo',
              children: (
                <>
                  <Button type="primary" icon={<PlusOutlined />} style={{ marginBottom: 12 }} onClick={() => setMeter(null)}>
                    Thêm máy đo
                  </Button>
                  <Table rowKey="id" columns={meterColumns} dataSource={catalog.data?.meters} loading={catalog.isPending} pagination={false} scroll={{ x: 'max-content' }} />
                </>
              ),
            },
            {
              key: 'lots',
              label: 'Lô que thử, dung dịch chứng',
              children: (
                <>
                  <Flex gap={8} wrap style={{ marginBottom: 8 }}>
                    <Button icon={<PlusOutlined />} onClick={() => setLot({ lot: null, kind: 'TestStrip' })}>
                      Thêm lô que thử
                    </Button>
                    <Button icon={<PlusOutlined />} onClick={() => setLot({ lot: null, kind: 'ControlSolution' })}>
                      Thêm lô dung dịch chứng
                    </Button>
                  </Flex>
                  <Typography.Paragraph type="secondary">
                    Ngưỡng chấp nhận khai báo ở lô dung dịch chứng, mỗi mức một dòng: Level 1 và Level 2 của cùng số lô là hai dòng.
                  </Typography.Paragraph>
                  <Table rowKey="id" columns={lotColumns} dataSource={catalog.data?.lots} loading={catalog.isPending} pagination={false} scroll={{ x: 'max-content' }} />
                </>
              ),
            },
          ]}
        />
      </Card>
      {meter !== undefined ? <MeterModal meter={meter} onClose={() => setMeter(undefined)} /> : null}
      {lot ? <LotModal lot={lot.lot} kind={lot.kind} onClose={() => setLot(null)} /> : null}
    </>
  )
}
