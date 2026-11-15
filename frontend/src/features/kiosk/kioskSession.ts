import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api/http'

/** Bốn bước người bệnh thấy trên đầu màn hình. */
export const KIOSK_STEPS = ['Nhận diện', 'Trả lời', 'Ký tên', 'Xong'] as const

/**
 * Tự về trang đầu khi không ai chạm màn hình trong <paramref name="timeoutMs"/> (quyết định 24: 60 giây), để thông tin
 * của người trước không nằm lại trên máy. Trả về số giây còn lại khi sắp hết giờ (10 giây cuối), để nhắc người bệnh.
 */
export function useIdleReset(active: boolean, timeoutMs: number, onTimeout: () => void): number | null {
  const [secondsLeft, setSecondsLeft] = useState<number | null>(null)
  const last = useRef(0)
  const callback = useRef(onTimeout)
  useEffect(() => {
    callback.current = onTimeout
  }, [onTimeout])

  useEffect(() => {
    if (!active) {
      return
    }
    last.current = Date.now()
    const touch = () => {
      last.current = Date.now()
      setSecondsLeft(null)
    }
    const events = ['pointerdown', 'keydown', 'input'] as const
    events.forEach((name) => window.addEventListener(name, touch, true))
    const interval = window.setInterval(() => {
      const left = timeoutMs - (Date.now() - last.current)
      if (left <= 0) {
        setSecondsLeft(null)
        callback.current()
      } else {
        setSecondsLeft(left <= 10_000 ? Math.ceil(left / 1000) : null)
      }
    }, 500)
    return () => {
      events.forEach((name) => window.removeEventListener(name, touch, true))
      window.clearInterval(interval)
      setSecondsLeft(null)
    }
  }, [active, timeoutMs])

  return active ? secondsLeft : null
}

/** Thông báo lỗi cho người bệnh: thêm số lần thử còn lại khi tra cứu sai. */
export function kioskErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const remaining = error.problem?.remainingAttempts
    if (error.code === 'kiosk.lookup_failed' && typeof remaining === 'number') {
      return `${error.message} Còn ${remaining} lần thử.`
    }
    if (error.status === 0 || error.status >= 500) {
      return 'Máy đang gặp sự cố kết nối. Mời ông/bà thử lại hoặc đến quầy tiếp đón.'
    }
    return error.message
  }
  return 'Máy đang gặp sự cố. Mời ông/bà thử lại hoặc đến quầy tiếp đón.'
}
