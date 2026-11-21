/** Hằng số và hàm dùng chung cho biểu đồ (tách riêng để tệp thành phần chỉ xuất thành phần). */

/**
 * Màu các dãy số liệu, dùng theo thứ tự (không xoay vòng). Đã kiểm tra bằng công cụ: đủ sắc độ, phân biệt được với người mù
 * màu (ΔE ≥ 10 mọi cặp), tương phản ≥ 3:1 trên nền trắng. Màu xanh ngọc thương hiệu (#0d7c74) quá ít sắc độ để làm màu dãy.
 */
export const SERIES_COLORS = ['#008f84', '#eb6834', '#2a78d6'] as const

/** Số kiểu Việt Nam (dấu chấm ngăn cách hàng nghìn). */
export const formatNumber = (value: number) => value.toLocaleString('vi-VN')

/** Thang trục tung từ 0, bước tròn (1, 2, 5 × 10ⁿ, số nguyên), khoảng 4 vạch. */
export function niceScale(max: number): { top: number; ticks: number[] } {
  const rough = Math.max(max, 1) / 4
  const power = 10 ** Math.floor(Math.log10(rough))
  const step = Math.max(1, [1, 2, 5, 10].map((m) => m * power).find((s) => s >= rough) ?? 10 * power)
  const top = Math.max(step, Math.ceil(Math.max(max, 1) / step) * step)
  const ticks: number[] = []
  for (let value = 0; value <= top; value += step) {
    ticks.push(value)
  }
  return { top, ticks }
}
