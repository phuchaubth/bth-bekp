import { DeleteOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Popconfirm, Spin, Typography } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { LOGO_MAX_BYTES, settingsApi, settingsKeys, type SystemSettings } from '../../../api/settings'
import { ApiError } from '../../../api/http'
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

/** Logo bệnh viện in ở góc trên bên trái mọi phiếu: tải lên, xem trước, thay, xóa. */
export function LogoCard({ settings }: { settings: SystemSettings }) {
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const input = useRef<HTMLInputElement>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const logo = settings.logo

  const preview = useQuery({
    queryKey: [...settingsKeys.all, 'logo', logo?.updatedAtUtc] as const,
    queryFn: ({ signal }) => settingsApi.logo(signal),
    enabled: !!logo,
  })
  // Ảnh xem trước tải có xác thực, hiển thị qua blob URL (được giải phóng khi đổi ảnh hoặc rời trang).
  const url = useMemo(() => (logo && preview.data ? URL.createObjectURL(preview.data) : null), [logo, preview.data])
  useEffect(() => () => {
    if (url) URL.revokeObjectURL(url)
  }, [url])

  const onSaved = (updated: SystemSettings, text: string) => {
    queryClient.setQueryData(settingsKeys.all, updated)
    setProblem(null)
    message.success(text)
  }
  const upload = useMutation({
    mutationFn: (image: string) => settingsApi.uploadLogo(image),
    onSuccess: (updated) => onSaved(updated, 'Đã lưu logo. Phiếu in từ giờ có logo mới; PDF đã ký giữ nguyên.'),
    onError: (e) => setProblem(e instanceof ApiError && e.fieldErrors?.image ? e.fieldErrors.image.join(' ') : errorMessage(e)),
  })
  const remove = useMutation({
    mutationFn: () => settingsApi.deleteLogo(),
    onSuccess: (updated) => onSaved(updated, 'Đã bỏ logo. Phiếu in từ giờ không có logo.'),
    onError: (e) => message.error(errorMessage(e)),
  })

  const onFile = async (file: File | undefined) => {
    if (input.current) input.current.value = ''
    if (!file) return
    if (!ACCEPTED.includes(file.type)) {
      setProblem('Logo phải là ảnh PNG hoặc JPG.')
      return
    }
    if (file.size > LOGO_MAX_BYTES) {
      setProblem('Logo tối đa 1 MB.')
      return
    }
    try {
      upload.mutate(await readAsDataUrl(file))
    } catch (e) {
      setProblem(errorMessage(e))
    }
  }

  return (
    <Card title="Logo bệnh viện">
      <Flex gap={20} align="start" wrap>
        <div
          style={{
            width: 132,
            height: 132,
            border: '1px dashed #c7cfdb',
            borderRadius: 8,
            display: 'grid',
            placeItems: 'center',
            background: '#fff',
            flex: 'none',
          }}
        >
          {logo ? (
            url ? (
              <img src={url} alt="Logo bệnh viện đang dùng" style={{ maxWidth: 112, maxHeight: 112, objectFit: 'contain' }} />
            ) : preview.isError ? (
              <Typography.Text type="danger" style={{ fontSize: 12, textAlign: 'center' }}>
                Không tải được ảnh
              </Typography.Text>
            ) : (
              <Spin />
            )
          ) : (
            <Typography.Text type="secondary" style={{ fontSize: 12, textAlign: 'center', padding: 8 }}>
              Chưa có logo
            </Typography.Text>
          )}
        </div>
        <Flex vertical gap={10} style={{ flex: '1 1 240px', minWidth: 0 }}>
          <Typography.Paragraph style={{ marginBottom: 0 }}>
            In ở góc trên bên trái mọi phiếu, cạnh tên đơn vị (khoảng 18 × 18 mm). Ảnh PNG hoặc JPG, tối đa 1 MB, nên là ảnh
            vuông nền trắng hoặc trong suốt, rộng từ 300 điểm ảnh để in rõ.
          </Typography.Paragraph>
          {logo && (
            <Typography.Text type="secondary">
              {logo.contentType === 'image/png' ? 'PNG' : 'JPG'} {logo.width} × {logo.height} điểm ảnh · cập nhật{' '}
              {formatDateTime(logo.updatedAtUtc)}
            </Typography.Text>
          )}
          {problem && <Alert type="error" showIcon title={problem} />}
          <input
            ref={input}
            id="logo-file"
            type="file"
            accept="image/png,image/jpeg"
            hidden
            aria-label="Chọn file logo"
            onChange={(e) => void onFile(e.target.files?.[0])}
          />
          <Flex gap={8} wrap>
            <Button type="primary" icon={<UploadOutlined />} loading={upload.isPending} onClick={() => input.current?.click()}>
              {logo ? 'Thay logo' : 'Tải logo lên'}
            </Button>
            {logo && (
              <Popconfirm
                title="Bỏ logo?"
                description="Phiếu in từ giờ không có logo. PDF đã ký giữ nguyên."
                okText="Bỏ logo"
                cancelText="Hủy"
                okButtonProps={{ danger: true }}
                onConfirm={() => remove.mutate()}
              >
                <Button danger icon={<DeleteOutlined />} loading={remove.isPending}>
                  Bỏ logo
                </Button>
              </Popconfirm>
            )}
          </Flex>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            Áp dụng cho PDF tạo sau khi lưu; phiếu đã hoàn tất giữ nguyên PDF đã ký. Mọi thay đổi được ghi nhật ký hệ thống.
          </Typography.Text>
        </Flex>
      </Flex>
    </Card>
  )
}
