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

/**
 * Thông báo lỗi kèm lỗi từng trường máy chủ trả (mỗi lỗi một lần), để người dùng biết dòng nào sai thay vì chỉ thấy
 * "Dữ liệu không hợp lệ.".
 */
export function errorMessageWithDetails(error: unknown): string {
  const details = error instanceof ApiError && error.fieldErrors ? [...new Set(Object.values(error.fieldErrors).flat())] : []
  return details.length > 0 ? `${errorMessage(error)} ${details.join(' ')}` : errorMessage(error)
}

/** Lỗi máy chủ trả cho trường <paramref name="field"/> (nếu có), nếu không thì thông báo chung của lỗi. */
export function fieldErrorMessage(error: unknown, field: string): string {
  const messages = error instanceof ApiError ? error.fieldErrors?.[field] : undefined
  return messages?.length ? messages.join(' ') : errorMessage(error)
}
