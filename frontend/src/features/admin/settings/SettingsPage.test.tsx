import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { SystemSettings } from '../../../api/settings'
import { adminSession, baseRoutes, session } from '../../../test/fixtures'
import { json, mockApi, problem } from '../../../test/mockApi'
import { renderApp } from '../../../test/renderApp'

const settings: SystemSettings = {
  hospital: { parentAgency: 'SỞ Y TẾ [TỈNH/THÀNH PHỐ]', name: 'BỆNH VIỆN [TÊN BỆNH VIỆN]', place: '[Địa danh]', updatedAtUtc: null },
  hisEnabled: false,
  smartCaEnabled: false,
  exportMaxRows: 100000,
  retentionPolicy: 'Theo thời hạn lưu trữ hồ sơ bệnh án; hệ thống không tự động xóa phiếu.',
}

describe('Cấu hình hệ thống (màn hình 19)', () => {
  it('hiển thị giá trị mẫu, lưu tên đơn vị và báo thành công', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/settings': settings,
      'PUT /api/v1/settings/hospital': (call) => {
        const body = call.body as { parentAgency: string; name: string; place: string }
        return json({ ...settings, hospital: { ...body, updatedAtUtc: '2026-11-12T03:00:00Z' } })
      },
    })
    renderApp('/quan-tri/cau-hinh')
    const user = userEvent.setup()

    expect(await screen.findByText(/Đang dùng giá trị mẫu/)).toBeInTheDocument()
    expect(screen.getByText('Chưa kết nối')).toBeInTheDocument()
    expect(screen.getByText('100.000 phiếu')).toBeInTheDocument()

    const name = screen.getByLabelText('Tên bệnh viện')
    await user.clear(name)
    await user.type(name, 'BỆNH VIỆN GIẢ LẬP')
    await user.click(screen.getByRole('button', { name: 'Lưu' }))

    expect(await screen.findByText(/Cập nhật lần cuối/)).toBeInTheDocument()
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.body).toEqual({ parentAgency: 'SỞ Y TẾ [TỈNH/THÀNH PHỐ]', name: 'BỆNH VIỆN GIẢ LẬP', place: '[Địa danh]' })
  })

  it('hiển thị lỗi theo trường từ máy chủ', async () => {
    mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/settings': settings,
      'PUT /api/v1/settings/hospital': () => problem(400, 'Dữ liệu không hợp lệ.', { code: 'validation', errors: { place: ['Vui lòng nhập địa danh.'] } }),
    })
    renderApp('/quan-tri/cau-hinh')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(screen.getByText('Vui lòng nhập địa danh.')).toBeInTheDocument())
  })

  it('người không có quyền cấu hình thấy trang 403', async () => {
    mockApi(baseRoutes(session()))
    renderApp('/quan-tri/cau-hinh')

    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
  })
})
