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
    // Chunk Ant Design (~900 KB, ~290 KB gzip) là thư viện giao diện bắt buộc, được cache lâu dài;
    // mã của ứng dụng nằm ở các chunk nhỏ và trang chức năng được tải khi cần.
    chunkSizeWarningLimit: 1000,
    rolldownOptions: {
      output: {
        // Tách thư viện ra chunk riêng: trình duyệt cache lâu dài, chỉ tải lại mã ứng dụng khi cập nhật.
        codeSplitting: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/, priority: 30 },
            { name: 'antd', test: /node_modules[\\/](antd|@ant-design|@rc-component|rc-[^\\/]+)[\\/]/, priority: 20 },
            { name: 'vendor', test: /node_modules[\\/]/, priority: 10 },
          ],
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
