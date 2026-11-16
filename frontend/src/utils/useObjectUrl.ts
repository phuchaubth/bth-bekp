import { useCallback, useState, useSyncExternalStore } from 'react'

const noop = () => {}

/** Đường dẫn tạm đang dùng của một component: tạo khi React đăng ký theo dõi một blob, thu hồi khi hủy đăng ký. */
function createObjectUrlStore() {
  let url: string | null = null
  return {
    track(blob: Blob | null | undefined, onChange: () => void) {
      if (!blob) return noop
      const created = URL.createObjectURL(blob)
      url = created
      onChange()
      return () => {
        if (url === created) url = null
        URL.revokeObjectURL(created)
      }
    },
    current: () => url,
  }
}

/**
 * Đường dẫn tạm (blob URL) để hiển thị dữ liệu nhị phân đã tải có xác thực (ảnh, PDF); tự thu hồi khi dữ liệu đổi hoặc
 * component bị gỡ.
 *
 * Đường dẫn gắn với lượt đăng ký (subscribe) của React: tạo khi đăng ký, thu hồi khi hủy đăng ký; React luôn đi theo cặp, kể cả
 * khi StrictMode gỡ thử rồi gắn lại component (lần gắn lại có đường dẫn mới). Cách cũ tạo bằng `useMemo` rồi thu hồi trong
 * cleanup của effect: lần gỡ thử thu hồi đúng đường dẫn đang hiển thị nên ảnh vỡ.
 *
 * Trả về null khi không có dữ liệu và khi dữ liệu vừa có lần đầu (đường dẫn được tạo ngay sau lần render đó). Dữ liệu đổi (ví dụ
 * tải lại) thì vẫn trả về đường dẫn cũ, còn hiệu lực, cho tới khi có đường dẫn mới: ảnh đang hiện không bị gỡ rồi vẽ lại.
 */
export function useObjectUrl(blob: Blob | null | undefined): string | null {
  const [store] = useState(createObjectUrlStore)
  const subscribe = useCallback((onChange: () => void) => store.track(blob, onChange), [store, blob])
  const url = useSyncExternalStore(subscribe, store.current)
  return blob ? url : null
}
