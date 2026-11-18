import type { Rule } from 'antd/es/form'

/** Quy tắc mật khẩu ở giao diện, khớp chính sách phía máy chủ (PasswordPolicy). */
export const passwordRules: Rule[] = [
  { required: true, message: 'Vui lòng nhập mật khẩu.' },
  { min: 10, message: 'Mật khẩu phải có ít nhất 10 ký tự.' },
  { max: 128, message: 'Mật khẩu không được dài quá 128 ký tự.' },
  { pattern: /\p{Ll}/u, message: 'Mật khẩu phải có ít nhất một chữ thường.' },
  { pattern: /\p{Lu}/u, message: 'Mật khẩu phải có ít nhất một chữ hoa.' },
  { pattern: /\d/, message: 'Mật khẩu phải có ít nhất một chữ số.' },
  { pattern: /[^\p{L}\p{N}]/u, message: 'Mật khẩu phải có ít nhất một ký tự đặc biệt.' },
]

export const passwordHint = 'Ít nhất 10 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.'
