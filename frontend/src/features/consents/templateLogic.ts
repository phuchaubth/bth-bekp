import type { ScoreResult } from '../../api/consentForms'
import type { TemplateBlock, TemplateCondition, TemplateDefinition } from '../../api/consentTemplates'

export type Values = Record<string, unknown>

export interface Evaluation {
  /** Tên các trường đang hiển thị. */
  visible: Set<string>
  scores: Record<string, ScoreResult>
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
    if (hasValue(values[key])) {
      visibleValues[key] = values[key]
    }
  }

  return { visible, scores }
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
