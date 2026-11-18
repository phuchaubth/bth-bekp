/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

const apiTarget = process.env.VITE_DEV_API_PROXY ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
    },
  },
  build: {
    rolldownOptions: {
      output: {
        // React (ổn định, ít thay đổi) tách riêng để trình duyệt cache lâu dài. Các thư viện khác (Ant Design,
        // camera QR…) để bundler tự chia theo trang: thành phần chỉ dùng ở trang tải sau sẽ không bị tải ngay từ đầu.
        codeSplitting: {
          groups: [{ name: 'react', test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/, priority: 30 }],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
