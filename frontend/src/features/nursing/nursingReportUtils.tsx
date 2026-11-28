import { Tag } from 'antd'
import {
  DELEGATION_CONTENT_LABELS,
  type NursingCheckValue,
  type NursingDelegationContent,
  type NursingReport,
  type NursingReportFormDefinition,
  type NursingReportFormula,
  type NursingReportItem,
  type NursingReportState,
  type SaveNursingReportInput,
} from '../../api/nursingReports'
import { formatDateRange } from '../../utils/format'

/** Giá trị của form nhập báo cáo khoa: số theo khóa dòng, phần tick theo khóa dòng, phần nhập thêm. */
export interface NursingReportFormValues {
  values: Record<string, number | null | undefined>
  checks: Record<string, NursingCheckValue | null | undefined>
  notes?: string | null
}

export function toFormValues(report: NursingReport): NursingReportFormValues {
  return { values: { ...report.values }, checks: { ...report.checks }, notes: report.notes ?? '' }
}

/** Các dòng tính tự động của mẫu (quyết định 46). */
export function formulaItems(definition: NursingReportFormDefinition): (NursingReportItem & { formula: NursingReportFormula })[] {
  return definition.sections.flatMap((s) => s.items).filter((i): i is NursingReportItem & { formula: NursingReportFormula } => !!i.formula)
}

/** Kết quả công thức như máy chủ tính: ô trống tính 0, `base` trống thì chưa tính được (null). */
export function evaluateFormula(formula: NursingReportFormula, values: Record<string, number | null | undefined> | undefined) {
  const of = (key: string) => {
    const value = values?.[key]
    return typeof value === 'number' ? value : null
  }
  const base = of(formula.base)
  if (base === null) {
    return null
  }
  const sum = (keys: string[]) => keys.reduce((total, key) => total + (of(key) ?? 0), 0)
  return base + sum(formula.add) - sum(formula.subtract)
}

/** Đổi giá trị form thành yêu cầu lưu: ô trống gửi null, phần nhập thêm trống gửi null; không gửi dòng tính tự động. */
export function toSaveInput(
  definition: NursingReportFormDefinition,
  form: NursingReportFormValues,
  reportDate: string,
  submit: boolean,
): SaveNursingReportInput {
  const values: Record<string, number | null> = {}
  for (const section of definition.sections) {
    for (const key of [...(section.valueKey ? [section.valueKey] : []), ...section.items.filter((i) => !i.formula).map((i) => i.key)]) {
      const value = form.values?.[key]
      values[key] = typeof value === 'number' ? value : null
    }
  }
  const checks: Record<string, NursingCheckValue | null> = {}
  for (const check of definition.checks) {
    checks[check.key] = form.checks?.[check.key] ?? null
  }
  const notes = form.notes?.trim()
  return { reportDate, values, checks, notes: notes ? notes : null, submit }
}

export function statusTag(status: NursingReportState) {
  switch (status) {
    case 'Submitted':
      return <Tag color="green">Đã gửi</Tag>
    case 'Draft':
      return <Tag color="gold">Nháp</Tag>
    default:
      return <Tag>Chưa lập</Tag>
  }
}

/** Lưu file về máy (ví dụ Excel bảng tổng hợp); dùng chung với các trang khác. */
export { saveBlob } from '../../utils/download'

/** Thời gian ủy quyền (quyết định 48): "20/11 – 22/11/2026"; một ngày thì "20/11/2026"; khác năm thì ghi đủ năm cả hai ngày. */
export function delegationPeriod({ startDate, endDate }: { startDate: string; endDate: string }) {
  return formatDateRange(startDate, endDate)
}

/** Nội dung được ủy quyền, cách nhau dấu phẩy: "Báo cáo của khoa, Số liệu HIS khi HIS lỗi". */
export function delegationContents(contents: readonly NursingDelegationContent[]) {
  return contents.map((c) => DELEGATION_CONTENT_LABELS[c]).join(', ')
}
