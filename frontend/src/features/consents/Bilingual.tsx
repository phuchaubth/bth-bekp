import { Fragment } from 'react'
import { splitBilingual } from './bilingual'

/** Chữ của mẫu phiếu song ngữ: phần tiếng Anh màu xanh, chữ nghiêng (lớp `bilingual-en`), tiếng Việt giữ màu chữ thường. */
export function Bilingual({ text, wholeLines = false }: { text: string | null | undefined; wholeLines?: boolean }) {
  if (!text) return null
  return (
    <>
      {splitBilingual(text, wholeLines).map((segment, i) =>
        segment.text === '\n' ? (
          <br key={i} />
        ) : segment.english ? (
          <span key={i} className="bilingual-en">
            {segment.text}
          </span>
        ) : (
          <Fragment key={i}>{segment.text}</Fragment>
        ),
      )}
    </>
  )
}
