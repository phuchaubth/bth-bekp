/**
 * Ngày sinh nhập trên bàn phím số của kiosk: tối đa 8 chữ số theo thứ tự ngày, tháng, năm (ddMMyyyy), hiện dạng
 * "12/03/1985" (chỗ chưa nhập là gạch dưới).
 */
export const DATE_DIGITS = 8

export function formatDateDigits(digits: string): string {
  const padded = digits.padEnd(DATE_DIGITS, '_')
  return `${padded.slice(0, 2)}/${padded.slice(2, 4)}/${padded.slice(4)}`
}

/** ddMMyyyy → yyyy-MM-dd nếu là ngày có thật, ngược lại null. */
export function dateDigitsToIso(digits: string): string | null {
  if (!/^\d{8}$/.test(digits)) {
    return null
  }
  const day = Number(digits.slice(0, 2))
  const month = Number(digits.slice(2, 4))
  const year = Number(digits.slice(4))
  const date = new Date(Date.UTC(year, month - 1, day))
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) {
    return null
  }
  return `${digits.slice(4)}-${digits.slice(2, 4)}-${digits.slice(0, 2)}`
}

/** yyyy-MM-dd hôm nay theo giờ Việt Nam (UTC+7). */
export function todayIso(now: Date = new Date()): string {
  return new Date(now.getTime() + 7 * 60 * 60 * 1000).toISOString().slice(0, 10)
}

/** Ngày sinh hợp lệ: ngày có thật, từ năm 1900 đến hôm nay. Trả về lỗi hoặc null. */
export function birthDateError(digits: string, now: Date = new Date()): string | null {
  if (digits.length < DATE_DIGITS) {
    return 'Vui lòng nhập đủ ngày, tháng, năm sinh.'
  }
  const iso = dateDigitsToIso(digits)
  if (!iso || iso < '1900-01-01' || iso > todayIso(now)) {
    return 'Ngày sinh chưa đúng.'
  }
  return null
}

/** yyyy-MM-dd → dd/MM/yyyy. */
export function displayDate(iso: string): string {
  const [y, m, d] = iso.slice(0, 10).split('-')
  return `${d}/${m}/${y}`
}
