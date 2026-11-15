import { DeleteOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Popconfirm, Typography } from 'antd'
import { useRef, useState } from 'react'
import { brandingKeys, signatureFrameImageUrl } from '../../../api/branding'
import { ApiError } from '../../../api/http'
import { LOGO_MAX_BYTES, settingsApi, settingsKeys, type SystemSettings } from '../../../api/settings'
import { errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

const ACCEPTED = ['image/png', 'image/jpeg']

function readAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(new Error('Không đọc được file.'))
    reader.readAsDataURL(file)
  })
}

/**
 * Khung chữ ký: ảnh thiết kế của bệnh viện (logo, viền màu) làm nền cho khung ký trên điện thoại, kiosk và máy tính, và cho
 * khung hiện chữ ký mẫu của nhân viên. Chỉ để hiển thị: ảnh chữ ký lưu lại và PDF không có khung.
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
      setProblem('Ảnh khung chữ ký phải là ảnh PNG hoặc JPG.')
      return
    }
    if (file.size > LOGO_MAX_BYTES) {
      setProblem('Ảnh khung chữ ký tối đa 1 MB. Ảnh lớn hơn thì lưu lại dạng JPG.')
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
          Ảnh thiết kế của bệnh viện (logo, viền màu) làm nền khung ký trên điện thoại, kiosk và máy tính, và khung hiện chữ ký mẫu
          của nhân viên. Người ký ký vào khoảng trống giữa khung. Ảnh PNG hoặc JPG, tối đa 1 MB, nên ngang tỉ lệ 4:3, rộng từ 1200
          điểm ảnh. Chỉ để hiển thị: ảnh chữ ký lưu lại và PDF không in khung.
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
        {problem && <Alert type="error" showIcon title={problem} />}
        <input
          ref={input}
          type="file"
          accept="image/png,image/jpeg"
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
          Có hiệu lực ở mọi màn hình ký mở sau khi lưu. Mọi thay đổi được ghi nhật ký hệ thống.
        </Typography.Text>
      </Flex>
    </Card>
  )
}
