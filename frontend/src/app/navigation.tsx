import {
  AuditOutlined,
  BarChartOutlined,
  DashboardOutlined,
  ExportOutlined,
  FileProtectOutlined,
  FileTextOutlined,
  SettingOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import type { ReactNode } from 'react'

export interface NavItem {
  path: string
  label: string
  icon?: ReactNode
  /** Sprint dự kiến hoàn thành chức năng (hiển thị trên trang đang phát triển). */
  sprint?: number
  children?: NavItem[]
}

/** Menu chính. Dùng chung cho thanh điều hướng và định nghĩa route. */
export const navItems: NavItem[] = [
  { path: '/', label: 'Tổng quan', icon: <DashboardOutlined /> },
  { path: '/phieu-dong-thuan', label: 'Phiếu đồng thuận', icon: <FileProtectOutlined />, sprint: 3 },
  { path: '/nguoi-benh', label: 'Người bệnh', icon: <TeamOutlined />, sprint: 2 },
  { path: '/mau-phieu', label: 'Mẫu phiếu', icon: <FileTextOutlined />, sprint: 3 },
  { path: '/xuat-du-lieu', label: 'Xuất dữ liệu', icon: <ExportOutlined />, sprint: 5 },
  { path: '/bao-cao', label: 'Báo cáo thống kê', icon: <BarChartOutlined />, sprint: 5 },
  { path: '/nhat-ky', label: 'Nhật ký hệ thống', icon: <AuditOutlined />, sprint: 5 },
  {
    path: '/quan-tri',
    label: 'Quản trị',
    icon: <SettingOutlined />,
    children: [
      { path: '/quan-tri/nguoi-dung', label: 'Người dùng', sprint: 1 },
      { path: '/quan-tri/vai-tro', label: 'Vai trò và quyền', sprint: 1 },
      { path: '/quan-tri/khoa-phong', label: 'Khoa/phòng', sprint: 1 },
      { path: '/quan-tri/cau-hinh', label: 'Cấu hình hệ thống', sprint: 4 },
    ],
  },
]

/** Danh sách phẳng các mục có trang riêng (bỏ nhóm cha). */
export function flattenNavItems(items: NavItem[] = navItems): NavItem[] {
  return items.flatMap((item) => (item.children ? flattenNavItems(item.children) : [item]))
}
