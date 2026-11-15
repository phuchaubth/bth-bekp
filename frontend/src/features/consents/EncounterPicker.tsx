import { ReloadOutlined } from '@ant-design/icons'
import type { UseQueryResult } from '@tanstack/react-query'
import { Alert, Button, DatePicker, Flex, Table, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import {
  encounterDate,
  type ConsentFormEncounter,
  type EncounterChoice,
  type EncounterList,
  type EncounterOption,
  type EncounterRange,
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
 * Chọn lượt tiếp nhận trên HIS mà phiếu thuộc về khi ký hoàn tất (quyết định 25, quản trị đang bật nên bắt buộc chọn). Mặc
 * định các lượt trong ngày, mới nhất trước; "Chọn ngày khác" để tra từ ngày đến ngày. Mỗi lượt kèm các phiếu đã gắn vào, để
 * nhân viên không gắn nhầm lượt. Danh sách do hộp ký tải (<paramref name="list"/>), khoảng ngày rỗng là hôm nay.
 */
export function EncounterPicker({
  list,
  range,
  onRangeChange,
  value,
  onChange,
  hint,
}: {
  list: UseQueryResult<EncounterList>
  range: EncounterRange | null
  onRangeChange: (range: EncounterRange | null) => void
  value: EncounterChoice | null
  onChange: (choice: EncounterChoice | null) => void
  hint?: string
}) {
  const [pickingDates, setPickingDates] = useState(false)

  return (
    <section aria-label="Lượt tiếp nhận" style={{ marginBottom: 16 }}>
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 4 }}>
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
                  onRangeChange({ from: from.format('YYYY-MM-DD'), to: to.format('YYYY-MM-DD') })
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
                onRangeChange(null)
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
      <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
        {hint ?? 'Chọn lượt tiếp nhận mà phiếu thuộc về. Số tiếp nhận và người tiếp nhận được in ở chân phiếu để đối soát.'}
      </Typography.Paragraph>
      {list.isError ? (
        <Alert
          type="error"
          showIcon
          title={errorMessage(list.error)}
          description="Nếu chưa khắc phục ngay được, quản trị tạm tắt “Gắn phiếu với lượt tiếp nhận” ở Quản trị → Cấu hình hệ thống, rồi bấm Thử lại để ký hoàn tất tiếp."
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
          loading={list.isFetching}
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
