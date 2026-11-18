import { useState } from 'react'
import { appendChar, applyTone, backspace, TONES } from './vietnameseInput'

interface NumberPadProps {
  /** Chạm một chữ số. */
  onDigit: (digit: string) => void
  onBackspace: () => void
  /** Phím góc phải dưới, ví dụ "Ô tiếp" hoặc "Xóa hết". */
  extraKey?: { label: string; onPress: () => void }
}

/** Bàn phím số lớn (mã y tế, số CCCD, ngày sinh, số điện thoại). */
export function NumberPad({ onDigit, onBackspace, extraKey }: NumberPadProps) {
  return (
    <div className="kiosk-keypad" role="group" aria-label="Bàn phím số">
      {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map((digit) => (
        <button key={digit} type="button" onClick={() => onDigit(digit)}>
          {digit}
        </button>
      ))}
      <button type="button" className="fn" aria-label="Xóa một số" onClick={onBackspace}>
        Xóa
      </button>
      <button type="button" onClick={() => onDigit('0')}>
        0
      </button>
      {extraKey ? (
        <button type="button" className="fn" onClick={extraKey.onPress}>
          {extraKey.label}
        </button>
      ) : (
        <span />
      )}
    </div>
  )
}

interface KeySpec {
  label: string
  ariaLabel?: string
  className?: string
  span?: number
  onPress: () => void
}

interface TextKeyboardProps {
  value: string
  onChange: (value: string) => void
  /** <c>text</c>: chữ tiếng Việt có phím dấu; <c>code</c>: mã y tế (chữ in hoa không dấu và số). */
  variant?: 'text' | 'code'
  /** Viết hoa chữ cái đầu mỗi từ (họ tên, địa chỉ). */
  capitalizeWords?: boolean
  maxLength?: number
  /** Phím xanh cuối bàn phím, ví dụ "Ô tiếp ›" hoặc "Xong". */
  doneLabel?: string
  onDone?: () => void
}

/**
 * Bàn phím màn hình cho kiosk: hàng chữ QWERTY, hàng chữ riêng của tiếng Việt và hàng phím dấu (gõ chữ rồi chạm dấu),
 * phím "123" để nhập số và ký hiệu địa chỉ. Ô nhập đi kèm vẫn gõ được bằng bàn phím rời.
 */
export function TextKeyboard({
  value,
  onChange,
  variant = 'text',
  capitalizeWords = false,
  maxLength,
  doneLabel,
  onDone,
}: TextKeyboardProps) {
  const [symbols, setSymbols] = useState(false)
  const set = (next: string) => onChange(maxLength ? Array.from(next).slice(0, maxLength).join('') : next)
  const type = (char: string) =>
    set(variant === 'code' ? (value + char).toUpperCase() : appendChar(value, char, capitalizeWords))

  const char = (c: string, className?: string): KeySpec => ({
    label: variant === 'code' ? c.toUpperCase() : c,
    className,
    onPress: () => type(c),
  })
  const erase: KeySpec = { label: 'Xóa', ariaLabel: 'Xóa một chữ', className: 'fn', onPress: () => set(backspace(value)) }
  const space = (span: number): KeySpec => ({ label: 'Dấu cách', className: 'space', span, onPress: () => type(' ') })
  const done = (span: number): KeySpec[] =>
    onDone ? [{ label: doneLabel ?? 'Xong', className: 'go', span, onPress: onDone }] : []
  const letters = (row: string) => [...row].map((c) => char(c))

  let rows: KeySpec[][]
  if (variant === 'code') {
    rows = [
      letters('1234567890'),
      letters('qwertyuiop'),
      [...letters('asdfghjkl'), erase],
      [...letters('zxcvbnm'), char('-'), ...done(2)],
    ]
  } else if (symbols) {
    rows = [
      letters('1234567890'),
      [...letters('/,.-()#+'), { ...erase, span: 2 }],
      [
        { label: 'ABC', ariaLabel: 'Trở lại bàn phím chữ', className: 'fn', span: 3, onPress: () => setSymbols(false) },
        space(onDone ? 4 : 7),
        ...done(3),
      ],
    ]
  } else {
    rows = [
      letters('qwertyuiop'),
      [...letters('asdfghjkl'), erase],
      [...letters('zxcvbnm'), ...['ă', 'â', 'đ'].map((c) => char(c, 'vn'))],
      [
        ...['ê', 'ô', 'ơ', 'ư'].map((c) => char(c, 'vn')),
        ...TONES.map(({ tone, label, ariaLabel }) => ({
          label,
          ariaLabel,
          className: 'tone',
          onPress: () => set(applyTone(value, tone)),
        })),
        { label: '123', ariaLabel: 'Chuyển sang số và ký hiệu', className: 'fn', onPress: () => setSymbols(true) },
      ],
      [space(onDone ? 7 : 10), ...done(3)],
    ]
  }

  return (
    <div className="kiosk-keyboard" role="group" aria-label={variant === 'code' ? 'Bàn phím nhập mã' : 'Bàn phím tiếng Việt'}>
      {rows.map((row, index) => (
        <div key={index} className="kiosk-keyboard-row">
          {row.map((key) => (
            <button
              key={key.ariaLabel ?? key.label}
              type="button"
              className={`kiosk-key ${key.className ?? ''}`}
              aria-label={key.ariaLabel}
              style={key.span ? { gridColumn: `span ${key.span}` } : undefined}
              onClick={key.onPress}
            >
              {key.label}
            </button>
          ))}
        </div>
      ))}
    </div>
  )
}
