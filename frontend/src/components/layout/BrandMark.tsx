import { BRAND } from '../../app/theme'

/** Dấu nhận diện: ô vuông xanh ngọc có chữ thập y tế (thanh trên cùng, trang đăng nhập). */
export function BrandMark({ size = 34 }: { size?: number }) {
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
