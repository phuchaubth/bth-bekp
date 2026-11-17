import { Fragment } from 'react'
import { splitBilingual } from './bilingual'

/**
 * Chữ của mẫu phiếu song ngữ: phần tiếng Anh màu xanh, chữ nghiêng (lớp `bilingual-en`), tiếng Việt giữ màu chữ thường.
 * Bọc trong một thẻ span: nhãn ô nhập của Ant Design là hộp flex, nếu để rời từng đoạn thì dấu cách trước phần tiếng Anh bị
 * bỏ ("Dân tộc(Ethnicity)").
 */
export function Bilingual({ text, wholeLines = false }: { text: string | null | undefined; wholeLines?: boolean }) {
  if (!text) return null
  return (
    <span>
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
    </span>
  )
}
