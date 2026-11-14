import { DeleteOutlined, EditOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Flex, Modal, Popconfirm, Spin, Typography } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { accountApi, accountKeys, useSignatureSpecimen } from '../../api/account'
import { errorMessage } from '../../utils/formErrors'
import { SignaturePad, type SignaturePadHandle } from '../signing/SignaturePad'

/** Ảnh chữ ký mẫu của mình (tải có xác thực, hiển thị qua blob URL). Chưa có thì hiện `empty`. */
export function SpecimenImage({ height = 72, empty }: { height?: number; empty?: React.ReactNode }) {
  const specimen = useSignatureSpecimen()
  const url = useMemo(() => (specimen.data ? URL.createObjectURL(specimen.data) : null), [specimen.data])
  useEffect(() => () => {
    if (url) URL.revokeObjectURL(url)
  }, [url])

  if (specimen.isPending) return <Spin size="small" />
  if (!url) return <>{empty ?? null}</>
  return <img src={url} alt="Chữ ký mẫu của bạn" style={{ height, maxWidth: '100%', objectFit: 'contain' }} />
}

/** Thẻ "Chữ ký mẫu" ở trang Tài khoản: ký trong khung để lưu hoặc thay, xóa chữ ký mẫu. */
export function SignatureSpecimenCard() {
  const specimen = useSignatureSpecimen()
  const [drawing, setDrawing] = useState(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const remove = useMutation({
    mutationFn: () => accountApi.deleteSignatureSpecimen(),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: accountKeys.signatureSpecimen })
      message.success('Đã xóa chữ ký mẫu.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  return (
    <Flex vertical gap={12}>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        Chữ ký mẫu hiện bên cạnh khung ký mỗi khi bạn ký xác nhận phiếu, để bạn và người kiểm tra đối chiếu chữ ký.
      </Typography.Paragraph>
      <div
        style={{
          minHeight: 96,
          border: '1px dashed #c7d5d6',
          borderRadius: 10,
          display: 'grid',
          placeItems: 'center',
          padding: 12,
          background: '#ffffff',
        }}
      >
        <SpecimenImage height={88} empty={<Typography.Text type="secondary">Chưa có chữ ký mẫu</Typography.Text>} />
      </div>
      {specimen.isError && <Alert type="error" showIcon title={errorMessage(specimen.error)} />}
      <Flex gap={8} wrap>
        <Button type="primary" icon={<EditOutlined />} onClick={() => setDrawing(true)}>
          {specimen.data ? 'Ký lại chữ ký mẫu' : 'Ký chữ ký mẫu'}
        </Button>
        {specimen.data && (
          <Popconfirm
            title="Xóa chữ ký mẫu?"
            description="Phiếu đã ký giữ nguyên chữ ký đã ký."
            okText="Xóa"
            cancelText="Hủy"
            okButtonProps={{ danger: true }}
            onConfirm={() => remove.mutate()}
          >
            <Button danger icon={<DeleteOutlined />} loading={remove.isPending}>
              Xóa
            </Button>
          </Popconfirm>
        )}
      </Flex>
      {drawing && <DrawSpecimenModal onClose={() => setDrawing(false)} />}
    </Flex>
  )
}

function DrawSpecimenModal({ onClose }: { onClose: () => void }) {
  const pad = useRef<SignaturePadHandle>(null)
  const [hasInk, setHasInk] = useState(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (image: string) => accountApi.saveSignatureSpecimen(image),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: accountKeys.signatureSpecimen })
      message.success('Đã lưu chữ ký mẫu.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  return (
    <Modal
      open
      title="Ký chữ ký mẫu"
      okText="Lưu chữ ký mẫu"
      cancelText="Đóng"
      width={640}
      okButtonProps={{ disabled: !hasInk, loading: save.isPending }}
      onOk={() => pad.current && save.mutate(pad.current.toPngBase64())}
      onCancel={onClose}
    >
      <Typography.Paragraph>Ký như khi ký giấy tờ thường ngày. Chữ ký này dùng để đối chiếu khi bạn ký xác nhận phiếu.</Typography.Paragraph>
      <SignaturePad ref={pad} onInkChange={setHasInk} disabled={save.isPending} />
    </Modal>
  )
}
