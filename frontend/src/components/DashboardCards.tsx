import { Button, Card, Typography } from 'antd'
import { useState, type ReactNode } from 'react'
import { LineChart, SeriesTable, type LineChartProps } from './LineChart'

/** Đang tải số liệu mới (đổi ngày, tháng, khoảng ngày): giữ số cũ, mờ đi, không nháy trang. */
export function Busy({ busy, children }: { busy: boolean; children: ReactNode }) {
  return (
    <div aria-busy={busy} style={{ opacity: busy ? 0.6 : 1, transition: 'opacity 0.2s' }}>
      {children}
    </div>
  )
}

/** Ô chỉ số của dashboard: tên, số lớn, dòng giải thích nhỏ. */
export function StatTile({ label, value, children }: { label: string; value: ReactNode; children?: ReactNode }) {
  return (
    <Card size="small" role="group" aria-label={label} style={{ height: '100%' }}>
      <Typography.Text type="secondary">{label}</Typography.Text>
      <div style={{ fontSize: 28, fontWeight: 600, lineHeight: 1.3, margin: '2px 0 4px' }}>{value}</div>
      {children && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {children}
        </Typography.Text>
      )}
    </Card>
  )
}

/** Thẻ biểu đồ; bấm "Xem bảng" để đọc số chính xác từng điểm. */
export function ChartCard({ title, note, chart }: { title: string; note?: string; chart: LineChartProps }) {
  const [asTable, setAsTable] = useState(false)
  return (
    <Card
      size="small"
      title={title}
      style={{ height: '100%' }}
      extra={
        <Button type="link" size="small" onClick={() => setAsTable((value) => !value)} aria-label={`${asTable ? 'Xem biểu đồ' : 'Xem bảng'}: ${title}`}>
          {asTable ? 'Xem biểu đồ' : 'Xem bảng'}
        </Button>
      }
    >
      {note && (
        <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 8 }}>
          {note}
        </Typography.Paragraph>
      )}
      {asTable ? <SeriesTable {...chart} /> : <LineChart {...chart} />}
    </Card>
  )
}
