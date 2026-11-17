import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fakeCameras, installFakeMediaDevices } from '../../test/fakeCamera'
import { QrScanInput } from './QrScanInput'

vi.mock('@zxing/browser', () => import('../../test/fakeCamera').then((m) => m.fakeZxing))

// Dữ liệu giả lập, không phải người thật.
const QR = '079090000999||Trần Văn Giả Lập|15081990|Nam|Số 5 Đường Thử Nghiệm, Phường Mẫu|10102022'

let removeCamera: () => void

beforeEach(() => {
  removeCamera = installFakeMediaDevices()
})

afterEach(() => removeCamera())

async function openCamera(onScan = vi.fn()) {
  const user = userEvent.setup()
  const view = render(<QrScanInput onScan={onScan} />)
  await user.click(screen.getByRole('button', { name: /Quét bằng camera/ }))
  const dialog = await screen.findByRole('dialog')
  await waitFor(() => expect(fakeCameras).toHaveLength(1))
  return { user, view, dialog, camera: fakeCameras[0], onScan }
}

const closeButton = (dialog: HTMLElement) => within(dialog).getByRole('button', { name: /close|đóng/i })

describe('Quét CCCD bằng camera (màn hình nhân viên)', () => {
  // Ant Design 6 vẽ nội dung hộp thoại sau lần vẽ đầu: trước đây camera không bao giờ bật, khung hình đen, không báo lỗi.
  it('camera bật khi hộp thoại hiện ra; đọc được mã thì điền mã, đóng hộp thoại và tắt camera', async () => {
    const { dialog, camera, onScan } = await openCamera()

    expect(dialog).toContainElement(camera.video)
    expect(camera.constraints).toEqual({ video: { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } } })
    // Khung vuông hướng dẫn đưa mã vào vùng giữa (vùng bộ đọc phóng to).
    expect(within(dialog).getByTestId('camera-target')).toHaveStyle({ width: '46%' })
    expect(within(dialog).getByRole('status')).toHaveTextContent('Đang mở camera… Nếu trình duyệt hỏi quyền dùng camera, hãy chọn Cho phép.')

    await act(async () => camera.start())
    expect(within(dialog).getByRole('status')).toHaveTextContent(
      'Đưa mã QR ở mặt trước thẻ CCCD vào khung vuông, cách camera khoảng một gang tay, giữ yên 1–2 giây. Hình bị mờ thì đưa thẻ ra xa hơn một chút.',
    )
    // Webcam máy tính hướng về người dùng: hình lật như soi gương để đưa thẻ đúng hướng.
    expect(camera.video).toHaveStyle({ transform: 'scaleX(-1)' })

    act(() => {
      camera.read(QR)
      camera.read(QR)
    })
    expect(onScan).toHaveBeenCalledTimes(1)
    expect(onScan).toHaveBeenCalledWith(QR)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(camera.stop).toHaveBeenCalled()
  })

  it('camera sau của máy tính bảng, điện thoại: hình không lật', async () => {
    const { camera } = await openCamera()

    await act(async () => camera.start('environment'))

    expect(camera.video.style.transform).toBe('')
  })

  it('trang vẽ lại khi hộp thoại đang mở: camera không bị tắt rồi bật lại, mã đọc được chuyển cho hàm mới nhất', async () => {
    const { view, camera, onScan } = await openCamera()
    const latest = vi.fn()

    view.rerender(<QrScanInput onScan={latest} />)
    await act(async () => camera.start())
    act(() => camera.read(QR))

    expect(fakeCameras).toHaveLength(1)
    expect(camera.stop).toHaveBeenCalledTimes(1)
    expect(latest).toHaveBeenCalledWith(QR)
    expect(onScan).not.toHaveBeenCalled()
  })

  it('đóng hộp thoại khi camera đang chạy thì tắt camera', async () => {
    const { user, dialog, camera } = await openCamera()
    await act(async () => camera.start())

    await user.click(closeButton(dialog))

    await waitFor(() => expect(camera.stop).toHaveBeenCalled())
  })

  it('đóng hộp thoại khi trình duyệt còn đang hỏi quyền: camera tắt ngay khi được mở', async () => {
    const { user, dialog, camera } = await openCamera()

    await user.click(closeButton(dialog))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await act(async () => camera.start())

    expect(camera.stop).toHaveBeenCalled()
  })

  it.each([
    ['NotAllowedError', 'Bạn chưa cho phép trang này dùng camera'],
    ['NotFoundError', 'Không tìm thấy camera trên thiết bị này'],
    ['NotReadableError', 'Camera đang được ứng dụng khác sử dụng'],
    ['AbortError', 'Không mở được camera (AbortError). Hãy dùng máy quét 2D hoặc nhập tay.'],
  ])('không mở được camera (%s): báo lý do', async (name, message) => {
    const { dialog, camera } = await openCamera()

    await act(async () => camera.fail(name))

    expect(await within(dialog).findByText((text) => text.includes(message))).toBeInTheDocument()
    expect(within(dialog).queryByRole('status')).not.toBeInTheDocument()
  })
})
