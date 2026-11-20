import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { niceScale } from './chartUtils'
import { LineChart, SeriesTable } from './LineChart'

const labels = ['16/11', '17/11', '18/11', '19/11', '20/11', '21/11']
const fullLabels = labels.map((l) => `${l}/2026`)

describe('LineChart', () => {
  it('ngắt đường ở chỗ không có số, điểm lẻ vẽ thành chấm', () => {
    const { container } = render(
      <LineChart title="Thử" labels={labels} series={[{ name: 'A', color: '#008f84', values: [1, 2, null, 4, null, 6] }]} />,
    )

    expect(container.querySelectorAll('path[data-series="A"]')).toHaveLength(1)
    expect(container.querySelector('path[data-series="A"]')?.getAttribute('d')?.match(/[ML]/g)).toEqual(['M', 'L'])
    expect(container.querySelectorAll('circle[data-series="A"]')).toHaveLength(2)
  })

  it('nhãn cuối đường: một dãy ghi số cuối, nhiều dãy ghi tên; các nhãn sát nhau thì bỏ (đã có chú giải)', () => {
    const { container, rerender } = render(
      <LineChart title="Thử" labels={labels} series={[{ name: 'A', color: '#008f84', values: [10, 20, 30, 40, 1234, null] }]} />,
    )
    expect([...container.querySelectorAll('[data-end-label]')].map((e) => e.textContent)).toEqual(['1.234'])

    rerender(
      <LineChart
        title="Thử"
        labels={labels}
        series={[
          { name: 'Nhập khoa', color: '#008f84', values: [1, 2, 3, 4, 5, 40] },
          { name: 'Xuất viện', color: '#eb6834', values: [1, 2, 3, 4, 5, 2] },
        ]}
      />,
    )
    expect([...container.querySelectorAll('[data-end-label]')].map((e) => e.textContent)).toEqual(['Nhập khoa', 'Xuất viện'])
    expect(screen.getByText('Nhập khoa', { selector: 'span' })).toBeInTheDocument()

    rerender(
      <LineChart
        title="Thử"
        labels={labels}
        series={[
          { name: 'Nhập khoa', color: '#008f84', values: [1, 2, 3, 4, 5, 40] },
          { name: 'Xuất viện', color: '#eb6834', values: [1, 2, 3, 4, 5, 39] },
        ]}
      />,
    )
    expect(container.querySelectorAll('[data-end-label]')).toHaveLength(0)
  })

  it('dùng phím mũi tên xem số từng ngày của mọi dãy, kèm dòng ghi thêm', async () => {
    const user = userEvent.setup()
    render(
      <LineChart
        title="Số NB"
        labels={labels}
        fullLabels={fullLabels}
        series={[
          { name: 'Nhập khoa', color: '#008f84', values: [1, 2, 3, 4, 5, 6] },
          { name: 'Xuất viện', color: '#eb6834', values: [6, 5, 4, 3, null, 1] },
        ]}
        details={(i) => [`Ngày thứ ${i + 1}`]}
      />,
    )

    await user.tab()
    const chart = screen.getByRole('group', { name: 'Số NB. Dùng phím mũi tên trái, phải để xem số từng ngày.' })
    expect(chart).toHaveFocus()
    expect(screen.getByRole('status')).toHaveTextContent('21/11/20266Nhập khoa1Xuất việnNgày thứ 6')

    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('status')).toHaveTextContent('20/11/20265Nhập khoa—Xuất việnNgày thứ 5')
    await user.keyboard('{Home}')
    expect(screen.getByRole('status')).toHaveTextContent('16/11/2026')
    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('status')).toHaveTextContent('16/11/2026')
    await user.keyboard('{End}')
    expect(screen.getByRole('status')).toHaveTextContent('21/11/2026')
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('rê chuột tới gần ngày nào thì hiện số ngày đó', () => {
    const { container } = render(
      <LineChart title="Thử" labels={labels} fullLabels={fullLabels} series={[{ name: 'A', color: '#008f84', values: [1, 2, 3, 4, 5, 6] }]} />,
    )
    const overlay = container.querySelector('svg > rect') as SVGRectElement
    // Khung giả lập rộng 640 điểm ảnh, bắt đầu ở x = 0; điểm đầu ở lề trái, điểm cuối ở mép phải vùng vẽ.
    ;(container.querySelector('svg') as SVGSVGElement).getBoundingClientRect = () => ({ left: 0, top: 0, width: 640, height: 220 }) as DOMRect

    fireEvent.pointerMove(overlay, { clientX: 0 })
    expect(screen.getByRole('status')).toHaveTextContent('16/11/2026')
    fireEvent.pointerMove(overlay, { clientX: 639 })
    expect(screen.getByRole('status')).toHaveTextContent('21/11/2026')
    fireEvent.pointerLeave(overlay)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('đường mốc ngang có nhãn, trục tung đủ cao để thấy đường mốc', () => {
    const { container } = render(
      <LineChart
        title="Khoa đã gửi"
        labels={labels}
        series={[{ name: 'Khoa đã gửi', color: '#008f84', values: [1, 2, 2, 3, 3, 2] }]}
        reference={{ value: 15, label: 'Phải báo cáo: 15' }}
      />,
    )

    expect(within(container.querySelector('[data-reference]') as HTMLElement).getByText('Phải báo cáo: 15')).toBeInTheDocument()
    const ticks = [...container.querySelectorAll('svg text')].map((t) => t.textContent)
    expect(ticks).toContain('15')
  })

  it('bảng số liệu: ngày mới nhất trước, ô không có số ghi "—", có cột ghi chú', () => {
    render(
      <SeriesTable
        title="Thử"
        labels={labels.slice(0, 3)}
        fullLabels={fullLabels.slice(0, 3)}
        series={[{ name: 'Số NB', color: '#008f84', values: [1200, null, 7] }]}
        details={(i) => [`${i} khoa`, 'gửi']}
      />,
    )

    const rows = screen.getAllByRole('row').slice(1)
    expect(rows.map((row) => within(row).getAllByRole('cell').map((c) => c.textContent))).toEqual([
      ['18/11/2026', '7', '2 khoa · gửi'],
      ['17/11/2026', '—', '1 khoa · gửi'],
      ['16/11/2026', '1.200', '0 khoa · gửi'],
    ])
  })
})

describe('niceScale', () => {
  it('bước tròn, số nguyên, đủ chứa số lớn nhất', () => {
    expect(niceScale(0)).toEqual({ top: 1, ticks: [0, 1] })
    expect(niceScale(3)).toEqual({ top: 3, ticks: [0, 1, 2, 3] })
    expect(niceScale(47)).toEqual({ top: 60, ticks: [0, 20, 40, 60] })
    expect(niceScale(523)).toEqual({ top: 600, ticks: [0, 200, 400, 600] })
    expect(niceScale(40)).toEqual({ top: 40, ticks: [0, 10, 20, 30, 40] })
  })
})
