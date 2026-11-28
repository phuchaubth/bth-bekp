import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions'
import { safeRedirectPath } from '../auth/paths'
import { filterNavItems, flattenNavItems, matchesNavPath, navItems, openNavKeys, selectedNavKey, type NavItem } from './navigation'

/** Mọi khóa menu, kể cả khóa nhóm. */
function allKeys(items: NavItem[]): string[] {
  return items.flatMap((item) => [item.path, ...(item.children ? allKeys(item.children) : [])])
}

describe('navigation', () => {
  it('làm phẳng menu và bỏ các nhóm cha', () => {
    const paths = flattenNavItems().map((item) => item.path)

    expect(paths).toContain('/quan-tri/nguoi-dung')
    expect(paths).not.toContain('/quan-tri')
  })

  it('không có đường dẫn trùng nhau', () => {
    const paths = flattenNavItems(navItems).map((item) => item.path)

    expect(new Set(paths).size).toBe(paths.length)
  })

  it('khóa nhóm không trùng khóa mục nào (menu đánh dấu đúng mục đang mở)', () => {
    const keys = allKeys(navItems)

    expect(new Set(keys).size).toBe(keys.length)
  })

  it('lọc menu theo quyền và bỏ nhóm rỗng', () => {
    const granted = new Set<string>([Permissions.ConsentsRead, Permissions.UsersRead])

    const visible = filterNavItems(navItems, (p) => granted.has(p))

    const labels = visible.map((i) => i.label)
    expect(labels).toEqual(['Tổng quan', 'Phiếu đồng thuận', 'Quản trị'])
    expect(visible.find((i) => i.label === 'Phiếu đồng thuận')!.children!.map((c) => c.label)).toEqual(['Danh sách phiếu'])
    expect(visible.find((i) => i.label === 'Quản trị')!.children!.map((c) => c.label)).toEqual(['Người dùng'])
    expect(filterNavItems(navItems, () => false).map((i) => i.label)).toEqual(['Tổng quan'])
  })

  it('quyết định 62: năm chức năng phiếu đồng thuận chung một menu, đường dẫn và quyền giữ nguyên', () => {
    const consent = navItems.find((i) => i.label === 'Phiếu đồng thuận')!

    expect(consent.children!.map((c) => [c.label, c.path, c.permission])).toEqual([
      ['Danh sách phiếu', '/phieu-dong-thuan', Permissions.ConsentsRead],
      ['Người bệnh', '/nguoi-benh', Permissions.PatientsRead],
      ['Mẫu phiếu', '/mau-phieu', Permissions.TemplatesRead],
      ['Xuất dữ liệu', '/xuat-du-lieu', Permissions.ExportsRun],
      ['Báo cáo thống kê', '/bao-cao', Permissions.ReportsRead],
    ])
    // Khóa nhóm không phải trang: không sinh route, không trùng trang danh sách phiếu.
    expect(flattenNavItems().map((item) => item.path)).not.toContain(consent.path)
    expect(consent.path).not.toBe('/phieu-dong-thuan')
    // Menu cấp trên cùng: Tổng quan, các phân hệ, rồi Nhật ký hệ thống (mọi phân hệ) cạnh Quản trị.
    expect(navItems.map((i) => i.label)).toEqual([
      'Tổng quan',
      'Phiếu đồng thuận',
      'Báo cáo điều dưỡng',
      'Nội kiểm đường huyết',
      'Kết quả nguy cấp',
      'Nhật ký hệ thống',
      'Quản trị',
    ])
  })

  it('quản lý mẫu phiếu chỉ thấy Mẫu phiếu trong nhóm; báo cáo thấy Báo cáo thống kê, Xuất dữ liệu', () => {
    const consent = (granted: string[]) =>
      filterNavItems(navItems, (p) => granted.includes(p)).find((i) => i.label === 'Phiếu đồng thuận')?.children?.map((c) => c.label)

    expect(consent([Permissions.TemplatesRead])).toEqual(['Mẫu phiếu'])
    expect(consent([Permissions.ReportsRead, Permissions.ExportsRun])).toEqual(['Xuất dữ liệu', 'Báo cáo thống kê'])
    expect(consent([Permissions.NursingReportsSubmit])).toBeUndefined()
  })

  it('trang đang mở: chọn đúng mục, mở đúng nhóm, không nhận nhầm đường dẫn có cùng phần đầu', () => {
    expect(matchesNavPath('/mau-phieu', '/mau-phieu')).toBe(true)
    expect(matchesNavPath('/mau-phieu', '/mau-phieu/m-1')).toBe(true)
    expect(matchesNavPath('/bao-cao', '/bao-cao-khac')).toBe(false)
    expect(matchesNavPath('/', '/mau-phieu')).toBe(false)
    expect(matchesNavPath('/', '/')).toBe(true)

    expect(selectedNavKey(navItems, '/mau-phieu/m-1')).toBe('/mau-phieu')
    expect(selectedNavKey(navItems, '/phieu-dong-thuan/tao-moi')).toBe('/phieu-dong-thuan')
    expect(selectedNavKey(navItems, '/nguoi-benh/tiep-nhan')).toBe('/nguoi-benh')
    expect(selectedNavKey(navItems, '/')).toBe('/')
    expect(selectedNavKey(navItems, '/tai-khoan')).toBe('/tai-khoan')

    expect(openNavKeys(navItems, '/nguoi-benh/tiep-nhan')).toEqual(['/dong-thuan'])
    expect(openNavKeys(navItems, '/bao-cao')).toEqual(['/dong-thuan'])
    expect(openNavKeys(navItems, '/dieu-duong/dashboard')).toEqual(['/dieu-duong'])
    expect(openNavKeys(navItems, '/bao-cao-khac')).toEqual([])
    expect(openNavKeys(navItems, '/nhat-ky')).toEqual([])
    expect(openNavKeys(navItems, '/')).toEqual([])
  })

  it('báo cáo điều dưỡng: điều dưỡng thấy báo cáo của khoa, Phòng Điều dưỡng thấy dashboard (có bảng tổng hợp); cả hai thấy ủy quyền', () => {
    const nursing = (granted: string[]) =>
      filterNavItems(navItems, (p) => granted.includes(p)).find((i) => i.label === 'Báo cáo điều dưỡng')?.children?.map((c) => c.label)

    expect(nursing([Permissions.NursingReportsSubmit])).toEqual(['Báo cáo của khoa', 'Ủy quyền báo cáo'])
    // Quyết định 58: không còn mục "Tổng hợp hằng ngày", bảng tổng hợp nằm trong Dashboard.
    expect(nursing([Permissions.NursingReportsRead, Permissions.NursingReportsManage])).toEqual(['Dashboard', 'Ủy quyền báo cáo'])
    expect(flattenNavItems().map((item) => item.path)).not.toContain('/dieu-duong/tong-hop')
    expect(nursing([Permissions.ConsentsRead])).toBeUndefined()
  })

  it('nội kiểm: khoa và Khoa HSH thấy sổ và dashboard; quyền chỉ xem dashboard chỉ thấy Dashboard (quyết định 59)', () => {
    const qc = (granted: string[]) =>
      filterNavItems(navItems, (p) => granted.includes(p)).find((i) => i.label === 'Nội kiểm đường huyết')?.children?.map((c) => c.label)

    expect(qc([Permissions.QcRecord])).toEqual(['Sổ nội kiểm', 'Dashboard'])
    expect(qc([Permissions.QcReview, Permissions.QcManage])).toEqual(['Sổ nội kiểm', 'Dashboard', 'Máy đo và lô vật tư'])
    expect(qc([Permissions.QcMonitor])).toEqual(['Dashboard'])
    expect(qc([Permissions.ConsentsRead])).toBeUndefined()
  })

  it('mục cần một trong nhiều quyền: có một quyền là thấy, không có quyền nào thì ẩn', () => {
    const items = [{ path: '/a', label: 'A', permission: ['x', 'y'] as const }, { path: '/b', label: 'B', permission: [] as const }]

    expect(filterNavItems(items, (p) => p === 'y').map((i) => i.label)).toEqual(['A'])
    expect(filterNavItems(items, () => false).map((i) => i.label)).toEqual([])
  })
})

describe('safeRedirectPath', () => {
  it.each([
    [null, '/'],
    ['/quan-tri/nguoi-dung?page=2', '/quan-tri/nguoi-dung?page=2'],
    ['//trang-gia-mao.example', '/'],
    ['/\\trang-gia-mao.example', '/'],
    ['https://trang-gia-mao.example', '/'],
  ])('%s → %s', (input, expected) => {
    expect(safeRedirectPath(input)).toBe(expected)
  })
})
