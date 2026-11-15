import { useQuery } from '@tanstack/react-query'

/** Khung chữ ký đang dùng: ảnh nền thiết kế của bệnh viện quanh khung ký, do quản trị tải lên ở Cấu hình hệ thống. */
export interface SignatureFrame {
  contentType: 'image/png' | 'image/jpeg'
  width: number
  height: number
  updatedAtUtc: string
}

const SIGNATURE_FRAME = '/api/v1/branding/signature-frame'

/** Địa chỉ ảnh khung, kèm thời điểm cập nhật để trình duyệt tải ảnh mới khi quản trị thay khung. */
export function signatureFrameImageUrl(frame: SignatureFrame): string {
  return `${SIGNATURE_FRAME}/image?v=${encodeURIComponent(frame.updatedAtUtc)}`
}

/**
 * Lấy khung chữ ký. Không cần đăng nhập: điện thoại đang ở chế độ ký và kiosk cũng dùng. Chưa có khung, hoặc không tải được,
 * thì trả về null để khung ký hiện như mặc định; không bao giờ chặn việc ký.
 */
export async function fetchSignatureFrame(signal?: AbortSignal): Promise<SignatureFrame | null> {
  try {
    const response = await fetch(SIGNATURE_FRAME, { signal, headers: { Accept: 'application/json' } })
    if (!response.ok) {
      return null
    }
    const frame = (await response.json()) as SignatureFrame
    return frame.width > 0 && frame.height > 0 ? frame : null
  } catch {
    return null
  }
}

export const brandingKeys = { signatureFrame: ['branding', 'signature-frame'] as const }

export function useSignatureFrame() {
  return useQuery({
    queryKey: brandingKeys.signatureFrame,
    queryFn: ({ signal }) => fetchSignatureFrame(signal),
    staleTime: 5 * 60_000,
    retry: false,
  })
}
