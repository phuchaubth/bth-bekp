import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Card, Input, Modal, Timeline, Typography } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import {
  consentFormKeys,
  consentFormsApi,
  decisionLabels,
  signerRoleLabels,
  type ConsentFormDetail,
} from '../../api/consentForms'
import { useAuth } from '../../auth/authContext'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { SIGNING_PATH, signingSession } from '../signing/signingSession'

/**
 * Chuyển máy sang chế độ ký: lấy token phiên ký, đóng phiên đăng nhập của nhân viên trên máy này rồi mở màn hình ký.
 * Người bệnh cầm máy không làm được gì khác; nhân viên đăng nhập lại để thoát.
 */
export function StartSigningModal({ form, onClose }: { form: ConsentFormDetail; onClose: () => void }) {
  const { user, logout } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const start = async () => {
    // Phải gọi ngay trong thao tác bấm của người dùng thì trình duyệt mới cho toàn màn hình.
    void document.documentElement.requestFullscreen?.().catch(() => {})
    setBusy(true)
    setError(null)
    try {
      const session = await consentFormsApi.startSigning(form.id)
      signingSession.set({ token: session.token, expiresAtUtc: session.expiresAtUtc, form, staffUserName: user?.userName ?? '' })
      // Đóng phiên nhân viên trước; trong lúc có phiên ký, mọi trang cần đăng nhập đều chuyển về màn hình ký.
      await logout()
      // Thông báo dành cho nhân viên không hiện tiếp trên màn hình của người bệnh.
      message.destroy()
      navigate(SIGNING_PATH, { replace: true })
    } catch (e) {
      setError(errorMessage(e))
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      title="Chuyển sang chế độ ký"
      okText="Bắt đầu ký"
      cancelText="Đóng"
      confirmLoading={busy}
      onOk={() => void start()}
      onCancel={onClose}
    >
      <Typography.Paragraph>
        Máy này sẽ chuyển sang màn hình ký để {form.representative ? 'người đại diện' : 'người bệnh'} đọc lại nội dung, chọn
        Đồng ý hoặc Từ chối và ký tay.
      </Typography.Paragraph>
      <Typography.Paragraph>
        <Typography.Text strong>Phiên đăng nhập của bạn trên máy này sẽ được đóng.</Typography.Text> Sau khi ký xong, bạn
        đăng nhập lại để thoát chế độ ký và xác nhận phiếu.
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary">
        Nên bật khóa ứng dụng của máy tính bảng (Guided Access trên iPad, Ghim màn hình trên Android) để người bệnh không
        thoát ra ngoài trình duyệt.
      </Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
    </Modal>
  )
}

/** Thu hồi phiếu đã hoàn tất (bắt buộc lý do). */
export function RevokeFormModal({ form, onClose }: { form: ConsentFormDetail; onClose: () => void }) {
  const [reason, setReason] = useState('')
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const revoke = useMutation({
    mutationFn: () => consentFormsApi.revoke(form.id, reason.trim()),
    onSuccess: (updated) => {
      queryClient.setQueryData(consentFormKeys.detail(form.id), updated)
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      message.success('Đã thu hồi phiếu.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  const valid = reason.trim().length >= 5

  return (
    <Modal
      open
      title={`Thu hồi phiếu ${form.formNo}`}
      okText="Thu hồi"
      cancelText="Đóng"
      okButtonProps={{ danger: true, disabled: !valid, loading: revoke.isPending }}
      onOk={() => revoke.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph>
        Phiếu đã ký sẽ hết hiệu lực nhưng vẫn được lưu nguyên. Muốn thay đổi nội dung, sau khi thu hồi hãy lập phiếu thay thế.
      </Typography.Paragraph>
      <Input.TextArea
        aria-label="Lý do thu hồi"
        placeholder="Lý do thu hồi (ít nhất 5 ký tự)"
        value={reason}
        maxLength={500}
        showCount
        onChange={(e) => setReason(e.target.value)}
        autoSize={{ minRows: 3 }}
      />
    </Modal>
  )
}

/** Lịch sử trạng thái và các chữ ký của phiếu (màn hình 8). */
export function FormHistory({ form }: { form: ConsentFormDetail }) {
  const events: { at: string; color?: string; text: string }[] = [
    { at: form.createdAtUtc, text: `Lập phiếu – ${form.createdByName}` },
  ]
  if (form.submittedAtUtc) {
    events.push({ at: form.submittedAtUtc, text: 'Trình ký (nội dung và thông tin người bệnh được khóa)' })
  }
  for (const signature of form.signatures) {
    events.push(
      signature.method === 'Handwritten'
        ? {
            at: signature.signedAtUtc,
            color: 'blue',
            text: `${signature.signerName} (${signerRoleLabels[signature.role].toLowerCase()}) ký tay trên máy tính bảng${
              form.decision ? ` – ${decisionLabels[form.decision]}` : ''
            }`,
          }
        : {
            at: signature.signedAtUtc,
            color: 'blue',
            text: `${signature.signerName}${signature.signerTitle ? ` (${signature.signerTitle})` : ''} xác nhận bằng tài khoản${
              signature.stepKey ? `: ${form.steps.find((step) => step.key === signature.stepKey)?.label ?? signature.stepKey}` : ''
            }`,
          },
    )
  }
  if (form.completedAtUtc) {
    events.push({ at: form.completedAtUtc, color: 'green', text: 'Hoàn tất – PDF đã ký được lưu kèm mã băm' })
  }
  if (form.cancelledAtUtc) {
    events.push({ at: form.cancelledAtUtc, color: 'red', text: `Hủy phiếu: ${form.cancelReason ?? ''}` })
  }
  if (form.revokedAtUtc) {
    events.push({ at: form.revokedAtUtc, color: 'red', text: `Thu hồi: ${form.revokeReason ?? ''}` })
  }
  events.sort((a, b) => a.at.localeCompare(b.at))

  return (
    <Card title="Lịch sử phiếu">
      <Timeline
        items={events.map((event) => ({
          color: event.color,
          content: (
            <>
              <Typography.Text>{event.text}</Typography.Text>
              <br />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {formatDateTime(event.at)}
              </Typography.Text>
            </>
          ),
        }))}
      />
      {form.signedPdfSha256 && (
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0, fontSize: 12, wordBreak: 'break-all' }}>
          SHA-256 của PDF đã ký: <Typography.Text code>{form.signedPdfSha256}</Typography.Text>
        </Typography.Paragraph>
      )}
    </Card>
  )
}
