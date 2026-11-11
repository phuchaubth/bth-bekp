import { BrowserQRCodeReader, type IScannerControls } from '@zxing/browser'
import { Alert, Modal, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'

interface Props {
  onDetected: (raw: string) => void
  onClose: () => void
}

function cameraErrorMessage(error: unknown): string {
  const name = error instanceof DOMException || error instanceof Error ? error.name : ''
  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return 'Bạn chưa cho phép trang này dùng camera. Hãy cho phép trong cài đặt trình duyệt rồi thử lại.'
  }
  if (name === 'NotFoundError' || name === 'OverconstrainedError') {
    return 'Không tìm thấy camera trên thiết bị này. Hãy dùng máy quét 2D hoặc nhập tay.'
  }
  if (name === 'NotReadableError') {
    return 'Camera đang được ứng dụng khác sử dụng. Hãy đóng ứng dụng đó rồi thử lại.'
  }
  return 'Không mở được camera. Hãy dùng máy quét 2D hoặc nhập tay.'
}

/** Quét mã QR bằng camera (ưu tiên camera sau trên máy tính bảng/điện thoại). Được tải khi cần. */
export default function CameraScannerModal({ onDetected, onClose }: Props) {
  const videoRef = useRef<HTMLVideoElement>(null)
  const [error, setError] = useState<string | null>(() =>
    typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia
      ? 'Trình duyệt không cho phép dùng camera trên kết nối này. Camera chỉ hoạt động khi truy cập qua HTTPS (hoặc localhost).'
      : null,
  )

  useEffect(() => {
    if (error || !videoRef.current) {
      return undefined
    }

    const reader = new BrowserQRCodeReader()
    let controls: IScannerControls | undefined
    let stopped = false

    reader
      .decodeFromConstraints({ video: { facingMode: 'environment' } }, videoRef.current, (result, _err, scannerControls) => {
        if (result && !stopped) {
          stopped = true
          scannerControls.stop()
          onDetected(result.getText())
        }
      })
      .then((started) => {
        controls = started
        if (stopped) {
          started.stop()
        }
      })
      .catch((e: unknown) => setError(cameraErrorMessage(e)))

    return () => {
      stopped = true
      controls?.stop()
    }
  }, [error, onDetected])

  return (
    <Modal open title="Quét mã QR trên CCCD bằng camera" footer={null} onCancel={onClose} destroyOnHidden>
      {error ? (
        <Alert type="warning" showIcon title={error} />
      ) : (
        <>
          <video
            ref={videoRef}
            muted
            playsInline
            style={{ width: '100%', borderRadius: 8, background: '#000', aspectRatio: '4 / 3', objectFit: 'cover' }}
          />
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
            Đưa mã QR ở mặt trước thẻ CCCD vào giữa khung hình, giữ yên khoảng 1–2 giây.
          </Typography.Paragraph>
        </>
      )}
    </Modal>
  )
}
