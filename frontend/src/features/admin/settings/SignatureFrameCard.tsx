import { DeleteOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Popconfirm, Typography } from 'antd'
import { useRef, useState } from 'react'
import { brandingKeys, signatureFrameImageUrl } from '../../../api/branding'
import { ApiError } from '../../../api/http'
import { LOGO_MAX_BYTES, settingsApi, settingsKeys, type SystemSettings } from '../../../api/settings'
import { errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

/** Chỉ PNG: máy chủ ghép ảnh khung với chữ ký để in lên PDF. */
const ACCEPTED = ['image/png']

function readAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(new Error('Không đọc được file.'))
    reader.readAsDataURL(file)
  })
}

/**
 * Khung chữ ký (quyết định 26): ảnh thiết kế của bệnh viện (logo, viền màu) làm nền cho khung ký trên điện thoại, kiosk và máy
 * tính, khung hiện chữ ký mẫu của nhân viên, và khung ở mỗi chỗ ký trên PDF (chưa ký thì khung trống).
 */
export function SignatureFrameCard({ settings }: { settings: SystemSettings }) {
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const input = useRef<HTMLInputElement>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const frame = settings.signatureFrame

  const onSaved = async (updated: SystemSettings, text: string) => {
    queryClient.setQueryData(settingsKeys.all, updated)
    await queryClient.invalidateQueries({ queryKey: brandingKeys.signatureFrame })
    setProblem(null)
    message.success(text)
  }
  const upload = useMutation({
    mutationFn: (image: string) => settingsApi.uploadSignatureFrame(image),
    onSuccess: (updated) => onSaved(updated, 'Đã lưu khung chữ ký. Màn hình ký mở từ giờ dùng khung mới.'),
    onError: (e) => setProblem(e instanceof ApiError && e.fieldErrors?.image ? e.fieldErrors.image.join(' ') : errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: () => settingsApi.deleteSignatureFrame(),
    onSuccess: (updated) => onSaved(updated, 'Đã bỏ khung chữ ký. Khung ký trở lại dạng mặc định.'),
    onError: (e) => message.error(errorMessage(e)),
  })

  const onFile = async (file: File | undefined) => {
    if (input.current) input.current.value = ''
    if (!file) return
    if (!ACCEPTED.includes(file.type)) {
      setProblem('Ảnh khung chữ ký cần là ảnh PNG.')
      return
    }
    if (file.size > LOGO_MAX_BYTES) {
      setProblem('Ảnh khung chữ ký tối đa 1 MB. Ảnh lớn hơn thì thu nhỏ (rộng khoảng 1200–1500 điểm ảnh) rồi lưu lại PNG.')
      return
    }
    try {
      upload.mutate(await readAsDataUrl(file))
    } catch (e) {
      setProblem(errorMessage(e))
    }
  }

  return (
    <Card title="Khung chữ ký">
      <Flex vertical gap={10}>
        <Typography.Paragraph style={{ marginBottom: 0 }}>
          Ảnh thiết kế của bệnh viện (logo, viền màu) làm nền khung ký trên điện thoại, kiosk và máy tính; chữ ký mẫu của nhân viên
          hiện trong khung; trên PDF, mỗi chỗ ký (người bệnh, nhân viên) in khung này: đã ký thì chữ ký nằm giữa khung, chưa ký thì
          khung trống. Người ký ký vào khoảng trống giữa khung. Ảnh PNG, tối đa 1 MB, nên ngang tỉ lệ 4:3, rộng từ 1200 điểm ảnh.
        </Typography.Paragraph>
        {frame ? (
          <div style={{ width: '100%', maxWidth: 360 }}>
            <img
              src={signatureFrameImageUrl(frame)}
              alt="Khung chữ ký đang dùng"
              style={{ width: '100%', border: '1px solid #e2e8f0', borderRadius: 8, display: 'block' }}
            />
            <Typography.Text type="secondary">
              {frame.contentType === 'image/png' ? 'PNG' : 'JPG'} {frame.width} × {frame.height} điểm ảnh · cập nhật{' '}
              {formatDateTime(frame.updatedAtUtc)}
            </Typography.Text>
          </div>
        ) : (
          <Typography.Text type="secondary">Chưa có khung: khung ký là nền trắng, viền nét đứt.</Typography.Text>
        )}
        {frame?.contentType === 'image/jpeg' && (
          <Alert
            type="warning"
            showIcon
            title="Ảnh khung đang dùng là JPG: chỉ hiện trên màn hình ký, chưa in được lên PDF."
            description="Tải lại ảnh khung dạng PNG để chữ ký trên PDF nằm trong khung."
          />
        )}
        {problem && <Alert type="error" showIcon title={problem} />}
        <input
          ref={input}
          type="file"
          accept="image/png"
          hidden
          aria-label="Chọn file khung chữ ký"
          onChange={(e) => void onFile(e.target.files?.[0])}
        />
        <Flex gap={8} wrap>
          <Button type="primary" icon={<UploadOutlined />} loading={upload.isPending} onClick={() => input.current?.click()}>
            {frame ? 'Thay khung' : 'Tải khung lên'}
          </Button>
          {frame && (
            <Popconfirm
              title="Bỏ khung chữ ký?"
              description="Khung ký trở lại dạng mặc định. Chữ ký đã ký giữ nguyên."
              okText="Bỏ khung"
              cancelText="Hủy"
              okButtonProps={{ danger: true }}
              onConfirm={() => remove.mutate()}
            >
              <Button danger icon={<DeleteOutlined />} loading={remove.isPending}>
                Bỏ khung
              </Button>
            </Popconfirm>
          )}
        </Flex>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Có hiệu lực ở mọi màn hình ký mở sau khi lưu và ở PDF tạo sau khi lưu; PDF đã ký giữ nguyên. Mọi thay đổi được ghi nhật ký hệ
          thống.
        </Typography.Text>
      </Flex>
    </Card>
  )
}
