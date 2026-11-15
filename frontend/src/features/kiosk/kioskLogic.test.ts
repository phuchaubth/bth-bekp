import { describe, expect, it } from 'vitest'
import type { TemplateBlock, TemplateDefinition } from '../../api/consentTemplates'
import type { KioskTemplate } from '../../api/kiosk'
import { birthDateError, dateDigitsToIso, formatDateDigits } from './kioskDates'
import { keyboardFor, kioskQuestions, questionKey, usesNumberPad, validateAnswer } from './kioskQuestions'
import { appendChar, applyTone, backspace } from './vietnameseInput'

describe('applyTone (gõ dấu trên bàn phím kiosk)', () => {
  it.each([
    ['Nguyên', 'nga', 'Nguyễn'],
    ['Nguyen', 'nga', 'Nguyẽn'],
    ['Hoang', 'huyen', 'Hoàng'],
    ['hoa', 'huyen', 'hòa'],
    ['thuy', 'hoi', 'thủy'],
    ['qua', 'hoi', 'quả'],
    ['gia', 'sac', 'giá'],
    ['gi', 'huyen', 'gì'],
    ['nguơi', 'huyen', 'nguời'],
    ['ngươi', 'huyen', 'người'],
    ['truong', 'sac', 'truóng'],
    ['trương', 'huyen', 'trường'],
    ['Mâu', 'nga', 'Mẫu'],
    ['mua', 'sac', 'múa'],
    ['toan', 'sac', 'toán'],
    ['khuyu', 'hoi', 'khuỷu'],
  ] as const)('%s + %s → %s', (text, tone, expected) => {
    expect(applyTone(text, tone)).toBe(expected)
  })

  it('chỉ đặt dấu cho chữ cuối, thay dấu cũ, chạm lại thì bỏ dấu', () => {
    expect(applyTone('Trần Thi', 'nang')).toBe('Trần Thị')
    expect(applyTone('Thị', 'sac')).toBe('Thí')
    expect(applyTone('Thị', 'nang')).toBe('Thi')
    expect(applyTone('Số 1', 'sac')).toBe('Số 1')
    expect(applyTone('', 'sac')).toBe('')
  })

  it('viết hoa chữ đầu mỗi từ, không thêm hai dấu cách liền nhau, xóa được chữ có dấu', () => {
    let text = ''
    for (const char of ['t', 'r', 'a', 'n', ' ', ' ', 'a', 'n']) {
      text = appendChar(text, char, true)
    }
    expect(text).toBe('Tran An')
    expect(backspace(applyTone('Trân', 'huyen'))).toBe('Trầ')
    expect(backspace('Mẫu')).toBe('Mẫ')
  })
})

describe('ngày sinh trên bàn phím số', () => {
  it('hiện dần theo ngày/tháng/năm và chỉ nhận ngày có thật', () => {
    expect(formatDateDigits('1203')).toBe('12/03/____')
    expect(dateDigitsToIso('12031985')).toBe('1985-03-12')
    expect(dateDigitsToIso('31021985')).toBeNull()
    expect(birthDateError('1203', new Date('2026-11-15T00:00:00Z'))).toMatch(/đủ ngày/)
    expect(birthDateError('01012030', new Date('2026-11-15T00:00:00Z'))).toBe('Ngày sinh chưa đúng.')
    expect(birthDateError('12031985', new Date('2026-11-15T00:00:00Z'))).toBeNull()
  })
})

const block = (overrides: Partial<TemplateBlock>): TemplateBlock => ({ type: 'text', key: 'k', ...overrides }) as TemplateBlock

const template = (id: string, definition: Partial<TemplateDefinition>): KioskTemplate => ({
  id,
  code: id,
  name: `Mẫu ${id}`,
  definition: { title: id, sections: [], ...definition } as TemplateDefinition,
})

describe('danh sách câu hỏi trên kiosk', () => {
  const ttnb = template('ttnb', {
    signatures: { decision: 'none' },
    sections: [
      {
        blocks: [
          block({ key: 'ethnicity', label: 'Dân tộc', required: true, prefill: 'patient.ethnicity' }),
          block({ type: 'choice', key: 'religion', label: 'Tôn giáo', required: true, options: [{ value: 'other', label: 'Khác' }, { value: 'none', label: 'Không' }] }),
          block({ key: 'religionOther', label: 'Tôn giáo khác', required: true, visibleWhen: { field: 'religion', in: ['other'] } }),
          block({ type: 'paragraph', text: 'Đoạn văn' }),
        ],
      },
    ],
  })
  const tuvan = template('tuvan', { sections: [{ blocks: [block({ type: 'paragraph', text: 'Nội dung' })] }] })

  it('bỏ câu đã tự điền từ hồ sơ, hiện câu phụ theo câu trả lời, câu Đồng ý/Không đồng ý ở cuối mẫu', () => {
    const prefilled = new Set(['patient.ethnicity'])
    const first = kioskQuestions([ttnb, tuvan], prefilled, {})
    expect(first.map(questionKey)).toEqual(['ttnb:religion', 'tuvan:decision'])

    const withOther = kioskQuestions([ttnb, tuvan], prefilled, { ttnb: { religion: 'other' } })
    expect(withOther.map(questionKey)).toEqual(['ttnb:religion', 'ttnb:religionOther', 'tuvan:decision'])

    expect(kioskQuestions([ttnb], new Set(), {}).map(questionKey)).toEqual(['ttnb:ethnicity', 'ttnb:religion'])
  })

  it('kiểm tra câu trả lời như máy chủ và chọn bàn phím số cho trường chỉ nhận số', () => {
    const phone = block({ key: 'familyPhone', required: true, pattern: '^\\+?[0-9][0-9 .-]{7,18}$', patternMessage: 'Số điện thoại chưa đúng.' })
    expect(validateAnswer(phone, '')).toBe('Vui lòng trả lời câu này.')
    expect(validateAnswer(phone, 'abc')).toBe('Số điện thoại chưa đúng.')
    expect(validateAnswer(phone, '0912345678')).toBeNull()
    expect(usesNumberPad(phone)).toBe(true)
    expect(usesNumberPad(block({ key: 'bhyt', pattern: '^[A-Z]{2}([0-9]{15}|[0-9]{13})$' }))).toBe(false)
    expect(keyboardFor(block({ key: 'bhyt', pattern: '^[A-Z]{2}([0-9]{15}|[0-9]{13})$' }))).toBe('code')
    expect(keyboardFor(block({ key: 'familyPhone', pattern: '^\\+?[0-9][0-9 .-]{7,18}$' }))).toBe('number')
    expect(keyboardFor(block({ key: 'name' }))).toBe('text')
    expect(usesNumberPad(block({ key: 'name' }))).toBe(false)
    expect(validateAnswer(block({ type: 'choice', key: 'c', required: true }), undefined)).toBe('Vui lòng chọn một câu trả lời.')
    expect(validateAnswer(block({ type: 'confirm', key: 'ok', required: true }), false)).toBe('Vui lòng chạm để xác nhận.')
  })
})
