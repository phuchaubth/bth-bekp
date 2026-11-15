import type { ConsentDecision } from '../../api/consentForms'
import type { TemplateBlock } from '../../api/consentTemplates'
import type { KioskTemplate } from '../../api/kiosk'
import { evaluate, type Values } from '../consents/templateLogic'

/** Một màn hình câu hỏi trên kiosk: một trường của mẫu, hoặc câu hỏi Đồng ý/Không đồng ý của mẫu. */
export type KioskQuestion =
  | { kind: 'field'; template: KioskTemplate; templateIndex: number; block: TemplateBlock }
  | { kind: 'decision'; template: KioskTemplate; templateIndex: number }

export type KioskAnswers = Record<string, Values>

export type KioskDecisions = Record<string, ConsentDecision>

/** Khối người bệnh tự trả lời (khối tự tính và tổng điểm thì máy tính). */
const ASKABLE = new Set(['text', 'date', 'choice', 'multiChoice', 'confirm'])

export const asksDecision = (template: KioskTemplate) => template.definition.signatures?.decision !== 'none'

/**
 * Các câu cần hỏi, theo thứ tự mẫu rồi thứ tự trường: chỉ trường đang hiện (theo câu trả lời trước đó), bỏ trường đã tự
 * điền từ hồ sơ (<paramref name="prefilled"/>), câu đã trả lời ở phiếu lần trước khi người bệnh chọn giữ như lần trước
 * (<paramref name="kept"/>, theo mẫu) và câu hỏi tự chọn theo trường khác. Mẫu có hỏi Đồng ý/Không đồng ý thì câu đó ở cuối
 * mẫu (luôn hỏi lại). Tính lại sau mỗi câu trả lời vì điều kiện hiển thị có thể đổi.
 */
export function kioskQuestions(
  templates: KioskTemplate[],
  prefilled: ReadonlySet<string>,
  answers: KioskAnswers,
  kept: ReadonlyMap<string, ReadonlySet<string>> = new Map(),
): KioskQuestion[] {
  return templates.flatMap((template, templateIndex) => {
    const values = answers[template.id] ?? {}
    const evaluation = evaluate(template.definition, values)
    const fields: KioskQuestion[] = template.definition.sections
      .flatMap((section) => section.blocks)
      .filter((block) => block.key && ASKABLE.has(block.type) && evaluation.visible.has(block.key))
      .filter((block) => !(block.prefill && prefilled.has(block.prefill)))
      .filter((block) => !kept.get(template.id)?.has(block.key!))
      .filter((block) => !(block.valueFrom && evaluation.derived[block.key!]))
      .map((block) => ({ kind: 'field', template, templateIndex, block }))
    return asksDecision(template) ? [...fields, { kind: 'decision', template, templateIndex }] : fields
  })
}

export const questionKey = (question: KioskQuestion) =>
  question.kind === 'field' ? `${question.template.id}:${question.block.key}` : `${question.template.id}:decision`

const hasValue = (value: unknown) =>
  value !== undefined && value !== null && value !== '' && value !== false && !(Array.isArray(value) && value.length === 0)

/** Ngày yyyy-MM-dd có thật. */
export function isRealDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) {
    return false
  }
  const [, y, m, d] = match.map(Number)
  const date = new Date(Date.UTC(y, m - 1, d))
  return date.getUTCFullYear() === y && date.getUTCMonth() === m - 1 && date.getUTCDate() === d
}

/** Kiểm tra câu trả lời của một trường như máy chủ (bắt buộc, độ dài, định dạng, số lựa chọn). Trả về lỗi hoặc null. */
export function validateAnswer(block: TemplateBlock, value: unknown): string | null {
  if (!hasValue(value)) {
    if (block.type === 'confirm' && block.required) {
      return 'Vui lòng chạm để xác nhận.'
    }
    return block.required ? (block.type === 'choice' ? 'Vui lòng chọn một câu trả lời.' : 'Vui lòng trả lời câu này.') : null
  }
  if (block.type === 'text' && typeof value === 'string') {
    const text = value.trim()
    if (block.maxLength && text.length > block.maxLength) {
      return `Tối đa ${block.maxLength} ký tự.`
    }
    if (block.pattern && !new RegExp(block.pattern, 'u').test(text)) {
      return block.patternMessage ?? 'Chưa đúng định dạng.'
    }
  }
  if (block.type === 'date' && (typeof value !== 'string' || !isRealDate(value))) {
    return 'Ngày chưa đúng.'
  }
  if (block.type === 'multiChoice' && Array.isArray(value)) {
    if (block.requireAll && value.length < (block.options?.length ?? 0)) {
      return 'Vui lòng chọn đủ mọi mục.'
    }
    if (block.minItems && value.length < block.minItems) {
      return `Chọn ít nhất ${block.minItems} mục.`
    }
    if (block.maxItems && value.length > block.maxItems) {
      return `Chọn tối đa ${block.maxItems} mục.`
    }
  }
  return null
}

/**
 * Bàn phím cho trường chữ theo định dạng của trường: chỉ nhận số (ví dụ số điện thoại người nhà) → bàn phím số; có định
 * dạng mà không nhận chữ tiếng Việt (ví dụ số thẻ BHYT "GD4790000000125") → bàn phím nhập mã (chữ in hoa và số); còn lại
 * → bàn phím tiếng Việt.
 */
export function keyboardFor(block: TemplateBlock): 'number' | 'code' | 'text' {
  if (block.type !== 'text' || !block.pattern) {
    return 'text'
  }
  try {
    const pattern = new RegExp(block.pattern, 'u')
    if (pattern.test('Nguyễn')) {
      return 'text'
    }
    return pattern.test('0912345678') ? 'number' : 'code'
  } catch {
    return 'text'
  }
}

export const usesNumberPad = (block: TemplateBlock) => keyboardFor(block) === 'number'
