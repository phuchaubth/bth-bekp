/**
 * Gõ tiếng Việt trên bàn phím màn hình của kiosk: gõ chữ (có sẵn phím ă â đ ê ô ơ ư) rồi chạm phím dấu (sắc, huyền, hỏi,
 * ngã, nặng). Dấu được đặt vào đúng nguyên âm của chữ cuối theo cách bỏ dấu truyền thống ("hòa", "thủy", "quả", "người").
 * Chạm lại đúng dấu đang có thì bỏ dấu. Dễ hơn gõ Telex với người lớn tuổi.
 */
export type Tone = 'sac' | 'huyen' | 'hoi' | 'nga' | 'nang'

export const TONES: { tone: Tone; label: string; ariaLabel: string }[] = [
  { tone: 'sac', label: 'sắc', ariaLabel: 'Dấu sắc' },
  { tone: 'huyen', label: 'huyền', ariaLabel: 'Dấu huyền' },
  { tone: 'hoi', label: 'hỏi', ariaLabel: 'Dấu hỏi' },
  { tone: 'nga', label: 'ngã', ariaLabel: 'Dấu ngã' },
  { tone: 'nang', label: 'nặng', ariaLabel: 'Dấu nặng' },
]

const TONE_MARK: Record<Tone, string> = {
  sac: '́',
  huyen: '̀',
  hoi: '̉',
  nga: '̃',
  nang: '̣',
}

const TONE_MARKS = /[̣̀́̃̉]/gu
/** Dấu mũ, dấu trăng, dấu móc (â ê ô, ă, ơ ư): nguyên âm mang các dấu này được ưu tiên đặt dấu thanh. */
const VOWEL_MODIFIERS = /[̛̂̆]/u
const VOWELS = new Set(['a', 'e', 'i', 'o', 'u', 'y'])

/** Tách một chữ (dạng NFD) thành từng ký tự gốc kèm các dấu đi theo nó. */
function letters(word: string): string[] {
  const result: string[] = []
  for (const char of word.normalize('NFD')) {
    if (/\p{M}/u.test(char) && result.length > 0) {
      result[result.length - 1] += char
    } else {
      result.push(char)
    }
  }
  return result
}

const baseOf = (letter: string) => letter[0].toLowerCase()

/** Vị trí nguyên âm nhận dấu thanh trong chữ, hoặc -1 nếu chữ không có nguyên âm. */
function toneTarget(parts: string[]): number {
  const vowelIndexes = parts.map((letter, index) => (VOWELS.has(baseOf(letter)) ? index : -1)).filter((index) => index >= 0)
  if (vowelIndexes.length === 0) {
    return -1
  }

  // Cụm nguyên âm liền nhau chứa nguyên âm cuối cùng.
  const last = vowelIndexes[vowelIndexes.length - 1]
  let first = last
  while (first > 0 && VOWELS.has(baseOf(parts[first - 1]))) {
    first--
  }

  // "qu", "gi" là phụ âm đầu: u/i không thuộc cụm nguyên âm (quả, giá) — trừ khi chỉ còn nguyên âm đó (gì).
  if (first === 1 && last > first) {
    const initial = baseOf(parts[0])
    const vowel = baseOf(parts[1])
    if ((initial === 'q' && vowel === 'u') || (initial === 'g' && vowel === 'i')) {
      first = 2
    }
  }

  const cluster = Array.from({ length: last - first + 1 }, (_, i) => first + i)
  const modified = cluster.filter((index) => VOWEL_MODIFIERS.test(parts[index]))
  if (modified.length > 0) {
    return modified[modified.length - 1]
  }
  if (last < parts.length - 1) {
    return last // Có phụ âm cuối: dấu ở nguyên âm cuối (toán, hoàng).
  }
  return cluster.length === 3 ? cluster[1] : cluster[0]
}

/** Đặt (hoặc bỏ, nếu chạm lại đúng dấu đang có) dấu thanh cho chữ cuối cùng của <paramref name="text"/>. */
export function applyTone(text: string, tone: Tone): string {
  const start = text.search(/\S+$/u)
  if (start < 0) {
    return text
  }

  const parts = letters(text.slice(start))
  const target = toneTarget(parts)
  if (target < 0) {
    return text
  }

  const hadSameTone = parts.some((letter) => letter.includes(TONE_MARK[tone]))
  const stripped = parts.map((letter) => letter.replace(TONE_MARKS, ''))
  if (!hadSameTone) {
    stripped[target] += TONE_MARK[tone]
  }
  return (text.slice(0, start) + stripped.join('')).normalize('NFC')
}

/** Thêm một ký tự; <paramref name="capitalizeWords"/>: viết hoa chữ cái đầu mỗi từ (họ tên, địa chỉ). */
export function appendChar(text: string, char: string, capitalizeWords = false): string {
  if (char === ' ') {
    return text.length === 0 || text.endsWith(' ') ? text : text + ' '
  }
  const startsWord = text.length === 0 || /\s$/u.test(text)
  return text + (capitalizeWords && startsWord ? char.toLocaleUpperCase('vi') : char)
}

/** Xóa ký tự cuối (cả chữ có dấu). */
export function backspace(text: string): string {
  return Array.from(text.normalize('NFC')).slice(0, -1).join('')
}
