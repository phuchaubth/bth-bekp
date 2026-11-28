import {
  AlertOutlined,
  AuditOutlined,
  DashboardOutlined,
  ExperimentOutlined,
  FileProtectOutlined,
  MedicineBoxOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import type { ReactNode } from 'react'
import { Permissions, hasAnyPermission } from '../auth/permissions'

export interface NavItem {
  /** Đường dẫn trang; với nhóm (có children) chỉ là khóa menu, không phải trang. */
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
  {
    // Quyết định 62: các chức năng phiếu đồng thuận chung một menu. Đường dẫn và quyền từng trang giữ nguyên (liên kết đã lưu
    // vẫn mở được); "/dong-thuan" chỉ là khóa của nhóm, không có trang riêng.
    path: '/dong-thuan',
    label: 'Phiếu đồng thuận',
    icon: <FileProtectOutlined />,
    children: [
      { path: '/phieu-dong-thuan', label: 'Danh sách phiếu', permission: Permissions.ConsentsRead },
      { path: '/nguoi-benh', label: 'Người bệnh', permission: Permissions.PatientsRead },
      { path: '/mau-phieu', label: 'Mẫu phiếu', permission: Permissions.TemplatesRead },
      { path: '/xuat-du-lieu', label: 'Xuất dữ liệu', permission: Permissions.ExportsRun },
      { path: '/bao-cao', label: 'Báo cáo thống kê', permission: Permissions.ReportsRead },
    ],
  },
  {
    // Quyết định 31: khoa lập báo cáo hằng ngày, Phòng Điều dưỡng tổng hợp.
    path: '/dieu-duong',
    label: 'Báo cáo điều dưỡng',
    shortLabel: 'Điều dưỡng',
    icon: <MedicineBoxOutlined />,
    children: [
      { path: '/dieu-duong/bao-cao-khoa', label: 'Báo cáo của khoa', permission: Permissions.NursingReportsSubmit },
      // Quyết định 47: số liệu nhiều ngày dạng biểu đồ cho Phòng Điều dưỡng. Quyết định 58: bảng tổng hợp hằng ngày nằm trong
      // Dashboard, bỏ mục "Tổng hợp hằng ngày" (đường dẫn cũ chuyển sang Dashboard, xem routes.tsx).
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
      // Quyết định 59: giám sát nội kiểm; quyền chỉ xem dashboard (lãnh đạo, Phòng Điều dưỡng, QLCL) chỉ thấy mục này.
      {
        path: '/noi-kiem/dashboard',
        label: 'Dashboard',
        permission: [Permissions.QcRecord, Permissions.QcReview, Permissions.QcMonitor],
      },
      { path: '/noi-kiem/danh-muc', label: 'Máy đo và lô vật tư', permission: Permissions.QcManage },
    ],
  },
  {
    // Quyết định 60: khoa cận lâm sàng báo kết quả nguy cấp, khoa điều trị nhận, bác sĩ xử trí, QLCL giám sát. Màn hình theo
    // dõi tại khoa (TV) mở từ trang "Gửi đến khoa tôi", toàn màn hình (routes.tsx).
    path: '/ket-qua-nguy-cap',
    label: 'Kết quả nguy cấp',
    shortLabel: 'KQ nguy cấp',
    icon: <AlertOutlined />,
    children: [
      { path: '/ket-qua-nguy-cap/khoa-bao', label: 'Khoa tôi báo', permission: [Permissions.CriticalRecord, Permissions.CriticalMonitor] },
      {
        path: '/ket-qua-nguy-cap/khoa-nhan',
        label: 'Gửi đến khoa tôi',
        permission: [Permissions.CriticalReceive, Permissions.CriticalTreat, Permissions.CriticalMonitor],
      },
      {
        path: '/ket-qua-nguy-cap/so',
        label: 'Sổ kết quả nguy cấp',
        permission: [Permissions.CriticalRecord, Permissions.CriticalReceive, Permissions.CriticalTreat, Permissions.CriticalMonitor],
      },
      { path: '/ket-qua-nguy-cap/dashboard', label: 'Dashboard', permission: Permissions.CriticalMonitor },
      { path: '/ket-qua-nguy-cap/danh-muc', label: 'Danh mục và cấu hình', permission: Permissions.CriticalManage },
    ],
  },
  // Nhật ký ghi thao tác của mọi phân hệ nên đứng riêng, cạnh Quản trị (quyết định 62).
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

/** Trang đang mở là trang của mục menu hoặc trang con của nó (/mau-phieu/... thuộc /mau-phieu, /bao-cao-x thì không). */
export function matchesNavPath(path: string, pathname: string): boolean {
  return path === '/' ? pathname === '/' : pathname === path || pathname.startsWith(`${path}/`)
}

/** Mục menu đang chọn: mục có đường dẫn dài nhất khớp với trang đang mở. */
export function selectedNavKey(items: NavItem[], pathname: string): string {
  return (
    flattenNavItems(items)
      .map((item) => item.path)
      .filter((path) => matchesNavPath(path, pathname))
      .sort((a, b) => b.length - a.length)[0] ?? pathname
  )
}

/** Nhóm mở sẵn trên menu điện thoại: nhóm chứa trang đang mở. */
export function openNavKeys(items: NavItem[], pathname: string): string[] {
  return items
    .filter((item) => item.children?.some((child) => matchesNavPath(child.path, pathname)))
    .map((item) => item.path)
}
