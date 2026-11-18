/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Địa chỉ gốc của API. Để trống khi web và API chạy chung domain (qua reverse proxy). */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
