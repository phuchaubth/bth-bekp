/**
 * Access token chỉ được giữ trong bộ nhớ (không lưu localStorage/sessionStorage) để giảm rủi ro bị đánh cắp
 * qua XSS. Khi tải lại trang, ứng dụng lấy access token mới bằng refresh token trong cookie HttpOnly.
 */
let accessToken: string | null = null

export const tokenStore = {
  get: (): string | null => accessToken,
  set: (token: string | null): void => {
    accessToken = token
  },
}
