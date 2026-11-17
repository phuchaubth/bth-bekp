import { describe, expect, it } from 'vitest'
import { citizenIdMismatch, maskCitizenId, maskInsurance, maskPhone } from './cccd'
import { parseCccdQr, type CccdParseResult } from './cccdQrParser'

// Dữ liệu giả lập, không phải người thật.
const TODAY = new Date('2026-11-11T03:00:00Z')
const VALID = '036188000125||Phạm Thị Dung|05041988|Nữ|Số 9 Đường Thử Nghiệm, Phường Mẫu|01062021'

/**
 * Chuỗi như máy quét 2D ở chế độ bàn phím chưa cài tiếng Việt gõ ra: mỗi byte UTF-8 thành một ký tự của bảng mã Windows.
 * `dropUnassigned`: máy bỏ mất byte bảng mã không gán ký tự.
 */
function typedByScanner(text: string, codePage: 'windows-1252' | 'windows-1258' | 'iso-8859-1', dropUnassigned = false) {
  // TextDecoder đọc "iso-8859-1" như windows-1252, nên tự đổi byte → ký tự cùng số cho ISO-8859-1 thật.
  const decode = codePage === 'iso-8859-1' ? String.fromCharCode : (byte: number) => new TextDecoder(codePage).decode(Uint8Array.of(byte))
  return [...new TextEncoder().encode(text)]
    .map((byte) => decode(byte))
    .filter((char) => !dropUnassigned || !/[\u0080-\u009f]/.test(char))
    .join('')
}

describe('parseCccdQr', () => {
  it('đọc đủ 7 trường và chuyển ngày sang yyyy-MM-dd', () => {
    const result = parseCccdQr(VALID, TODAY)

    expect(result).toEqual({
      ok: true,
      warnings: [],
      card: {
        citizenId: '036188000125',
        oldIdNumber: null,
        fullName: 'Phạm Thị Dung',
        dateOfBirth: '1988-04-05',
        gender: 'Female',
        permanentAddress: 'Số 9 Đường Thử Nghiệm, Phường Mẫu',
        issueDate: '2021-06-01',
      },
    })
  })

  it('bỏ ký tự xuống dòng do máy quét gửi kèm và bỏ qua trường thừa', () => {
    const result = parseCccdQr(`\r\n${VALID}|truong-thua\r\n`, TODAY)

    expect(result.ok).toBe(true)
  })

  it('nhận giới tính viết không dấu hoặc hoa/thường khác nhau', () => {
    const male = parseCccdQr('001085000001|123456789|Nguyễn Văn An|12031985|NAM|Hà Nội|15082021', TODAY)
    const female = parseCccdQr(VALID.replace('|Nữ|', '|nu|'), TODAY)

    expect(male.ok && male.card.gender).toBe('Male')
    expect(male.ok && male.card.oldIdNumber).toBe('123456789')
    expect(female.ok && female.card.gender).toBe('Female')
  })

  it('báo lỗi khi thiếu trường', () => {
    const result = parseCccdQr('036188000125|Phạm Thị Dung|05041988', TODAY)

    expect(result).toEqual({
      ok: false,
      errors: ['Mã QR không đúng định dạng CCCD (cần 7 trường thông tin phân tách bởi dấu "|").'],
    })
  })

  it.each([
    ['0361880001|', 'Số CCCD trong mã QR không hợp lệ (cần 12 chữ số).'],
    ['03618800012A|', 'Số CCCD trong mã QR không hợp lệ (cần 12 chữ số).'],
    ['999188000125|', 'Số CCCD trong mã QR không hợp lệ (cần 12 chữ số).'],
  ])('báo lỗi số CCCD sai: %s', (prefix, message) => {
    const result = parseCccdQr(VALID.replace('036188000125|', prefix), TODAY)

    expect(result.ok).toBe(false)
    expect(!result.ok && result.errors).toContain(message)
  })

  it.each(['31021988', '05131988', '0504198', '05042030'])('báo lỗi ngày sinh không hợp lệ: %s', (dob) => {
    const result = parseCccdQr(VALID.replace('05041988', dob), TODAY)

    expect(!result.ok && result.errors).toContain('Ngày sinh trong mã QR không hợp lệ.')
  })

  it('báo lỗi giới tính lạ', () => {
    const result = parseCccdQr(VALID.replace('|Nữ|', '|Khac|'), TODAY)

    expect(!result.ok && result.errors).toContain('Giới tính trong mã QR không hợp lệ.')
  })

  it('cảnh báo khi họ tên có ký tự lạ (máy quét không hỗ trợ tiếng Việt)', () => {
    const result = parseCccdQr(VALID.replace('Phạm Thị Dung', 'Ph?m Th? Dung'), TODAY)

    expect(result.ok).toBe(true)
    expect(result.ok && result.warnings[0]).toMatch(/Họ tên có ký tự lạ/)
  })

  describe('chữ có dấu bị máy quét gõ thành ký tự lạ', () => {
    const CARD = (parseCccdQr(VALID, TODAY) as Extract<CccdParseResult, { ok: true }>).card
    const ADDRESS = 'Số 9 Đường Thử Nghiệm, Phường Mẫu'

    it.each(['windows-1252', 'windows-1258'] as const)('sửa lại chuỗi máy quét gõ từng byte theo bảng mã %s', (codePage) => {
      const cases: [string, string][] = [
        [ADDRESS, ADDRESS],
        // Chữ Latin-1 (ã, ì, â, à): hai bảng mã gõ ra khác nhau.
        ['Xã Bình Tây, Cà Mau', 'Xã Bình Tây, Cà Mau'],
        // Mã QR ghi Unicode tổ hợp: Windows-1258 gõ dấu thanh thành dấu kết hợp, phải sửa trước khi chuẩn hóa.
        [ADDRESS.normalize('NFD'), ADDRESS],
      ]
      for (const [address, expected] of cases) {
        const typed = typedByScanner(VALID.replace(ADDRESS, address), codePage)
        expect(typed).toContain('á»')

        expect(parseCccdQr(typed, TODAY)).toEqual({ ok: true, card: { ...CARD, permanentAddress: expected }, warnings: [] })
      }
    })

    it.each(['windows-1252', 'iso-8859-1'] as const)('sửa địa chỉ bị mã hóa hai lần (%s) trong mã QR, giữ nguyên các trường đúng', (codePage) => {
      const result = parseCccdQr(VALID.replace(ADDRESS, typedByScanner(ADDRESS, codePage)), TODAY)

      expect(result).toEqual({ ok: true, card: CARD, warnings: [] })
    })

    it('bỏ địa chỉ không sửa được và cảnh báo nhập bổ sung', () => {
      // Máy quét bỏ mất byte chưa gán ký tự (Đ, ờ): không đổi ngược được.
      const lossy = parseCccdQr(typedByScanner(VALID, 'windows-1252', true), TODAY)
      const questionMarks = parseCccdQr(VALID.replace(ADDRESS, 'S? 9 ???ng Th? Nghi?m'), TODAY)

      for (const result of [lossy, questionMarks]) {
        expect(result.ok && result.card.permanentAddress).toBe('')
        expect(result.ok && result.warnings).toEqual([
          'Địa chỉ trong mã QR bị lỗi chữ có dấu (máy quét chưa đọc được tiếng Việt) nên được bỏ qua: vui lòng nhập bổ sung.',
        ])
      }
      expect(lossy.ok && lossy.card.fullName).toBe('Phạm Thị Dung')
    })

    it('ghép địa chỉ dạng Unicode tổ hợp thành chữ dựng sẵn', () => {
      const result = parseCccdQr(VALID.replace(ADDRESS, ADDRESS.normalize('NFD')), TODAY)

      expect(result.ok && result.card.permanentAddress).toBe(ADDRESS)
    })

    it('không sửa nhầm chữ tiếng Việt đúng chỉ gồm chữ Latin-1', () => {
      const address = 'Xã Bình Tây, Cà Mau'
      const result = parseCccdQr(VALID.replace(ADDRESS, address), TODAY)

      expect(result).toEqual({ ok: true, card: { ...CARD, permanentAddress: address }, warnings: [] })
    })
  })

  it('cảnh báo khi số CCCD không khớp năm sinh/giới tính', () => {
    const result = parseCccdQr(VALID.replace('|Nữ|', '|Nam|'), TODAY)

    expect(result.ok && result.warnings).toContain('Số CCCD không khớp với giới tính. Vui lòng kiểm tra lại thẻ.')
  })

  it('bỏ qua ngày cấp không hợp lệ hoặc trước ngày sinh', () => {
    const invalid = parseCccdQr(VALID.replace('01062021', '99999999'), TODAY)
    const beforeBirth = parseCccdQr(VALID.replace('01062021', '01011980'), TODAY)
    const empty = parseCccdQr(VALID.replace('|01062021', '|'), TODAY)

    expect(invalid.ok && invalid.card.issueDate).toBeNull()
    expect(beforeBirth.ok && beforeBirth.card.issueDate).toBeNull()
    expect(empty.ok && empty.card.issueDate).toBeNull()
    expect(empty.ok && empty.warnings).toEqual([])
  })
})

describe('cccd', () => {
  it('kiểm tra số CCCD với năm sinh và giới tính', () => {
    expect(citizenIdMismatch('001085000001', 1985, 'Male')).toBeNull()
    expect(citizenIdMismatch('001085000001', 1986, 'Male')).toBe('Số CCCD không khớp với năm sinh.')
    expect(citizenIdMismatch('048210000010', 2010, 'Male')).toBeNull()
  })

  it('che dữ liệu nhạy cảm giống phía máy chủ', () => {
    expect(maskCitizenId('001085000001')).toBe('0010******01')
    expect(maskPhone('0900000001')).toBe('090*****01')
    expect(maskInsurance('DN4790000000002')).toBe('DN**********002')
    expect(maskCitizenId(null)).toBe('—')
  })
})
