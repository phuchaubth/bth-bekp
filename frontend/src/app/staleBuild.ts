import { maintenanceStore } from '../api/maintenanceStore'

/** Khóa (sessionStorage của từng thẻ trình duyệt) ghi lúc vừa tự tải lại vì không tải được file giao diện. */
export const STALE_BUILD_RELOAD_KEY = 'consent.staleBuildReloadAt'

/** Vừa tự tải lại trong khoảng này mà vẫn không tải được file thì thôi, không tải lại nữa. */
export const STALE_BUILD_RETRY_MS = 60_000

type SessionStore = Pick<Storage, 'getItem' | 'setItem'>

/**
 * Không tải được file giao diện của một trang (sự kiện `vite:preloadError`). Thường gặp ngay sau khi cài gói mới: máy đang mở
 * phần mềm vẫn chạy giao diện cũ, còn file của các trang chưa mở (tải khi cần) đã được thay bằng file của gói mới; lúc máy chủ
 * đang dừng để cài gói cũng vậy. Thay vì trang "Đã xảy ra lỗi", hiện màn hình "Máy chủ đang tạm dừng" như khi API dừng
 * (quyết định 53): máy chủ trả lời thì tải lại trang, lấy giao diện mới.
 *
 * Trả `false` (để trang lỗi hiện, có nút Tải lại trang) khi vừa tự tải lại mà vẫn lỗi (thư mục giao diện trên máy chủ thiếu
 * file), hoặc khi trình duyệt chặn sessionStorage (không biết đã tải lại chưa): tránh tải lại mãi.
 */
export function handleStaleBuild(storage: SessionStore | null, now: number): boolean {
  if (!storage) {
    return false
  }
  try {
    const last = Number(storage.getItem(STALE_BUILD_RELOAD_KEY) ?? Number.NaN)
    if (Number.isFinite(last) && now >= last && now - last < STALE_BUILD_RETRY_MS) {
      return false
    }
    storage.setItem(STALE_BUILD_RELOAD_KEY, String(now))
  } catch {
    return false
  }
  maintenanceStore.report({ kind: 'unavailable' })
  return true
}

function sessionStore(): SessionStore | null {
  try {
    return window.sessionStorage
  } catch {
    return null
  }
}

/** Theo dõi lỗi tải file giao diện trên cả ứng dụng; trả hàm bỏ theo dõi. */
export function watchStaleBuild(target: Window = window): () => void {
  const listener = () => {
    handleStaleBuild(sessionStore(), Date.now())
  }
  target.addEventListener('vite:preloadError', listener)
  return () => target.removeEventListener('vite:preloadError', listener)
}
