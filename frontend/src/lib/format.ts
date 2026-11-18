const decimal1 = new Intl.NumberFormat('vi-VN', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const integer = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
const time = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'Asia/Ho_Chi_Minh' });
const clock = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false, timeZone: 'Asia/Ho_Chi_Minh' });
const longDate = new Intl.DateTimeFormat('vi-VN', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'Asia/Ho_Chi_Minh' });

export const formatPercent = (value: number): string => `${decimal1.format(value)}%`;
export const formatNumber = (value: number): string => integer.format(value);
export const formatDecimal = (value: number): string => decimal1.format(value);
export const formatTime = (iso: string): string => time.format(new Date(iso));
export const formatClock = (date: Date): string => clock.format(date);
export const formatLongDate = (date: Date): string => {
  const text = longDate.format(date);
  return text.charAt(0).toUpperCase() + text.slice(1);
};

/** 135 → "2 giờ 15 phút", 42 → "42 phút". */
export function formatMinutes(minutes: number): string {
  const safe = Math.max(0, Math.round(minutes));
  const hours = Math.floor(safe / 60);
  const rest = safe % 60;
  if (hours === 0) {
    return `${rest} phút`;
  }

  return rest === 0 ? `${hours} giờ` : `${hours} giờ ${rest} phút`;
}

/** Elapsed seconds → "mm:ss" (or "h:mm:ss" past one hour). */
export function formatTimer(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(safe / 3600);
  const m = Math.floor((safe % 3600) / 60);
  const s = safe % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}
