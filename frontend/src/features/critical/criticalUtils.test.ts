import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Permissions } from '../../auth/permissions'
import {
  clockState,
  countdownText,
  formatCountdown,
  isDisplayOnly,
  minutesBetween,
  readMuted,
  secondsLeft,
  useAlertChime,
  writeMuted,
} from './criticalUtils'

describe('đồng hồ thời hạn kết quả nguy cấp', () => {
  const now = Date.parse('2026-11-27T03:10:00Z')

  it('đếm giây còn lại theo giờ máy chủ; âm là quá hạn', () => {
    expect(secondsLeft('2026-11-27T03:15:30Z', now)).toBe(330)
    expect(secondsLeft('2026-11-27T03:09:00Z', now)).toBe(-60)
  })

  it('còn dưới mốc cảnh báo thì cam, quá hạn thì đỏ', () => {
    expect(clockState(181, 3)).toBe('ok')
    expect(clockState(180, 3)).toBe('ok')
    expect(clockState(179, 3)).toBe('warn')
    expect(clockState(0, 3)).toBe('warn')
    expect(clockState(-1, 3)).toBe('late')
    expect(clockState(10, 0)).toBe('ok')
  })

  it('hiện phút:giây, từ 1 giờ trở lên hiện giờ và phút', () => {
    expect(formatCountdown(425)).toBe('7:05')
    expect(formatCountdown(-130)).toBe('2:10')
    expect(formatCountdown(3 * 3600 + 5 * 60)).toBe('3 giờ 05 phút')
    expect(countdownText(59)).toBe('Còn 0:59')
    expect(countdownText(-61)).toBe('Quá hạn 1:01')
  })

  it('số phút giữa hai mốc làm tròn lên như máy chủ, không âm', () => {
    expect(minutesBetween('2026-11-27T03:01:00Z', '2026-11-27T03:05:00Z')).toBe(4)
    expect(minutesBetween('2026-11-27T03:01:00Z', '2026-11-27T03:05:01Z')).toBe(5)
    expect(minutesBetween('2026-11-27T03:05:00Z', '2026-11-27T03:01:00Z')).toBe(0)
  })
})

describe('tài khoản màn hình theo dõi', () => {
  it('chỉ có quyền màn hình theo dõi là tài khoản màn hình; thêm quyền khác là nhân viên', () => {
    expect(isDisplayOnly([Permissions.CriticalDisplay])).toBe(true)
    expect(isDisplayOnly([Permissions.CriticalDisplay, Permissions.CriticalReceive])).toBe(false)
    expect(isDisplayOnly([])).toBe(false)
    expect(isDisplayOnly(undefined)).toBe(false)
  })
})

describe('tắt tiếng trên máy này', () => {
  afterEach(() => window.localStorage.clear())

  it('lưu trong trình duyệt; trình duyệt chặn lưu trữ thì vẫn chạy (không tắt tiếng)', () => {
    expect(readMuted()).toBe(false)
    writeMuted(true)
    expect(readMuted()).toBe(true)
    writeMuted(false)
    expect(window.localStorage.getItem('critical.muted')).toBeNull()

    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    expect(readMuted()).toBe(false)
  })
})

describe('chuông hồ sơ chờ khoa xác nhận', () => {
  afterEach(() => vi.useRealTimers())

  it('kêu khi có hồ sơ mới, nhắc lại theo chu kỳ khi còn hồ sơ chờ, thôi khi hết hồ sơ', () => {
    vi.useFakeTimers()
    const play = vi.fn()
    const { rerender } = renderHook(({ ids, enabled }) => useAlertChime(ids, enabled, 60, play), {
      initialProps: { ids: ['a'], enabled: true },
    })
    expect(play).toHaveBeenCalledTimes(1)

    act(() => vi.advanceTimersByTime(59_000))
    expect(play).toHaveBeenCalledTimes(1)
    act(() => vi.advanceTimersByTime(1_000))
    expect(play).toHaveBeenCalledTimes(2)

    // Cùng hồ sơ (lần hỏi lại máy chủ): không kêu thêm; có hồ sơ mới: kêu ngay.
    rerender({ ids: ['a'], enabled: true })
    expect(play).toHaveBeenCalledTimes(2)
    rerender({ ids: ['a', 'b'], enabled: true })
    expect(play).toHaveBeenCalledTimes(3)

    // Khoa đã xác nhận hết: không nhắc lại nữa.
    rerender({ ids: [], enabled: true })
    act(() => vi.advanceTimersByTime(120_000))
    expect(play).toHaveBeenCalledTimes(3)
  })

  it('tắt tiếng thì không kêu; bật lại khi còn hồ sơ chờ thì kêu một lần; nhắc lại 0 giây là không nhắc lại', () => {
    vi.useFakeTimers()
    const play = vi.fn()
    const { rerender } = renderHook(({ enabled }) => useAlertChime(['a'], enabled, 0, play), { initialProps: { enabled: false } })
    act(() => vi.advanceTimersByTime(300_000))
    expect(play).not.toHaveBeenCalled()

    rerender({ enabled: true })
    expect(play).toHaveBeenCalledTimes(1)
    act(() => vi.advanceTimersByTime(300_000))
    expect(play).toHaveBeenCalledTimes(1)
  })
})
