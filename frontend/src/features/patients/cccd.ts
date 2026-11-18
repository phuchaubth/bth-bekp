/** Quy tắc số CCCD 12 chữ số (khớp backend CitizenIdNumber). */

export type Gender = 'Male' | 'Female'

const CENTURY_STARTS = [1900, 2000, 2100, 2200, 1800]

export function isValidCitizenId(value: string | null | undefined): boolean {
  if (!value || !/^\d{12}$/.test(value)) {
    return false
  }
  const province = Number(value.slice(0, 3))
  return province >= 1 && province <= 96
}

export function birthYearOf(citizenId: string): number {
  const centuryCode = Number(citizenId[3])
  return CENTURY_STARTS[Math.floor(centuryCode / 2)] + Number(citizenId.slice(4, 6))
}

export function genderOf(citizenId: string): Gender {
  return Number(citizenId[3]) % 2 === 0 ? 'Male' : 'Female'
}

/** Trả về thông báo lỗi nếu số CCCD không khớp năm sinh/giới tính, ngược lại null. */
export function citizenIdMismatch(citizenId: string, birthYear: number, gender: Gender): string | null {
  if (!isValidCitizenId(citizenId)) {
    return 'Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.'
  }
  if (birthYearOf(citizenId) !== birthYear) {
    return 'Số CCCD không khớp với năm sinh.'
  }
  return genderOf(citizenId) !== gender ? 'Số CCCD không khớp với giới tính.' : null
}

/** Che một phần dữ liệu nhạy cảm, ví dụ 0790******12 (khớp backend SensitiveMask). */
export function mask(value: string | null | undefined, keepStart: number, keepEnd: number): string {
  if (!value) {
    return '—'
  }
  if (value.length <= keepStart + keepEnd) {
    return '*'.repeat(value.length)
  }
  return value.slice(0, keepStart) + '*'.repeat(value.length - keepStart - keepEnd) + value.slice(value.length - keepEnd)
}

export const maskCitizenId = (value: string | null | undefined) => mask(value, 4, 2)
export const maskPhone = (value: string | null | undefined) => mask(value, 3, 2)
export const maskInsurance = (value: string | null | undefined) => mask(value, 2, 3)
