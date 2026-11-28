/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Địa chỉ gốc của API. Để trống khi web và API chạy chung domain (qua reverse proxy). */
  readonly VITE_API_BASE_URL?: string
  /** Số gói (số lần chạy quy trình "Gói triển khai IIS"); quy trình đóng gói truyền vào lúc build (quyết định 67). */
  readonly VITE_APP_PACKAGE?: string
  /** Mã commit đầy đủ của gói (quyết định 67). */
  readonly VITE_APP_COMMIT?: string
  /** Thời điểm đóng gói, UTC, dạng 2026-11-28T07:05:09Z (quyết định 67). */
  readonly VITE_APP_BUILT_AT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
