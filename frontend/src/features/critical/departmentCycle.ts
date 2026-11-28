import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { nextInCycle } from './criticalUtils'

/** Chạy lướt từng khoa (quyết định 64, 66): mỗi khoa hiện bấy nhiêu giây rồi sang khoa kế tiếp. */
export const CYCLE_SECONDS = [10, 15, 30, 60] as const
export const DEFAULT_CYCLE_SECONDS = 15

/**
 * Khoa đang xem và chạy lướt từng khoa, dùng chung cho dashboard và màn hình theo dõi. Khoa ("khoa") và số giây ("chay") nằm
 * trên địa chỉ trang để lưu dấu trang được (màn hình lớn mở thẳng chế độ chạy lướt). `cycle` là các khoa để chạy lướt (đã xếp),
 * `departmentId` là khoa đang xem. Hết số giây thì sang khoa kế tiếp, hết danh sách thì quay lại khoa đầu; mỗi lần sang khoa (hoặc
 * đổi số giây, tạm dừng) thì hẹn giờ lại từ đầu. Tự chọn khoa thì thôi chạy lướt.
 */
export function useDepartmentCycle(cycle: readonly { id: string }[], departmentId: string | undefined) {
  const [params, setParams] = useSearchParams()
  const seconds = CYCLE_SECONDS.find((s) => String(s) === params.get('chay'))
  const [paused, setPaused] = useState(false)
  const cycleKey = cycle.map((d) => d.id).join(',')

  const update = (changes: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === undefined) next.delete(key)
      else next.set(key, value)
    }
    setParams(next, { replace: true })
  }

  // Hàm chuyển khoa luôn là của lần hiển thị mới nhất để không ghi đè các tham số khác vừa đổi trên địa chỉ trang.
  const advance = useRef(() => {})
  useEffect(() => {
    advance.current = () => update({ khoa: nextInCycle(cycleKey.split(','), departmentId) })
  })
  useEffect(() => {
    if (!seconds || paused || !cycleKey) return
    const timer = window.setTimeout(() => advance.current(), seconds * 1000)
    return () => window.clearTimeout(timer)
  }, [seconds, paused, cycleKey, departmentId])

  return {
    /** Số giây mỗi khoa khi đang chạy lướt; không chạy lướt thì undefined. */
    seconds,
    paused,
    /** Thứ tự khoa đang xem trong lượt (1, 2, …); 0 khi khoa không có trong lượt. */
    positionOf: (id: string | null | undefined) => (id ? cycle.findIndex((d) => d.id === id) + 1 : 0),
    /** Bắt đầu từ khoa đang xem nếu khoa đó có trong lượt, không thì từ khoa đầu. */
    start: () => {
      setPaused(false)
      update({ chay: String(DEFAULT_CYCLE_SECONDS), khoa: cycle.some((d) => d.id === departmentId) ? departmentId : cycle[0]?.id })
    },
    stop: () => {
      setPaused(false)
      update({ chay: undefined })
    },
    togglePause: () => setPaused((value) => !value),
    setSeconds: (value: number) => update({ chay: String(value) }),
    /** Tự chọn khoa (bỏ trống là tất cả): thôi chạy lướt. */
    choose: (id: string | undefined) => update({ khoa: id, chay: undefined }),
    /** Đổi tham số khác trên địa chỉ trang (tháng, kỳ) mà giữ khoa và chạy lướt. */
    update,
  }
}
