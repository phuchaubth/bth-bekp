import type { CSSProperties, ReactNode } from 'react'
import { useSignatureFrame } from '../../api/branding'
import { frameStyle } from './signatureStyle'

/**
 * Một ô lưới phủ đúng vùng nội dung của hộp, nội dung đặt giữa ô. Hàng và cột `minmax(0, 1fr)` có kích thước theo hộp, không
 * nở theo nội dung, nên ảnh rộng/cao 100% co vừa ô và giữ tỉ lệ (object-fit: contain). Không khai báo hàng thì hàng tự động
 * (`auto`) không thấp hơn chiều cao ảnh tính theo tỉ lệ ảnh và bề rộng ô: ảnh tràn khỏi hộp, che chữ và nút bên dưới.
 */
const centeredCell: CSSProperties = { display: 'grid', gridTemplate: 'minmax(0, 1fr) / minmax(0, 1fr)', placeItems: 'center' }

/**
 * Hộp hiện chữ ký (ví dụ chữ ký mẫu của nhân viên) nằm trong khung chữ ký của bệnh viện. Chữ ký đặt ở vùng trống giữa khung,
 * tránh phần logo phía trên, và co giãn vừa vùng đó. Chưa có khung thì là hộp trắng viền mảnh cao `plainHeight`; chữ ký cũng
 * co giãn vừa hộp.
 */
export function SignatureFrameBox({ children, plainHeight = 120 }: { children: ReactNode; plainHeight?: number }) {
  const frame = useSignatureFrame().data ?? null
  if (!frame) {
    return (
      <div
        style={{
          ...centeredCell,
          height: plainHeight,
          boxSizing: 'border-box',
          padding: 12,
          background: '#ffffff',
          border: '1px solid #e1eaeb',
          borderRadius: 10,
        }}
      >
        {children}
      </div>
    )
  }
  return (
    <div data-testid="signature-frame" style={{ ...frameStyle(frame), position: 'relative' }}>
      <div style={{ ...centeredCell, position: 'absolute', inset: '26% 8% 8% 8%' }}>{children}</div>
    </div>
  )
}
