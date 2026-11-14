import dayjs from 'dayjs'

/** Hiển thị thời gian theo giờ máy người dùng (Việt Nam: UTC+7), định dạng dd/MM/yyyy HH:mm. */
export function formatDateTime(value: string | null | undefined): string {
  return value ? dayjs(value).format('DD/MM/YYYY HH:mm') : '—'
}

/** Ngày dạng yyyy-MM-dd → dd/MM/yyyy. */
export function formatDate(value: string | null | undefined): string {
  return value ? dayjs(value).format('DD/MM/YYYY') : '—'
}
