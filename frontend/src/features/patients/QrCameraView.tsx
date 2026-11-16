import { BrowserQRCodeReader, type IScannerControls } from '@zxing/browser'
import { useEffect, useEffectEvent, useRef, type CSSProperties } from 'react'

/** Mã còn trong khung hình thì camera đọc lại liên tục: chỉ báo lại cùng mã khi mã đã rời khung hình quá khoảng này. */
const REPEAT_AFTER_MS = 2000

export interface QrCameraViewProps {
  /** Đọc được mã QR. Camera vẫn chạy đến khi thành phần bị gỡ; cùng mã còn nằm trong khung hình thì không báo lại. */
  onDetected: (text: string) => void
  /** Camera đã bật (người dùng đã cho phép). */
  onStarted?: () => void
  /** Không mở được camera; camera đã tắt. */
  onError: (error: unknown) => void
  /** `environment`: ưu tiên camera sau (máy tính bảng, điện thoại). `user`: camera hướng về người dùng (kiosk). */
  facingMode?: 'environment' | 'user'
  /** Lật ngang hình như soi gương, để người đứng trước camera đưa thẻ đúng hướng. Không ảnh hưởng việc đọc mã. */
  mirrored?: boolean
  className?: string
  style?: CSSProperties
}

/**
 * Khung hình camera đọc mã QR trên CCCD. Camera bật khi khung hình được vẽ và tắt khi bị gỡ. Hình chỉ dùng để đọc mã ngay
 * trên máy, không lưu, không gửi đi.
 *
 * Trong hộp thoại, đặt thành phần này bên trong nội dung hộp thoại: Ant Design chỉ vẽ nội dung hộp thoại sau lần vẽ đầu
 * (hiệu ứng mở). Bật camera từ thành phần bao ngoài hộp thoại thì lúc đó chưa có thẻ video, và camera không bao giờ bật.
 */
export default function QrCameraView({
  onDetected,
  onStarted,
  onError,
  facingMode = 'environment',
  mirrored = false,
  className,
  style,
}: QrCameraViewProps) {
  const videoRef = useRef<HTMLVideoElement>(null)
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

    new BrowserQRCodeReader()
      .decodeFromConstraints(
        // Độ phân giải cao hơn mặc định 640×480: webcam thường không lấy nét gần, thẻ để xa hơn vẫn đọc được mã QR dày của CCCD.
        { video: { facingMode, width: { ideal: 1280 }, height: { ideal: 720 } } },
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
        } else {
          started()
        }
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
