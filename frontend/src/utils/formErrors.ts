import type { FormInstance } from 'antd'
import { ApiError } from '../api/http'

/**
 * Gắn lỗi theo từng trường từ máy chủ (ProblemDetails.errors) vào form Ant Design.
 * Trả về true nếu đã gắn được ít nhất một lỗi trường.
 */
export function applyServerFieldErrors(form: FormInstance, error: unknown): boolean {
  if (!(error instanceof ApiError) || !error.fieldErrors) {
    return false
  }
  const fieldNames = new Set(Object.keys(form.getFieldsValue(true) ?? {}))
  const fields = Object.entries(error.fieldErrors)
    .filter(([name]) => fieldNames.has(name))
    .map(([name, errors]) => ({ name, errors }))
  form.setFields(fields)
  return fields.length > 0
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Đã xảy ra lỗi không xác định.'
}
