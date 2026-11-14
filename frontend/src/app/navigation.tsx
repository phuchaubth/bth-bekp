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
import { Permissions } from '../auth/permissions'

export interface NavItem {
  path: string
  label: string
  /** Tên ngắn trên thanh menu ngang (máy tính); menu điện thoại dùng tên đầy đủ. */
  shortLabel?: string
  icon?: ReactNode
  /** Sprint dự kiến hoàn thành chức năng (hiển thị trên trang đang phát triển). */
  sprint?: number
  /** Quyền cần có để thấy mục menu và mở trang. */
  permission?: string
  children?: NavItem[]
}

/** Menu chính. Dùng chung cho thanh điều hướng và định nghĩa route. */
export const navItems: NavItem[] = [
  { path: '/', label: 'Tổng quan', icon: <DashboardOutlined /> },
  { path: '/phieu-dong-thuan', label: 'Phiếu đồng thuận', icon: <FileProtectOutlined />, permission: Permissions.ConsentsRead },
  { path: '/nguoi-benh', label: 'Người bệnh', icon: <TeamOutlined />, permission: Permissions.PatientsRead },
  { path: '/mau-phieu', label: 'Mẫu phiếu', icon: <FileTextOutlined />, permission: Permissions.TemplatesRead },
  { path: '/xuat-du-lieu', label: 'Xuất dữ liệu', icon: <ExportOutlined />, permission: Permissions.ExportsRun },
  { path: '/bao-cao', label: 'Báo cáo thống kê', shortLabel: 'Báo cáo', icon: <BarChartOutlined />, permission: Permissions.ReportsRead },
  { path: '/nhat-ky', label: 'Nhật ký hệ thống', shortLabel: 'Nhật ký', icon: <AuditOutlined />, permission: Permissions.AuditRead },
  {
    path: '/quan-tri',
    label: 'Quản trị',
    icon: <SettingOutlined />,
    children: [
      { path: '/quan-tri/nguoi-dung', label: 'Người dùng', permission: Permissions.UsersRead },
      { path: '/quan-tri/vai-tro', label: 'Vai trò và quyền', permission: Permissions.RolesManage },
      { path: '/quan-tri/khoa-phong', label: 'Khoa/phòng', permission: Permissions.DepartmentsManage },
      { path: '/quan-tri/cau-hinh', label: 'Cấu hình hệ thống', permission: Permissions.SettingsManage },
    ],
  },
]

/** Lọc menu theo quyền; bỏ nhóm không còn mục con nào. */
export function filterNavItems(items: NavItem[], hasPermission: (permission: string) => boolean): NavItem[] {
  return items.flatMap((item) => {
    if (item.permission && !hasPermission(item.permission)) {
      return []
    }
    if (!item.children) {
      return [item]
    }
    const children = filterNavItems(item.children, hasPermission)
    return children.length > 0 ? [{ ...item, children }] : []
  })
}

/** Danh sách phẳng các mục có trang riêng (bỏ nhóm cha). */
export function flattenNavItems(items: NavItem[] = navItems): NavItem[] {
  return items.flatMap((item) => (item.children ? flattenNavItems(item.children) : [item]))
}
