import { DecodeHintType } from '@zxing/library'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { FakeQrReader } from '../../test/fakeCamera'
import { CccdQrReader, SCAN_INTERVAL_MS, zoomRegion } from './cccdQrReader'

vi.mock('@zxing/browser', () => import('../../test/fakeCamera').then((m) => m.fakeZxing))

afterEach(() => {
  vi.restoreAllMocks()
})

const canvas = (width: number, height: number) => Object.assign(document.createElement('canvas'), { width, height })

describe('vùng giữa khung hình được phóng to để đọc mã nhỏ trên thẻ CCCD', () => {
  it.each([
    // Webcam 1080p, 720p, 480p; khung hình dọc của điện thoại; camera 4K.
    [1920, 1080, { x: 625, y: 205, side: 670, size: 960 }],
    [1280, 720, { x: 417, y: 137, side: 446, size: 892 }],
    [640, 480, { x: 171, y: 91, side: 298, size: 596 }],
    [720, 1280, { x: 137, y: 417, side: 446, size: 892 }],
    [3840, 2160, { x: 1251, y: 411, side: 1339, size: 1339 }],
  ])('khung hình %i×%i', (width, height, expected) => {
    const region = zoomRegion(width, height)

    expect(region).toEqual(expected)
    // Chứa trọn khung vuông hướng dẫn trên màn hình: 46% bề ngang của khung hình hiển thị 4:3.
    expect(region.side).toBeGreaterThanOrEqual(0.46 * (4 / 3) * Math.min(height, (width * 3) / 4))
    // Phóng tối đa 2 lần; không thu nhỏ.
    expect(region.size / region.side).toBeLessThanOrEqual(2)
    expect(region.size).toBeGreaterThanOrEqual(region.side)
  })
})

describe('CccdQrReader', () => {
  it('bật chế độ dò kỹ và nghỉ 100 ms giữa hai lần đọc (thư viện mặc định 500 ms)', () => {
    const reader = new CccdQrReader() as unknown as FakeQrReader

    expect(reader.hints?.get(DecodeHintType.TRY_HARDER)).toBe(true)
    expect(reader.options).toEqual({ delayBetweenScanAttempts: SCAN_INTERVAL_MS })
    expect(SCAN_INTERVAL_MS).toBe(100)
  })

  it('đọc luân phiên: vùng giữa phóng to, rồi cả khung hình', () => {
    const drawImage = vi.fn()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage } as never)
    const reader = new CccdQrReader()
    const frame = canvas(1920, 1080)

    for (let i = 0; i < 4; i++) {
      reader.decodeFromCanvas(frame)
    }

    const [zoomed, full, zoomedAgain, fullAgain] = (reader as unknown as FakeQrReader).decodedCanvases
    expect(zoomed).not.toBe(frame)
    expect(zoomed).toMatchObject({ width: 960, height: 960 })
    expect(drawImage).toHaveBeenCalledWith(frame, 625, 205, 670, 670, 0, 0, 960, 960)
    expect(full).toBe(frame)
    expect(zoomedAgain).toBe(zoomed)
    expect(fullAgain).toBe(frame)
    expect(drawImage).toHaveBeenCalledTimes(2)
  })

  it('đổi cỡ vùng phóng theo cỡ khung hình camera', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage: vi.fn() } as never)
    const reader = new CccdQrReader()

    reader.decodeFromCanvas(canvas(1920, 1080))
    reader.decodeFromCanvas(canvas(1920, 1080))
    reader.decodeFromCanvas(canvas(1280, 720))

    const decoded = (reader as unknown as FakeQrReader).decodedCanvases
    expect(decoded[2]).toBe(decoded[0])
    expect(decoded[2]).toMatchObject({ width: 892, height: 892 })
  })

  it('trình duyệt không vẽ được lên khung phụ: đọc cả khung hình', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null)
    const reader = new CccdQrReader()
    const frame = canvas(1280, 720)

    reader.decodeFromCanvas(frame)

    expect((reader as unknown as FakeQrReader).decodedCanvases).toEqual([frame])
  })
})
