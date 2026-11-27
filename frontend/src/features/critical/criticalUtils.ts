import dayjs from 'dayjs'
import { useEffect, useRef, useState } from 'react'
import { Permissions } from '../../auth/permissions'

/** Hàm và hook dùng chung của kết quả nguy cấp (quyết định 60); tách khỏi tệp thành phần để tệp đó chỉ xuất thành phần. */

/** Màn hình theo dõi kết quả nguy cấp tại khoa (TV): toàn màn hình, không có menu. */
export const SCREEN_PATH = '/man-hinh-kqnc'

/** Trạng thái đồng hồ: còn hạn, sắp quá hạn (còn dưới mốc cảnh báo), quá hạn. */
export type ClockState = 'ok' | 'warn' | 'late'

/** Số giây còn lại tới hạn (âm là đã quá hạn). */
export function secondsLeft(dueUtc: string, nowMs: number): number {
  return Math.floor((Date.parse(dueUtc) - nowMs) / 1000)
}

export function clockState(seconds: number, warnMinutes: number): ClockState {
  if (seconds < 0) return 'late'
  return seconds < warnMinutes * 60 ? 'warn' : 'ok'
}

/** "7:05" (phút:giây); từ 1 giờ trở lên "1 giờ 05 phút". Quá hạn thì lấy giá trị tuyệt đối. */
export function formatCountdown(seconds: number): string {
  const total = Math.abs(seconds)
  if (total >= 3600) {
    return `${Math.floor(total / 3600)} giờ ${String(Math.floor((total % 3600) / 60)).padStart(2, '0')} phút`
  }
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

/** "Còn 7:05" hoặc "Quá hạn 2:10". */
export function countdownText(seconds: number): string {
  return seconds < 0 ? `Quá hạn ${formatCountdown(seconds)}` : `Còn ${formatCountdown(seconds)}`
}

/** Số phút giữa hai mốc, làm tròn lên như máy chủ (dashboard, sổ). */
export function minutesBetween(fromUtc: string, toUtc: string): number {
  return Math.max(0, Math.ceil((Date.parse(toUtc) - Date.parse(fromUtc)) / 60000))
}

/** Giờ:phút theo giờ máy (máy bệnh viện đặt giờ Việt Nam). */
export const timeOf = (utc: string | null | undefined) => (utc ? dayjs(utc).format('HH:mm') : '—')

/** Ngày giờ đầy đủ "dd/MM/yyyy HH:mm". */
export const dateTimeOf = (utc: string | null | undefined) => (utc ? dayjs(utc).format('DD/MM/YYYY HH:mm') : '—')

/** Ngày "dd/MM/yyyy" từ ngày dạng yyyy-MM-dd. */
export const dateOf = (date: string | null | undefined) => (date ? dayjs(date).format('DD/MM/YYYY') : '—')

/** Tuổi tròn năm tại ngày hôm nay (để KTV đối chiếu khi gọi điện). */
export function ageOf(dateOfBirth: string, today = dayjs()): number {
  return today.diff(dayjs(dateOfBirth), 'year')
}

/**
 * Giờ máy chủ, cập nhật mỗi giây: đồng hồ đếm ngược trên giao diện theo giờ máy chủ chứ không theo giờ máy trạm (máy trạm có thể
 * lệch giờ). `receivedAtMs` là lúc nhận câu trả lời có `serverTimeUtc` (dataUpdatedAt của truy vấn).
 */
export function useServerNow(serverTimeUtc: string | undefined, receivedAtMs: number): number {
  const offset = serverTimeUtc && receivedAtMs > 0 ? Date.parse(serverTimeUtc) - receivedAtMs : 0
  return useNow() + offset
}

/** Giờ máy trạm, cập nhật mỗi giây (đo khoảng thời gian từ lần cập nhật cuối). */
export function useNow(): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])
  return now
}

/** Các quyền kết quả nguy cấp của nhân viên (không tính quyền màn hình theo dõi). */
const STAFF_PERMISSIONS = [
  Permissions.CriticalRecord,
  Permissions.CriticalReceive,
  Permissions.CriticalTreat,
  Permissions.CriticalMonitor,
  Permissions.CriticalManage,
] as const

/**
 * Tài khoản chỉ dùng cho màn hình theo dõi tại khoa: chỉ có quyền màn hình theo dõi (vai trò "Màn hình theo dõi KQNC"). Tài
 * khoản này mở trang nào cũng được chuyển về màn hình theo dõi.
 */
export function isDisplayOnly(permissions: readonly string[] | undefined): boolean {
  return Boolean(permissions?.length) && permissions!.every((p) => p === Permissions.CriticalDisplay)
}

export function hasCriticalStaffPermission(hasPermission: (permission: string) => boolean): boolean {
  return STAFF_PERMISSIONS.some((p) => hasPermission(p))
}

// ---- Chuông báo (Web Audio, không cần file âm thanh) ----

let audio: AudioContext | null = null

function audioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null
  const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
  if (!Ctor) return null
  try {
    audio ??= new Ctor()
  } catch {
    return null
  }
  return audio
}

/** Trình duyệt chỉ cho phát âm thanh sau một thao tác của người dùng: gọi hàm này trong sự kiện bấm. */
export async function unlockChime(): Promise<boolean> {
  const context = audioContext()
  if (!context) return false
  if (context.state === 'suspended') {
    try {
      await context.resume()
    } catch {
      return false
    }
  }
  return context.state === 'running'
}

/** Đã phát được âm thanh (người dùng đã bấm một lần trên trang). */
export function chimeReady(): boolean {
  return audio?.state === 'running'
}

/** Chuông ba nốt ngắn (khoảng 1 giây), âm lượng vừa phải; trả false khi trình duyệt chưa cho phát. */
export function playChime(): boolean {
  const context = audioContext()
  if (!context || context.state !== 'running') return false
  const start = context.currentTime + 0.02
  for (const [offset, frequency] of [
    [0, 880],
    [0.32, 660],
    [0.64, 880],
  ] as const) {
    const oscillator = context.createOscillator()
    const gain = context.createGain()
    oscillator.type = 'sine'
    oscillator.frequency.value = frequency
    gain.gain.setValueAtTime(0.0001, start + offset)
    gain.gain.exponentialRampToValueAtTime(0.3, start + offset + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, start + offset + 0.28)
    oscillator.connect(gain).connect(context.destination)
    oscillator.start(start + offset)
    oscillator.stop(start + offset + 0.3)
  }
  return true
}

/**
 * Chuông cho hồ sơ chờ khoa xác nhận: kêu khi có hồ sơ mới (kể cả lần tải đầu, hoặc lúc vừa bật chuông, còn hồ sơ chờ), nhắc lại
 * mỗi `repeatSeconds` giây khi vẫn còn hồ sơ chờ (0 là không nhắc lại).
 */
export function useAlertChime(pendingIds: readonly string[], enabled: boolean, repeatSeconds: number, play: () => void = playChime) {
  const seen = useRef<Set<string> | null>(null)
  const playRef = useRef(play)
  useEffect(() => {
    playRef.current = play
  })
  const key = pendingIds.join(',')
  useEffect(() => {
    if (!enabled) return
    const ids = key ? key.split(',') : []
    const previous = seen.current
    seen.current = new Set(ids)
    if (ids.some((id) => !previous?.has(id))) {
      playRef.current()
    }
  }, [key, enabled])
  useEffect(() => {
    if (!enabled || !key || repeatSeconds <= 0) return
    const timer = window.setInterval(() => playRef.current(), repeatSeconds * 1000)
    return () => window.clearInterval(timer)
  }, [key, enabled, repeatSeconds])
}

// ---- Tắt tiếng trên máy này (tiện ích riêng của trình duyệt, không ảnh hưởng máy khác) ----

const MUTE_KEY = 'critical.muted'

export function readMuted(): boolean {
  try {
    return window.localStorage.getItem(MUTE_KEY) === '1'
  } catch {
    return false
  }
}

export function writeMuted(muted: boolean) {
  try {
    if (muted) {
      window.localStorage.setItem(MUTE_KEY, '1')
    } else {
      window.localStorage.removeItem(MUTE_KEY)
    }
  } catch {
    // Trình duyệt chặn lưu trữ: chỉ tắt tiếng trong lần mở trang này.
  }
}

/** Tháng dạng yyyy-MM của hôm nay. */
export const thisMonth = () => dayjs().format('YYYY-MM')
