import { describe, expect, it } from 'vitest'
import { splitBilingual } from './bilingual'

const english = (text: string, wholeLines = false) =>
  splitBilingual(text, wholeLines)
    .filter((s) => s.english)
    .map((s) => s.text)
    .join('|')

// Cùng các trường hợp với BilingualTextTests (backend) để màn hình và PDF tô màu giống nhau.
describe('splitBilingual', () => {
  it.each([
    ['Có (Yes)', '(Yes)'],
    ['Không (No)', '(No)'],
    ['Không Bảo hiểm y tế (Non–Health Insurance)', '(Non–Health Insurance)'],
    ['Mã số (Code): QT-KNGT/BM10', '(Code)'],
    [
      'Đăng ký khám (Register via the mobile application named “BV Truyền máu Huyết học” or “BTH”)',
      '(Register via the mobile application named “BV Truyền máu Huyết học” or “BTH”)',
    ],
    ['Nếu trả lời “CÓ” (If you answered “YES” (any question), please ask).', '(If you answered “YES” (any question), please ask)'],
  ])('tiếng Anh trong ngoặc: %s', (text, expected) => {
    expect(english(text)).toBe(expected)
  })

  it.each(['Nguy cơ (0 điểm)', 'Địa chỉ (theo hộ khẩu)', 'Cân nặng (kg)', 'Ông (bà)', 'Chỉ số (BMI)', 'Email: ctxh_bth@bth.org.vn', 'Wong – Baker', 'Ngoặc chưa đóng (Yes'])(
    'giữ màu thường: %s',
    (text) => {
      expect(english(text)).toBe('')
    },
  )

  it('ngoặc ghép tiếng Việt – tiếng Anh: chỉ tô phần tiếng Anh', () => {
    expect(english('(Đã đối chiếu thông tin trên giấy tờ tùy thân – verified against identification documents)')).toBe(
      'verified against identification documents',
    )
  })

  it('cả dòng tiếng Anh chỉ áp dụng cho tiêu đề', () => {
    expect(english('Giấy đồng thuận\nConsent Form', true)).toBe('Consent Form')
    expect(english('Giấy đồng thuận\nConsent Form')).toBe('')
    // Phụ đề BM10: chỉ tô phần trong ngoặc, giữ "Mã số" và mã mẫu màu đen.
    expect(english('(Outpatient – Department of Medical Examination) – Mã số (Code): QT-KNGT/BM10', true)).toBe(
      '(Outpatient – Department of Medical Examination)|(Code)',
    )
  })

  it.each([
    ['Giấy đồng thuận\nConsent Form', true],
    ['Tôi đồng ý (I agree), (I accept) và ký (sign here).', false],
    ['((Yes))', false],
  ])('giữ nguyên toàn bộ chữ: %s', (text, wholeLines) => {
    expect(
      splitBilingual(text, wholeLines)
        .map((s) => s.text)
        .join(''),
    ).toBe(text)
  })
})
