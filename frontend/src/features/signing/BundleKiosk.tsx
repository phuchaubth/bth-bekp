import { FullscreenOutlined, LockOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Collapse, ConfigProvider, Flex, Form, Grid, Radio, Result, Typography } from 'antd'
import { useMemo, useRef, useState } from 'react'
import {
  consentFormsApi,
  decisionLabels,
  type ConsentDecision,
  type ConsentFormDetail,
  type FormData,
} from '../../api/consentForms'
import type { TemplateStatement } from '../../api/consentTemplates'
import { ApiError } from '../../api/http'
import { errorMessage } from '../../utils/formErrors'
import { formatDate, formatDateTime } from '../../utils/format'
import { Bilingual } from '../consents/Bilingual'
import { DynamicForm } from '../consents/DynamicForm'
import { relationshipLabels } from '../patients/labels'
import { readyHint } from './kioskText'
import { SignaturePad, type SignaturePadHandle } from './SignaturePad'
import type { SigningSession } from './signingSession'
import { StaffUnlock } from './StaffUnlock'
import { StatementBox } from './StatementBox'

type Stage = 'signing' | 'signed' | 'ended'

const MAX_SUGGESTIONS = 8

/**
 * Một lời xác nhận cho cả bộ: nhãn của phiếu đầu có ô lời xác nhận; bắt buộc nếu có phiếu bắt buộc; gộp câu gợi ý của các
 * phiếu (không trùng, tối đa 8). Không phiếu nào có ô lời xác nhận thì không hiện.
 */
function bundleStatement(forms: ConsentFormDetail[]): TemplateStatement | undefined {
  const configs = forms.map((form) => form.definition.signatures?.statement).filter((config): config is TemplateStatement => !!config)
  if (configs.length === 0) {
    return undefined
  }
  const suggestions = [...new Set(configs.flatMap((config) => config.suggestions ?? []))].slice(0, MAX_SUGGESTIONS)
  return { label: configs[0].label, required: configs.some((config) => config.required), suggestions }
}

const asksDecision = (form: ConsentFormDetail) => form.definition.signatures?.decision !== 'none'

/**
 * Ký cả bộ trên điện thoại (quyết định 23, phương án b): người bệnh xem lần lượt các phiếu (chạm để mở nội dung), chọn
 * Đồng ý/Không đồng ý ở phiếu có hỏi, ghi một lời xác nhận và ký một lần; chữ ký và lời xác nhận dùng cho mọi phiếu.
 */
export function BundleKiosk({
  session,
  bundle,
}: {
  session: SigningSession
  bundle: NonNullable<SigningSession['bundle']>
}) {
  const { forms } = bundle
  const first = forms[0]
  const pad = useRef<SignaturePadHandle>(null)
  const [decisions, setDecisions] = useState<Record<string, ConsentDecision>>({})
  const [statement, setStatement] = useState('')
  const [hasInk, setHasInk] = useState(false)
  const [stage, setStage] = useState<Stage>(() => (Date.parse(session.expiresAtUtc) <= Date.now() ? 'ended' : 'signing'))
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [signedAt, setSignedAt] = useState<string | null>(null)
  const [unlockOpen, setUnlockOpen] = useState(false)
  const narrow = Grid.useBreakpoint().sm === false
  const cardSize = narrow ? 'small' : 'medium'

  const representative = first.representative
  const signerLabel = representative ? 'người đại diện' : 'người bệnh'
  const statementConfig = useMemo(() => bundleStatement(forms), [forms])
  const needsStatement = !!statementConfig?.required
  const asking = forms.filter(asksDecision)
  const missingDecisions = asking.filter((form) => !decisions[form.id]).length
  const ready = missingDecisions === 0 && hasInk && (!needsStatement || statement.trim().length > 0)

  const submit = async () => {
    if (!ready || !pad.current) {
      return
    }
    setSubmitting(true)
    setError(null)
    try {
      const result = await consentFormsApi.signBundleAsPatient(session.token, {
        image: pad.current.toPngBase64(),
        statement: statementConfig && statement.trim() ? statement.trim() : undefined,
        decisions: Object.fromEntries(asking.map((form) => [form.id, decisions[form.id]])),
      })
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
    <div style={{ minHeight: '100vh', background: '#f1f5f9', padding: narrow ? '12px 8px 40px' : '16px 16px 48px' }}>
      <Flex vertical gap={16} style={{ maxWidth: 960, margin: '0 auto' }}>
        <Flex justify="space-between" align="start" gap={12} wrap>
          <div>
            <Typography.Title level={3} style={{ margin: 0 }}>
              Ký {forms.length} phiếu một lần
            </Typography.Title>
            <Typography.Text type="secondary">
              Người bệnh {first.patient.fullName} · Ngày sinh {formatDate(first.patient.dateOfBirth)}
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
              title={`Đã ký xong ${forms.length} phiếu`}
              subTitle={`Ký lúc ${formatDateTime(signedAt)}. Vui lòng đưa máy lại cho nhân viên y tế.`}
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
            <Card title={`Các phiếu cần ký (${forms.length})`} size={cardSize}>
              <Flex vertical gap={12}>
                <Typography.Text type="secondary">Chạm “Xem nội dung” để đọc từng phiếu.</Typography.Text>
                {forms.map((form, index) => (
                  <BundleFormItem
                    key={form.id}
                    index={index}
                    form={form}
                    signerLabel={signerLabel}
                    decision={decisions[form.id]}
                    onDecision={(value) => setDecisions((current) => ({ ...current, [form.id]: value }))}
                    disabled={submitting}
                  />
                ))}
              </Flex>
            </Card>

            <Card title={`Chữ ký của ${signerLabel} cho cả ${forms.length} phiếu`} size={cardSize}>
              <Flex vertical gap={16}>
                <Typography.Text>
                  Người ký:{' '}
                  <Typography.Text strong>
                    {representative
                      ? `${representative.fullName} (${relationshipLabels[representative.relationship]}), ký thay cho người bệnh ${first.patient.fullName}`
                      : `${first.patient.fullName} (người bệnh)`}
                  </Typography.Text>
                </Typography.Text>
                {statementConfig && (
                  <StatementBox config={statementConfig} value={statement} onChange={setStatement} disabled={submitting} />
                )}
                <SignaturePad ref={pad} onInkChange={setHasInk} disabled={submitting} />
                <Typography.Text type="secondary">
                  Chữ ký và lời xác nhận này được dùng cho cả {forms.length} phiếu ở trên.
                </Typography.Text>
                {error && <Alert type="error" showIcon title={error} />}
                <Button type="primary" size="large" block loading={submitting} disabled={!ready} onClick={() => void submit()}>
                  Xác nhận chữ ký cho cả {forms.length} phiếu
                </Button>
                {!ready && (
                  <Typography.Text type="secondary" style={{ textAlign: 'center' }}>
                    {readyHint(missingDecisions > 0 ? `chọn ý kiến cho ${missingDecisions} phiếu còn thiếu` : null, needsStatement)}
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

/** Một phiếu trong bộ: tên phiếu, nội dung (chạm để mở, chỉ đọc) và câu hỏi Đồng ý/Không đồng ý nếu mẫu có hỏi. */
function BundleFormItem({
  index,
  form,
  signerLabel,
  decision,
  onDecision,
  disabled,
}: {
  index: number
  form: ConsentFormDetail
  signerLabel: string
  decision: ConsentDecision | undefined
  onDecision: (value: ConsentDecision) => void
  disabled: boolean
}) {
  const [previewForm] = Form.useForm<FormData>()
  const labels = form.definition.signatures
  const agreeLabel = labels?.agreeLabel ?? decisionLabels.Agreed
  const refuseLabel = labels?.refuseLabel ?? decisionLabels.Refused
  const question = labels?.decisionLabel ?? `Ý kiến của ${signerLabel}`

  return (
    <Card size="small" title={`${index + 1}. ${form.templateName}`} extra={<Typography.Text type="secondary">{form.formNo}</Typography.Text>}>
      <Flex vertical gap={12}>
        <Collapse
          size="small"
          items={[
            {
              key: 'content',
              label: 'Xem nội dung phiếu',
              children: (
                <ConfigProvider theme={{ token: { colorTextDisabled: 'rgba(0, 0, 0, 0.82)' } }}>
                  <DynamicForm definition={form.definition} form={previewForm} initialValues={form.data} disabled />
                </ConfigProvider>
              ),
            },
          ]}
        />
        {asksDecision(form) && (
          <div>
            <Typography.Paragraph strong style={{ marginBottom: 8 }}>
              <Bilingual text={question} />
            </Typography.Paragraph>
            <Radio.Group
              aria-label={`${question} – ${form.templateName}`}
              optionType="button"
              buttonStyle="solid"
              size="large"
              disabled={disabled}
              value={decision}
              onChange={(e) => onDecision(e.target.value as ConsentDecision)}
              options={[
                { value: 'Agreed', label: <Bilingual text={agreeLabel} /> },
                { value: 'Refused', label: <Bilingual text={refuseLabel} /> },
              ]}
            />
          </div>
        )}
      </Flex>
    </Card>
  )
}
