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

/** Thông tin một ảnh nhận diện; chưa có, hoặc không tải được, thì null (không bao giờ chặn việc đang làm). */
async function fetchImageInfo(path: string, signal?: AbortSignal): Promise<SignatureFrame | null> {
  try {
    const response = await fetch(path, { signal, headers: { Accept: 'application/json' } })
    if (!response.ok) {
      return null
    }
    const info = (await response.json()) as SignatureFrame
    return info.width > 0 && info.height > 0 ? info : null
  } catch {
    return null
  }
}

/**
 * Lấy khung chữ ký. Không cần đăng nhập: điện thoại đang ở chế độ ký và kiosk cũng dùng. Chưa có khung, hoặc không tải được,
 * thì trả về null để khung ký hiện như mặc định; không bao giờ chặn việc ký.
 */
export function fetchSignatureFrame(signal?: AbortSignal): Promise<SignatureFrame | null> {
  return fetchImageInfo(SIGNATURE_FRAME, signal)
}

/** Logo bệnh viện đang dùng (tải lên ở Cấu hình hệ thống), cùng dạng thông tin như khung chữ ký. */
export type BrandLogo = SignatureFrame

const LOGO = '/api/v1/branding/logo'

/** Địa chỉ ảnh logo, kèm thời điểm cập nhật để trình duyệt tải ảnh mới khi quản trị thay logo. */
export function logoImageUrl(logo: BrandLogo): string {
  return `${LOGO}/image?v=${encodeURIComponent(logo.updatedAtUtc)}`
}

/**
 * Logo phần mềm trên web (quyết định 61): logo bệnh viện ở thanh trên, trang đăng nhập, trang ký qua mã QR và biểu tượng thẻ
 * trình duyệt. Không cần đăng nhập; chưa có logo thì dùng dấu nhận diện mặc định.
 */
export function fetchLogo(signal?: AbortSignal): Promise<BrandLogo | null> {
  return fetchImageInfo(LOGO, signal)
}

export const brandingKeys = { signatureFrame: ['branding', 'signature-frame'] as const, logo: ['branding', 'logo'] as const }

export function useLogo() {
  return useQuery({
    queryKey: brandingKeys.logo,
    queryFn: ({ signal }) => fetchLogo(signal),
    staleTime: 5 * 60_000,
    retry: false,
  })
}

export function useSignatureFrame() {
  return useQuery({
    queryKey: brandingKeys.signatureFrame,
    queryFn: ({ signal }) => fetchSignatureFrame(signal),
    staleTime: 5 * 60_000,
    retry: false,
  })
}
