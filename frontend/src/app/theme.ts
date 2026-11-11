import type { ThemeConfig } from 'antd'

/** Giao diện chủ đạo: xanh y tế, bo góc vừa phải, font hệ thống hỗ trợ tiếng Việt. */
export const appTheme: ThemeConfig = {
  token: {
    colorPrimary: '#0b6bcb',
    colorInfo: '#0b6bcb',
    borderRadius: 8,
    fontFamily:
      "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, 'Noto Sans', sans-serif",
  },
  components: {
    Layout: {
      headerBg: '#ffffff',
      siderBg: '#0a2540',
    },
    Menu: {
      darkItemBg: '#0a2540',
      darkSubMenuItemBg: '#081d33',
    },
  },
}
