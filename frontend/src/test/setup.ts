import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
import { setRefreshHandler } from '../api/http'
import { tokenStore } from '../auth/tokenStore'

// Các trang được tải khi cần (lazy); khi chạy cả bộ test song song có thể mất hơn 1 giây mặc định.
configure({ asyncUtilTimeout: 5000 })

// Vitest ẩn console của test đạt, nên cảnh báo của Ant Design (API lỗi thời, dùng sai thành phần…) dễ bị bỏ sót.
// Gom lại và đánh trượt test để sửa ngay. Không dùng vi.spyOn vì restoreAllMocks() bên dưới sẽ gỡ nó.
const antdWarnings: string[] = []
const consoleError = console.error.bind(console)
console.error = (...args: unknown[]) => {
  if (typeof args[0] === 'string' && args[0].startsWith('Warning: [antd:')) {
    antdWarnings.push(args[0])
  }
  consoleError(...args)
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  tokenStore.set(null)
  setRefreshHandler(null)

  if (antdWarnings.length > 0) {
    const unique = [...new Set(antdWarnings)]
    antdWarnings.length = 0
    throw new Error(`Ant Design cảnh báo:\n${unique.join('\n')}`)
  }
})

// jsdom không có matchMedia; Ant Design (Grid.useBreakpoint, Drawer…) cần API này.
if (!window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: query.includes('min-width'),
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  })
}

if (!window.ResizeObserver) {
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}
