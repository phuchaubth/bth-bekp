import type { ScoreResult } from '../../api/consentForms'
import type { TemplateBlock, TemplateCondition, TemplateDefinition } from '../../api/consentTemplates'

export type Values = Record<string, unknown>

export interface Evaluation {
  /** Tên các trường đang hiển thị. */
  visible: Set<string>
  scores: Record<string, ScoreResult>
  /** Giá trị của các trường tự tính (khối `computed`), ví dụ BMI "21,5". */
  computed: Record<string, string>
  /** Câu hỏi tự chọn theo trường khác (`valueFrom`): lựa chọn được chọn, ví dụ mức BMI "atLeast20". */
  derived: Record<string, string>
}

/** Lựa chọn có khoảng [min, max) chứa giá trị số của trường nguồn (giống FormDataEvaluator.DerivedOption). */
export function derivedOption(block: TemplateBlock, values: Values): string | null {
  const number = parseNumber(values[block.valueFrom ?? ''])
  if (number == null) return null
  return block.options?.find((o) => (o.min == null || number >= o.min) && (o.max == null || number < o.max))?.value ?? null
}

const parseNumber = (value: unknown) => {
  if (typeof value !== 'string' || !/^\s*\d+([.,]\d+)?\s*$/.test(value)) return null
  return Number(value.trim().replace(',', '.'))
}

/**
 * Công thức của trường tự tính, giống ComputedFormulas ở máy chủ (máy chủ tự tính lại khi lưu).
 * bmi: cân nặng (kg) / [chiều cao (m)]², làm tròn 1 chữ số thập phân, dấu phẩy thập phân.
 */
export function computeFormula(formula: string | undefined, inputs: unknown[]): string | null {
  if (formula === 'bmi' && inputs.length === 2) {
    const weightKg = parseNumber(inputs[0])
    const heightCm = parseNumber(inputs[1])
    if (!weightKg || !heightCm) return null
    const bmi = (weightKg * 10000) / (heightCm * heightCm)
    return (Math.round(bmi * 10 + 1e-9) / 10).toFixed(1).replace('.', ',')
  }
  return null
}

const isInput = (block: TemplateBlock) => block.type !== 'paragraph' && block.type !== 'list'

const hasValue = (value: unknown) =>
  value !== undefined && value !== null && value !== '' && value !== false && !(Array.isArray(value) && value.length === 0)

/**
 * Đánh giá điều kiện hiển thị và tổng điểm theo đúng thứ tự khối như máy chủ (FormDataEvaluator), để giao diện ẩn/hiện
 * trường và hiện điểm ngay khi người dùng chọn. Máy chủ vẫn là nơi kiểm tra cuối cùng.
 */
export function evaluate(definition: TemplateDefinition, values: Values): Evaluation {
  const visible = new Set<string>()
  const visibleValues: Values = {}
  const scores: Record<string, ScoreResult> = {}
  const computed: Record<string, string> = {}
  const derived: Record<string, string> = {}
  const blocks = new Map<string, TemplateBlock>()

  for (const block of definition.sections.flatMap((s) => s.blocks).filter(isInput)) {
    const key = block.key!
    blocks.set(key, block)
    if (block.type === 'score') {
      scores[key] = computeScore(block, visibleValues, blocks)
      continue
    }
    if (!conditionHolds(block.visibleWhen, visibleValues, scores, blocks)) {
      continue
    }
    visible.add(key)
    if (block.valueFrom) {
      const option = derivedOption(block, visibleValues)
      if (option) {
        visibleValues[key] = option
        derived[key] = option
        continue
      }
    }
    if (block.type === 'computed') {
      const value = computeFormula(block.formula, (block.inputs ?? []).map((k) => visibleValues[k]))
      if (value) {
        visibleValues[key] = value
        computed[key] = value
      }
      continue
    }
    if (hasValue(values[key])) {
      visibleValues[key] = values[key]
    }
  }

  return { visible, scores, computed, derived }
}

function conditionHolds(
  condition: TemplateCondition | undefined,
  values: Values,
  scores: Record<string, ScoreResult>,
  blocks: Map<string, TemplateBlock>,
): boolean {
  if (!condition) {
    return true
  }
  if (condition.minLevel) {
    const level = scores[condition.field]?.levelCode
    const codes = blocks.get(condition.field)?.levels?.map((l) => l.code) ?? []
    return level != null && codes.indexOf(level) >= codes.indexOf(condition.minLevel)
  }
  const accepted = condition.in ?? []
  const value = values[condition.field]
  if (Array.isArray(value)) {
    return value.some((v) => accepted.includes(String(v)))
  }
  if (typeof value === 'boolean') {
    return accepted.includes(value ? 'true' : 'false')
  }
  return typeof value === 'string' && accepted.includes(value)
}

function computeScore(block: TemplateBlock, values: Values, blocks: Map<string, TemplateBlock>): ScoreResult {
  let total = 0
  for (const key of block.sumOf ?? []) {
    const option = blocks.get(key)?.options?.find((o) => o.value === values[key])
    if (option?.score == null) {
      return { total: null, levelCode: null, levelLabel: null }
    }
    total += option.score
  }
  const level = [...(block.levels ?? [])].reverse().find((l) => total >= l.min)
  return { total, levelCode: level?.code ?? null, levelLabel: level?.label ?? null }
}

/** Chỉ số mức (0 = thấp nhất) để tô màu kết quả điểm. */
export function levelIndex(block: TemplateBlock, levelCode: string | null): number {
  return levelCode ? (block.levels ?? []).findIndex((l) => l.code === levelCode) : -1
}
