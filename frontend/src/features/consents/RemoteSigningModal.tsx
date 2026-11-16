import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Flex, Modal, QRCode, Result, Spin, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useState } from 'react'
import { consentFormKeys } from '../../api/consentForms'
import {
  remoteSigningApi,
  remoteSigningKeys,
  remoteSigningUrl,
  type RemoteSigningLink,
  type RemoteSigningState,
} from '../../api/remoteSigning'
import { errorMessage } from '../../utils/formErrors'

/** Mã đang dùng được: còn theo dõi trạng thái mỗi 3 giây. */
const ACTIVE: RemoteSigningState[] = ['Waiting', 'Opened']

const ENDED: Partial<Record<RemoteSigningState, string>> = {
  Expired: 'Mã đã hết hạn.',
  Cancelled: 'Mã đã bị hủy.',
  Locked: 'Nhập sai ngày sinh 5 lần nên mã đã bị hủy. Kiểm tra lại ngày sinh trong hồ sơ người bệnh.',
  Superseded: 'Phiếu đã được mở phiên ký khác (mã mới hoặc ký trên máy của bệnh viện), mã này không dùng được nữa.',
}

const time = (iso: string | null | undefined) => (iso ? dayjs(iso).format('HH:mm') : '')

/**
 * Ký trên điện thoại riêng của người bệnh (quyết định 32): nhân viên tạo mã QR cho một phiếu hoặc cả bộ phiếu; người bệnh quét
 * bằng camera điện thoại, nhập ngày sinh của người bệnh, đọc và ký trên máy của mình. Hộp này theo dõi trạng thái (đã mở, đã ký)
 * và cho hủy mã. Máy của nhân viên vẫn giữ phiên đăng nhập.
 */
export function RemoteSigningModal({
  target,
  count,
  byRepresentative,
  onClose,
}: {
  target: { formId: string } | { bundleId: string }
  count: number
  byRepresentative: boolean
  onClose: () => void
}) {
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [link, setLink] = useState<RemoteSigningLink | null>(null)
  const signer = byRepresentative ? 'người đại diện' : 'người bệnh'
  const forms = count > 1 ? `${count} phiếu` : 'phiếu'

  const create = useMutation({
    mutationFn: () =>
      'formId' in target ? remoteSigningApi.createForForm(target.formId) : remoteSigningApi.createForBundle(target.bundleId),
    onSuccess: (created) => setLink(created),
  })
  const status = useQuery({
    queryKey: remoteSigningKeys.status(link?.id ?? ''),
    queryFn: ({ signal }) => remoteSigningApi.status(link!.id, signal),
    enabled: !!link,
    refetchInterval: (query) => (query.state.data && !ACTIVE.includes(query.state.data.state) ? false : 3000),
  })
  const cancel = useMutation({
    mutationFn: () => remoteSigningApi.cancel(link!.id),
    onSuccess: (cancelled) => queryClient.setQueryData(remoteSigningKeys.status(cancelled.id), cancelled),
    onError: (e) => message.error(errorMessage(e)),
  })

  const state: RemoteSigningState | null = link ? (status.data?.state ?? 'Waiting') : null

  // Người bệnh ký xong hoặc mã thôi hiệu lực: tải lại phiếu phía sau để trang hiện đúng trạng thái.
  useEffect(() => {
    if (state && !ACTIVE.includes(state)) {
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
    }
  }, [state, queryClient])

  const close = () => {
    void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
    onClose()
  }

  const startOver = () => {
    setLink(null)
    create.mutate()
  }

  const footer =
    state === 'Signed'
      ? [
          <Button key="done" type="primary" onClick={close}>
            Xong
          </Button>,
        ]
      : state && ACTIVE.includes(state)
        ? [
            <Button key="cancel" danger loading={cancel.isPending} onClick={() => cancel.mutate()}>
              Hủy mã
            </Button>,
            <Button key="close" onClick={close}>
              Đóng
            </Button>,
          ]
        : [
            <Button key="close" onClick={close}>
              Đóng
            </Button>,
            <Button key="create" type="primary" loading={create.isPending} onClick={state ? startOver : () => create.mutate()}>
              {state ? 'Tạo mã mới' : 'Tạo mã QR'}
            </Button>,
          ]

  return (
    <Modal open title="Ký trên điện thoại của người bệnh (mã QR)" footer={footer} onCancel={close} width={520} style={{ top: 24 }}>
      {!link ? (
        <Flex vertical gap={8}>
          <Typography.Paragraph style={{ marginBottom: 0 }}>
            {signer.charAt(0).toUpperCase() + signer.slice(1)} dùng camera điện thoại của mình quét mã, nhập{' '}
            <Typography.Text strong>ngày sinh của người bệnh</Typography.Text>, rồi đọc và ký {forms} ngay trên điện thoại.
          </Typography.Paragraph>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Mã dùng một lần, mở được trên một điện thoại và hết hạn sau 15 phút nếu chưa quét. Máy này vẫn giữ phiên đăng nhập của
            bạn. Điện thoại cần vào được địa chỉ của phần mềm (ví dụ qua Wi-Fi của bệnh viện).
          </Typography.Paragraph>
          {create.isError && <Alert type="error" showIcon title={errorMessage(create.error)} />}
        </Flex>
      ) : state === 'Signed' ? (
        <Result
          status="success"
          title={`${signer.charAt(0).toUpperCase() + signer.slice(1)} đã ký xong`}
          subTitle={`Ký lúc ${time(status.data?.signedAtUtc)}. Bạn xác nhận phiếu như thường lệ.`}
        />
      ) : state === 'Opened' ? (
        <Flex vertical align="center" gap={12} style={{ padding: '16px 0' }}>
          <Spin />
          <Typography.Text strong>Đã mở trên điện thoại lúc {time(status.data?.openedAtUtc)}</Typography.Text>
          <Typography.Text type="secondary">Đang chờ {signer} đọc và ký. Trang phiếu tự cập nhật khi ký xong.</Typography.Text>
        </Flex>
      ) : state === 'Waiting' ? (
        <Flex vertical align="center" gap={12}>
          <QRCode type="svg" value={remoteSigningUrl(link.code)} size={240} errorLevel="M" bordered={false} />
          <Typography.Text strong style={{ textAlign: 'center' }}>
            Mời {signer} quét mã bằng camera điện thoại
          </Typography.Text>
          <Typography.Text type="secondary" style={{ textAlign: 'center' }}>
            Mã dùng được đến {time(link.expiresAtUtc)}. Sau khi quét, nhập ngày sinh của người bệnh để mở {forms}.
          </Typography.Text>
        </Flex>
      ) : (
        <Flex vertical gap={8}>
          <Alert type="warning" showIcon title={state ? ENDED[state] : ''} />
          {create.isError && <Alert type="error" showIcon title={errorMessage(create.error)} />}
        </Flex>
      )}
    </Modal>
  )
}
