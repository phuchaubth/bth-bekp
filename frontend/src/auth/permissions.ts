/** Mã quyền, khớp với backend (Consent.Domain.Authorization.Permissions). */
export const Permissions = {
  UsersRead: 'users.read',
  UsersManage: 'users.manage',
  RolesManage: 'roles.manage',
  DepartmentsManage: 'departments.manage',
  AuditRead: 'audit.read',
  SettingsManage: 'settings.manage',
  PatientsRead: 'patients.read',
  PatientsManage: 'patients.manage',
  TemplatesRead: 'templates.read',
  TemplatesManage: 'templates.manage',
  ConsentsRead: 'consents.read',
  ConsentsCreate: 'consents.create',
  ConsentsSign: 'consents.sign',
  ConsentsRevoke: 'consents.revoke',
  /** Dùng màn hình kiosk cho người bệnh tự làm phiếu (quyết định 24). */
  KioskUse: 'kiosk.use',
  ReportsRead: 'reports.read',
  ExportsRun: 'exports.run',
  ExportsSensitive: 'exports.sensitive',
  /** Lập và gửi báo cáo điều dưỡng hằng ngày của khoa mình (quyết định 31). */
  NursingReportsSubmit: 'nursingreports.submit',
  /** Xem báo cáo điều dưỡng mọi khoa và bảng tổng hợp, in PDF, xuất Excel. */
  NursingReportsRead: 'nursingreports.read',
  /** Phòng Điều dưỡng: mở lại báo cáo, cài khoa báo cáo và số giường, số liệu HIS. */
  NursingReportsManage: 'nursingreports.manage',
  ScopeAllDepartments: 'scope.all_departments',
  /** Phân công kiểm thử tính năng mới, xem và xử lý báo cáo kiểm thử (quyết định 49). */
  TestingManage: 'testing.manage',
  /** Bật/tắt chế độ bảo trì, vẫn vào hệ thống khi đang bảo trì (quyết định 53). */
  SystemMaintenance: 'system.maintenance',
  /** Khoa ghi kết quả nội kiểm đường huyết của khoa mình, gửi sổ tháng (quyết định 57). */
  QcRecord: 'qc.record',
  /** Khoa HSH: xem xét kết quả nội kiểm các khoa, xác nhận sổ tháng. */
  QcReview: 'qc.review',
  /** Khoa HSH: khai báo máy đo, lô que thử, lô dung dịch chứng. */
  QcManage: 'qc.manage',
  /** Xem dashboard nội kiểm mọi khoa, chỉ xem (lãnh đạo, Phòng Điều dưỡng, QLCL; quyết định 59). */
  QcMonitor: 'qc.monitor',
  /** Khoa cận lâm sàng: ghi kết quả nguy cấp và lần báo của khoa mình, hủy hồ sơ báo nhầm (quyết định 60). */
  CriticalRecord: 'critical.record',
  /** Khoa điều trị: xem kết quả nguy cấp gửi đến khoa, xác nhận đã nhận cuộc gọi. */
  CriticalReceive: 'critical.receive',
  /** Bác sĩ: ghi hướng xử trí kết quả nguy cấp gửi đến khoa bằng tài khoản của mình. */
  CriticalTreat: 'critical.treat',
  /** Màn hình theo dõi kết quả nguy cấp tại khoa (chỉ xem, tên viết tắt). */
  CriticalDisplay: 'critical.display',
  /** Xem hồ sơ, sổ, dashboard kết quả nguy cấp mọi khoa (QLCL, KHTH). */
  CriticalMonitor: 'critical.monitor',
  /** QLCL: danh mục kết quả nguy cấp và thông số vận hành. */
  CriticalManage: 'critical.manage',
} as const

export type Permission = (typeof Permissions)[keyof typeof Permissions]

/** Mã vai trò Kiosk: tài khoản máy kiosk chỉ dùng màn hình kiosk. */
export const KIOSK_ROLE_CODE = 'KIOSK'

/** Có quyền, hoặc có một trong các quyền khi truyền danh sách (mục menu mở cho nhiều nhóm người dùng). */
export function hasAnyPermission(permission: string | readonly string[], hasPermission: (permission: string) => boolean): boolean {
  return typeof permission === 'string' ? hasPermission(permission) : permission.some((p) => hasPermission(p))
}
