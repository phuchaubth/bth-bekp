import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions'
import { safeRedirectPath } from '../auth/paths'
import { filterNavItems, flattenNavItems, navItems } from './navigation'

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

  it('lọc menu theo quyền và bỏ nhóm rỗng', () => {
    const granted = new Set<string>([Permissions.ConsentsRead, Permissions.UsersRead])

    const visible = filterNavItems(navItems, (p) => granted.has(p))

    const labels = visible.map((i) => i.label)
    expect(labels).toEqual(['Tổng quan', 'Phiếu đồng thuận', 'Quản trị'])
    expect(visible.find((i) => i.label === 'Quản trị')!.children!.map((c) => c.label)).toEqual(['Người dùng'])
    expect(filterNavItems(navItems, () => false).map((i) => i.label)).toEqual(['Tổng quan'])
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
