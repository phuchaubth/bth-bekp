import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { ConsentTemplateDetail, ConsentTemplateListItem, ConsentTemplateVersion, TemplateDefinition, TemplateVersionStatus } from '../../api/consentTemplates'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, session } from '../../test/fixtures'
import { json, mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Mẫu phiếu giả lập để thử giao diện.
const manager = () => session({ permissions: [Permissions.TemplatesRead, Permissions.TemplatesManage] })
const reader = () => session({ permissions: [Permissions.TemplatesRead] })

const definition: TemplateDefinition = {
  title: 'Phiếu thử nghiệm',
  sections: [
    {
      title: 'Nội dung',
      blocks: [
        {
          type: 'choice',
          key: 'allergy',
          label: 'Có tiền sử dị ứng?',
          options: [
            { value: 'no', label: 'Không' },
            { value: 'yes', label: 'Có' },
          ],
        },
        { type: 'text', key: 'allergyDetail', label: 'Dị ứng với', visibleWhen: { field: 'allergy', in: ['yes'] } },
      ],
    },
  ],
}

const summary = (versionNo: number, status: TemplateVersionStatus) => ({
  versionNo,
  status,
  createdAtUtc: '2026-11-10T02:00:00Z',
  updatedAtUtc: `2026-11-1${versionNo}T02:00:00Z`,
  publishedAtUtc: status === 'Draft' ? null : '2026-11-10T03:00:00Z',
  retiredAtUtc: status === 'Retired' ? '2026-11-11T03:00:00Z' : null,
})

const template = (versions: ConsentTemplateDetail['versions']): ConsentTemplateDetail => ({
  id: 't-1',
  code: 'PT-01',
  name: 'Phiếu thử nghiệm',
  hospitalSignerRoleCode: 'NURSE',
  departmentId: null,
  isActive: true,
  versions,
  createdAtUtc: '2026-11-10T02:00:00Z',
  updatedAtUtc: '2026-11-11T02:00:00Z',
})

const version = (versionNo: number, status: TemplateVersionStatus): ConsentTemplateVersion => ({
  templateId: 't-1',
  templateCode: 'PT-01',
  templateName: 'Phiếu thử nghiệm',
  versionId: `v-${versionNo}`,
  versionNo,
  status,
  definition,
  updatedAtUtc: summary(versionNo, status).updatedAtUtc,
  publishedAtUtc: summary(versionNo, status).publishedAtUtc,
})

/** Máy chủ giả lập có trạng thái: phiên bản 1 đang phát hành, phiên bản 2 là nháp. */
function templateServer() {
  let statuses: Record<number, TemplateVersionStatus> = { 1: 'Published', 2: 'Draft' }
  const detail = () =>
    template(
      Object.entries(statuses)
        .map(([no, status]) => summary(Number(no), status))
        .sort((a, b) => b.versionNo - a.versionNo),
    )
  return {
    'GET /api/v1/consent-templates/t-1': () => detail(),
    'GET /api/v1/consent-templates/t-1/versions/1': () => version(1, statuses[1]),
    'GET /api/v1/consent-templates/t-1/versions/2': () => version(2, statuses[2]),
    'POST /api/v1/consent-templates/t-1/versions/2/publish': () => {
      statuses = { 1: 'Retired', 2: 'Published' }
      return detail()
    },
  }
}

describe('Danh sách mẫu phiếu (màn hình 11)', () => {
  const list: ConsentTemplateListItem[] = [
    {
      id: 't-1',
      code: 'PT-01',
      name: 'Phiếu thử nghiệm',
      hospitalSignerRoleCode: 'NURSE',
      departmentId: null,
      isActive: true,
      publishedVersionNo: 1,
      publishedAtUtc: '2026-11-10T03:00:00Z',
      draftVersionNo: 2,
      updatedAtUtc: '2026-11-11T02:00:00Z',
    },
    {
      id: 't-2',
      code: 'PT-02',
      name: 'Phiếu chưa phát hành',
      hospitalSignerRoleCode: 'DOCTOR',
      departmentId: 'd-noi',
      isActive: false,
      publishedVersionNo: null,
      publishedAtUtc: null,
      draftVersionNo: 1,
      updatedAtUtc: '2026-11-11T02:00:00Z',
    },
  ]

  it('hiển thị phiên bản đang phát hành, bản nháp và trạng thái; xem cả mẫu ngừng dùng', async () => {
    const { calls } = mockApi({ ...baseRoutes(reader()), 'GET /api/v1/consent-templates': list })
    renderApp('/mau-phieu')

    const published = await screen.findByRole('row', { name: /PT-01/ })
    expect(within(published).getByText('Phiên bản 1')).toBeInTheDocument()
    expect(within(published).getByText('Phiên bản 2')).toBeInTheDocument()
    expect(within(published).getByText('Điều dưỡng')).toBeInTheDocument()
    expect(within(published).getByText('Toàn viện')).toBeInTheDocument()

    const unpublished = screen.getByRole('row', { name: /PT-02/ })
    expect(within(unpublished).getByText('Chưa phát hành')).toBeInTheDocument()
    expect(within(unpublished).getByText('Ngừng sử dụng')).toBeInTheDocument()
    expect(within(unpublished).getByText('Một khoa')).toBeInTheDocument()

    expect(calls.find((c) => c.path === '/api/v1/consent-templates')!.search).toBe('?includeInactive=true')
    expect(screen.queryByRole('button', { name: /Thêm mẫu phiếu/ })).not.toBeInTheDocument()
  })

  it('thêm mẫu mới rồi chuyển tới trang mẫu để soạn phiên bản nháp', async () => {
    const { calls } = mockApi({
      ...baseRoutes(manager()),
      'GET /api/v1/consent-templates': list,
      'POST /api/v1/consent-templates': () => json({ ...template([summary(1, 'Draft')]), id: 't-new', code: 'PT-03' }, 201),
    })
    const { router } = renderApp('/mau-phieu')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Thêm mẫu phiếu/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Thêm mẫu phiếu' })
    await user.type(within(dialog).getByLabelText('Mã mẫu'), 'pt-03')
    await user.type(within(dialog).getByLabelText('Tên mẫu'), 'Phiếu mới')
    await user.click(within(dialog).getByRole('button', { name: 'Tạo' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mau-phieu/t-new'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/consent-templates')!.body).toEqual({
      code: 'pt-03',
      name: 'Phiếu mới',
      hospitalSignerRoleCode: 'NURSE',
      departmentId: null,
    })
  })
})

describe('Phiên bản mẫu phiếu (màn hình 12)', () => {
  it('mặc định mở bản nháp; xem trước có điều kiện hiển thị; phát hành sau khi xác nhận', async () => {
    const { calls } = mockApi({ ...baseRoutes(manager()), ...templateServer() })
    renderApp('/mau-phieu/t-1')
    const user = userEvent.setup()

    expect(await screen.findByText('Phiên bản 2 · Nháp')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tạo phiên bản mới/ })).toBeDisabled()

    // Xem trước: trường chỉ hiện khi chọn "Có" (nhãn trường không bắt buộc có thêm chữ "tùy chọn").
    expect(screen.queryByLabelText(/^Dị ứng với/)).not.toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: 'Có' }))
    expect(await screen.findByLabelText(/^Dị ứng với/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /check-circle Phát hành/ }))
    expect(await screen.findByText('Phát hành phiên bản 2?')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Phát hành' }))

    expect(await screen.findByText('Đã phát hành phiên bản 2. Phiếu mới sẽ dùng phiên bản này.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/consent-templates/t-1/versions/2/publish')).toBe(true)
    expect(await screen.findByText('Phiên bản 2 · Đang phát hành')).toBeInTheDocument()
    expect(within(screen.getByRole('row', { name: /^1 / })).getByText('Ngừng dùng')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tạo phiên bản mới/ })).toBeEnabled()
  })

  it('soạn định nghĩa JSON: báo JSON hỏng tại chỗ và hiện từng lỗi máy chủ trả về', async () => {
    mockApi({
      ...baseRoutes(manager()),
      ...templateServer(),
      'PUT /api/v1/consent-templates/t-1/versions/2': () =>
        problem(400, 'Định nghĩa mẫu chưa hợp lệ.', {
          code: 'consent_templates.invalid_definition',
          errors: { definition: ['Mục 1, khối 1: thiếu "key".'] },
        }),
    })
    renderApp('/mau-phieu/t-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: 'Soạn định nghĩa (JSON)' }))
    const editor = screen.getByLabelText('Định nghĩa mẫu (JSON)')
    expect(editor).toHaveValue(JSON.stringify(definition, null, 2))

    await user.clear(editor)
    await user.click(editor)
    await user.paste('{ "title": ')
    await user.click(screen.getByRole('button', { name: 'Lưu phiên bản nháp' }))
    expect(await screen.findByText('Nội dung không phải JSON hợp lệ.')).toBeInTheDocument()

    await user.clear(editor)
    await user.click(editor)
    await user.paste('{ "title": "Phiếu", "sections": [{ "blocks": [{ "type": "text" }] }] }')
    await user.click(screen.getByRole('button', { name: 'Lưu phiên bản nháp' }))
    expect(await screen.findByText('Mục 1, khối 1: thiếu "key".')).toBeInTheDocument()
    expect(screen.getByText('Định nghĩa chưa hợp lệ')).toBeInTheDocument()
  })

  it('sửa thông tin mẫu: đổi vai trò xác nhận cuối có cảnh báo áp dụng ngay, giữ phạm vi khoa', async () => {
    const { calls } = mockApi({
      ...baseRoutes(manager()),
      ...templateServer(),
      'PUT /api/v1/consent-templates/t-1': () => ({ ...template([summary(2, 'Draft'), summary(1, 'Published')]), hospitalSignerRoleCode: 'DOCTOR' }),
    })
    renderApp('/mau-phieu/t-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Sửa thông tin mẫu/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByLabelText('Vai trò xác nhận cuối (Xác nhận và hoàn tất)'))
    await user.click(await screen.findByTitle('Bác sĩ'))
    expect(within(dialog).getByText('Áp dụng ngay cho mọi phiếu chưa hoàn tất của mẫu này')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/consent-templates/t-1')).toBe(true))
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/consent-templates/t-1')!.body).toEqual({
      name: 'Phiếu thử nghiệm',
      hospitalSignerRoleCode: 'DOCTOR',
      isActive: true,
      departmentId: null,
    })
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('cấu hình ký bằng biểu mẫu: thêm bước xác nhận, chỉ ký xác nhận, đặt tên ô ký; lưu vào khối signatures', async () => {
    const { calls } = mockApi({
      ...baseRoutes(manager()),
      ...templateServer(),
      'PUT /api/v1/consent-templates/t-1/versions/2': (request) =>
        ({ ...version(2, 'Draft'), definition: (request.body as { definition: TemplateDefinition }).definition }),
    })
    renderApp('/mau-phieu/t-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: 'Cấu hình ký' }))
    expect(screen.getByText('Không có bước xác nhận trung gian.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Thêm bước xác nhận/ }))
    await user.click(screen.getByLabelText('Vai trò xác nhận'))
    await user.click(await screen.findByTitle('Tiếp đón'))
    await user.type(screen.getByLabelText('Tên ô ký trên phiếu'), 'Người tư vấn (Consultant)')
    await user.click(screen.getByLabelText('Chỉ ký xác nhận (không hỏi)'))
    await user.type(screen.getByLabelText('Ô xác nhận cuối (để trống: “Nhân viên y tế”)'), 'Điều dưỡng khoa (Ward Nurse)')
    await user.click(screen.getByRole('button', { name: 'Lưu cấu hình ký' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/consent-templates/t-1/versions/2')).toBe(true))
    const saved = calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/consent-templates/t-1/versions/2')!.body as {
      definition: TemplateDefinition
    }
    // Giữ nguyên phần nội dung, chỉ thay khối signatures; ô để trống không ghi (dùng nhãn mặc định).
    expect(saved.definition.sections).toEqual(definition.sections)
    expect(saved.definition.signatures).toEqual({
      hospital: 'Điều dưỡng khoa (Ward Nurse)',
      decision: 'none',
      steps: [{ key: 'step1', roleCode: 'RECEPTIONIST', label: 'Người tư vấn (Consultant)' }],
    })
  })

  it('cấu hình ký: bắt buộc chọn vai trò và tên ô ký cho mỗi bước; giữ mã bước cũ khi đổi thứ tự', async () => {
    const withSteps: TemplateDefinition = {
      ...definition,
      signatures: {
        steps: [
          { key: 'screeningStaff', roleCode: 'RECEPTIONIST', label: 'Nhân viên hướng dẫn' },
          { key: 'nurse', roleCode: 'NURSE', label: 'Điều dưỡng thực hiện' },
        ],
      },
    }
    const { calls } = mockApi({
      ...baseRoutes(manager()),
      ...templateServer(),
      'GET /api/v1/consent-templates/t-1/versions/2': () => ({ ...version(2, 'Draft'), definition: withSteps }),
      'PUT /api/v1/consent-templates/t-1/versions/2': (request) => ({
        ...version(2, 'Draft'),
        definition: (request.body as { definition: TemplateDefinition }).definition,
      }),
    })
    renderApp('/mau-phieu/t-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('tab', { name: 'Cấu hình ký' }))
    await user.click(screen.getByRole('button', { name: /Thêm bước xác nhận/ }))
    await user.click(screen.getByRole('button', { name: 'Lưu cấu hình ký' }))
    expect(await screen.findByText('Vui lòng chọn vai trò.')).toBeInTheDocument()
    expect(screen.getByText('Vui lòng nhập tên ô ký.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: /Xóa bước 3/ }))
    await user.click(screen.getByRole('button', { name: /Đưa bước 2 lên/ }))
    await user.click(screen.getByRole('button', { name: 'Lưu cấu hình ký' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    const saved = calls.find((c) => c.method === 'PUT')!.body as { definition: TemplateDefinition }
    expect(saved.definition.signatures?.steps?.map((step) => step.key)).toEqual(['nurse', 'screeningStaff'])
  })

  it('người chỉ được xem: không có nút phát hành hay tạo phiên bản, định nghĩa chỉ đọc', async () => {
    mockApi({ ...baseRoutes(reader()), ...templateServer() })
    renderApp('/mau-phieu/t-1')
    const user = userEvent.setup()

    expect(await screen.findByText('Phiên bản 2 · Nháp')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Phát hành/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Tạo phiên bản mới/ })).not.toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Định nghĩa (JSON)' }))
    expect(screen.getByLabelText('Định nghĩa mẫu (JSON)')).toHaveAttribute('readonly')
    expect(screen.queryByRole('button', { name: 'Lưu phiên bản nháp' })).not.toBeInTheDocument()
  })
})
