/**
 * Tách chữ của mẫu phiếu song ngữ thành phần tiếng Việt (giữ màu chữ thường) và tiếng Anh (màu xanh).
 * Mẫu ghi tiếng Anh trong ngoặc sau tiếng Việt ("Có (Yes)") hoặc thành dòng riêng ở tiêu đề/phụ đề.
 * Cùng quy tắc với backend/src/Consent.Infrastructure/Pdf/BilingualText.cs (PDF).
 */
export interface TextSegment {
  text: string
  english: boolean
}

const PART_SEPARATOR = ' – '
const QUOTED = /“[^”]*”/g

const isVietnameseLetter = (c: string) => c > '\u007f' && /\p{L}/u.test(c)

/** allowVietnameseWords: cho phép tối đa 1/4 số từ có dấu (tên riêng); cả dòng tiêu đề thì không cho phép. */
export function isEnglish(text: string, allowVietnameseWords = true): boolean {
  const trimmed = text.trim()
  if (!/[a-z]/.test(trimmed)) return false
  // Đơn vị, chữ viết tắt ngắn viết thường ("kg", "cm") không coi là tiếng Anh; "No", "Yes" thì có.
  if (trimmed.length < 4 && !/^[A-Z]/.test(trimmed)) return false
  // Tên riêng trong ngoặc kép (“BV Truyền máu Huyết học”) không tính khi đếm từ có dấu.
  const words = trimmed
    .replace(QUOTED, ' ')
    .split(' ')
    .filter((w) => /\p{L}/u.test(w))
  const vietnamese = words.filter((w) => [...w].some(isVietnameseLetter)).length
  return allowVietnameseWords ? vietnamese * 4 <= words.length : vietnamese === 0
}

function push(segments: TextSegment[], text: string, english: boolean) {
  if (!text) return
  const last = segments.at(-1)
  if (last && last.english === english && text !== '\n' && last.text !== '\n') {
    last.text += text
  } else {
    segments.push({ text, english })
  }
}

function matchingClose(line: string, open: number): number {
  let depth = 0
  for (let i = open; i < line.length; i++) {
    if (line[i] === '(') depth++
    else if (line[i] === ')' && --depth === 0) return i
  }
  return -1
}

function splitParentheses(segments: TextSegment[], line: string) {
  let plain = ''
  let i = 0
  while (i < line.length) {
    const close = line[i] === '(' ? matchingClose(line, i) : -1
    if (close < 0) {
      plain += line[i]
      i++
      continue
    }
    const inner = line.slice(i + 1, close)
    if (isEnglish(inner)) {
      push(segments, plain, false)
      plain = ''
      push(segments, line.slice(i, close + 1), true)
    } else if (inner.includes(PART_SEPARATOR)) {
      plain += '('
      inner.split(PART_SEPARATOR).forEach((part, p) => {
        if (p > 0) plain += PART_SEPARATOR
        if (isEnglish(part)) {
          push(segments, plain, false)
          plain = ''
          push(segments, part, true)
        } else {
          plain += part
        }
      })
      plain += ')'
    } else {
      plain += line.slice(i, close + 1)
    }
    i = close + 1
  }
  push(segments, plain, false)
}

/** @param wholeLines cho phép cả dòng là tiếng Anh (chỉ dùng cho tiêu đề, phụ đề). */
export function splitBilingual(text: string, wholeLines = false): TextSegment[] {
  const segments: TextSegment[] = []
  text.split('\n').forEach((line, index) => {
    if (index > 0) push(segments, '\n', false)
    if (wholeLines && isEnglish(line, false)) push(segments, line, true)
    else splitParentheses(segments, line)
  })
  return segments
}
