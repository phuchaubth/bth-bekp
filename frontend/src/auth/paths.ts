export const LOGIN_PATH = '/dang-nhap'
export const FORCED_PASSWORD_CHANGE_PATH = '/doi-mat-khau'

/** Chỉ chấp nhận đường dẫn nội bộ (bắt đầu bằng "/" nhưng không phải "//") để tránh chuyển hướng ra trang ngoài. */
export function safeRedirectPath(value: string | null): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/'
  }
  return value
}
