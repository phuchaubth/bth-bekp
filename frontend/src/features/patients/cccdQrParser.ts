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
  // "n": máy quét bỏ mất chữ "ữ" của "Nữ" ("Nam" không có chữ có dấu nên không bị mất chữ).
  return normalized === 'nu' || normalized === 'n' ? 'Female' : null
}

function isoToday(today: Date): string {
  const local = new Date(today.getTime() + 7 * 60 * 60 * 1000) // giờ Việt Nam (UTC+7)
  return local.toISOString().slice(0, 10)
}

/** Bảng mã máy quét dùng khi gõ từng byte như bàn phím: Windows tiếng Anh, Windows tiếng Việt. */
const SCANNER_CODE_PAGES = ['windows-1252', 'windows-1258'] as const

let codePageBytes: ReadonlyMap<string, number>[] | undefined

/** Ký tự → byte của từng bảng mã (byte chưa gán ký tự thì máy để nguyên mã điều khiển cùng số). */
function codePageTables(): ReadonlyMap<string, number>[] {
  codePageBytes ??= SCANNER_CODE_PAGES.map((label) => {
    const decoder = new TextDecoder(label)
    const table = new Map<string, number>()
    for (let byte = 0x80; byte <= 0xff; byte++) {
      table.set(decoder.decode(Uint8Array.of(byte)), byte)
    }
    for (let byte = 0x80; byte <= 0x9f; byte++) {
      if (!table.has(String.fromCharCode(byte))) {
        table.set(String.fromCharCode(byte), byte)
      }
    }
    return table
  })
  return codePageBytes
}

/**
 * Máy quét 2D ở chế độ bàn phím chưa cài đọc tiếng Việt (hoặc chuỗi bị mã hóa UTF-8 hai lần) gõ mỗi byte của chữ có dấu
 * thành một ký tự Windows-1252 hay Windows-1258: "đường" thành "Ä‘Æ°á»ng". Đổi các ký tự đó ngược lại thành byte rồi đọc
 * lại UTF-8. Chữ tiếng Việt đúng không đổi ngược được như vậy (chữ có dấu không bao giờ đứng trước một byte tiếp nối UTF-8),
 * nên không bị sửa nhầm. Không sửa được thì trả nguyên chuỗi.
 */
function repairScannerText(value: string): string {
  if (!/[\u0080-\uffff]/.test(value)) {
    return value
  }
  for (const table of codePageTables()) {
    const bytes: number[] = []
    let mapped = true
    for (const char of value) {
      const byte = char.charCodeAt(0) < 0x80 ? char.charCodeAt(0) : table.get(char)
      if (byte === undefined) {
        mapped = false
        break
      }
      bytes.push(byte)
    }
    if (!mapped) {
      continue
    }
    try {
      return new TextDecoder('utf-8', { fatal: true }).decode(Uint8Array.from(bytes))
    } catch {
      // Không phải UTF-8. Bảng mã kia, nếu đổi được, cũng ra đúng các byte này: giữ nguyên chuỗi.
      return value
    }
  }
  return value
}

/** Ký tự byte đầu của chữ có dấu UTF-8 khi hiện theo Windows-1252/1258 (Ã Ă Ä Å Æ Ì á). */
const MOJIBAKE_LEAD = '\u00c3\u0102\u00c4\u00c5\u00c6\u00cc\u00e1'
/** Ký tự byte tiếp nối UTF-8 (0x80–0xBF) khi hiện theo Windows-1252/1258. */
const MOJIBAKE_CONTINUATION =
  '\u0080-\u00bf\u0152\u0153\u0160\u0161\u0178\u017d\u017e\u0192\u02c6\u02dc\u2013\u2014\u2018-\u201e\u2020-\u2022\u2026\u2030\u2039\u203a\u20ac\u2122'
const BROKEN_TEXT = new RegExp(`[?\\ufffd\\u0080-\\u009f]|[${MOJIBAKE_LEAD}][${MOJIBAKE_CONTINUATION}]`, 'u')

/**
 * Máy quét không gõ được chữ có dấu thì bỏ hẳn các chữ đó: "Đường" thành "ng", "Hồ Chí Minh" thành "H Ch Minh". Chuỗi như
 * vậy không còn chữ có dấu nào và có chữ không có nguyên âm, điều không có trong tiếng Việt. Trong địa chỉ, chữ viết tắt in
 * hoa (TP, KP) và một chữ cái lẻ (số nhà 12b) không tính.
 */
function lostAccentedLetters(text: string, isAddress: boolean): boolean {
  if (/[\u0080-\uffff]/.test(text)) {
    return false
  }
  return text
    .split(/[^A-Za-z]+/)
    .some((word) => word !== '' && !/[aeiouy]/i.test(word) && (!isAddress || (word.length > 1 && /[a-z]/.test(word))))
}

/**
 * Họ tên đọc từ mã QR có ký tự lạ hoặc bị mất chữ có dấu (máy quét chưa đọc được tiếng Việt). Họ tên như vậy chỉ để đối
 * chiếu: không dùng để lập hồ sơ người bệnh mới.
 */
export function isUnreliableName(fullName: string): boolean {
  return (
    fullName.includes('\ufffd') ||
    fullName.includes('?') ||
    !/^[\p{L}\p{M} .'-]+$/u.test(fullName) ||
    lostAccentedLetters(fullName, false)
  )
}

/**
 * Phân tích chuỗi QR trên CCCD gắn chip (cả mã QR trên ứng dụng VNeID):
 * `Số CCCD|Số CMND cũ|Họ và tên|Ngày sinh (ddMMyyyy)|Giới tính|Nơi thường trú|Ngày cấp (ddMMyyyy)`.
 * Trường thừa phía sau (nếu có) được bỏ qua. Chữ có dấu bị máy quét gõ thành ký tự lạ thì sửa lại. Địa chỉ vẫn hỏng, hoặc
 * bị máy quét bỏ mất chữ có dấu, thì bỏ (không lưu chữ hỏng vào hồ sơ và phiếu); họ tên như vậy thì cảnh báo. Không lưu
 * chuỗi QR gốc.
 */
export function parseCccdQr(raw: string, today: Date = new Date()): CccdParseResult {
  // Bỏ ký tự điều khiển (máy quét có thể gửi kèm CR/LF/TAB), sửa chữ có dấu bị gõ thành ký tự lạ, rồi chuẩn hóa Unicode
  // (địa chỉ "Unicode tổ hợp" thành "dựng sẵn"). Sửa trước khi chuẩn hóa: chuẩn hóa có thể ghép mất byte cần đổi ngược.
  // eslint-disable-next-line no-control-regex -- cố ý loại ký tự điều khiển do máy quét gửi kèm
  const text = raw.replace(/[\u0000-\u001f\u007f﻿]/g, '').trim()
  const fields = text.split('|').map((f) => repairScannerText(f).normalize('NFC').trim())
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
  } else if (isUnreliableName(fullName)) {
    warnings.push(
      'Họ tên có ký tự lạ hoặc mất chữ có dấu. Máy quét có thể chưa đọc được tiếng Việt có dấu: vui lòng đối chiếu với thẻ và sửa lại.',
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

  let permanentAddress = address
  if (!address) {
    warnings.push('Mã QR không có địa chỉ thường trú: vui lòng nhập bổ sung.')
  } else if (BROKEN_TEXT.test(address) || lostAccentedLetters(address, true)) {
    warnings.push(
      'Địa chỉ trong mã QR bị lỗi chữ có dấu (máy quét chưa đọc được tiếng Việt) nên được bỏ qua: vui lòng nhập bổ sung.',
    )
    permanentAddress = ''
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
      permanentAddress,
      issueDate,
    },
    warnings,
  }
}
