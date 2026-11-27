import { FilePdfOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Descriptions, Flex, Space, Spin, Table, Tag, Typography, type TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import {
  QC_OUTCOME_LABELS,
  formatQcRange,
  formatQcValue,
  qcApi,
  qcKeys,
  useQcBook,
  type QcBook,
  type QcDay,
  type QcLotUsage,
  type QcRun,
} from '../../api/qc'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { PdfModal } from '../nursing/NursingReportFields'
import { QcFollowUpModal, QcReasonModal, QcReviewModal, QcRunModal } from './QcModals'
import { QcEvaluationTag, QcStatusTag } from './qcTags'

const SHOW_DATE = 'DD/MM/YYYY'

type Dialog =
  | { kind: 'record'; date: string }
  | { kind: 'correct'; run: QcRun }
  | { kind: 'followUp'; run: QcRun }
  | { kind: 'review'; days: QcDay[] }
  | { kind: 'returnDay'; date: string }
  | { kind: 'returnBook' }
  | { kind: 'pdf' }

const usage = (items: QcLotUsage[]) =>
  items.length === 0
    ? '—'
    : items
        .map((u) =>
          [
            `Lô ${u.lotNumber}`,
            `hạn ${dayjs(u.expiryDate).format(SHOW_DATE)}`,
            `mở ${dayjs(u.openedOn).format(SHOW_DATE)}`,
            u.level === null ? null : `ngưỡng ${formatQcRange(u.low, u.high, u.unit)}`,
          ]
            .filter(Boolean)
            .join(', '),
        )
        .join('; ')

const levels = (items: QcLotUsage[]) => [...new Set(items.map((u) => u.level))].map((l) => `Level ${l}`).join('/') || 'Level'

/** Kết quả hai mức của một lần chạy, mức không đạt tô đỏ. */
function Values({ run, repeat = false }: { run: QcRun; repeat?: boolean }) {
  const one = (control: QcRun['control1']) => {
    const value = repeat ? control.repeatValue : control.value
    const passed = repeat ? control.repeatPassed : control.passed
    return value === null ? '—' : <Typography.Text type={passed ? undefined : 'danger'}>{formatQcValue(value)}</Typography.Text>
  }
  return (
    <>
      {one(run.control1)} / {one(run.control2)}
    </>
  )
}

/**
 * Một sổ nội kiểm tháng (quyết định 57) như sổ giấy: phần đầu (khoa, máy, lô đã dùng), bảng mỗi ngày một dòng. Khoa ghi kết quả,
 * ghi nguyên nhân và chạy lại, ghi điều chỉnh (kết quả cũ giữ lại, gạch ngang), gửi sổ tháng; Khoa HSH xem xét từng ngày hoặc
 * nhiều ngày một lần, trả lại ngày hoặc sổ, xác nhận sổ tháng. In PDF theo mẫu.
 */
export function QcBookPage() {
  const { id = '' } = useParams()
  const book = useQcBook(id)
  const queryClient = useQueryClient()
  const { message, modal } = App.useApp()
  const [dialog, setDialog] = useState<Dialog | null>(null)
  const [selected, setSelected] = useState<string[]>([])

  const saved = (next: QcBook, text: string) => {
    queryClient.setQueryData(qcKeys.book(next.id), next)
    void queryClient.invalidateQueries({ queryKey: qcKeys.books })
    setDialog(null)
    setSelected([])
    void message.success(text)
  }

  const action = useMutation({
    mutationFn: ({ run }: { run: () => Promise<QcBook>; done: string }) => run(),
    onSuccess: (next, { done }) => saved(next, done),
    onError: (error) => void message.error(errorMessage(error)),
  })

  if (book.isPending) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }
  if (book.isError) {
    return <Alert type="error" showIcon title={errorMessage(book.error)} />
  }

  const data = book.data
  const monthLabel = `${String(data.month).padStart(2, '0')}/${data.year}`
  const level1 = levels(data.header.controls1)
  const level2 = levels(data.header.controls2)
  // Chọn nhanh các ngày chờ xem xét: bỏ hôm nay khi khoa chưa ghi kết quả (khoa có thể còn chạy QC trong ngày).
  const today = dayjs().format('YYYY-MM-DD')
  const toReview = data.days.filter((d) => d.canReview && (d.run !== null || d.date < today))

  const confirmAction = (title: string, content: string, run: () => Promise<QcBook>, done: string) =>
    modal.confirm({ title, content, okText: title, cancelText: 'Không', onOk: () => action.mutateAsync({ run, done }) })

  const columns: TableColumnsType<QcDay> = [
    {
      title: 'Ngày thực hiện',
      width: 105,
      render: (_, day) => (
        <>
          {dayjs(day.date).format(SHOW_DATE)}
          {day.run?.late ? (
            <div>
              <Tag color="orange">Ghi muộn</Tag>
            </div>
          ) : null}
        </>
      ),
    },
    {
      title: `Kết quả (${level1} / ${level2})`,
      width: 160,
      render: (_, day) => (
        <>
          {day.cancelledRuns.map((run) => (
            <div key={run.id}>
              <Typography.Text delete type="secondary">
                {formatQcValue(run.control1.value)} / {formatQcValue(run.control2.value)}
              </Typography.Text>{' '}
              <Typography.Text type="secondary" italic>
                Đã hủy: {run.cancelReason} ({run.cancelledBy})
              </Typography.Text>
            </div>
          ))}
          {day.run ? <Values run={day.run} /> : null}
        </>
      ),
    },
    { title: 'Đánh giá', width: 115, render: (_, day) => (day.run ? <QcEvaluationTag passed={day.run.passed} /> : null) },
    { title: 'Nguyên nhân không đạt', width: 190, render: (_, day) => day.run?.failureReason },
    {
      title: `Chạy lại (${level1} / ${level2})`,
      width: 140,
      render: (_, day) =>
        day.run?.hasRepeat ? (
          <Space size={4} wrap>
            <Values run={day.run} repeat />
            <QcEvaluationTag passed={day.run.repeatPassed === true} />
          </Space>
        ) : null,
    },
    {
      title: 'Nhân viên thực hiện',
      width: 140,
      render: (_, day) =>
        day.run ? (
          <>
            {day.run.performedBy}
            {day.run.followUpBy && day.run.followUpBy !== day.run.performedBy ? (
              <div>
                <Typography.Text type="secondary">Chạy lại: {day.run.followUpBy}</Typography.Text>
              </div>
            ) : null}
          </>
        ) : null,
    },
    {
      title: 'Khoa HSH xem xét',
      width: 200,
      render: (_, day) => (
        <>
          {day.returnedReviews.map((review) => (
            <div key={review.id}>
              <Typography.Text delete type="secondary">
                {QC_OUTCOME_LABELS[review.outcome]}
              </Typography.Text>{' '}
              <Typography.Text type="secondary" italic>
                Trả lại: {review.returnReason}
              </Typography.Text>
            </div>
          ))}
          {day.review ? (
            <>
              {QC_OUTCOME_LABELS[day.review.outcome]}
              <div>
                <Typography.Text type="secondary">{day.review.reviewedBy}</Typography.Text>
              </div>
              {day.review.note ? <div>Ghi chú: {day.review.note}</div> : null}
            </>
          ) : null}
        </>
      ),
    },
    {
      title: '',
      width: 150,
      render: (_, day) => (
        <Space wrap size={4}>
          {day.canRecord ? (
            <Button size="small" type="primary" onClick={() => setDialog({ kind: 'record', date: day.date })}>
              Ghi kết quả
            </Button>
          ) : null}
          {day.run?.canFollowUp ? (
            <Button size="small" danger onClick={() => setDialog({ kind: 'followUp', run: day.run! })}>
              Ghi nguyên nhân, chạy lại
            </Button>
          ) : null}
          {day.run?.canCorrect ? (
            <Button size="small" onClick={() => setDialog({ kind: 'correct', run: day.run! })}>
              Ghi điều chỉnh
            </Button>
          ) : null}
          {day.canReview ? (
            <Button size="small" onClick={() => setDialog({ kind: 'review', days: [day] })}>
              Xem xét
            </Button>
          ) : null}
          {day.canReturn ? (
            <Button size="small" onClick={() => setDialog({ kind: 'returnDay', date: day.date })}>
              Trả lại ngày
            </Button>
          ) : null}
        </Space>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title={`Sổ nội kiểm tháng ${monthLabel}`}
        extra={
          <Space wrap>
            <Link to={`/noi-kiem/so?thang=${data.year}-${String(data.month).padStart(2, '0')}`}>Danh sách sổ</Link>
            <Button icon={<FilePdfOutlined />} onClick={() => setDialog({ kind: 'pdf' })}>
              In PDF
            </Button>
            {data.actions.submit ? (
              <Button
                type="primary"
                onClick={() =>
                  confirmAction(
                    'Gửi sổ tháng',
                    'Gửi rồi khoa không ghi thêm, không điều chỉnh được; Khoa HSH xem xét và xác nhận sổ (hoặc trả lại sổ nếu cần sửa).',
                    () => qcApi.submit(data.id),
                    'Đã gửi sổ tháng cho Khoa HSH.',
                  )
                }
              >
                Gửi sổ tháng
              </Button>
            ) : null}
            {data.actions.return ? <Button onClick={() => setDialog({ kind: 'returnBook' })}>Trả lại sổ</Button> : null}
            {data.actions.confirm ? (
              <Button
                type="primary"
                onClick={() =>
                  confirmAction(
                    'Xác nhận sổ tháng',
                    'Xác nhận rồi sổ khóa hẳn, không ai thay đổi được nữa.',
                    () => qcApi.confirm(data.id),
                    'Đã xác nhận sổ tháng.',
                  )
                }
              >
                Xác nhận sổ tháng
              </Button>
            ) : null}
          </Space>
        }
      />
      {data.status === 'Open' && data.returnReason ? (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title={`Khoa HSH đã trả lại sổ: ${data.returnReason}`}
          description={data.returnedBy ? `${data.returnedBy}, ${formatDateTime(data.returnedAtUtc!)}` : undefined}
        />
      ) : null}
      {data.status === 'Submitted' ? (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title={`Khoa đã gửi sổ tháng (${data.submittedBy}, ${formatDateTime(data.submittedAtUtc!)}), chờ Khoa HSH xác nhận.`}
        />
      ) : null}
      {data.status === 'Confirmed' ? (
        <Alert
          type="success"
          showIcon
          style={{ marginBottom: 16 }}
          title={`Khoa HSH đã xác nhận sổ tháng (${data.confirmedBy}, ${formatDateTime(data.confirmedAtUtc!)}). Sổ đã khóa.`}
        />
      ) : null}
      <Card style={{ marginBottom: 16 }}>
        <Descriptions
          size="small"
          column={{ xs: 1, md: 2 }}
          items={[
            { key: 'department', label: 'Khoa/phòng thực hiện', children: data.departmentName },
            { key: 'meter', label: 'Seri máy', children: data.meterLabel },
            { key: 'month', label: 'Tháng', children: monthLabel },
            { key: 'status', label: 'Trạng thái', children: <QcStatusTag status={data.status} /> },
            { key: 'strips', label: 'Que thử', children: usage(data.header.strips), span: 2 },
            { key: 'controls1', label: `Dung dịch chứng ${level1}`, children: usage(data.header.controls1), span: 2 },
            { key: 'controls2', label: `Dung dịch chứng ${level2}`, children: usage(data.header.controls2), span: 2 },
          ]}
        />
      </Card>
      <Card>
        {data.actions.review && toReview.length > 0 ? (
          <Flex gap={8} wrap style={{ marginBottom: 12 }}>
            <Button onClick={() => setSelected(toReview.map((d) => d.date))}>Chọn mọi ngày chờ xem xét ({toReview.length})</Button>
            <Button
              type="primary"
              disabled={selected.length === 0}
              onClick={() => setDialog({ kind: 'review', days: data.days.filter((d) => selected.includes(d.date)) })}
            >
              Xem xét {selected.length} ngày đã chọn
            </Button>
          </Flex>
        ) : null}
        <Table
          rowKey="date"
          size="small"
          columns={columns}
          dataSource={data.days}
          pagination={false}
          tableLayout="fixed"
          scroll={{ x: 1100 }}
          rowSelection={
            data.actions.review
              ? {
                  selectedRowKeys: selected,
                  onChange: (keys) => setSelected(keys.map(String)),
                  getCheckboxProps: (day) => ({ disabled: !day.canReview, 'aria-label': `Chọn ngày ${dayjs(day.date).format(SHOW_DATE)}` }),
                }
              : undefined
          }
        />
      </Card>

      {dialog?.kind === 'record' ? (
        <QcRunModal book={data} date={dialog.date} onClose={() => setDialog(null)} onSaved={(next) => saved(next, 'Đã ghi kết quả.')} />
      ) : null}
      {dialog?.kind === 'correct' ? (
        <QcRunModal book={data} run={dialog.run} onClose={() => setDialog(null)} onSaved={(next) => saved(next, 'Đã ghi điều chỉnh.')} />
      ) : null}
      {dialog?.kind === 'followUp' ? (
        <QcFollowUpModal book={data} run={dialog.run} onClose={() => setDialog(null)} onSaved={(next) => saved(next, 'Đã ghi nguyên nhân và kết quả chạy lại.')} />
      ) : null}
      {dialog?.kind === 'review' ? (
        <QcReviewModal book={data} days={dialog.days} onClose={() => setDialog(null)} onSaved={(next) => saved(next, 'Đã lưu xem xét.')} />
      ) : null}
      {dialog?.kind === 'returnDay' ? (
        <QcReasonModal
          title={`Trả lại ngày ${dayjs(dialog.date).format(SHOW_DATE)}`}
          okText="Trả lại ngày"
          description="Khoa được ghi điều chỉnh kết quả ngày này, rồi Khoa HSH xem xét lại. Lần xem xét cũ vẫn được giữ (gạch ngang) kèm lý do. Sổ đã gửi thì sổ cũng được trả lại khoa."
          onSubmit={(reason) => qcApi.returnDay(data.id, dialog.date, reason)}
          onClose={() => setDialog(null)}
          onSaved={(next) => saved(next, 'Đã trả lại ngày.')}
        />
      ) : null}
      {dialog?.kind === 'returnBook' ? (
        <QcReasonModal
          title="Trả lại sổ tháng"
          okText="Trả lại sổ"
          description="Khoa ghi thêm hoặc ghi điều chỉnh (với ngày đã xem xét thì trả lại ngày đó), rồi gửi lại sổ."
          onSubmit={(reason) => qcApi.returnBook(data.id, reason)}
          onClose={() => setDialog(null)}
          onSaved={(next) => saved(next, 'Đã trả lại sổ cho khoa.')}
        />
      ) : null}
      <PdfModal
        open={dialog?.kind === 'pdf'}
        title={`Sổ nội kiểm – ${data.departmentName} – ${monthLabel}`}
        fileName={`so-noi-kiem-${data.year}-${String(data.month).padStart(2, '0')}.pdf`}
        queryKey={[...qcKeys.book(data.id), data.status, data.days.length]}
        load={() => qcApi.pdf(data.id)}
        onClose={() => setDialog(null)}
      />
    </>
  )
}
