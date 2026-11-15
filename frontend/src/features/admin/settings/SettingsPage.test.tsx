import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SystemSettings } from '../../../api/settings'
import { adminSession, baseRoutes, session } from '../../../test/fixtures'
import { json, mockApi, problem } from '../../../test/mockApi'
import { renderApp } from '../../../test/renderApp'

const settings: SystemSettings = {
  hospital: { parentAgency: 'SỞ Y TẾ [TỈNH/THÀNH PHỐ]', name: 'BỆNH VIỆN [TÊN BỆNH VIỆN]', place: '[Địa danh]', updatedAtUtc: null },
  hisEnabled: false,
  logo: null,
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

  describe('logo bệnh viện', () => {
    beforeEach(() => {
      // jsdom không có URL.createObjectURL.
      URL.createObjectURL = vi.fn(() => 'blob:logo-xem-truoc')
      URL.revokeObjectURL = vi.fn()
    })
    afterEach(() => vi.restoreAllMocks())

    const withLogo: SystemSettings = {
      ...settings,
      logo: { contentType: 'image/png', width: 512, height: 512, updatedAtUtc: '2026-11-14T03:00:00Z' },
    }

    it('tải logo PNG lên: gửi ảnh dạng data URL, hiện xem trước và thông tin ảnh', async () => {
      const { calls } = mockApi({
        ...baseRoutes(adminSession()),
        'GET /api/v1/settings': settings,
        'PUT /api/v1/settings/logo': () => json(withLogo),
        'GET /api/v1/settings/logo': () => new Response('PNG-gia-lap', { headers: { 'Content-Type': 'image/png' } }),
      })
      renderApp('/quan-tri/cau-hinh')
      const user = userEvent.setup()

      expect(await screen.findByText('Chưa có logo')).toBeInTheDocument()
      const file = new File([new Uint8Array([137, 80, 78, 71, 13, 10, 26, 10])], 'logo-gia-lap.png', { type: 'image/png' })
      await user.upload(screen.getByLabelText('Chọn file logo'), file)

      expect(await screen.findByRole('img', { name: 'Logo bệnh viện đang dùng' })).toHaveAttribute('src', 'blob:logo-xem-truoc')
      expect(screen.getByText(/PNG 512 × 512 điểm ảnh/)).toBeInTheDocument()
      const put = calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/settings/logo')!
      expect((put.body as { image: string }).image).toMatch(/^data:image\/png;base64,/)
    })

    it('chặn file không phải PNG/JPG ngay trên trình duyệt; lỗi máy chủ hiện tại chỗ', async () => {
      const { calls } = mockApi({
        ...baseRoutes(adminSession()),
        'GET /api/v1/settings': settings,
        'PUT /api/v1/settings/logo': () =>
          problem(400, 'Dữ liệu không hợp lệ.', { code: 'validation', errors: { image: ['Kích thước logo phải từ 32 đến 4000 điểm ảnh mỗi chiều.'] } }),
      })
      renderApp('/quan-tri/cau-hinh')
      const user = userEvent.setup({ applyAccept: false })

      await screen.findByText('Chưa có logo')
      await user.upload(screen.getByLabelText('Chọn file logo'), new File(['GIF89a'], 'logo.gif', { type: 'image/gif' }))
      expect(await screen.findByText('Logo phải là ảnh PNG hoặc JPG.')).toBeInTheDocument()
      expect(calls.some((c) => c.method === 'PUT')).toBe(false)

      await user.upload(screen.getByLabelText('Chọn file logo'), new File([new Uint8Array([1, 2, 3])], 'nho.png', { type: 'image/png' }))
      expect(await screen.findByText('Kích thước logo phải từ 32 đến 4000 điểm ảnh mỗi chiều.')).toBeInTheDocument()
    })

    it('bỏ logo sau khi xác nhận', async () => {
      const { calls } = mockApi({
        ...baseRoutes(adminSession()),
        'GET /api/v1/settings': withLogo,
        'GET /api/v1/settings/logo': () => new Response('PNG-gia-lap', { headers: { 'Content-Type': 'image/png' } }),
        'DELETE /api/v1/settings/logo': () => json(settings),
      })
      renderApp('/quan-tri/cau-hinh')
      const user = userEvent.setup()

      await user.click(await screen.findByRole('button', { name: /Bỏ logo/ }))
      await user.click(await screen.findByRole('button', { name: 'Bỏ logo' }))

      expect(await screen.findByText('Chưa có logo')).toBeInTheDocument()
      expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/settings/logo')).toBe(true)
    })
  })

  it('người không có quyền cấu hình thấy trang 403', async () => {
    mockApi(baseRoutes(session()))
    renderApp('/quan-tri/cau-hinh')

    expect(await screen.findByText('Bạn không có quyền truy cập')).toBeInTheDocument()
  })
})

describe('Kiosk người bệnh tự làm phiếu (quyết định 24)', () => {
  const template = (id: string, code: string, name: string) => ({
    id,
    code,
    name,
    hospitalSignerRoleCode: 'RECEPTIONIST',
    departmentId: null,
    isActive: true,
    publishedVersionNo: 1,
    publishedAtUtc: '2026-11-01T00:00:00Z',
    draftVersionNo: null,
    updatedAtUtc: '2026-11-01T00:00:00Z',
  })

  it('chọn thêm mẫu theo thứ tự làm rồi lưu; báo mẫu đã chọn nhưng không còn dùng được', async () => {
    const { calls } = mockApi({
      ...baseRoutes(adminSession()),
      'GET /api/v1/settings': settings,
      'GET /api/v1/settings/kiosk': {
        templates: [
          { id: 't-bm02', code: 'QT-KNGT-BM02', name: 'Giấy đồng thuận BHYT', problems: [] },
          { id: 't-cu', code: 'CU-01', name: 'Mẫu cũ', problems: ['Mẫu đang ngừng sử dụng.'] },
        ],
        updatedAtUtc: '2026-11-15T02:00:00Z',
      },
      'GET /api/v1/consent-templates': [
        template('t-bm02', 'QT-KNGT-BM02', 'Giấy đồng thuận BHYT'),
        template('t-ttnb', 'TTNB-NGOAITRU', 'Phiếu thông tin người bệnh'),
      ],
      'PUT /api/v1/settings/kiosk': (call) =>
        json({
          templates: (call.body as { templateIds: string[] }).templateIds.map((id) => ({ id, code: id, name: id, problems: [] })),
          updatedAtUtc: '2026-11-15T03:00:00Z',
        }),
    })
    renderApp('/quan-tri/cau-hinh')
    const user = userEvent.setup()

    expect(await screen.findByText('CU-01: Mẫu đang ngừng sử dụng.')).toBeInTheDocument()
    const save = screen.getByRole('button', { name: 'Lưu mẫu kiosk' })
    expect(save).toBeDisabled()

    await user.click(screen.getByRole('combobox', { name: 'Mẫu dùng trên kiosk' }))
    await user.click(await screen.findByTitle('TTNB-NGOAITRU – Phiếu thông tin người bệnh'))
    await user.click(save)

    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ templateIds: ['t-bm02', 't-cu', 't-ttnb'] }))
  })
})
