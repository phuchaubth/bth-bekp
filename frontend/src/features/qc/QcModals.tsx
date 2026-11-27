import { useMutation } from '@tanstack/react-query'
import { Alert, Checkbox, DatePicker, Flex, Form, Input, InputNumber, Modal, Select, Table, Typography, type FormInstance } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { ApiError } from '../../api/http'
import {
  QC_OUTCOME_LABELS,
  formatQcRange,
  formatQcValue,
  inQcRange,
  qcApi,
  type QcBook,
  type QcDay,
  type QcLot,
  type QcReviewOutcome,
  type QcRun,
  type QcRunInput,
} from '../../api/qc'
import { errorMessage } from '../../utils/formErrors'
import { QcEvaluationTag } from './qcTags'

const DATE = 'YYYY-MM-DD'
const SHOW_DATE = 'DD/MM/YYYY'

const lotLabel = (lot: QcLot) =>
  lot.kind === 'TestStrip'
    ? `${lot.lotNumber} – ${lot.productName} (hạn ${dayjs(lot.expiryDate).format(SHOW_DATE)})`
    : `Level ${lot.level}: ${lot.lotNumber} – ${lot.productName} (${formatQcRange(lot.rangeLow, lot.rangeHigh, lot.unit)}, hạn ${dayjs(lot.expiryDate).format(SHOW_DATE)})`

/** Gắn lỗi từng trường máy chủ trả (bỏ tiền tố "replacement." của kết quả điều chỉnh) vào form; trả về true nếu gắn được. */
function applyFieldErrors<T>(form: FormInstance<T>, error: unknown): boolean {
  if (!(error instanceof ApiError) || !error.fieldErrors) {
    return false
  }
  const fields = Object.entries(error.fieldErrors).map(([name, errors]) => ({ name: name.replace(/^replacement\./, ''), errors }))
  form.setFields(fields as Parameters<FormInstance<T>['setFields']>[0])
  return fields.length > 0
}

interface RunFormValues {
  runDate: Dayjs
  stripLotId: string
  stripOpenedOn: Dayjs
  control1LotId: string
  control1OpenedOn: Dayjs
  control1Value: number
  control2LotId: string
  control2OpenedOn: Dayjs
  control2Value: number
  reason?: string
  cancelOnly?: boolean
}

/** Đánh giá ngay khi nhập: kết quả so với ngưỡng của lô đã chọn. */
function LiveEvaluation({ lot, value }: { lot: QcLot | undefined; value: number | null | undefined }) {
  if (!lot || lot.rangeLow === null || lot.rangeHigh === null || value === null || value === undefined) {
    return null
  }
  return <QcEvaluationTag passed={inQcRange(value, lot.rangeLow, lot.rangeHigh)} />
}

/**
 * Ghi kết quả của một ngày, hoặc ghi điều chỉnh một kết quả đã ghi (bắt buộc lý do; có thể chỉ hủy kết quả ghi sai). Lô và ngày
 * mở điền sẵn theo kết quả ghi gần nhất của máy; đánh giá Đạt/Không đạt hiện ngay theo ngưỡng của lô.
 */
export function QcRunModal({
  book,
  date,
  run,
  onClose,
  onSaved,
}: {
  book: QcBook
  /** Ngày ghi kết quả mới. */
  date?: string
  /** Kết quả cần ghi điều chỉnh. */
  run?: QcRun
  onClose: () => void
  onSaved: (book: QcBook) => void
}) {
  const [form] = Form.useForm<RunFormValues>()
  const correcting = run !== undefined
  const lots = book.availableLots
  const strips = lots.filter((l) => l.kind === 'TestStrip')
  const controls = lots.filter((l) => l.kind === 'ControlSolution')
  const known = (id: string | null | undefined) => (id && lots.some((l) => l.id === id) ? id : undefined)
  const asDate = (value: string | null | undefined) => (value ? dayjs(value) : undefined)
  const defaults = book.defaults
  const initialValues: Partial<RunFormValues> = run
    ? {
        runDate: dayjs(run.runDate),
        stripLotId: known(run.strip.lotId),
        stripOpenedOn: dayjs(run.strip.openedOn),
        control1LotId: known(run.control1.lotId),
        control1OpenedOn: dayjs(run.control1.openedOn),
        control1Value: run.control1.value,
        control2LotId: known(run.control2.lotId),
        control2OpenedOn: dayjs(run.control2.openedOn),
        control2Value: run.control2.value,
      }
    : {
        runDate: date ? dayjs(date) : undefined,
        stripLotId: known(defaults?.stripLotId),
        stripOpenedOn: asDate(defaults?.stripOpenedOn),
        control1LotId: known(defaults?.control1LotId),
        control1OpenedOn: asDate(defaults?.control1OpenedOn),
        control2LotId: known(defaults?.control2LotId),
        control2OpenedOn: asDate(defaults?.control2OpenedOn),
      }

  const cancelOnly = Form.useWatch('cancelOnly', form) === true
  const control1Id = Form.useWatch('control1LotId', form)
  const control2Id = Form.useWatch('control2LotId', form)
  const control1 = lots.find((l) => l.id === control1Id)
  const control2 = lots.find((l) => l.id === control2Id)
  const value1 = Form.useWatch('control1Value', form)
  const value2 = Form.useWatch('control2Value', form)

  const first = dayjs(`${book.year}-${String(book.month).padStart(2, '0')}-01`)
  const outsideMonth = (d: Dayjs) => d.isBefore(first, 'day') || d.isAfter(first.endOf('month'), 'day') || d.isAfter(dayjs(), 'day')

  const save = useMutation({
    mutationFn: (values: RunFormValues) => {
      const input: QcRunInput = {
        runDate: values.runDate?.format(DATE),
        stripLotId: values.stripLotId,
        stripOpenedOn: values.stripOpenedOn?.format(DATE),
        control1LotId: values.control1LotId,
        control1OpenedOn: values.control1OpenedOn?.format(DATE),
        control1Value: values.control1Value,
        control2LotId: values.control2LotId,
        control2OpenedOn: values.control2OpenedOn?.format(DATE),
        control2Value: values.control2Value,
      }
      return run
        ? qcApi.correct(book.id, run.id, { reason: values.reason ?? '', replacement: values.cancelOnly ? null : input })
        : qcApi.recordRun(book.id, input)
    },
    onSuccess: onSaved,
    onError: (error) => applyFieldErrors(form, error),
  })

  const opened = (name: keyof RunFormValues, label: string) => (
    <Form.Item name={name} label={label} rules={[{ required: true, message: 'Nhập ngày mở.' }]}>
      <DatePicker format={SHOW_DATE} disabledDate={(d) => d.isAfter(dayjs(), 'day')} style={{ width: '100%' }} />
    </Form.Item>
  )

  return (
    <Modal
      open
      title={correcting ? `Ghi điều chỉnh kết quả ngày ${dayjs(run.runDate).format(SHOW_DATE)}` : 'Ghi kết quả nội kiểm'}
      okText={correcting ? 'Lưu điều chỉnh' : 'Lưu kết quả'}
      onOk={() => form.submit()}
      confirmLoading={save.isPending}
      onCancel={onClose}
      width="min(760px, 96vw)"
      destroyOnHidden
    >
      {correcting ? (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title="Kết quả đã ghi không bị sửa: kết quả cũ được giữ lại (in gạch ngang) kèm lý do điều chỉnh, kết quả mới thay thế."
        />
      ) : null}
      {save.isError && !(save.error instanceof ApiError && save.error.fieldErrors) ? (
        <Alert type="error" showIcon title={errorMessage(save.error)} style={{ marginBottom: 16 }} />
      ) : null}
      <Form form={form} layout="vertical" initialValues={initialValues} onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {correcting ? (
          <>
            <Form.Item name="reason" label="Lý do điều chỉnh" rules={[{ required: true, whitespace: true, message: 'Ghi lý do điều chỉnh.' }]}>
              <Input.TextArea rows={2} maxLength={500} showCount />
            </Form.Item>
            <Form.Item name="cancelOnly" valuePropName="checked">
              <Checkbox>Chỉ hủy kết quả này, không ghi kết quả thay thế (ví dụ ghi nhầm máy)</Checkbox>
            </Form.Item>
          </>
        ) : null}
        {cancelOnly ? null : (
          <>
            <Form.Item name="runDate" label="Ngày thực hiện" rules={[{ required: true, message: 'Chọn ngày thực hiện.' }]}>
              <DatePicker format={SHOW_DATE} disabledDate={outsideMonth} />
            </Form.Item>
            <Typography.Title level={5}>Que thử</Typography.Title>
            <Flex gap={12} wrap>
              <Form.Item name="stripLotId" label="Lô que thử" style={{ flex: '2 1 320px' }} rules={[{ required: true, message: 'Chọn lô que thử.' }]}>
                <Select options={strips.map((l) => ({ value: l.id, label: lotLabel(l) }))} placeholder="Chọn lô" />
              </Form.Item>
              <div style={{ flex: '1 1 160px' }}>{opened('stripOpenedOn', 'Ngày mở hộp')}</div>
            </Flex>
            <Typography.Title level={5}>Dung dịch chứng (mức thấp ở cột đầu)</Typography.Title>
            {(
              [
                ['control1LotId', 'control1OpenedOn', 'control1Value', control1, value1, 'Mức thứ nhất'],
                ['control2LotId', 'control2OpenedOn', 'control2Value', control2, value2, 'Mức thứ hai'],
              ] as const
            ).map(([lotField, openedField, valueField, lot, value, label]) => (
              <Flex key={lotField} gap={12} wrap align="flex-end">
                <Form.Item name={lotField} label={label} style={{ flex: '2 1 320px' }} rules={[{ required: true, message: 'Chọn lô dung dịch chứng.' }]}>
                  <Select options={controls.map((l) => ({ value: l.id, label: lotLabel(l) }))} placeholder="Chọn lô" />
                </Form.Item>
                <div style={{ flex: '1 1 160px' }}>{opened(openedField, 'Ngày mở lọ')}</div>
                <Form.Item
                  name={valueField}
                  label={`Kết quả${lot?.unit ? ` (${lot.unit === 'MmolPerL' ? 'mmol/L' : 'mg/dL'})` : ''}`}
                  style={{ flex: '1 1 140px' }}
                  rules={[{ required: true, message: 'Nhập kết quả.' }]}
                >
                  <InputNumber min={0.1} max={9999.9} step={0.1} precision={1} style={{ width: '100%' }} />
                </Form.Item>
                <div style={{ paddingBottom: 28 }}>
                  <LiveEvaluation lot={lot} value={value} />
                </div>
              </Flex>
            ))}
          </>
        )}
      </Form>
    </Modal>
  )
}

/** Kết quả không đạt: ghi nguyên nhân (bắt buộc) và kết quả chạy lại hai mức (không chạy lại thì bỏ chọn). */
export function QcFollowUpModal({ book, run, onClose, onSaved }: { book: QcBook; run: QcRun; onClose: () => void; onSaved: (book: QcBook) => void }) {
  const [form] = Form.useForm<{ reason: string; repeated: boolean; repeat1?: number; repeat2?: number }>()
  const repeated = Form.useWatch('repeated', form) !== false
  const repeat1 = Form.useWatch('repeat1', form)
  const repeat2 = Form.useWatch('repeat2', form)
  const save = useMutation({
    mutationFn: (values: { reason: string; repeated: boolean; repeat1?: number; repeat2?: number }) =>
      qcApi.followUp(book.id, run.id, {
        reason: values.reason,
        control1RepeatValue: values.repeated ? (values.repeat1 ?? null) : null,
        control2RepeatValue: values.repeated ? (values.repeat2 ?? null) : null,
      }),
    onSuccess: onSaved,
    onError: (error) => applyFieldErrors(form, error),
  })
  const level = (control: QcRun['control1'], value: number | undefined, name: 'repeat1' | 'repeat2') => (
    <Flex gap={12} align="flex-end">
      <Form.Item
        name={name}
        label={`Kết quả chạy lại Level ${control.level} (${formatQcRange(control.low, control.high, control.unit)})`}
        rules={[{ required: true, message: 'Nhập kết quả chạy lại.' }]}
      >
        <InputNumber min={0.1} max={9999.9} step={0.1} precision={1} />
      </Form.Item>
      <div style={{ paddingBottom: 28 }}>
        {value === undefined || value === null ? null : <QcEvaluationTag passed={inQcRange(value, control.low, control.high)} />}
      </div>
    </Flex>
  )
  return (
    <Modal
      open
      title={`Nguyên nhân không đạt và chạy lại – ngày ${dayjs(run.runDate).format(SHOW_DATE)}`}
      okText="Lưu"
      onOk={() => form.submit()}
      confirmLoading={save.isPending}
      onCancel={onClose}
      destroyOnHidden
    >
      <Typography.Paragraph>
        Kết quả: Level {run.control1.level} = {formatQcValue(run.control1.value)}, Level {run.control2.level} = {formatQcValue(run.control2.value)}{' '}
        <QcEvaluationTag passed={run.passed} />
      </Typography.Paragraph>
      {save.isError && !(save.error instanceof ApiError && save.error.fieldErrors) ? (
        <Alert type="error" showIcon title={errorMessage(save.error)} style={{ marginBottom: 16 }} />
      ) : null}
      <Form form={form} layout="vertical" initialValues={{ repeated: true }} onFinish={(values) => save.mutate(values)} requiredMark={false}>
        <Form.Item name="reason" label="Nguyên nhân không đạt" rules={[{ required: true, whitespace: true, message: 'Ghi nguyên nhân không đạt.' }]}>
          <Input.TextArea rows={2} maxLength={500} showCount />
        </Form.Item>
        <Form.Item name="repeated" valuePropName="checked">
          <Checkbox>Đã chạy lại QC</Checkbox>
        </Form.Item>
        {repeated ? (
          <>
            {level(run.control1, repeat1, 'repeat1')}
            {level(run.control2, repeat2, 'repeat2')}
          </>
        ) : null}
      </Form>
    </Modal>
  )
}

interface ReviewRow {
  date: string
  outcome: QcReviewOutcome
  note: string
}

/** Khoa HSH xem xét một hoặc nhiều ngày: mỗi ngày chọn kết quả xem xét (điền sẵn theo gợi ý, chỉ các lựa chọn khớp dữ liệu). */
export function QcReviewModal({ book, days, onClose, onSaved }: { book: QcBook; days: QcDay[]; onClose: () => void; onSaved: (book: QcBook) => void }) {
  const [form] = Form.useForm<{ rows: ReviewRow[] }>()
  const save = useMutation({
    // Form chỉ trả các ô đã khai báo (kết quả, ghi chú): ngày lấy theo thứ tự các ngày đang xem xét.
    mutationFn: (rows: ReviewRow[]) =>
      qcApi.review(
        book.id,
        rows.map((r, index) => ({ date: days[index].date, outcome: r.outcome, note: r.note?.trim() ? r.note.trim() : null })),
      ),
    onSuccess: onSaved,
  })
  return (
    <Modal
      open
      title={days.length === 1 ? `Xem xét ngày ${dayjs(days[0].date).format(SHOW_DATE)}` : `Xem xét ${days.length} ngày`}
      okText="Lưu xem xét"
      onOk={() => form.submit()}
      confirmLoading={save.isPending}
      onCancel={onClose}
      width="min(820px, 96vw)"
      destroyOnHidden
    >
      {save.isError ? <Alert type="error" showIcon title={errorMessage(save.error)} style={{ marginBottom: 16 }} /> : null}
      <Form
        form={form}
        initialValues={{ rows: days.map((d) => ({ date: d.date, outcome: d.suggestedOutcome, note: '' })) }}
        onFinish={(values) => save.mutate(values.rows)}
      >
        <Form.List name="rows">
          {(fields) => (
            <Table
              size="small"
              pagination={false}
              rowKey="key"
              dataSource={fields.map((field, index) => ({ ...field, day: days[index] }))}
              columns={[
                { title: 'Ngày', render: (_, row) => dayjs(row.day.date).format(SHOW_DATE), width: 100 },
                {
                  title: 'Kết quả khoa ghi',
                  render: (_, row) =>
                    row.day.run ? (
                      <>
                        {formatQcValue(row.day.run.control1.value)} / {formatQcValue(row.day.run.control2.value)}{' '}
                        <QcEvaluationTag passed={row.day.run.passed} />
                        {row.day.run.hasRepeat ? (
                          <div>
                            Chạy lại: <QcEvaluationTag passed={row.day.run.repeatPassed === true} />
                          </div>
                        ) : null}
                      </>
                    ) : (
                      <Typography.Text type="secondary">Không có kết quả</Typography.Text>
                    ),
                },
                {
                  title: 'Kết quả xem xét',
                  render: (_, row) => (
                    <Form.Item name={[row.name, 'outcome']} style={{ margin: 0 }}>
                      <Select
                        aria-label={`Kết quả xem xét ngày ${dayjs(row.day.date).format(SHOW_DATE)}`}
                        style={{ minWidth: 300 }}
                        options={row.day.allowedOutcomes.map((o) => ({ value: o, label: QC_OUTCOME_LABELS[o] }))}
                      />
                    </Form.Item>
                  ),
                },
                {
                  title: 'Ghi chú',
                  render: (_, row) => (
                    <Form.Item name={[row.name, 'note']} style={{ margin: 0 }}>
                      <Input maxLength={500} placeholder="Không bắt buộc" />
                    </Form.Item>
                  ),
                },
              ]}
            />
          )}
        </Form.List>
      </Form>
    </Modal>
  )
}

/** Hỏi lý do (bắt buộc) trước khi trả lại ngày hoặc sổ. */
export function QcReasonModal({
  title,
  okText,
  description,
  onSubmit,
  onClose,
  onSaved,
}: {
  title: string
  okText: string
  description: string
  onSubmit: (reason: string) => Promise<QcBook>
  onClose: () => void
  onSaved: (book: QcBook) => void
}) {
  const [form] = Form.useForm<{ reason: string }>()
  const save = useMutation({ mutationFn: (reason: string) => onSubmit(reason), onSuccess: onSaved })
  return (
    <Modal open title={title} okText={okText} onOk={() => form.submit()} confirmLoading={save.isPending} onCancel={onClose} destroyOnHidden>
      <Typography.Paragraph>{description}</Typography.Paragraph>
      {save.isError ? <Alert type="error" showIcon title={errorMessage(save.error)} style={{ marginBottom: 16 }} /> : null}
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values.reason)} requiredMark={false}>
        <Form.Item name="reason" label="Lý do" rules={[{ required: true, whitespace: true, message: 'Ghi lý do.' }]}>
          <Input.TextArea rows={3} maxLength={500} showCount />
        </Form.Item>
      </Form>
    </Modal>
  )
}
