import { describe, expect, it } from 'vitest'
import { delegationContents, delegationPeriod } from './nursingReportUtils'

describe('thời gian và nội dung ủy quyền (quyết định 48)', () => {
  it.each([
    ['2026-11-20', '2026-11-20', '20/11/2026'],
    ['2026-11-20', '2026-11-22', '20/11 – 22/11/2026'],
    // Khác năm: ghi đủ năm ở cả hai ngày, không đọc nhầm 28/12/2027.
    ['2026-12-28', '2027-01-03', '28/12/2026 – 03/01/2027'],
  ])('%s đến %s → %s', (startDate, endDate, expected) => {
    expect(delegationPeriod({ startDate, endDate })).toBe(expected)
  })

  it('ghi nội dung theo thứ tự máy chủ trả', () => {
    expect(delegationContents(['report'])).toBe('Báo cáo của khoa')
    expect(delegationContents(['report', 'hisEntry'])).toBe('Báo cáo của khoa, Số liệu HIS khi HIS lỗi')
    expect(delegationContents(['hisEntry'])).toBe('Số liệu HIS khi HIS lỗi')
  })
})
