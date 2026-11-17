/**
 * Kiểm tra camera mà không tải thư viện đọc mã QR (thư viện chỉ tải khi thật sự mở camera).
 */

/** Trình duyệt chỉ cho dùng camera ở địa chỉ an toàn (HTTPS hoặc localhost); ở địa chỉ khác không có `navigator.mediaDevices`. */
export function canUseCamera(): boolean {
  return typeof navigator !== 'undefined' && typeof navigator.mediaDevices?.getUserMedia === 'function'
}

/**
 * Máy có camera hay không, mà không hỏi quyền: trước khi được cho phép, trình duyệt vẫn cho biết có camera (không kèm tên
 * thiết bị). Không kiểm tra được thì coi như không có.
 */
export async function hasCamera(): Promise<boolean> {
  if (!canUseCamera() || typeof navigator.mediaDevices.enumerateDevices !== 'function') {
    return false
  }
  try {
    const devices = await navigator.mediaDevices.enumerateDevices()
    return devices.some((device) => device.kind === 'videoinput')
  } catch {
    return false
  }
}

/**
 * Trình duyệt đang chặn camera cho trang này (người dùng chọn Chặn, hoặc chính sách trình duyệt), mà không hỏi quyền.
 * Trình duyệt không cho biết (không hỗ trợ hỏi quyền camera) thì coi như không chặn.
 */
export async function cameraBlocked(): Promise<boolean> {
  if (typeof navigator === 'undefined' || typeof navigator.permissions?.query !== 'function') {
    return false
  }
  try {
    const status = await navigator.permissions.query({ name: 'camera' as PermissionName })
    return status.state === 'denied'
  } catch {
    return false
  }
}

/** Tên lỗi trình duyệt trả khi mở camera (`NotAllowedError`, `NotFoundError`…); rỗng nếu không rõ. */
export function cameraErrorName(error: unknown): string {
  return error instanceof DOMException || error instanceof Error ? error.name : ''
}
