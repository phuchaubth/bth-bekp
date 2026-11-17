import { BrowserQRCodeReader } from '@zxing/browser'
import { DecodeHintType, type Result } from '@zxing/library'

/** Nghỉ giữa hai lần đọc. Thư viện mặc định nghỉ 500 ms, mỗi giây chỉ thử khoảng 2 khung hình nên camera chậm nhận mã. */
export const SCAN_INTERVAL_MS = 100

/**
 * Cạnh vùng giữa khung hình được phóng to để đọc, tính theo cạnh ngắn của khung hình. Vùng này chứa trọn khung vuông hướng
 * dẫn trên màn hình (46% bề ngang khung hình 4:3).
 */
const ZOOM_REGION = 0.62
/** Cạnh vùng giữa sau khi phóng (điểm ảnh); phóng tối đa 2 lần. */
const ZOOM_SIZE = 960

/** Vùng giữa khung hình cần phóng to (`x`, `y`, `side`: điểm ảnh trên khung hình) và cạnh sau khi phóng (`size`). */
export function zoomRegion(width: number, height: number): { x: number; y: number; side: number; size: number } {
  const side = Math.round(Math.min(width, height) * ZOOM_REGION)
  return {
    x: Math.round((width - side) / 2),
    y: Math.round((height - side) / 2),
    side,
    size: Math.max(side, Math.min(ZOOM_SIZE, side * 2)),
  }
}

/**
 * Bộ đọc mã QR cho CCCD. Mã QR trên thẻ CCCD nhỏ (khoảng 1,4 cm) và dày: qua webcam, mỗi ô của mã chỉ được 2–3 điểm ảnh,
 * thư viện thường không nhận ra. Bộ đọc này:
 * - đọc luân phiên cả khung hình (mã to hoặc lệch khỏi giữa, như mã trên VNeID) và vùng giữa khung hình phóng to (mã nhỏ trên
 *   thẻ);
 * - bật chế độ dò kỹ (`TRY_HARDER`: dò mọi dòng ảnh để không bỏ sót ô định vị nhỏ);
 * - thử khoảng 7 khung hình mỗi giây thay vì 2.
 */
export class CccdQrReader extends BrowserQRCodeReader {
  #zoomNext = false
  #zoomCanvas: HTMLCanvasElement | undefined

  constructor() {
    super(new Map([[DecodeHintType.TRY_HARDER, true]]), { delayBetweenScanAttempts: SCAN_INTERVAL_MS })
  }

  /** Thư viện gọi hàm này với mỗi khung hình camera (đã vẽ lên `canvas`). */
  override decodeFromCanvas(canvas: HTMLCanvasElement): Result {
    this.#zoomNext = !this.#zoomNext
    if (!this.#zoomNext) {
      return super.decodeFromCanvas(canvas)
    }
    const region = zoomRegion(canvas.width, canvas.height)
    const zoom = (this.#zoomCanvas ??= document.createElement('canvas'))
    if (zoom.width !== region.size) {
      zoom.width = region.size
      zoom.height = region.size
    }
    const context = zoom.getContext('2d', { willReadFrequently: true })
    if (!context) {
      return super.decodeFromCanvas(canvas)
    }
    context.drawImage(canvas, region.x, region.y, region.side, region.side, 0, 0, region.size, region.size)
    return super.decodeFromCanvas(zoom)
  }
}
