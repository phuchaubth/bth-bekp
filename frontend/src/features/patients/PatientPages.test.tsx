import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { PatientDetail, PatientLookupResult } from '../../api/patients'
import { Permissions } from '../../auth/permissions'
import { baseRoutes, session } from '../../test/fixtures'
import { json, mockApi, problem } from '../../test/mockApi'
import { renderApp } from '../../test/renderApp'

// Dữ liệu giả lập, không phải người thật.
const QR = '036188000125||Phạm Thị Dung|05041988|Nữ|Số 9 Đường Thử Nghiệm, Phường Mẫu|01062021'

const nurse = () =>
  session({
    userName: 'dieuduong.demo',
    fullName: 'Điều dưỡng Demo',
    permissions: [Permissions.PatientsRead, Permissions.PatientsManage, Permissions.ConsentsRead],
  })

const detail = (overrides: Partial<PatientDetail> = {}): PatientDetail => ({
  id: 'p-1',
  patientCode: 'BN88000125',
  fullName: 'Phạm Thị Dung',
  dateOfBirth: '1988-04-05',
  gender: 'Female',
  citizenId: '036188000125',
  oldIdNumber: null,
  citizenIdIssueDate: '2021-06-01',
  permanentAddress: 'Số 9 Đường Thử Nghiệm, Phường Mẫu',
  currentAddress: null,
  phoneNumber: '0900000125',
  ethnicity: 'Kinh',
  occupation: null,
  nationality: 'Việt Nam',
  healthInsuranceNumber: null,
  hisVerified: true,
  hisVerifiedAtUtc: '2026-11-11T02:00:00Z',
  representatives: [],
  createdAtUtc: '2026-11-11T02:00:00Z',
  updatedAtUtc: '2026-11-11T02:00:00Z',
  ...overrides,
})

// Máy chủ mặc định chưa kết nối HIS (baseRoutes); các kịch bản HIS bật cờ này.
const withHis = { 'GET /api/v1/features': { his: true, smartCa: false, patientSource: false } }

const hisFound: PatientLookupResult = {
  existingPatient: null,
  hisStatus: 'Found',
  hisPatient: {
    patientCode: 'BN88000125',
    fullName: null,
    dateOfBirth: null,
    coverageType: 'Dịch vụ',
    healthInsuranceNumber: null,
    healthInsuranceValidTo: null,
    currentEncounter: { encounterCode: 'TN202611110125', admittedAtUtc: null, departmentName: 'Khoa Khám bệnh', clinicName: 'Phòng khám Nội 1' },
  },
}

async function scan(raw: string) {
  const user = userEvent.setup()
  await user.type(await screen.findByLabelText('Mã QR trên CCCD'), `${raw}{Enter}`)
  return user
}

describe('Tiếp nhận người bệnh (màn hình 4)', () => {
  it('quét CCCD bằng máy quét 2D: tự điền, tra cứu HIS và lưu hồ sơ', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      ...withHis,
      'POST /api/v1/patients/lookup': hisFound,
      'POST /api/v1/patients': () => json(detail(), 201),
      'GET /api/v1/patients/p-1': detail(),
    })
    const { router } = renderApp('/nguoi-benh/tiep-nhan')

    const user = await scan(QR)

    expect(await screen.findByText('Đã tìm thấy người bệnh trên HIS')).toBeInTheDocument()
    expect(screen.getByText('TN202611110125')).toBeInTheDocument()
    expect(screen.getByLabelText('Họ và tên')).toHaveValue('Phạm Thị Dung')
    expect(screen.getByLabelText('Số CCCD')).toHaveValue('036188000125')
    expect(calls.find((c) => c.path === '/api/v1/patients/lookup')!.body).toEqual({ citizenId: '036188000125' })

    await user.type(screen.getByLabelText('Số điện thoại'), '0900000125')
    await user.click(screen.getByRole('button', { name: 'Lưu hồ sơ' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/nguoi-benh/p-1'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/patients')!.body).toEqual({
      fullName: 'Phạm Thị Dung',
      dateOfBirth: '1988-04-05',
      gender: 'Female',
      citizenId: '036188000125',
      oldIdNumber: null,
      citizenIdIssueDate: '2021-06-01',
      permanentAddress: 'Số 9 Đường Thử Nghiệm, Phường Mẫu',
      currentAddress: null,
      phoneNumber: '0900000125',
      ethnicity: null,
      occupation: null,
      nationality: 'Việt Nam',
      healthInsuranceNumber: null,
      patientCode: 'BN88000125',
    })
  })

  it('mã QR sai định dạng: báo lỗi, không tra cứu', async () => {
    const { calls } = mockApi(baseRoutes(nurse()))
    renderApp('/nguoi-benh/tiep-nhan')

    await scan('khong-phai-ma-cccd')

    expect(await screen.findByText('Không đọc được mã QR')).toBeInTheDocument()
    expect(calls.some((c) => c.path === '/api/v1/patients/lookup')).toBe(false)
  })

  it('người bệnh đã có hồ sơ: cảnh báo và cho mở hồ sơ', async () => {
    mockApi({
      ...baseRoutes(nurse()),
      ...withHis,
      'POST /api/v1/patients/lookup': { ...hisFound, existingPatient: detail() },
      'GET /api/v1/patients/p-1': detail(),
    })
    const { router } = renderApp('/nguoi-benh/tiep-nhan')

    const user = await scan(QR)
    await screen.findByText('Người bệnh đã có hồ sơ trong hệ thống')
    await user.click(screen.getByRole('button', { name: 'Mở hồ sơ' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/nguoi-benh/p-1'))
  })

  it('cập nhật hồ sơ đã có theo CCCD mới quét: thông tin trên thẻ được ưu tiên, giữ thông tin bổ sung', async () => {
    const existing = detail({ permanentAddress: 'Địa chỉ cũ trước khi đổi thẻ', occupation: 'Giáo viên' })
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      ...withHis,
      'POST /api/v1/patients/lookup': { ...hisFound, existingPatient: existing },
      'PUT /api/v1/patients/p-1': detail(),
      'GET /api/v1/patients/p-1': detail(),
    })
    const { router } = renderApp('/nguoi-benh/tiep-nhan')

    const user = await scan(QR)
    await screen.findByText('Người bệnh đã có hồ sơ trong hệ thống')
    await user.click(screen.getByRole('button', { name: 'Cập nhật hồ sơ theo CCCD' }))
    expect(screen.getByText('Đang cập nhật hồ sơ đã có')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Lưu thay đổi' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/nguoi-benh/p-1'))
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/patients')).toBe(false)
    expect(calls.find((c) => c.method === 'PUT' && c.path === '/api/v1/patients/p-1')!.body).toMatchObject({
      permanentAddress: 'Số 9 Đường Thử Nghiệm, Phường Mẫu',
      occupation: 'Giáo viên',
      phoneNumber: '0900000125',
      patientCode: 'BN88000125',
    })
  })

  it('HIS không khả dụng: vẫn cho nhập tay', async () => {
    mockApi({
      ...baseRoutes(nurse()),
      ...withHis,
      'POST /api/v1/patients/lookup': { existingPatient: null, hisStatus: 'Unavailable', hisPatient: null },
    })
    renderApp('/nguoi-benh/tiep-nhan')

    await scan(QR)

    expect(await screen.findByText('Không kết nối được HIS')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Lưu hồ sơ' })).toBeEnabled()
  })

  it('trùng số CCCD khi lưu: hiển thị liên kết tới hồ sơ đã có', async () => {
    mockApi({
      ...baseRoutes(nurse()),
      ...withHis,
      'POST /api/v1/patients/lookup': { existingPatient: null, hisStatus: 'NotFound', hisPatient: null },
      'POST /api/v1/patients': () =>
        problem(409, 'Đã có hồ sơ người bệnh với số CCCD này.', { code: 'patients.duplicate_citizen_id', existingPatientId: 'p-9' }),
    })
    renderApp('/nguoi-benh/tiep-nhan')

    const user = await scan(QR)
    await screen.findByText('Chưa có thông tin người bệnh này trên HIS')
    await user.click(screen.getByRole('button', { name: 'Lưu hồ sơ' }))

    expect(await screen.findByText('Số CCCD này đã có hồ sơ')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Mở hồ sơ đã có' })).toHaveAttribute('href', '/nguoi-benh/p-9')
  })

  it('camera không dùng được khi không có HTTPS: hướng dẫn dùng máy quét hoặc nhập tay', async () => {
    mockApi(baseRoutes(nurse()))
    renderApp('/nguoi-benh/tiep-nhan')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Quét bằng camera/ }))

    expect(await screen.findByText(/Camera chỉ hoạt động khi truy cập qua HTTPS/)).toBeInTheDocument()
  })
})

describe('Chưa kết nối HIS (mặc định)', () => {
  it('tiếp nhận: chỉ tra cứu hồ sơ đã có, không hiển thị thông tin HIS; mã người bệnh nhập tay', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'POST /api/v1/patients/lookup': { existingPatient: null, hisStatus: 'Disabled', hisPatient: null },
      'POST /api/v1/patients': () => json(detail({ patientCode: 'BN-NHAP-TAY', hisVerified: false, hisVerifiedAtUtc: null }), 201),
      'GET /api/v1/patients/p-1': detail({ patientCode: 'BN-NHAP-TAY', hisVerified: false, hisVerifiedAtUtc: null }),
    })
    const { router } = renderApp('/nguoi-benh/tiep-nhan')

    const user = await scan(QR)

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/patients/lookup')).toBe(true))
    expect(screen.getByLabelText('Họ và tên')).toHaveValue('Phạm Thị Dung')
    expect(await screen.findByText('Mã người bệnh trên HIS (nếu có), nhập tay.')).toBeInTheDocument()
    for (const hisText of [/Đã tìm thấy người bệnh trên HIS/, /Không kết nối được HIS/, /Chưa có thông tin người bệnh này trên HIS/]) {
      expect(screen.queryByText(hisText)).not.toBeInTheDocument()
    }

    await user.type(screen.getByLabelText('Mã người bệnh'), 'BN-NHAP-TAY')
    await user.click(screen.getByRole('button', { name: 'Lưu hồ sơ' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/nguoi-benh/p-1'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/patients')!.body).toMatchObject({ patientCode: 'BN-NHAP-TAY' })
    expect(await screen.findByText('BN-NHAP-TAY')).toBeInTheDocument()
    expect(screen.queryByText('Đối chiếu HIS')).not.toBeInTheDocument()
  })

  it('số thẻ BHYT: mã mới 17 ký tự lưu bình thường; mã cũ 15 ký tự chỉ cảnh báo nhẹ, vẫn lưu được; dạng khác bị chặn', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'POST /api/v1/patients/lookup': { existingPatient: null, hisStatus: 'Disabled', hisPatient: null },
      'POST /api/v1/patients': () => json(detail({ healthInsuranceNumber: 'GD4790000000125' }), 201),
      'GET /api/v1/patients/p-1': detail({ healthInsuranceNumber: 'GD4790000000125' }),
    })
    const { router } = renderApp('/nguoi-benh/tiep-nhan')
    const user = await scan(QR)
    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/patients/lookup')).toBe(true))
    const input = screen.getByLabelText('Mã thẻ BHYT')

    await user.type(input, '7900000001')
    await user.click(screen.getByRole('button', { name: 'Lưu hồ sơ' }))
    expect(await screen.findByText(/Số thẻ BHYT gồm 17 ký tự/)).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/v1/patients')).toBe(false)

    await user.clear(input)
    await user.type(input, 'gd479036188000125')
    await waitFor(() => expect(screen.queryByText(/Số thẻ BHYT gồm 17 ký tự/)).not.toBeInTheDocument())
    expect(screen.queryByText(/mã thẻ BHYT cũ 15 ký tự/)).not.toBeInTheDocument()

    await user.clear(input)
    await user.type(input, 'gd4790000000125')
    expect(await screen.findByText(/Đây là mã thẻ BHYT cũ 15 ký tự/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Lưu hồ sơ' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/nguoi-benh/p-1'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/v1/patients')!.body).toMatchObject({
      healthInsuranceNumber: 'GD4790000000125',
    })
  })

  it('danh sách: không có cột đối chiếu HIS', async () => {
    mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/patients': {
        items: [
          {
            id: 'p-1',
            patientCode: null,
            fullName: 'Phạm Thị Dung',
            dateOfBirth: '1988-04-05',
            gender: 'Female',
            citizenIdMasked: '0361******25',
            hisVerified: false,
            updatedAtUtc: '2026-11-11T02:00:00Z',
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
      },
    })
    renderApp('/nguoi-benh')

    expect(await screen.findByText('0361******25')).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'HIS' })).not.toBeInTheDocument()
    expect(screen.queryByText('Chưa đối chiếu HIS')).not.toBeInTheDocument()
  })
})

describe('Danh sách và hồ sơ người bệnh (màn hình 9, 10)', () => {
  const list = {
    items: [
      {
        id: 'p-1',
        patientCode: 'BN88000125',
        fullName: 'Phạm Thị Dung',
        dateOfBirth: '1988-04-05',
        gender: 'Female',
        citizenIdMasked: '0361******25',
        hisVerified: true,
        updatedAtUtc: '2026-11-11T02:00:00Z',
      },
      {
        id: 'p-2',
        patientCode: null,
        fullName: 'Lê Minh Cường',
        dateOfBirth: '2010-11-02',
        gender: 'Male',
        citizenIdMasked: null,
        hisVerified: false,
        updatedAtUtc: '2026-11-10T02:00:00Z',
      },
    ],
    page: 1,
    pageSize: 20,
    totalCount: 2,
    totalPages: 1,
  }

  it('hiển thị số CCCD đã che và trạng thái đối chiếu HIS', async () => {
    mockApi({ ...baseRoutes(nurse()), ...withHis, 'GET /api/v1/patients': list })
    renderApp('/nguoi-benh')

    expect(await screen.findByText('0361******25')).toBeInTheDocument()
    expect(await screen.findByText('Đã đối chiếu HIS')).toBeInTheDocument()
    expect(screen.getByText('Chưa đối chiếu HIS')).toBeInTheDocument()
    expect(screen.getByText('05/04/1988')).toBeInTheDocument()
  })

  // Quyết định 41: người bệnh chia theo khoa.
  it('người không xem toàn viện thấy dòng hướng dẫn tìm người bệnh khoa khác', async () => {
    mockApi({ ...baseRoutes(nurse()), 'GET /api/v1/patients': list })
    renderApp('/nguoi-benh')

    expect(
      await screen.findByText(
        'Danh sách chỉ có người bệnh có phiếu ở khoa của bạn, phiếu bạn lập hoặc hồ sơ bạn tiếp nhận. Người bệnh khác: tìm bằng đúng mã người bệnh hoặc đủ 12 số CCCD.',
      ),
    ).toBeInTheDocument()
  })

  it('người xem toàn viện không có dòng hướng dẫn đó', async () => {
    mockApi({
      ...baseRoutes(session({ permissions: [Permissions.PatientsRead, Permissions.ScopeAllDepartments] })),
      'GET /api/v1/patients': list,
    })
    renderApp('/nguoi-benh')

    expect(await screen.findByText('Phạm Thị Dung')).toBeInTheDocument()
    expect(screen.queryByText(/Người bệnh khác: tìm bằng đúng mã/)).not.toBeInTheDocument()
  })

  it('tìm kiếm gửi từ khóa; người chỉ được xem không thấy nút tiếp nhận', async () => {
    const { calls } = mockApi({
      ...baseRoutes(session({ permissions: [Permissions.PatientsRead] })),
      'GET /api/v1/patients': list,
    })
    renderApp('/nguoi-benh')
    const user = userEvent.setup()

    await user.type(await screen.findByPlaceholderText(/Tìm theo họ tên/), 'pham thi dung{Enter}')

    await waitFor(() => expect(calls.some((c) => c.search.includes('keyword=pham+thi+dung'))).toBe(true))
    expect(screen.queryByRole('button', { name: /Tiếp nhận người bệnh/ })).not.toBeInTheDocument()
  })

  it('hồ sơ: số CCCD được che, bấm để xem đầy đủ', async () => {
    mockApi({ ...baseRoutes(nurse()), 'GET /api/v1/patients/p-1': detail() })
    renderApp('/nguoi-benh/p-1')
    const user = userEvent.setup()

    expect(await screen.findByText('0361******25')).toBeInTheDocument()
    expect(screen.queryByText('036188000125')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Hiện số CCCD' }))

    expect(screen.getByText('036188000125')).toBeInTheDocument()
  })

  it('hồ sơ: các phiếu của người bệnh và nút lập phiếu mới cho người bệnh này', async () => {
    const { calls } = mockApi({
      ...baseRoutes(session({ permissions: [Permissions.PatientsRead, Permissions.ConsentsRead, Permissions.ConsentsCreate] })),
      'GET /api/v1/patients/p-1': detail(),
      'GET /api/v1/consent-forms': {
        items: [
          {
            id: 'f-1',
            formNo: 'PDT-2026-000001',
            status: 'PendingSignature',
            templateId: 't-ck',
            templateCode: 'CK-01',
            templateName: 'Giấy cam kết sử dụng dịch vụ',
            templateVersionNo: 1,
            patientId: 'p-1',
            patientName: 'Phạm Thị Dung',
            patientCode: 'BN88000125',
            departmentId: 'd-noi',
            departmentName: 'Khoa Nội tổng hợp',
            createdByName: 'Điều dưỡng Demo',
            createdAtUtc: '2026-11-11T02:00:00Z',
            updatedAtUtc: '2026-11-11T02:00:00Z',
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        totalPages: 1,
      },
    })
    const { router } = renderApp('/nguoi-benh/p-1')
    const user = userEvent.setup()

    expect(await screen.findByRole('link', { name: 'PDT-2026-000001' })).toHaveAttribute('href', '/phieu-dong-thuan/f-1')
    expect(screen.getByText('Chờ ký')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/v1/consent-forms')!.search).toContain('patientId=p-1')

    await user.click(screen.getByRole('button', { name: /Lập phiếu/ }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/phieu-dong-thuan/tao-moi'))
    expect(router.state.location.search).toBe('?patientId=p-1')
  })

  it('hồ sơ: không có quyền với phiếu thì không thấy nút lập phiếu và không tải danh sách phiếu', async () => {
    const { calls } = mockApi({ ...baseRoutes(session({ permissions: [Permissions.PatientsRead] })), 'GET /api/v1/patients/p-1': detail() })
    renderApp('/nguoi-benh/p-1')

    expect(await screen.findByText('Bạn không có quyền xem phiếu.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Lập phiếu/ })).not.toBeInTheDocument()
    expect(calls.some((c) => c.path === '/api/v1/consent-forms')).toBe(false)
  })

  it('thêm người đại diện bằng cách quét CCCD của người đó', async () => {
    const { calls } = mockApi({
      ...baseRoutes(nurse()),
      'GET /api/v1/patients/p-1': detail(),
      'POST /api/v1/patients/p-1/representatives': (call) =>
        detail({
          representatives: [{ id: 'r-1', ...(call.body as object), relationship: 'Spouse' } as PatientDetail['representatives'][0]],
        }),
    })
    renderApp('/nguoi-benh/p-1')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Thêm người đại diện/ }))
    const dialog = await screen.findByRole('dialog')
    await user.type(
      within(dialog).getByLabelText('Mã QR trên CCCD'),
      '036085000777||Phạm Văn Chồng|01011985|Nam|Số 9 Đường Thử Nghiệm|{Enter}',
    )
    expect(within(dialog).getByLabelText('Họ và tên')).toHaveValue('Phạm Văn Chồng')

    await user.click(within(dialog).getByRole('combobox'))
    await user.click(await screen.findByTitle('Vợ/Chồng'))
    await user.click(within(dialog).getByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(calls.some((c) => c.path === '/api/v1/patients/p-1/representatives')).toBe(true))
    expect(calls.find((c) => c.path === '/api/v1/patients/p-1/representatives')!.body).toEqual({
      fullName: 'Phạm Văn Chồng',
      relationship: 'Spouse',
      citizenId: '036085000777',
      dateOfBirth: '1985-01-01',
      gender: 'Male',
      phoneNumber: null,
      address: 'Số 9 Đường Thử Nghiệm',
    })
  })

  describe('lấy thông tin từ hệ thống bệnh viện', () => {
    const withSource = { 'GET /api/v1/features': { his: false, smartCa: false, patientSource: true } }
    const sourcePatient = {
      patientCode: 'BN-GL-001',
      fullName: 'Lê Văn Thử',
      dateOfBirth: '1970-02-03',
      gender: 'Male',
      citizenId: '079070000123',
      phoneNumber: '0900000123',
      permanentAddress: 'Số 1 Đường Giả Lập',
      currentAddress: 'Số 2 Đường Hiện Tại',
      ethnicity: 'Kinh',
      occupation: null,
      nationality: null,
      healthInsuranceNumber: null,
      citizenIdIssueDate: '2021-06-01',
      oldIdNumber: null,
    }

    it('ẩn khi chưa cấu hình', async () => {
      mockApi(baseRoutes(nurse()))
      renderApp('/nguoi-benh/tiep-nhan')

      await screen.findByLabelText('Mã QR trên CCCD')
      expect(screen.queryByText('Lấy thông tin từ hệ thống bệnh viện')).not.toBeInTheDocument()
    })

    it('theo mã người bệnh: điền thông tin hành chính vào form, gửi mã trong thân yêu cầu', async () => {
      const { calls } = mockApi({
        ...baseRoutes(nurse()),
        ...withSource,
        'POST /api/v1/patients/lookup-source': { status: 'Found', patient: sourcePatient, existingPatient: null },
      })
      renderApp('/nguoi-benh/tiep-nhan')
      const user = userEvent.setup()

      await user.type(await screen.findByLabelText('Mã người bệnh cần tra'), 'BN-GL-001')
      await user.click(screen.getByRole('button', { name: /Lấy thông tin/ }))

      await waitFor(() => expect(screen.getByLabelText('Họ và tên')).toHaveValue('Lê Văn Thử'))
      expect(screen.getByLabelText('Số điện thoại')).toHaveValue('0900000123')
      expect(screen.getByLabelText('Nơi thường trú')).toHaveValue('Số 1 Đường Giả Lập')
      expect(screen.getByPlaceholderText('Để trống nếu giống nơi thường trú')).toHaveValue('Số 2 Đường Hiện Tại')
      expect(screen.getByLabelText('Ngày cấp')).toHaveValue('01/06/2021')
      // Đủ thông tin bắt buộc: chỉ báo các ô còn trống để bổ sung nếu có (địa chỉ hiện tại để trống là giống nơi thường trú).
      expect(screen.getByText('Đã lấy thông tin từ hệ thống bệnh viện')).toBeInTheDocument()
      expect(screen.getByText(/Chỉ cần bổ sung nếu có: Nghề nghiệp, Mã thẻ BHYT\./)).toBeInTheDocument()
      expect(calls.find((c) => c.path === '/api/v1/patients/lookup-source')!.body).toEqual({ patientCode: 'BN-GL-001' })
    })

    it('theo CCCD: kiểm tra 12 chữ số, báo hồ sơ đã có', async () => {
      const { calls } = mockApi({
        ...baseRoutes(nurse()),
        ...withSource,
        'POST /api/v1/patients/lookup-source': {
          status: 'Found',
          patient: { ...sourcePatient, citizenId: '036188000125' },
          existingPatient: detail(),
        },
      })
      renderApp('/nguoi-benh/tiep-nhan')
      const user = userEvent.setup()

      await user.click((await screen.findByRole('radio', { name: 'Số CCCD' })).closest('label')!)
      await user.type(screen.getByLabelText('Số CCCD cần tra'), '123')
      await user.click(screen.getByRole('button', { name: /Lấy thông tin/ }))
      expect(await screen.findByText('Số CCCD gồm 12 chữ số.')).toBeInTheDocument()
      expect(calls.some((c) => c.path === '/api/v1/patients/lookup-source')).toBe(false)

      await user.clear(screen.getByLabelText('Số CCCD cần tra'))
      await user.type(screen.getByLabelText('Số CCCD cần tra'), '036188000125{Enter}')

      expect(await screen.findByText('Người bệnh đã có hồ sơ trong hệ thống')).toBeInTheDocument()
      expect(calls.find((c) => c.path === '/api/v1/patients/lookup-source')!.body).toEqual({ citizenId: '036188000125' })
    })

    it('quét CCCD: lấy thêm từ hệ thống bệnh viện, chỉ điền ô còn trống (thông tin trên thẻ được ưu tiên)', async () => {
      const { calls } = mockApi({
        ...baseRoutes(nurse()),
        ...withSource,
        'POST /api/v1/patients/lookup': { existingPatient: null, hisStatus: 'Disabled', hisPatient: null },
        'POST /api/v1/patients/lookup-source': {
          status: 'Found',
          patient: { ...sourcePatient, fullName: 'Tên khác trên hệ thống', gender: null, dateOfBirth: null, citizenId: '036188000125' },
          existingPatient: null,
        },
      })
      renderApp('/nguoi-benh/tiep-nhan')

      await scan(QR)

      await waitFor(() => expect(screen.getByLabelText('Số điện thoại')).toHaveValue('0900000123'))
      expect(screen.getByLabelText('Họ và tên')).toHaveValue('Phạm Thị Dung')
      expect(screen.getByRole('textbox', { name: 'Mã người bệnh' })).toHaveValue('BN-GL-001')
      expect(calls.find((c) => c.path === '/api/v1/patients/lookup-source')!.body).toEqual({ citizenId: '036188000125' })
    })

    it('không tìm thấy hoặc mất kết nối: báo để nhập tay', async () => {
      mockApi({
        ...baseRoutes(nurse()),
        ...withSource,
        'POST /api/v1/patients/lookup-source': { status: 'Unavailable', patient: null, existingPatient: null },
      })
      renderApp('/nguoi-benh/tiep-nhan')
      const user = userEvent.setup()

      await user.type(await screen.findByLabelText('Mã người bệnh cần tra'), 'BN-GL-001{Enter}')

      expect(await screen.findByText(/Chưa kết nối được hệ thống thông tin người bệnh/)).toBeInTheDocument()
    })
  })
})
