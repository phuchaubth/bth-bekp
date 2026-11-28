import dayjs from 'dayjs'

/** Hiển thị thời gian theo giờ máy người dùng (Việt Nam: UTC+7), định dạng dd/MM/yyyy HH:mm. */
export function formatDateTime(value: string | null | undefined): string {
  return value ? dayjs(value).format('DD/MM/YYYY HH:mm') : '—'
}

/** Ngày dạng yyyy-MM-dd → dd/MM/yyyy. */
export function formatDate(value: string | null | undefined): string {
  return value ? dayjs(value).format('DD/MM/YYYY') : '—'
}

/** Khoảng ngày (tính cả hai ngày): "20/11 – 22/11/2026"; một ngày thì "20/11/2026"; khác năm thì ghi đủ năm cả hai ngày. */
export function formatDateRange(startDate: string, endDate: string): string {
  const start = dayjs(startDate)
  const end = dayjs(endDate)
  if (startDate === endDate) {
    return end.format('DD/MM/YYYY')
  }
  return `${start.format(start.year() === end.year() ? 'DD/MM' : 'DD/MM/YYYY')} – ${end.format('DD/MM/YYYY')}`
}
