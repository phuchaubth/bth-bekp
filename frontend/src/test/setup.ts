import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
import { setRefreshHandler } from '../api/http'
import { tokenStore } from '../auth/tokenStore'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  tokenStore.set(null)
  setRefreshHandler(null)
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
