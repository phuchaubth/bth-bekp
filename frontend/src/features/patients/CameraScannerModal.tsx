import { Alert, Modal, Typography } from 'antd'
import { useRef, useState } from 'react'
import { cameraErrorName, canUseCamera } from './cameraSupport'
import QrCameraView from './QrCameraView'

interface Props {
  onDetected: (raw: string) => void
  onClose: () => void
}

const INSECURE_CONNECTION =
  'Trình duyệt không cho phép dùng camera trên kết nối này. Camera chỉ hoạt động khi truy cập qua HTTPS (hoặc localhost).'

function cameraErrorMessage(error: unknown): string {
  const name = cameraErrorName(error)
  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return 'Bạn chưa cho phép trang này dùng camera. Hãy cho phép trong cài đặt trình duyệt rồi thử lại.'
  }
  if (name === 'NotFoundError' || name === 'OverconstrainedError') {
    return 'Không tìm thấy camera trên thiết bị này. Hãy dùng máy quét 2D hoặc nhập tay.'
  }
  if (name === 'NotReadableError') {
    return 'Camera đang được ứng dụng khác sử dụng. Hãy đóng ứng dụng đó rồi thử lại.'
  }
  // Ghi kèm tên lỗi của trình duyệt để bộ phận CNTT tra cứu khi người dùng chụp màn hình gửi.
  return `Không mở được camera${name ? ` (${name})` : ''}. Hãy dùng máy quét 2D hoặc nhập tay.`
}

/** Quét mã QR trên CCCD bằng camera (ưu tiên camera sau trên máy tính bảng/điện thoại). Được tải khi cần. */
export default function CameraScannerModal({ onDetected, onClose }: Props) {
  const [error, setError] = useState<string | null>(() => (canUseCamera() ? null : INSECURE_CONNECTION))
  const [started, setStarted] = useState(false)
  // Camera đọc liên tục; chỉ chuyển mã đầu tiên (hộp thoại đóng ngay sau đó).
  const detected = useRef(false)

  return (
    <Modal open title="Quét mã QR trên CCCD bằng camera" footer={null} onCancel={onClose} destroyOnHidden>
      {error ? (
        <Alert type="warning" showIcon title={error} />
      ) : (
        <>
          <QrCameraView
            style={{ borderRadius: 8, aspectRatio: '4 / 3' }}
            onStarted={() => setStarted(true)}
            onError={(failure) => setError(cameraErrorMessage(failure))}
            onDetected={(text) => {
              if (!detected.current) {
                detected.current = true
                onDetected(text)
              }
            }}
          />
          <Typography.Paragraph type="secondary" role="status" style={{ marginTop: 12, marginBottom: 0 }}>
            {started
              ? 'Đưa mã QR ở mặt trước thẻ CCCD vào giữa khung hình, giữ yên khoảng 1–2 giây.'
              : 'Đang mở camera… Nếu trình duyệt hỏi quyền dùng camera, hãy chọn Cho phép.'}
          </Typography.Paragraph>
        </>
      )}
    </Modal>
  )
}
