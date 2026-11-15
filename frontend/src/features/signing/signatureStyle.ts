import type { CSSProperties } from 'react'
import { signatureFrameImageUrl, type SignatureFrame } from '../../api/branding'

/** Mực xanh dương như bút bi, trùng màu in trên PDF (SignatureImage.InkColor ở máy chủ). */
export const SIGNATURE_INK = '#1d4ed8'

/**
 * Độ dày nét (điểm ảnh CSS): tối thiểu 5 (gấp đôi nét cũ 2,5), to dần theo bề rộng khung (1/120) để chữ ký cân đối với khung
 * trên mọi màn hình, từ điện thoại tới kiosk 32 inch; khi in, nét có độ đậm như nhau dù ký trên máy nào.
 */
export function strokeWidth(frameWidth: number): number {
  return Math.max(5, frameWidth / 120)
}

/** Khung không cao quá nửa màn hình, để còn chỗ cho nút bấm và nội dung khác trong hộp ký. */
const MAX_FRAME_HEIGHT = '50vh'

/**
 * Kiểu dáng chung của ảnh khung chữ ký: giữ đúng tỉ lệ ảnh, ảnh phủ kín khung. Dùng cho khung vẽ chữ ký và khung hiện chữ ký
 * mẫu, để chữ ký luôn nằm trong thiết kế của bệnh viện.
 */
export function frameStyle(frame: SignatureFrame): CSSProperties {
  return {
    width: '100%',
    maxWidth: `calc(${MAX_FRAME_HEIGHT} * ${frame.width / frame.height})`,
    marginInline: 'auto',
    aspectRatio: `${frame.width} / ${frame.height}`,
    backgroundColor: '#ffffff',
    backgroundImage: `url("${signatureFrameImageUrl(frame)}")`,
    backgroundSize: '100% 100%',
    backgroundRepeat: 'no-repeat',
    border: '1px solid #e2e8f0',
    borderRadius: 8,
  }
}
