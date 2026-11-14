import type { ThemeConfig } from 'antd'

/** Màu chủ đạo xanh ngọc (phương án giao diện "bố cục C, màu A", 14/11/2026); chữ trắng trên nền này đạt 5:1. */
export const BRAND = '#0d7c74'

/** Nền trang: xám ngả xanh rất nhạt, để các thẻ trắng nổi lên. */
export const PAGE_BG = '#f3f7f8'

/** Giao diện chủ đạo: xanh ngọc, nền sáng, bo góc mềm, font Be Vietnam Pro (đóng gói kèm ứng dụng, thiết kế cho tiếng Việt). */
export const appTheme: ThemeConfig = {
  token: {
    colorPrimary: BRAND,
    colorInfo: BRAND,
    colorLink: BRAND,
    colorSuccess: '#1d7a3a',
    // Nền và viền của thông báo thành công/cảnh báo: nhạt để chữ đen dễ đọc.
    colorSuccessBg: '#ebf6ee',
    colorSuccessBorder: '#b9dfc3',
    colorWarningBg: '#fff6e5',
    colorWarningBorder: '#f3d49a',
    colorInfoBg: '#e8f4f3',
    colorInfoBorder: '#b5dbd7',
    colorWarning: '#b26a00',
    colorBgLayout: PAGE_BG,
    colorBorderSecondary: '#e1eaeb',
    colorText: '#16302e',
    colorTextSecondary: '#55706d',
    borderRadius: 10,
    borderRadiusLG: 14,
    controlHeight: 38,
    fontFamily: "'Be Vietnam Pro', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
  },
  components: {
    Layout: {
      headerBg: '#ffffff',
      headerHeight: 64,
      headerPadding: '0 24px',
      bodyBg: PAGE_BG,
      footerBg: PAGE_BG,
    },
    Menu: {
      horizontalItemSelectedColor: BRAND,
      itemSelectedColor: BRAND,
      itemSelectedBg: '#e2f3f1',
      itemHoverColor: '#0b5f59',
      activeBarHeight: 3,
      horizontalLineHeight: '62px',
    },
    Card: {
      headerFontSize: 16,
    },
    Button: {
      fontWeight: 500,
      primaryShadow: 'none',
    },
  },
}
