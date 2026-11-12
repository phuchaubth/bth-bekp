import { FullscreenOutlined, LockOutlined } from '@ant-design/icons'
import { Alert, Button, Card, ConfigProvider, Flex, Form, Input, Radio, Result, Typography } from 'antd'
import { useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { consentFormsApi, decisionLabels, type ConsentDecision, type FormData } from '../../api/consentForms'
import { ApiError } from '../../api/http'
import { useAuth } from '../../auth/authContext'
import { LOGIN_PATH } from '../../auth/paths'
import { errorMessage } from '../../utils/formErrors'
import { formatDate, formatDateTime } from '../../utils/format'
import { DynamicForm } from '../consents/DynamicForm'
import { relationshipLabels } from '../patients/labels'
import { SignaturePad, type SignaturePadHandle } from './SignaturePad'
import { signingSession, type SigningSession } from './signingSession'

type Stage = 'signing' | 'signed' | 'ended'

/**
 * Màn hình 6 – chế độ ký trên máy tính bảng. Phiên đăng nhập của nhân viên đã đóng; trang chỉ có token phiên ký để gửi
 * chữ ký cho đúng phiếu này. Muốn thoát phải đăng nhập lại bằng tài khoản nhân viên.
 */
export function SigningKioskPage() {
  const [session] = useState(() => signingSession.get())

  if (!session) {
    return <NoSession />
  }

  return (
    <ConfigProvider theme={{ token: { fontSize: 16 } }}>
      <Kiosk session={session} />
    </ConfigProvider>
  )
}

function NoSession() {
  const navigate = useNavigate()
  return (
    <Result
      status="info"
      title="Không có phiên ký đang mở"
      subTitle="Phiên ký kết thúc khi tải lại trang hoặc hết hạn. Nhân viên đăng nhập để mở lại phiếu."
      extra={
        <Button type="primary" onClick={() => navigate(LOGIN_PATH, { replace: true })}>
          Đăng nhập
        </Button>
      }
    />
  )
}

function Kiosk({ session }: { session: SigningSession }) {
  const { form } = session
  const [previewForm] = Form.useForm<FormData>()
  const pad = useRef<SignaturePadHandle>(null)
  const [decision, setDecision] = useState<ConsentDecision>()
  const [hasInk, setHasInk] = useState(false)
  const [stage, setStage] = useState<Stage>(() => (Date.parse(session.expiresAtUtc) <= Date.now() ? 'ended' : 'signing'))
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [signedAt, setSignedAt] = useState<string | null>(null)
  const [unlockOpen, setUnlockOpen] = useState(false)

  const representative = form.representative
  const signerLabel = representative ? 'người đại diện' : 'người bệnh'

  const submit = async () => {
    if (!decision || !pad.current) {
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      const result = await consentFormsApi.signAsPatient(form.id, session.token, { decision, image: pad.current.toPngBase64() })
      setSignedAt(result.signedAtUtc)
      setStage('signed')
    } catch (e) {
      if (e instanceof ApiError && (e.status === 401 || e.status === 409)) {
        setStage('ended')
      }
      setError(errorMessage(e))
    } finally {
      setSubmitting(false)
    }
  }

  const canFullscreen = typeof document.documentElement.requestFullscreen === 'function' && !document.fullscreenElement

  return (
    <div style={{ minHeight: '100vh', background: '#f1f5f9', padding: '16px 16px 48px' }}>
      <Flex vertical gap={16} style={{ maxWidth: 960, margin: '0 auto' }}>
        <Flex justify="space-between" align="start" gap={12} wrap>
          <div>
            <Typography.Title level={3} style={{ margin: 0 }}>
              {form.templateName}
            </Typography.Title>
            <Typography.Text type="secondary">
              Số phiếu {form.formNo} · Người bệnh {form.patient.fullName} · Ngày sinh {formatDate(form.patient.dateOfBirth)}
            </Typography.Text>
          </div>
          {canFullscreen && (
            <Button icon={<FullscreenOutlined />} onClick={() => void document.documentElement.requestFullscreen().catch(() => {})}>
              Toàn màn hình
            </Button>
          )}
        </Flex>

        {stage === 'signed' ? (
          <Card>
            <Result
              status="success"
              title="Đã ghi nhận chữ ký"
              subTitle={`Ký lúc ${formatDateTime(signedAt)} · ${decision ? decisionLabels[decision] : ''}. Vui lòng đưa máy lại cho nhân viên y tế.`}
            />
          </Card>
        ) : stage === 'ended' ? (
          <Alert
            type="warning"
            showIcon
            title="Phiên ký đã kết thúc"
            description={error ?? 'Phiên ký đã hết hạn hoặc đã được thay bằng phiên khác. Nhân viên đăng nhập lại để mở phiên mới.'}
          />
        ) : (
          <>
            <Card title="Nội dung phiếu (chỉ đọc)">
              {/* Nội dung khóa nhưng phải dễ đọc: chữ của ô bị khóa đậm hơn mặc định. */}
              <ConfigProvider theme={{ token: { colorTextDisabled: 'rgba(0, 0, 0, 0.82)' } }}>
                <DynamicForm definition={form.definition} form={previewForm} initialValues={form.data} disabled />
              </ConfigProvider>
            </Card>

            <Card title={`Ý kiến và chữ ký của ${signerLabel}`}>
              <Flex vertical gap={16}>
                <Typography.Text>
                  Người ký:{' '}
                  <Typography.Text strong>
                    {representative
                      ? `${representative.fullName} (${relationshipLabels[representative.relationship]}), ký thay cho người bệnh ${form.patient.fullName}`
                      : `${form.patient.fullName} (người bệnh)`}
                  </Typography.Text>
                </Typography.Text>
                <div>
                  <Typography.Paragraph strong style={{ marginBottom: 8 }}>
                    Ý kiến của {signerLabel}
                  </Typography.Paragraph>
                  <Radio.Group
                    aria-label={`Ý kiến của ${signerLabel}`}
                    optionType="button"
                    buttonStyle="solid"
                    size="large"
                    value={decision}
                    onChange={(e) => setDecision(e.target.value as ConsentDecision)}
                    options={[
                      { value: 'Agreed', label: decisionLabels.Agreed },
                      { value: 'Refused', label: decisionLabels.Refused },
                    ]}
                  />
                </div>
                <SignaturePad ref={pad} onInkChange={setHasInk} disabled={submitting} />
                {error && <Alert type="error" showIcon title={error} />}
                <Button
                  type="primary"
                  size="large"
                  block
                  loading={submitting}
                  disabled={!decision || !hasInk}
                  onClick={() => void submit()}
                >
                  Xác nhận chữ ký
                </Button>
                {(!decision || !hasInk) && (
                  <Typography.Text type="secondary" style={{ textAlign: 'center' }}>
                    Chọn Đồng ý hoặc Từ chối và ký vào khung trên để xác nhận.
                  </Typography.Text>
                )}
              </Flex>
            </Card>
          </>
        )}

        {stage === 'signing' && !unlockOpen ? (
          <Button type="link" icon={<LockOutlined />} onClick={() => setUnlockOpen(true)} style={{ alignSelf: 'center' }}>
            Nhân viên: thoát chế độ ký
          </Button>
        ) : (
          <StaffUnlock session={session} />
        )}
      </Flex>
    </div>
  )
}

/** Thoát chế độ ký: nhân viên đăng nhập lại (máy chủ kiểm tra mật khẩu, khóa tạm nếu sai nhiều lần). */
function StaffUnlock({ session }: { session: SigningSession }) {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const onFinish = async (values: { userName: string; password: string }) => {
    setBusy(true)
    setError(null)
    try {
      await login(values.userName.trim(), values.password)
      signingSession.clear()
      if (document.fullscreenElement) {
        void document.exitFullscreen().catch(() => {})
      }
      navigate(`/phieu-dong-thuan/${session.form.id}`, { replace: true })
    } catch (e) {
      setError(errorMessage(e))
      setBusy(false)
    }
  }

  return (
    <Card title="Nhân viên đăng nhập để thoát chế độ ký" style={{ maxWidth: 480, width: '100%', alignSelf: 'center' }}>
      <Form layout="vertical" initialValues={{ userName: session.staffUserName }} onFinish={(values) => void onFinish(values)} requiredMark={false}>
        <Form.Item label="Tên đăng nhập" name="userName" rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập.' }]}>
          <Input autoComplete="username" />
        </Form.Item>
        <Form.Item label="Mật khẩu" name="password" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu.' }]}>
          <Input.Password autoComplete="current-password" autoFocus />
        </Form.Item>
        {error && <Alert type="error" showIcon title={error} style={{ marginBottom: 16 }} />}
        <Button type="primary" htmlType="submit" block loading={busy}>
          Đăng nhập và thoát chế độ ký
        </Button>
      </Form>
    </Card>
  )
}
