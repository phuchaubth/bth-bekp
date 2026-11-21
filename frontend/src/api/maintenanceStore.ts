import { useSyncExternalStore } from 'react'

/** Mã lỗi máy chủ trả khi đang bảo trì (quyết định 53). */
export const MAINTENANCE_ERROR_CODE = 'system.maintenance'

/**
 * Lý do màn hình "Hệ thống đang bảo trì" đang hiện.
 * - `maintenance`: quản trị đang bật chế độ bảo trì (máy chủ trả 503 mã `system.maintenance`, kèm lý do và giờ dự kiến xong).
 *   Hết bảo trì thì màn hình tự đóng, trang và dữ liệu đang nhập vẫn còn để làm tiếp.
 * - `unavailable`: 503 không do API trả (IIS trả khi ứng dụng đang dừng, ví dụ đang cài gói hoặc chạy script xóa dữ liệu chạy
 *   thử). Hết thì tải lại trang, vì giao diện có thể đã lên phiên bản mới.
 */
export interface MaintenanceNotice {
  kind: 'maintenance' | 'unavailable'
  reason: string | null
  expectedEndAtUtc: string | null
  /** Lúc màn hình bảo trì bắt đầu hiện (Date.now()); chỉ tin kết quả kiểm tra lại có sau lúc này. */
  since: number
  /** Đã gặp lúc máy chủ dừng hẳn: hết bảo trì thì tải lại trang. */
  reloadOnEnd: boolean
}

export interface MaintenanceState {
  notice: MaintenanceNotice | null
  /** Đã bấm "Đăng nhập bằng tài khoản quản trị": trang đăng nhập không bị màn hình bảo trì che. */
  adminLogin: boolean
}

export interface MaintenanceReport {
  kind: MaintenanceNotice['kind']
  reason?: string | null
  expectedEndAtUtc?: string | null
}

const initialState: MaintenanceState = { notice: null, adminLogin: false }
let state: MaintenanceState = initialState
const listeners = new Set<() => void>()

function emit(next: MaintenanceState): void {
  state = next
  listeners.forEach((listener) => listener())
}

/** Trạng thái màn hình bảo trì dùng chung cho cả ứng dụng (mọi yêu cầu API đều có thể báo 503). */
export const maintenanceStore = {
  get: (): MaintenanceState => state,

  subscribe(listener: () => void): () => void {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },

  /** Máy chủ trả 503: hiện (hoặc cập nhật) màn hình bảo trì. */
  report(report: MaintenanceReport): void {
    const current = state.notice
    const maintenance = report.kind === 'maintenance'
    const next: MaintenanceNotice = {
      kind: report.kind,
      // 503 của IIS không có lý do: giữ lý do đang hiện (nếu có).
      reason: maintenance ? (report.reason ?? null) : (current?.reason ?? null),
      expectedEndAtUtc: maintenance ? (report.expectedEndAtUtc ?? null) : (current?.expectedEndAtUtc ?? null),
      since: current?.since ?? Date.now(),
      reloadOnEnd: (current?.reloadOnEnd ?? false) || !maintenance,
    }
    if (
      current &&
      current.kind === next.kind &&
      current.reason === next.reason &&
      current.expectedEndAtUtc === next.expectedEndAtUtc &&
      current.reloadOnEnd === next.reloadOnEnd
    ) {
      return
    }
    emit({ ...state, notice: next })
  },

  /** Hết bảo trì (hoặc tài khoản được vào khi đang bảo trì): đóng màn hình bảo trì. */
  clear(): void {
    if (state.notice !== null || state.adminLogin) {
      emit(initialState)
    }
  },

  /** Đăng nhập hoặc làm mới phiên thành công: đóng màn hình, trừ khi cần tải lại trang (máy chủ vừa dừng hẳn). */
  sessionStarted(): void {
    if (state.notice?.reloadOnEnd) {
      if (state.adminLogin) {
        emit({ ...state, adminLogin: false })
      }
      return
    }
    maintenanceStore.clear()
  },

  /** Tải lại trang (hết lúc máy chủ dừng hẳn: giao diện có thể đã lên phiên bản mới). Tách riêng để test thay thế. */
  reloadPage(): void {
    window.location.reload()
  },

  /** Người dùng chọn đăng nhập bằng tài khoản quản trị khi đang bảo trì. */
  startAdminLogin(): void {
    if (!state.adminLogin) {
      emit({ ...state, adminLogin: true })
    }
  },
}

export function useMaintenanceState(): MaintenanceState {
  return useSyncExternalStore(maintenanceStore.subscribe, maintenanceStore.get)
}
