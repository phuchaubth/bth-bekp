import { CameraOutlined, ScanOutlined } from '@ant-design/icons'
import { Button, Flex, Input, type InputRef } from 'antd'
import { lazy, Suspense, useRef, useState } from 'react'

const CameraScannerModal = lazy(() => import('./CameraScannerModal'))

interface Props {
  onScan: (raw: string) => void
  autoFocus?: boolean
  disabled?: boolean
}

/**
 * Nhận mã QR trên CCCD theo hai cách:
 * 1. Máy quét mã vạch 2D ở chế độ bàn phím: đặt con trỏ vào ô, quét, máy quét tự gõ nội dung rồi nhấn Enter.
 * 2. Camera của máy tính bảng/điện thoại/webcam (cần HTTPS).
 */
export function QrScanInput({ onScan, autoFocus = false, disabled = false }: Props) {
  const [value, setValue] = useState('')
  const [cameraOpen, setCameraOpen] = useState(false)
  const inputRef = useRef<InputRef>(null)

  const submit = (raw: string) => {
    const text = raw.trim()
    if (text) {
      onScan(text)
    }
    setValue('')
  }

  return (
    <Flex gap={8} wrap>
      <Input
        ref={inputRef}
        aria-label="Mã QR trên CCCD"
        prefix={<ScanOutlined />}
        placeholder="Đặt con trỏ vào đây rồi quét mã QR trên CCCD (máy quét 2D)"
        value={value}
        autoFocus={autoFocus}
        disabled={disabled}
        autoComplete="off"
        spellCheck={false}
        style={{ flex: '1 1 320px' }}
        onChange={(e) => setValue(e.target.value)}
        onPressEnter={() => submit(value)}
      />
      <Button icon={<CameraOutlined />} disabled={disabled} onClick={() => setCameraOpen(true)}>
        Quét bằng camera
      </Button>
      {cameraOpen && (
        <Suspense fallback={null}>
          <CameraScannerModal
            onClose={() => {
              setCameraOpen(false)
              inputRef.current?.focus()
            }}
            onDetected={(raw) => {
              setCameraOpen(false)
              submit(raw)
            }}
          />
        </Suspense>
      )}
    </Flex>
  )
}
