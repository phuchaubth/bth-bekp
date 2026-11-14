import type { ConsentFormDetail } from '../../api/consentForms'

/**
 * Phiên ký đang mở trên máy này. Chỉ giữ trong bộ nhớ: tải lại trang là mất phiên (người bệnh không dùng tiếp được),
 * nhân viên đăng nhập lại để mở phiên mới.
 */
export interface SigningSession {
  /** Token phiên ký: chỉ dùng để gửi chữ ký của người bệnh cho đúng phiếu này. */
  token: string
  expiresAtUtc: string
  /** Nội dung phiếu để hiển thị (đã tải trước khi đóng phiên đăng nhập của nhân viên). */
  form: ConsentFormDetail
  /** Nhân viên mở phiên (điền sẵn khi đăng nhập lại để thoát chế độ ký). */
  staffUserName: string
}

let current: SigningSession | null = null

export const signingSession = {
  get: (): SigningSession | null => current,
  set: (session: SigningSession): void => {
    current = session
  },
  clear: (): void => {
    current = null
  },
}

export const SIGNING_PATH = '/ky-phieu'
