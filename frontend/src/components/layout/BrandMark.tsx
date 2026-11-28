import { logoImageUrl, useLogo } from '../../api/branding'
import { BRAND } from '../../app/theme'

/**
 * Dấu nhận diện (thanh trên cùng, trang đăng nhập, trang ký qua mã QR): logo bệnh viện tải lên ở Cấu hình hệ thống (quyết định
 * 61); chưa có logo thì ô vuông xanh ngọc có chữ thập y tế. Logo cao bằng ô, rộng tối đa gấp 2,5 lần (logo ngang vẫn gọn).
 */
export function BrandMark({ size = 34 }: { size?: number }) {
  const logo = useLogo().data
  if (logo) {
    return (
      <img
        src={logoImageUrl(logo)}
        alt=""
        aria-hidden="true"
        style={{ height: size, width: 'auto', maxWidth: Math.round(size * 2.5), objectFit: 'contain', flex: 'none', display: 'block' }}
      />
    )
  }
  return (
    <span
      aria-hidden="true"
      style={{
        width: size,
        height: size,
        borderRadius: Math.round(size * 0.3),
        background: BRAND,
        display: 'grid',
        placeItems: 'center',
        flex: 'none',
      }}
    >
      <svg
        width={Math.round(size * 0.53)}
        height={Math.round(size * 0.53)}
        viewBox="0 0 24 24"
        fill="none"
        stroke="#ffffff"
        strokeWidth="2.2"
        strokeLinejoin="round"
      >
        <path d="M9 3h6v6h6v6h-6v6H9v-6H3V9h6z" />
      </svg>
    </span>
  )
}
