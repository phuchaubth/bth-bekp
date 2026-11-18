import type { ConsentFormDetail } from './consentForms'
import { apiRequest } from './http'

/** Mã ký vừa tạo (quyết định 32). `code` chỉ có lúc này, để dựng mã QR; máy chủ chỉ giữ mã băm. */
export interface RemoteSigningLink {
  id: string
  code: string
  expiresAtUtc: string
  formIds: string[]
}

export type RemoteSigningState = 'Waiting' | 'Opened' | 'Signed' | 'Expired' | 'Cancelled' | 'Locked' | 'Superseded'

export interface RemoteSigningStatus {
  id: string
  state: RemoteSigningState
  /** Hạn quét mã và nhập ngày sinh. */
  expiresAtUtc: string
  openedAtUtc: string | null
  signedAtUtc: string | null
}

/** Phiên ký trên điện thoại người bệnh: token phiên ký và nội dung các phiếu (số giấy tờ đã che bớt). */
export interface RemoteSigningSession {
  token: string
  expiresAtUtc: string
  /** Chỉ có ở lần mở đầu tiên: điện thoại giữ lại để tải lại trang mà không phải nhập lại ngày sinh. */
  deviceKey: string | null
  bundleId: string | null
  forms: ConsentFormDetail[]
}

/** Trang ký trên điện thoại riêng của người bệnh (không cần đăng nhập). */
export const REMOTE_SIGNING_PATH = '/ky-tu-xa'

/**
 * Đường dẫn trong mã QR. Mã nằm sau dấu "#": trình duyệt không gửi phần này lên máy chủ khi tải trang, nên mã không nằm trong
 * nhật ký truy cập của IIS.
 */
export const remoteSigningUrl = (code: string, origin: string = window.location.origin) => `${origin}${REMOTE_SIGNING_PATH}#${code}`

export const remoteSigningKeys = {
  status: (id: string) => ['remote-signing', id] as const,
}

export const remoteSigningApi = {
  createForForm: (formId: string) =>
    apiRequest<RemoteSigningLink>(`/api/v1/consent-forms/${formId}/remote-signing`, { method: 'POST' }),
  createForBundle: (bundleId: string) =>
    apiRequest<RemoteSigningLink>(`/api/v1/consent-forms/bundles/${bundleId}/remote-signing`, { method: 'POST' }),
  status: (id: string, signal?: AbortSignal) => apiRequest<RemoteSigningStatus>(`/api/v1/remote-signing/${id}`, { signal }),
  cancel: (id: string) => apiRequest<RemoteSigningStatus>(`/api/v1/remote-signing/${id}/cancel`, { method: 'POST' }),
  /** Điện thoại người bệnh mở mã: lần đầu kèm ngày sinh (yyyy-MM-dd), tải lại trang thì kèm khóa thiết bị. */
  claim: (input: { code: string; dateOfBirth?: string; deviceKey?: string }) =>
    apiRequest<RemoteSigningSession>('/api/v1/remote-signing/claim', { method: 'POST', body: input, auth: false }),
}
