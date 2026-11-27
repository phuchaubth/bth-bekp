import { Table, Typography } from 'antd'
import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { formatNumber, niceScale } from './chartUtils'

export interface LineSeries {
  name: string
  color: string
  /** Một giá trị cho mỗi nhãn; `null` là không có số (đường bị ngắt, không vẽ 0). */
  values: (number | null)[]
}

export interface LineChartProps {
  /** Nhãn trục hoành (ví dụ ngày "20/11"), cùng độ dài với mỗi dãy giá trị. */
  labels: string[]
  /** Nhãn đầy đủ trong khung gợi ý và bảng số liệu (ví dụ "20/11/2026"); mặc định như `labels`. */
  fullLabels?: string[]
  series: LineSeries[]
  /** Tên biểu đồ cho trình đọc màn hình. */
  title: string
  /** Chiều cao cả vùng vẽ lẫn nhãn trục hoành (điểm ảnh). */
  height?: number
  /** Dòng ghi thêm trong khung gợi ý và bảng số liệu của điểm thứ `index` (ví dụ số khoa đã gửi). */
  details?: (index: number) => string[]
  /** Đường mốc ngang có nhãn (ví dụ số khoa phải báo cáo). */
  reference?: { value: number; label: string }
  /** Mỗi điểm là gì (cột đầu của bảng số, lời nhắc phím mũi tên): mặc định "Ngày"; biểu đồ theo tháng là "Tháng". */
  pointLabel?: string
}

const TEXT_SECONDARY = '#55706d'
const GRID = '#e3eaeb'
const SURFACE = '#ffffff'
/** Khoảng cách tối thiểu giữa hai nhãn cuối đường; gần hơn thì bỏ nhãn, dùng chú giải (không đẩy nhãn lệch khỏi đường). */
const MIN_LABEL_GAP = 14

/** Bề rộng thật của khung chứa (vẽ đúng điểm ảnh để chữ trục không bị co nhỏ khi nhiều ngày). */
function useContainerWidth(fallback: number) {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(fallback)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) {
      return
    }
    const measure = () => {
      if (element.clientWidth > 0) {
        setWidth(element.clientWidth)
      }
    }
    measure()
    if (typeof ResizeObserver === 'undefined') {
      return
    }
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  return [ref, width] as const
}

/** Các đoạn liền nhau của một dãy (bỏ qua chỗ không có số). */
function segments(values: (number | null)[]): { index: number; value: number }[][] {
  const result: { index: number; value: number }[][] = []
  let current: { index: number; value: number }[] = []
  values.forEach((value, index) => {
    if (value === null) {
      if (current.length > 0) {
        result.push(current)
      }
      current = []
    } else {
      current.push({ index, value })
    }
  })
  if (current.length > 0) {
    result.push(current)
  }
  return result
}

function lastPoint(values: (number | null)[]) {
  for (let index = values.length - 1; index >= 0; index--) {
    const value = values[index]
    if (value !== null) {
      return { index, value }
    }
  }
  return null
}

/**
 * Biểu đồ đường theo ngày bằng SVG (không cần thư viện), vẽ theo bề rộng thật của khung. Đường 2px, chỗ không có số thì ngắt;
 * điểm cuối mỗi đường có chấm và nhãn (một dãy: số cuối; nhiều dãy: tên dãy, bỏ khi các nhãn sát nhau). Rê chuột hoặc dùng
 * phím mũi tên trái, phải trên biểu đồ để xem số của từng ngày (khung gợi ý ghi số mọi dãy). Có bảng số liệu ẩn cho trình đọc
 * màn hình; bảng hiện được bằng <see cref="SeriesTable" />.
 */
export function LineChart({
  labels,
  fullLabels = labels,
  series,
  title,
  height = 220,
  details,
  reference,
  pointLabel = 'Ngày',
}: LineChartProps) {
  const [containerRef, width] = useContainerWidth(640)
  const [active, setActive] = useState<number | null>(null)
  const count = labels.length
  const values = series.flatMap((s) => s.values).filter((v): v is number => v !== null)
  const { top, ticks } = niceScale(Math.max(0, ...values, reference?.value ?? 0))
  const padding = { top: 12, bottom: 26, left: 12 + formatNumber(top).length * 7 }
  const plotHeight = height - padding.top - padding.bottom
  const y = (value: number) => padding.top + plotHeight - (value / top) * plotHeight

  // Nhãn cuối đường: vị trí dọc không phụ thuộc bề ngang, nên kiểm tra chồng nhau trước khi chia lề phải.
  const ends = series
    .map((s) => ({ series: s, point: lastPoint(s.values) }))
    .filter((e): e is { series: LineSeries; point: { index: number; value: number } } => e.point !== null)
    .map((e) => ({ ...e, text: series.length === 1 ? formatNumber(e.point.value) : e.series.name, labelY: y(e.point.value) }))
  const sortedY = ends.map((e) => e.labelY).sort((a, b) => a - b)
  const showEndLabels = ends.length > 0 && sortedY.every((value, i) => i === 0 || value - sortedY[i - 1] >= MIN_LABEL_GAP)
  const right = showEndLabels ? Math.min(140, 14 + Math.max(...ends.map((e) => e.text.length)) * 7) : 12
  const plotWidth = Math.max(40, width - padding.left - right)
  const x = (index: number) => (count <= 1 ? padding.left + plotWidth / 2 : padding.left + (index / (count - 1)) * plotWidth)
  const labelStep = Math.max(1, Math.ceil(count / Math.max(1, Math.floor(plotWidth / 48))))

  const move = (event: PointerEvent<SVGRectElement>) => {
    const box = event.currentTarget.ownerSVGElement?.getBoundingClientRect()
    if (!box || count === 0) {
      return
    }
    const position = event.clientX - box.left
    const index = count <= 1 ? 0 : Math.round(((position - padding.left) / plotWidth) * (count - 1))
    setActive(Math.min(count - 1, Math.max(0, index)))
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const current = active ?? count - 1
    const next =
      event.key === 'ArrowLeft' ? current - 1
      : event.key === 'ArrowRight' ? current + 1
      : event.key === 'Home' ? 0
      : event.key === 'End' ? count - 1
      : null
    if (event.key === 'Escape') {
      setActive(null)
    } else if (next !== null && count > 0) {
      event.preventDefault()
      setActive(Math.min(count - 1, Math.max(0, next)))
    }
  }

  const activeX = active === null ? 0 : x(active)
  const tooltipOnLeft = active !== null && activeX > width / 2

  return (
    <div
      ref={containerRef}
      tabIndex={0}
      role="group"
      aria-label={`${title}. Dùng phím mũi tên trái, phải để xem số từng ${pointLabel.toLowerCase()}.`}
      onKeyDown={onKeyDown}
      onFocus={() => setActive((current) => current ?? (count > 0 ? count - 1 : null))}
      onBlur={() => setActive(null)}
      style={{ position: 'relative', outlineOffset: 2 }}
    >
      {series.length > 1 && (
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', marginBottom: 4 }}>
          {series.map((s) => (
            <Typography.Text key={s.name} style={{ fontSize: 12 }}>
              <span
                aria-hidden
                style={{ display: 'inline-block', width: 14, height: 2, background: s.color, borderRadius: 1, margin: '0 6px 3px 0' }}
              />
              {s.name}
            </Typography.Text>
          ))}
        </div>
      )}
      <svg role="img" aria-label={title} width={width} height={height} style={{ display: 'block', maxWidth: '100%', overflow: 'visible' }}>
        {ticks.map((tick) => (
          <g key={tick}>
            <line x1={padding.left} x2={padding.left + plotWidth} y1={y(tick)} y2={y(tick)} stroke={GRID} strokeWidth={1} />
            <text
              x={padding.left - 6}
              y={y(tick) + 4}
              fontSize={11}
              textAnchor="end"
              fill={TEXT_SECONDARY}
              style={{ fontVariantNumeric: 'tabular-nums' }}
            >
              {formatNumber(tick)}
            </text>
          </g>
        ))}
        {labels.map((label, index) =>
          (count - 1 - index) % labelStep === 0 ? (
            <text key={`${label}-${index}`} x={x(index)} y={height - 8} fontSize={11} textAnchor="middle" fill={TEXT_SECONDARY}>
              {label}
            </text>
          ) : null,
        )}
        {reference && (
          <g data-reference>
            <line
              x1={padding.left}
              x2={padding.left + plotWidth}
              y1={y(reference.value)}
              y2={y(reference.value)}
              stroke={TEXT_SECONDARY}
              strokeWidth={1}
              strokeDasharray="4 3"
            />
            <text x={padding.left + plotWidth} y={y(reference.value) - 5} fontSize={11} textAnchor="end" fill={TEXT_SECONDARY}>
              {reference.label}
            </text>
          </g>
        )}
        {series.map((s) =>
          segments(s.values).map((segment) =>
            segment.length === 1 ? (
              <circle
                key={`${s.name}-${segment[0].index}`}
                data-series={s.name}
                cx={x(segment[0].index)}
                cy={y(segment[0].value)}
                r={3}
                fill={s.color}
              />
            ) : (
              <path
                key={`${s.name}-${segment[0].index}`}
                data-series={s.name}
                d={segment.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.index)},${y(p.value)}`).join(' ')}
                fill="none"
                stroke={s.color}
                strokeWidth={2}
                strokeLinejoin="round"
                strokeLinecap="round"
              />
            ),
          ),
        )}
        {ends.map((e) => (
          <g key={e.series.name}>
            <circle cx={x(e.point.index)} cy={y(e.point.value)} r={4} fill={e.series.color} stroke={SURFACE} strokeWidth={2} />
            {showEndLabels && (
              <text data-end-label x={x(e.point.index) + 9} y={e.labelY + 4} fontSize={12} fill="#16302e">
                {e.text}
              </text>
            )}
          </g>
        ))}
        {active !== null && (
          <g pointerEvents="none">
            <line x1={activeX} x2={activeX} y1={padding.top} y2={padding.top + plotHeight} stroke={TEXT_SECONDARY} strokeWidth={1} />
            {series.map((s) => {
              const value = s.values[active]
              return value === null || value === undefined ? null : (
                <circle key={s.name} cx={activeX} cy={y(value)} r={4} fill={s.color} stroke={SURFACE} strokeWidth={2} />
              )
            })}
          </g>
        )}
        <rect
          x={padding.left - 8}
          y={0}
          width={plotWidth + 16}
          height={padding.top + plotHeight}
          fill="transparent"
          onPointerMove={move}
          onPointerLeave={() => setActive(null)}
        />
      </svg>
      {active !== null && (
        <div
          role="status"
          style={{
            position: 'absolute',
            top: series.length > 1 ? 28 : 4,
            ...(tooltipOnLeft ? { right: width - activeX + 12 } : { left: activeX + 12 }),
            pointerEvents: 'none',
            background: SURFACE,
            border: '1px solid #e1eaeb',
            borderRadius: 8,
            boxShadow: '0 4px 14px rgba(22, 48, 46, 0.12)',
            padding: '8px 10px',
            minWidth: 150,
            zIndex: 2,
            fontSize: 13,
          }}
        >
          <div style={{ fontWeight: 600, marginBottom: 4 }}>{fullLabels[active]}</div>
          {series.map((s) => {
            const value = s.values[active]
            return (
              <div key={s.name} style={{ display: 'flex', alignItems: 'center', gap: 8, whiteSpace: 'nowrap' }}>
                <span aria-hidden style={{ display: 'inline-block', width: 12, height: 2, background: s.color, borderRadius: 1 }} />
                <strong style={{ fontVariantNumeric: 'tabular-nums' }}>{value === null || value === undefined ? '—' : formatNumber(value)}</strong>
                <span style={{ color: TEXT_SECONDARY }}>{s.name}</span>
              </div>
            )
          })}
          {details?.(active).map((line) => (
            <div key={line} style={{ color: TEXT_SECONDARY, fontSize: 12, whiteSpace: 'nowrap' }}>
              {line}
            </div>
          ))}
        </div>
      )}
      <table style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
        <caption>{title}</caption>
        <thead>
          <tr>
            <th scope="col">{pointLabel}</th>
            {series.map((s) => (
              <th key={s.name} scope="col">
                {s.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {fullLabels.map((label, index) => (
            <tr key={`${label}-${index}`}>
              <th scope="row">{label}</th>
              {series.map((s) => (
                <td key={s.name}>{s.values[index] ?? '—'}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Bảng số liệu của biểu đồ đường (ngày mới nhất đứng đầu), cho người cần đọc số chính xác. */
export function SeriesTable({ labels, fullLabels = labels, series, details, pointLabel = 'Ngày' }: LineChartProps) {
  const rows = fullLabels.map((label, index) => ({ key: index, label })).reverse()
  return (
    <Table
      size="small"
      pagination={false}
      scroll={{ y: 260, x: 'max-content' }}
      dataSource={rows}
      columns={[
        { title: pointLabel, dataIndex: 'label', width: 110 },
        ...series.map((s) => ({
          title: s.name,
          key: s.name,
          align: 'right' as const,
          render: (_: unknown, row: { key: number }) => {
            const value = s.values[row.key]
            return value === null || value === undefined ? '—' : formatNumber(value)
          },
        })),
        ...(details
          ? [{ title: 'Ghi chú', key: 'details', render: (_: unknown, row: { key: number }) => details(row.key).join(' · ') }]
          : []),
      ]}
    />
  )
}
