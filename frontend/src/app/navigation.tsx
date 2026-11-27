import {
  AuditOutlined,
  BarChartOutlined,
  DashboardOutlined,
  ExperimentOutlined,
  ExportOutlined,
  FileProtectOutlined,
  FileTextOutlined,
  MedicineBoxOutlined,
  SettingOutlined,
  TeamOutlined,
} from '@ant-design/icons'
import type { ReactNode } from 'react'
import { Permissions, hasAnyPermission } from '../auth/permissions'

export interface NavItem {
  path: string
  label: string
  /** Tên ngắn trên thanh menu ngang (máy tính); menu điện thoại dùng tên đầy đủ. */
  shortLabel?: string
  icon?: ReactNode
  /** Sprint dự kiến hoàn thành chức năng (hiển thị trên trang đang phát triển). */
  sprint?: number
  /** Quyền cần có để thấy mục menu và mở trang; danh sách là cần một trong các quyền. */
  permission?: string | readonly string[]
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
    // Quyết định 31: khoa lập báo cáo hằng ngày, Phòng Điều dưỡng tổng hợp.
    path: '/dieu-duong',
    label: 'Báo cáo điều dưỡng',
    shortLabel: 'Điều dưỡng',
    icon: <MedicineBoxOutlined />,
    children: [
      { path: '/dieu-duong/bao-cao-khoa', label: 'Báo cáo của khoa', permission: Permissions.NursingReportsSubmit },
      { path: '/dieu-duong/tong-hop', label: 'Tổng hợp hằng ngày', permission: Permissions.NursingReportsRead },
      // Quyết định 47: số liệu nhiều ngày dạng biểu đồ cho Phòng Điều dưỡng.
      { path: '/dieu-duong/dashboard', label: 'Dashboard', permission: Permissions.NursingReportsRead },
      // Quyết định 48: người lập báo cáo ủy quyền khi vắng; Phòng Điều dưỡng xem mọi ủy quyền.
      {
        path: '/dieu-duong/uy-quyen',
        label: 'Ủy quyền báo cáo',
        permission: [Permissions.NursingReportsSubmit, Permissions.NursingReportsRead],
      },
    ],
  },
  {
    // Quyết định 57: khoa ghi kết quả nội kiểm đường huyết tại giường, Khoa HSH xem xét và xác nhận sổ tháng.
    path: '/noi-kiem',
    label: 'Nội kiểm đường huyết',
    shortLabel: 'Nội kiểm',
    icon: <ExperimentOutlined />,
    children: [
      { path: '/noi-kiem/so', label: 'Sổ nội kiểm', permission: [Permissions.QcRecord, Permissions.QcReview] },
      { path: '/noi-kiem/danh-muc', label: 'Máy đo và lô vật tư', permission: Permissions.QcManage },
    ],
  },
  {
    path: '/quan-tri',
    label: 'Quản trị',
    icon: <SettingOutlined />,
    children: [
      { path: '/quan-tri/nguoi-dung', label: 'Người dùng', permission: Permissions.UsersRead },
      { path: '/quan-tri/vai-tro', label: 'Vai trò và quyền', permission: Permissions.RolesManage },
      { path: '/quan-tri/khoa-phong', label: 'Khoa/phòng', permission: Permissions.DepartmentsManage },
      { path: '/quan-tri/cau-hinh', label: 'Cấu hình hệ thống', permission: Permissions.SettingsManage },
      // Quyết định 53: chế độ bảo trì.
      { path: '/quan-tri/bao-tri', label: 'Bảo trì hệ thống', permission: Permissions.SystemMaintenance },
      // Quyết định 49: phân công kiểm thử tính năng mới, báo cáo kiểm thử.
      { path: '/quan-tri/kiem-thu', label: 'Phân công kiểm thử', permission: Permissions.TestingManage },
    ],
  },
]

/** Lọc menu theo quyền; bỏ nhóm không còn mục con nào. */
export function filterNavItems(items: NavItem[], hasPermission: (permission: string) => boolean): NavItem[] {
  return items.flatMap((item) => {
    if (item.permission && !hasAnyPermission(item.permission, hasPermission)) {
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
