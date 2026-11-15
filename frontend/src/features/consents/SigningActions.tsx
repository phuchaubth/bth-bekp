import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Input, Modal, Spin, Timeline, Typography } from 'antd'
import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import {
  consentFormKeys,
  consentFormsApi,
  decisionLabels,
  encountersSwitchedOff,
  signerRoleLabels,
  useEncounterList,
  type ConsentFormDetail,
  type EncounterChoice,
  type EncounterRange,
  type EncounterSource,
  type StaffSignatureInput,
} from '../../api/consentForms'
import { useAuth } from '../../auth/authContext'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { useSignatureSpecimen } from '../../api/account'
import { SpecimenImage } from '../account/SignatureSpecimen'
import { SignaturePad, type SignaturePadHandle } from '../signing/SignaturePad'
import { SIGNING_PATH, signingSession } from '../signing/signingSession'
import { EncounterPicker } from './EncounterPicker'

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
        Nên bật khóa ứng dụng của điện thoại (Truy cập được hướng dẫn – Guided Access trên iPhone, Ghim ứng dụng trên
        Android) để người bệnh không thoát ra ngoài trình duyệt.{' '}
        Điện thoại cầm dọc vẫn ký được; xoay ngang thì khung ký rộng hơn.
      </Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
    </Modal>
  )
}

/**
 * Ký cả bộ (quyết định 23): mở một phiên ký cho mọi phiếu của bộ đang chờ người bệnh ký, tải nội dung các phiếu rồi chuyển
 * máy sang chế độ ký. Người bệnh xem các phiếu, ghi một lời xác nhận và ký một lần cho tất cả.
 */
export function StartBundleSigningModal({
  bundleId,
  count,
  byRepresentative,
  onClose,
}: {
  bundleId: string
  count: number
  byRepresentative: boolean
  onClose: () => void
}) {
  const { user, logout } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const start = async () => {
    void document.documentElement.requestFullscreen?.().catch(() => {})
    setBusy(true)
    setError(null)
    try {
      const session = await consentFormsApi.startBundleSigning(bundleId)
      // Tải nội dung các phiếu trước khi đóng phiên đăng nhập (màn hình ký không còn quyền đọc phiếu).
      const forms = await Promise.all(session.formIds.map((formId) => consentFormsApi.get(formId)))
      signingSession.set({
        token: session.token,
        expiresAtUtc: session.expiresAtUtc,
        form: forms[0],
        bundle: { bundleId, forms },
        staffUserName: user?.userName ?? '',
      })
      await logout()
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
      title="Ký cả bộ trên điện thoại"
      okText="Bắt đầu ký"
      cancelText="Đóng"
      confirmLoading={busy}
      onOk={() => void start()}
      onCancel={onClose}
    >
      <Typography.Paragraph>
        Máy này sẽ chuyển sang màn hình ký {count} phiếu một lần: {byRepresentative ? 'người đại diện' : 'người bệnh'} xem các
        phiếu, chọn ý kiến ở phiếu có hỏi, ghi một lời xác nhận và ký <Typography.Text strong>một lần cho cả {count} phiếu</Typography.Text>.
      </Typography.Paragraph>
      <Typography.Paragraph>
        <Typography.Text strong>Phiên đăng nhập của bạn trên máy này sẽ được đóng.</Typography.Text> Sau khi ký xong, bạn
        đăng nhập lại để thoát chế độ ký và xác nhận các phiếu.
      </Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
    </Modal>
  )
}

/**
 * Nhân viên ký tay khi xác nhận (quyết định 17): ký bằng ngón tay, bút hoặc chuột vào khung, rồi bấm xác nhận. Tài khoản
 * đang đăng nhập vẫn là căn cứ xác định người ký; ảnh chữ ký được in vào ô của nhân viên trên PDF. Khi ký hoàn tất mà quản
 * trị đang bật gắn lượt tiếp nhận (quyết định 25), phải chọn lượt tiếp nhận của người bệnh trước khi ký.
 */
export function StaffSignModal({
  title,
  description,
  okText,
  loading,
  encounterSource,
  encounterHint,
  onSign,
  onClose,
}: {
  title: string
  description: string
  okText: string
  loading: boolean
  /**
   * Người bệnh (của phiếu hoặc bộ phiếu) để tra lượt tiếp nhận khi ký hoàn tất; bỏ trống khi không hoàn tất phiếu nào hoặc
   * chưa cấu hình API thông tin người bệnh. Bật/tắt bắt buộc do máy chủ trả lời mỗi lần mở hộp.
   */
  encounterSource?: EncounterSource | null
  /** Câu giải thích riêng ở phần chọn lượt tiếp nhận (ví dụ các phiếu được gắn khi xác nhận cả bộ). */
  encounterHint?: string
  onSign: (signature: StaffSignatureInput, encounter: EncounterChoice | null) => void
  onClose: () => void
}) {
  const pad = useRef<SignaturePadHandle>(null)
  const [hasInk, setHasInk] = useState(false)
  const specimen = useSignatureSpecimen()
  // Có chữ ký mẫu: ký bằng chữ ký mẫu, không cần vẽ; vẫn chọn được "Ký tay lần này".
  const [drawInstead, setDrawInstead] = useState(false)
  const useSpecimen = !!specimen.data && !drawInstead
  const [encounter, setEncounter] = useState<EncounterChoice | null>(null)
  const [encounterRange, setEncounterRange] = useState<EncounterRange | null>(null)
  const encounters = useEncounterList(encounterSource ?? null, encounterRange)
  // Quản trị tắt gắn lượt tiếp nhận (Cấu hình hệ thống, ví dụ khi HIS lỗi): ký hoàn tất không cần chọn lượt. Có hiệu lực
  // ngay cả khi hộp đang mở: bấm "Thử lại" là máy chủ trả lời đang tắt.
  const encounterOff = !encounterSource || encountersSwitchedOff(encounters.error)
  const missingEncounter = !encounterOff && !encounter
  const chosenEncounter = encounterOff ? null : encounter

  return (
    <Modal
      open
      title={title}
      okText={okText}
      cancelText="Đóng"
      width={encounterSource ? 800 : 640}
      okButtonProps={{ disabled: specimen.isPending || (!useSpecimen && !hasInk) || missingEncounter, loading }}
      onOk={() => {
        if (missingEncounter) {
          return
        }
        if (useSpecimen) {
          onSign({ useSpecimen: true }, chosenEncounter)
        } else if (pad.current) {
          onSign({ image: pad.current.toPngBase64() }, chosenEncounter)
        }
      }}
      onCancel={onClose}
    >
      <Typography.Paragraph>{description}</Typography.Paragraph>
      {!encounterOff &&
        (encounters.isPending ? (
          <Typography.Paragraph type="secondary">
            <Spin size="small" /> Đang kiểm tra lượt tiếp nhận…
          </Typography.Paragraph>
        ) : (
          <EncounterPicker
            list={encounters}
            range={encounterRange}
            onRangeChange={(next) => {
              setEncounterRange(next)
              // Lượt đã chọn có thể không còn trong danh sách mới: chọn lại cho chắc.
              setEncounter(null)
            }}
            value={encounter}
            onChange={setEncounter}
            hint={encounterHint}
          />
        ))}
      {specimen.isPending ? (
        <Spin />
      ) : useSpecimen ? (
        <Flex vertical gap={8}>
          <Typography.Text strong>Ký bằng chữ ký mẫu của bạn:</Typography.Text>
          <div
            style={{
              display: 'grid',
              placeItems: 'center',
              minHeight: 120,
              padding: 12,
              background: '#ffffff',
              border: '1px solid #e1eaeb',
              borderRadius: 10,
            }}
          >
            <SpecimenImage height={96} />
          </div>
          <Typography.Text type="secondary">
            {missingEncounter ? 'Chọn lượt tiếp nhận ở trên, rồi bấm' : 'Bấm'} “{okText}” để ký bằng chữ ký mẫu.{' '}
            <Button type="link" style={{ padding: 0, height: 'auto' }} onClick={() => setDrawInstead(true)}>
              Ký tay lần này
            </Button>
          </Typography.Text>
        </Flex>
      ) : (
        <>
          {specimen.data ? (
            <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
              Ký tay vào khung dưới.{' '}
              <Button type="link" style={{ padding: 0, height: 'auto' }} onClick={() => setDrawInstead(false)}>
                Dùng chữ ký mẫu
              </Button>
            </Typography.Paragraph>
          ) : (
            <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
              Tài khoản chưa có chữ ký mẫu: ký vào khung dưới. Đăng ký chữ ký mẫu ở{' '}
              <Link to="/tai-khoan">Tài khoản của tôi</Link> để lần sau không phải ký lại.
            </Typography.Paragraph>
          )}
          <SignaturePad ref={pad} onInkChange={setHasInk} disabled={loading} />
          {(!hasInk || missingEncounter) && (
            <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
              {missingEncounter && !hasInk
                ? `Chọn lượt tiếp nhận và ký vào khung trên để bật nút ${okText}.`
                : missingEncounter
                  ? `Chọn lượt tiếp nhận ở trên để bật nút ${okText}.`
                  : `Ký vào khung trên để bật nút ${okText}.`}
            </Typography.Paragraph>
          )}
        </>
      )}
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
  const events: { at: string; color?: string; text: string; statement?: string | null }[] = [
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
            text: `${signature.signerName} (${signerRoleLabels[signature.role].toLowerCase()}) ký tay trên điện thoại${
              form.decision ? ` – ${decisionLabels[form.decision]}` : ''
            }`,
            statement: signature.statement,
          }
        : {
            at: signature.signedAtUtc,
            color: 'blue',
            text: `${signature.signerName}${signature.signerTitle ? ` (${signature.signerTitle})` : ''} ký xác nhận${
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
              {event.statement && (
                <Typography.Text className="signer-statement" style={{ display: 'block' }}>
                  “{event.statement}”
                </Typography.Text>
              )}
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
