import { citizenIdMismatch, isValidCitizenId, type Gender } from './cccd'

/** Thông tin đọc được từ mã QR trên CCCD gắn chip. */
export interface CccdCard {
  citizenId: string
  oldIdNumber: string | null
  fullName: string
  /** yyyy-MM-dd */
  dateOfBirth: string
  gender: Gender
  permanentAddress: string
  /** yyyy-MM-dd hoặc null nếu không có/không đọc được. */
  issueDate: string | null
}

export type CccdParseResult = { ok: true; card: CccdCard; warnings: string[] } | { ok: false; errors: string[] }

const FIELD_COUNT = 7

/** ddMMyyyy → yyyy-MM-dd (kiểm tra ngày có thật). */
function parseDate(value: string): string | null {
  const match = /^(\d{2})(\d{2})(\d{4})$/.exec(value)
  if (!match) {
    return null
  }
  const [, dd, mm, yyyy] = match
  const date = new Date(Date.UTC(Number(yyyy), Number(mm) - 1, Number(dd)))
  if (
    date.getUTCFullYear() !== Number(yyyy) ||
    date.getUTCMonth() !== Number(mm) - 1 ||
    date.getUTCDate() !== Number(dd)
  ) {
    return null
  }
  return `${yyyy}-${mm}-${dd}`
}

function parseGender(value: string): Gender | null {
  const normalized = value
    .normalize('NFD')
    .replace(/\p{M}/gu, '')
    .toLowerCase()
    .trim()
  if (normalized === 'nam') {
    return 'Male'
  }
  return normalized === 'nu' ? 'Female' : null
}

function isoToday(today: Date): string {
  const local = new Date(today.getTime() + 7 * 60 * 60 * 1000) // giờ Việt Nam (UTC+7)
  return local.toISOString().slice(0, 10)
}

/**
 * Phân tích chuỗi QR trên CCCD gắn chip:
 * `Số CCCD|Số CMND cũ|Họ và tên|Ngày sinh (ddMMyyyy)|Giới tính|Nơi thường trú|Ngày cấp (ddMMyyyy)`.
 * Trường thừa phía sau (nếu có) được bỏ qua. Không lưu chuỗi QR gốc.
 */
export function parseCccdQr(raw: string, today: Date = new Date()): CccdParseResult {
  // Bỏ ký tự điều khiển (máy quét có thể gửi kèm CR/LF/TAB) và chuẩn hóa Unicode.
  // eslint-disable-next-line no-control-regex -- cố ý loại ký tự điều khiển do máy quét gửi kèm
  const text = raw.replace(/[\u0000-\u001f\u007f﻿]/g, '').normalize('NFC').trim()
  const fields = text.split('|').map((f) => f.trim())
  if (!text || fields.length < FIELD_COUNT) {
    return {
      ok: false,
      errors: ['Mã QR không đúng định dạng CCCD (cần 7 trường thông tin phân tách bởi dấu "|").'],
    }
  }

  const [citizenId, oldId, fullName, dobText, genderText, address, issueText] = fields
  const errors: string[] = []
  const warnings: string[] = []
  const todayIso = isoToday(today)

  if (!isValidCitizenId(citizenId)) {
    errors.push('Số CCCD trong mã QR không hợp lệ (cần 12 chữ số).')
  }

  if (!fullName) {
    errors.push('Mã QR không có họ tên.')
  } else if (fullName.includes('�') || fullName.includes('?') || !/^[\p{L}\p{M} .'-]+$/u.test(fullName)) {
    warnings.push(
      'Họ tên có ký tự lạ. Máy quét có thể chưa hỗ trợ tiếng Việt có dấu: vui lòng đối chiếu với thẻ và sửa lại.',
    )
  }

  const dateOfBirth = parseDate(dobText)
  if (!dateOfBirth || dateOfBirth < '1900-01-01' || dateOfBirth > todayIso) {
    errors.push('Ngày sinh trong mã QR không hợp lệ.')
  }

  const gender = parseGender(genderText)
  if (!gender) {
    errors.push('Giới tính trong mã QR không hợp lệ.')
  }

  if (errors.length > 0) {
    return { ok: false, errors }
  }

  const oldIdNumber = /^(\d{9}|\d{12})$/.test(oldId) ? oldId : null
  if (oldId && !oldIdNumber) {
    warnings.push('Số CMND cũ trong mã QR không hợp lệ nên được bỏ qua.')
  }

  let issueDate = issueText ? parseDate(issueText) : null
  if (issueText && (!issueDate || issueDate > todayIso || issueDate < dateOfBirth!)) {
    warnings.push('Ngày cấp CCCD trong mã QR không hợp lệ nên được bỏ qua.')
    issueDate = null
  }

  if (!address) {
    warnings.push('Mã QR không có địa chỉ thường trú: vui lòng nhập bổ sung.')
  }

  const mismatch = citizenIdMismatch(citizenId, Number(dateOfBirth!.slice(0, 4)), gender!)
  if (mismatch) {
    warnings.push(`${mismatch} Vui lòng kiểm tra lại thẻ.`)
  }

  return {
    ok: true,
    card: {
      citizenId,
      oldIdNumber,
      fullName,
      dateOfBirth: dateOfBirth!,
      gender: gender!,
      permanentAddress: address,
      issueDate,
    },
    warnings,
  }
}
