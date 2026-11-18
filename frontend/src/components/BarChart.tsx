import { Tooltip, Typography } from 'antd'

export interface BarSeries {
  name: string
  color: string
  values: number[]
}

interface BarChartProps {
  /** Nhãn trục hoành, cùng độ dài với mỗi dãy giá trị. */
  labels: string[]
  series: BarSeries[]
  height?: number
  /** Tên dùng cho trình đọc màn hình. */
  title: string
  /** Số nhãn tối đa hiển thị trên trục hoành (bỏ bớt khi quá nhiều ngày). */
  maxLabels?: number
}

/**
 * Biểu đồ cột đơn giản bằng SVG (không cần thư viện): các dãy đứng cạnh nhau theo từng nhóm, có chú giải và tooltip.
 * Dữ liệu cũng được xuất dạng bảng ẩn cho trình đọc màn hình.
 */
export function BarChart({ labels, series, height = 200, title, maxLabels = 16 }: BarChartProps) {
  const max = Math.max(1, ...series.flatMap((s) => s.values))
  const groups = labels.length
  // Tỉ lệ khoảng 5:1 để biểu đồ không bị kéo cao trên màn hình rộng (SVG co giãn theo chiều ngang).
  const width = Math.max(height * 5, groups * 28)
  const padding = { left: 32, right: 8, top: 8, bottom: 24 }
  const plotWidth = width - padding.left - padding.right
  const plotHeight = height - padding.top - padding.bottom
  const groupWidth = plotWidth / Math.max(1, groups)
  const barWidth = (groupWidth * 0.7) / Math.max(1, series.length)
  const labelStep = Math.max(1, Math.ceil(groups / maxLabels))
  const ticks = niceTicks(max)

  return (
    <div>
      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', marginBottom: 4 }}>
        {series.map((s) => (
          <Typography.Text key={s.name} style={{ fontSize: 12 }}>
            <span aria-hidden style={{ display: 'inline-block', width: 10, height: 10, background: s.color, borderRadius: 2, marginRight: 6 }} />
            {s.name}
          </Typography.Text>
        ))}
      </div>
      <div style={{ overflowX: 'auto' }}>
        <svg role="img" aria-label={title} width="100%" viewBox={`0 0 ${width} ${height}`} style={{ minWidth: 320, maxHeight: height * 2, display: 'block' }}>
          {ticks.map((tick) => {
            const y = padding.top + plotHeight - (tick / max) * plotHeight
            return (
              <g key={tick}>
                <line x1={padding.left} x2={width - padding.right} y1={y} y2={y} stroke="#e5e7eb" strokeWidth={1} />
                <text x={padding.left - 4} y={y + 3} fontSize={10} textAnchor="end" fill="#6b7280">
                  {tick}
                </text>
              </g>
            )
          })}
          {labels.map((label, i) => (
            <g key={label}>
              {series.map((s, j) => {
                const value = s.values[i] ?? 0
                const barHeight = (value / max) * plotHeight
                const x = padding.left + i * groupWidth + groupWidth * 0.15 + j * barWidth
                return (
                  <Tooltip key={s.name} title={`${label}: ${s.name} ${value}`}>
                    <rect
                      x={x}
                      y={padding.top + plotHeight - barHeight}
                      width={Math.max(1, barWidth - 1)}
                      height={barHeight}
                      fill={s.color}
                      rx={1}
                    />
                  </Tooltip>
                )
              })}
              {i % labelStep === 0 && (
                <text x={padding.left + i * groupWidth + groupWidth / 2} y={height - 8} fontSize={10} textAnchor="middle" fill="#6b7280">
                  {label}
                </text>
              )}
            </g>
          ))}
        </svg>
      </div>
      <table style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
        <caption>{title}</caption>
        <thead>
          <tr>
            <th scope="col">Nhãn</th>
            {series.map((s) => (
              <th key={s.name} scope="col">
                {s.name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {labels.map((label, i) => (
            <tr key={label}>
              <th scope="row">{label}</th>
              {series.map((s) => (
                <td key={s.name}>{s.values[i] ?? 0}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function niceTicks(max: number): number[] {
  const step = Math.max(1, Math.pow(10, Math.floor(Math.log10(max))) * (max / Math.pow(10, Math.floor(Math.log10(max))) > 5 ? 2 : 1))
  const ticks: number[] = []
  for (let value = 0; value <= max; value += step) {
    ticks.push(value)
  }
  return ticks.slice(0, 7)
}
