import { Tag } from 'antd'
import type {
  NursingCheckValue,
  NursingReport,
  NursingReportFormDefinition,
  NursingReportState,
  SaveNursingReportInput,
} from '../../api/nursingReports'

/** Giá trị của form nhập báo cáo khoa: số theo khóa dòng, phần tick theo khóa dòng, phần nhập thêm. */
export interface NursingReportFormValues {
  values: Record<string, number | null | undefined>
  checks: Record<string, NursingCheckValue | null | undefined>
  notes?: string | null
}

export function toFormValues(report: NursingReport): NursingReportFormValues {
  return { values: { ...report.values }, checks: { ...report.checks }, notes: report.notes ?? '' }
}

/** Đổi giá trị form thành yêu cầu lưu: ô trống gửi null, phần nhập thêm trống gửi null. */
export function toSaveInput(
  definition: NursingReportFormDefinition,
  form: NursingReportFormValues,
  reportDate: string,
  submit: boolean,
): SaveNursingReportInput {
  const values: Record<string, number | null> = {}
  for (const section of definition.sections) {
    for (const key of [...(section.valueKey ? [section.valueKey] : []), ...section.items.map((i) => i.key)]) {
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

/** Lưu file về máy (ví dụ Excel bảng tổng hợp). */
export function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}
