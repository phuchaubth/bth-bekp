import type { CSSProperties, ReactNode } from 'react'
import { useSignatureFrame } from '../../api/branding'
import { frameStyle } from './signatureStyle'

/**
 * Vùng đặt chữ ký: một ô lưới có kích thước cố định (minmax(0, 1fr)), để ảnh chữ ký cao 100% co vừa vùng này thay vì giãn
 * theo cỡ thật của ảnh (ảnh chữ ký mẫu lớn hơn hộp sẽ tràn ra ngoài nếu ô lưới tự co theo nội dung).
 */
const signatureArea: CSSProperties = {
  display: 'grid',
  gridTemplate: 'minmax(0, 1fr) / minmax(0, 1fr)',
  placeItems: 'center',
}

/**
 * Hộp hiện chữ ký (ví dụ chữ ký mẫu của nhân viên) nằm trong khung chữ ký của bệnh viện. Chữ ký đặt ở vùng trống giữa khung,
 * tránh phần logo phía trên, và co giãn vừa vùng đó. Chưa có khung thì là hộp trắng viền mảnh cao `plainHeight`.
 * `maxFrameHeight`: chiều cao tối đa của khung (ví dụ trong hộp thoại, để không phải cuộn trên màn hình thấp).
 */
export function SignatureFrameBox({
  children,
  plainHeight = 120,
  maxFrameHeight,
}: {
  children: ReactNode
  plainHeight?: number
  maxFrameHeight?: string
}) {
  const frame = useSignatureFrame().data ?? null
  if (!frame) {
    return (
      <div
        data-testid="signature-box"
        style={{
          ...signatureArea,
          height: plainHeight,
          boxSizing: 'border-box',
          padding: 12,
          background: '#ffffff',
          border: '1px solid #e1eaeb',
          borderRadius: 10,
          overflow: 'hidden',
        }}
      >
        {children}
      </div>
    )
  }
  return (
    <div data-testid="signature-frame" style={{ ...frameStyle(frame, maxFrameHeight), position: 'relative' }}>
      <div style={{ ...signatureArea, position: 'absolute', inset: '26% 8% 8% 8%' }}>{children}</div>
    </div>
  )
}
