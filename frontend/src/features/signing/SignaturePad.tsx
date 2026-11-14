import { Button, Flex, Grid, Typography } from 'antd'
import { useCallback, useEffect, useImperativeHandle, useRef, type PointerEvent, type Ref } from 'react'

export interface SignaturePadHandle {
  clear: () => void
  /** Ảnh PNG nền trắng của khung ký (Base64, không kèm tiền tố data:). */
  toPngBase64: () => string
}

interface Props {
  ref?: Ref<SignaturePadHandle>
  /** Báo khi khung ký có/không có nét ký đủ dài. */
  onInkChange: (hasInk: boolean) => void
  disabled?: boolean
}

type Point = { x: number; y: number }

/** Tổng độ dài nét (điểm ảnh CSS) tối thiểu để coi là đã ký; máy chủ vẫn kiểm tra lại. */
const MIN_INK_LENGTH = 40
const LINE_WIDTH = 2.5
const INK = '#111827'

/**
 * Khung ký tay dùng Pointer Events: ký được bằng ngón tay, bút cảm ứng hoặc chuột. Nét ký chỉ nằm trong bộ nhớ để vẽ lại
 * khi xoay màn hình; thứ gửi đi chỉ là ảnh PNG (không gửi tọa độ, thời gian từng nét).
 */
export function SignaturePad({ ref, onInkChange, disabled = false }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  // Điện thoại cầm dọc: khung ký cao hơn (2:1 thay vì 3:1) để đủ chỗ ký bằng ngón tay, và gợi ý xoay ngang.
  const screens = Grid.useBreakpoint()
  const narrow = screens.sm === false
  // Tọa độ chuẩn hóa 0..1 theo kích thước khung, để vẽ lại đúng khi khung đổi kích thước.
  const strokes = useRef<Point[][]>([])
  const drawing = useRef(false)
  const inkLength = useRef(0)

  const redraw = useCallback(() => {
    const canvas = canvasRef.current
    const context = canvas?.getContext('2d')
    if (!canvas || !context) {
      return
    }
    const rect = canvas.getBoundingClientRect()
    const ratio = window.devicePixelRatio || 1
    const width = Math.max(1, Math.round(rect.width * ratio))
    const height = Math.max(1, Math.round(rect.height * ratio))
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width
      canvas.height = height
    }
    context.setTransform(1, 0, 0, 1, 0, 0)
    context.fillStyle = '#ffffff'
    context.fillRect(0, 0, width, height)
    context.lineCap = 'round'
    context.lineJoin = 'round'
    context.lineWidth = LINE_WIDTH * ratio
    context.strokeStyle = INK
    for (const stroke of strokes.current) {
      context.beginPath()
      stroke.forEach((point, index) => {
        const x = point.x * width
        const y = point.y * height
        if (index === 0) {
          context.moveTo(x, y)
          context.lineTo(x + 0.01, y)
        } else {
          context.lineTo(x, y)
        }
      })
      context.stroke()
    }
  }, [])

  useEffect(() => {
    redraw()
    const canvas = canvasRef.current
    if (!canvas || typeof ResizeObserver === 'undefined') {
      return
    }
    const observer = new ResizeObserver(() => redraw())
    observer.observe(canvas)
    return () => observer.disconnect()
  }, [redraw])

  const clear = useCallback(() => {
    strokes.current = []
    inkLength.current = 0
    redraw()
    onInkChange(false)
  }, [onInkChange, redraw])

  useImperativeHandle(
    ref,
    () => ({
      clear,
      toPngBase64: () => {
        redraw()
        const dataUrl = canvasRef.current?.toDataURL('image/png') ?? ''
        return dataUrl.slice(dataUrl.indexOf(',') + 1)
      },
    }),
    [clear, redraw],
  )

  const pointOf = (event: PointerEvent<HTMLCanvasElement>): Point => {
    const rect = event.currentTarget.getBoundingClientRect()
    return {
      x: rect.width > 0 ? (event.clientX - rect.left) / rect.width : 0,
      y: rect.height > 0 ? (event.clientY - rect.top) / rect.height : 0,
    }
  }

  const onPointerDown = (event: PointerEvent<HTMLCanvasElement>) => {
    if (disabled) {
      return
    }
    event.preventDefault()
    event.currentTarget.setPointerCapture?.(event.pointerId)
    drawing.current = true
    strokes.current.push([pointOf(event)])
    redraw()
  }

  const onPointerMove = (event: PointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) {
      return
    }
    event.preventDefault()
    const stroke = strokes.current[strokes.current.length - 1]
    const previous = stroke[stroke.length - 1]
    const point = pointOf(event)
    const rect = event.currentTarget.getBoundingClientRect()
    inkLength.current += Math.hypot((point.x - previous.x) * rect.width, (point.y - previous.y) * rect.height)
    stroke.push(point)
    redraw()
    if (inkLength.current >= MIN_INK_LENGTH) {
      onInkChange(true)
    }
  }

  const onPointerUp = () => {
    drawing.current = false
  }

  return (
    <Flex vertical gap={8}>
      <canvas
        ref={canvasRef}
        role="img"
        aria-label="Khung ký tên: ký bằng ngón tay, bút cảm ứng hoặc chuột"
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onPointerLeave={onPointerUp}
        style={{
          width: '100%',
          aspectRatio: narrow ? '2 / 1' : '3 / 1',
          border: '2px dashed #94a3b8',
          borderRadius: 8,
          background: '#ffffff',
          // Không cuộn trang hay phóng to khi đang ký bằng ngón tay.
          touchAction: 'none',
          cursor: disabled ? 'not-allowed' : 'crosshair',
        }}
      />
      <Flex justify="space-between" align="center" wrap gap={8}>
        <Typography.Text type="secondary">
          Ký trong khung trên bằng ngón tay hoặc bút cảm ứng.{narrow ? ' Có thể xoay ngang điện thoại để khung ký rộng hơn.' : ''}
        </Typography.Text>
        <Button onClick={clear} disabled={disabled}>
          Ký lại
        </Button>
      </Flex>
    </Flex>
  )
}
