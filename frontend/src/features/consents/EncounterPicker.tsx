import { ReloadOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, DatePicker, Flex, Table, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  consentFormKeys,
  consentFormsApi,
  encounterDate,
  type ConsentFormEncounter,
  type EncounterChoice,
  type EncounterOption,
  type EncounterRange,
  type EncounterSource,
} from '../../api/consentForms'
import { errorMessage } from '../../utils/formErrors'
import { formatDate, formatDateTime } from '../../utils/format'
import { FormStatusTag } from './FormStatusTag'

function choiceOf(option: EncounterOption): EncounterChoice {
  return { encounterNo: option.encounterNo, encounterDate: encounterDate(option.receivedAt) }
}

const columns: TableColumnsType<EncounterOption> = [
  {
    title: 'Thời gian tiếp nhận',
    key: 'receivedAt',
    width: 170,
    render: (_, item) => (
      <Flex vertical>
        <Typography.Text style={{ fontVariantNumeric: 'tabular-nums' }}>{formatDateTime(item.receivedAt)}</Typography.Text>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Số TN: {item.encounterNo}
        </Typography.Text>
      </Flex>
    ),
  },
  {
    title: 'Nơi tiếp nhận',
    key: 'place',
    render: (_, item) => (
      <Flex vertical>
        <Typography.Text>{item.place ?? '—'}</Typography.Text>
        {item.receivedBy && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {item.receivedBy}
          </Typography.Text>
        )}
      </Flex>
    ),
  },
  {
    title: 'Các phiếu đã gắn vào',
    key: 'forms',
    render: (_, item) =>
      item.forms.length === 0 ? (
        <Typography.Text type="secondary">Chưa có phiếu</Typography.Text>
      ) : (
        <Flex vertical gap={2}>
          {item.forms.map((form) => (
            <span key={form.id}>
              {form.templateName}{' '}
              <Typography.Text type="secondary" style={{ fontSize: 12, whiteSpace: 'nowrap' }}>
                {form.formNo}
              </Typography.Text>
              {form.status !== 'Completed' && (
                <>
                  {' '}
                  <FormStatusTag status={form.status} />
                </>
              )}
            </span>
          ))}
        </Flex>
      ),
  },
]

/**
 * Chọn lượt tiếp nhận trên HIS mà phiếu thuộc về khi ký hoàn tất (quyết định 25). Mặc định các lượt trong ngày, mới nhất
 * trước; "Chọn ngày khác" để tra từ ngày đến ngày. Mỗi lượt kèm các phiếu đã gắn vào, để nhân viên không gắn nhầm lượt.
 */
export function EncounterPicker({
  source,
  value,
  onChange,
}: {
  source: EncounterSource
  value: EncounterChoice | null
  onChange: (choice: EncounterChoice | null) => void
}) {
  // Rỗng: hôm nay (ngày của bệnh viện, máy chủ quyết định).
  const [range, setRange] = useState<EncounterRange | null>(null)
  const [pickingDates, setPickingDates] = useState(false)
  const list = useQuery({
    queryKey: consentFormKeys.encounters(source, range),
    queryFn: ({ signal }) => consentFormsApi.encounters(source, range, signal),
    // HIS có thể vừa tiếp nhận thêm lượt: mỗi lần mở hộp ký đều tải lại.
    staleTime: 0,
    retry: false,
  })

  const changeRange = (next: EncounterRange | null) => {
    setRange(next)
    // Lượt đã chọn có thể không còn trong danh sách mới: chọn lại cho chắc.
    onChange(null)
  }

  return (
    <section aria-label="Lượt tiếp nhận" style={{ marginBottom: 16 }}>
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 8 }}>
        <Typography.Text strong>
          Lượt tiếp nhận {range ? `từ ${formatDate(range.from)} đến ${formatDate(range.to)}` : 'hôm nay'}{' '}
          <Typography.Text type="danger">(bắt buộc)</Typography.Text>
        </Typography.Text>
        <Flex gap={8} wrap align="center">
          {pickingDates ? (
            <DatePicker.RangePicker
              format="DD/MM/YYYY"
              placeholder={['Từ ngày', 'Đến ngày']}
              aria-label="Khoảng ngày tiếp nhận"
              allowClear={false}
              onChange={(dates) => {
                const [from, to] = (dates ?? []) as (Dayjs | null)[]
                if (from && to) {
                  changeRange({ from: from.format('YYYY-MM-DD'), to: to.format('YYYY-MM-DD') })
                }
              }}
            />
          ) : (
            <Button onClick={() => setPickingDates(true)}>Chọn ngày khác</Button>
          )}
          {(range || pickingDates) && (
            <Button
              onClick={() => {
                setPickingDates(false)
                changeRange(null)
              }}
            >
              Hôm nay
            </Button>
          )}
          <Button
            icon={<ReloadOutlined />}
            aria-label="Tải lại danh sách lượt tiếp nhận"
            loading={list.isFetching}
            onClick={() => void list.refetch()}
          />
        </Flex>
      </Flex>
      {list.isError ? (
        <Alert
          type="error"
          showIcon
          title={errorMessage(list.error)}
          action={
            <Button size="small" onClick={() => void list.refetch()}>
              Thử lại
            </Button>
          }
        />
      ) : (
        <Table<EncounterOption>
          size="small"
          rowKey="encounterNo"
          columns={columns}
          dataSource={list.data?.items}
          loading={list.isPending}
          pagination={false}
          scroll={{ y: 240 }}
          rowSelection={{
            type: 'radio',
            selectedRowKeys: value ? [value.encounterNo] : [],
            onChange: (_, rows) => onChange(rows[0] ? choiceOf(rows[0]) : null),
            getCheckboxProps: (item) => ({ 'aria-label': `Chọn lượt tiếp nhận ${item.encounterNo}` }),
          }}
          onRow={(item) => ({ onClick: () => onChange(choiceOf(item)), style: { cursor: 'pointer' } })}
          locale={{
            emptyText: range
              ? 'Không có lượt tiếp nhận nào trong khoảng ngày này.'
              : 'Hôm nay người bệnh chưa có lượt tiếp nhận nào trên HIS. Nếu phiếu thuộc lượt trước, bấm “Chọn ngày khác”.',
          }}
        />
      )}
    </section>
  )
}

/** Lượt tiếp nhận đã gắn với phiếu, như dòng in ở chân trang PDF. */
export function EncounterSummary({ encounter }: { encounter: ConsentFormEncounter }) {
  const parts = [`Số tiếp nhận: ${encounter.encounterNo}`, formatDateTime(encounter.receivedAt)]
  if (encounter.place) {
    parts.push(encounter.place)
  }
  if (encounter.receivedBy) {
    parts.push(`Người tiếp nhận: ${encounter.receivedBy}`)
  }
  return <Typography.Text>{parts.join(' · ')}</Typography.Text>
}
