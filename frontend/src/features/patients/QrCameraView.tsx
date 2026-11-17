import type { IScannerControls } from '@zxing/browser'
import { useEffect, useEffectEvent, useRef, useState, type CSSProperties } from 'react'
import { CccdQrReader } from './cccdQrReader'

/** Mã còn trong khung hình thì camera đọc lại liên tục: chỉ báo lại cùng mã khi mã đã rời khung hình quá khoảng này. */
const REPEAT_AFTER_MS = 2000

/** Camera đang phát lên `video` là camera sau (máy tính bảng, điện thoại). Trình duyệt chỉ ghi `environment` cho camera sau. */
function isRearCamera(video: HTMLVideoElement): boolean {
  const stream = video.srcObject as MediaStream | null
  return stream?.getVideoTracks?.()[0]?.getSettings?.().facingMode === 'environment'
}

export interface QrCameraViewProps {
  /** Đọc được mã QR. Camera vẫn chạy đến khi thành phần bị gỡ; cùng mã còn nằm trong khung hình thì không báo lại. */
  onDetected: (text: string) => void
  /** Camera đã bật (người dùng đã cho phép). */
  onStarted?: () => void
  /** Không mở được camera; camera đã tắt. */
  onError: (error: unknown) => void
  /** `environment`: ưu tiên camera sau (máy tính bảng, điện thoại). `user`: camera hướng về người dùng (kiosk). */
  facingMode?: 'environment' | 'user'
  className?: string
  style?: CSSProperties
}

/**
 * Khung hình camera đọc mã QR trên CCCD. Camera bật khi khung hình được vẽ và tắt khi bị gỡ. Hình chỉ dùng để đọc mã ngay
 * trên máy, không lưu, không gửi đi.
 *
 * Camera hướng về người dùng (webcam máy tính, camera kiosk) thì hình được lật ngang như soi gương, để người cầm thẻ đưa đúng
 * hướng vào khung; camera sau của máy tính bảng, điện thoại thì giữ nguyên. Lật hình không ảnh hưởng việc đọc mã.
 *
 * Trong hộp thoại, đặt thành phần này bên trong nội dung hộp thoại: Ant Design chỉ vẽ nội dung hộp thoại sau lần vẽ đầu
 * (hiệu ứng mở). Bật camera từ thành phần bao ngoài hộp thoại thì lúc đó chưa có thẻ video, và camera không bao giờ bật.
 */
export default function QrCameraView({
  onDetected,
  onStarted,
  onError,
  facingMode = 'environment',
  className,
  style,
}: QrCameraViewProps) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const [mirrored, setMirrored] = useState(false)
  // Hàm của thành phần cha có thể đổi mỗi lần vẽ lại: luôn gọi bản mới nhất, không tắt rồi bật lại camera.
  const detected = useEffectEvent(onDetected)
  const started = useEffectEvent(() => onStarted?.())
  const failed = useEffectEvent(onError)

  useEffect(() => {
    const video = videoRef.current
    if (!video) {
      return undefined
    }
    let controls: IScannerControls | undefined
    let stopped = false
    let last = { text: '', at: 0 }

    new CccdQrReader()
      .decodeFromConstraints(
        // Xin độ phân giải cao (camera không có thì trình duyệt chọn mức gần nhất): mã QR trên thẻ có nhiều điểm ảnh hơn.
        { video: { facingMode, width: { ideal: 1920 }, height: { ideal: 1080 } } },
        video,
        (result) => {
          if (!result || stopped) {
            return
          }
          const text = result.getText()
          const now = Date.now()
          const repeated = text === last.text && now - last.at < REPEAT_AFTER_MS
          last = { text, at: now }
          if (!repeated) {
            detected(text)
          }
        },
      )
      .then((scanner) => {
        controls = scanner
        if (stopped) {
          scanner.stop()
          return
        }
        setMirrored(!isRearCamera(video))
        started()
      })
      .catch((error: unknown) => {
        if (!stopped) {
          failed(error)
        }
      })

    return () => {
      stopped = true
      controls?.stop()
    }
  }, [facingMode])

  return (
    <video
      ref={videoRef}
      className={className}
      muted
      playsInline
      style={{
        display: 'block',
        width: '100%',
        background: '#000',
        objectFit: 'cover',
        ...(mirrored ? { transform: 'scaleX(-1)' } : null),
        ...style,
      }}
    />
  )
}
