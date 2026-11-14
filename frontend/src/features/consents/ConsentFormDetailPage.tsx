import {
  CheckCircleOutlined,
  CopyOutlined,
  EditOutlined,
  FilePdfOutlined,
  RollbackOutlined,
  SaveOutlined,
  SendOutlined,
  StopOutlined,
} from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Descriptions, Flex, Form, Input, Modal, Popconfirm, Radio, Select, Space, Spin, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import {
  consentFormKeys,
  consentFormsApi,
  decisionLabels,
  useConsentForm,
  type ConsentFormDetail,
  type FormData,
  type SignerKind,
} from '../../api/consentForms'
import { ApiError } from '../../api/http'
import { usePatient } from '../../api/patients'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { maskCitizenId } from '../patients/cccd'
import { relationshipLabels } from '../patients/labels'
import { signerRoleLabel } from '../templates/signerRoles'
import { DynamicForm } from './DynamicForm'
import { FormStatusTag } from './FormStatusTag'
import { FormHistory, RevokeFormModal, StaffSignModal, StartSigningModal } from './SigningActions'

/**
 * Màn hình 8 (và bước soạn nội dung của màn hình 5): chi tiết phiếu, soạn phiếu nháp, trình ký, chuyển sang chế độ ký
 * trên điện thoại, xác nhận phía bệnh viện, xem PDF, thu hồi và lập phiếu thay thế.
 */
export function ConsentFormDetailPage() {
  const { id = '' } = useParams()
  const { hasPermission } = useAuth()
  const { data: form, isPending, isError, error } = useConsentForm(id)
  const [pdfOpen, setPdfOpen] = useState(false)
  const [cancelOpen, setCancelOpen] = useState(false)
  const [signingOpen, setSigningOpen] = useState(false)
  const [revokeOpen, setRevokeOpen] = useState(false)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { message } = App.useApp()
  const canManage = hasPermission(Permissions.ConsentsCreate)

  const returnToDraft = useMutation({
    mutationFn: () => consentFormsApi.returnToDraft(id),
    onSuccess: (updated) => {
      queryClient.setQueryData(consentFormKeys.detail(id), updated)
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      message.success('Đã trả phiếu về nháp.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  // Hộp thoại ký của nhân viên: bước trung gian đang chờ, hoặc xác nhận cuối.
  const [staffSign, setStaffSign] = useState<'step' | 'complete' | null>(null)

  const complete = useMutation({
    mutationFn: (image: string) => consentFormsApi.complete(id, image),
    onSuccess: (updated) => {
      queryClient.setQueryData(consentFormKeys.detail(id), updated)
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      setStaffSign(null)
      message.success('Đã xác nhận và hoàn tất phiếu. PDF đã ký được lưu.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const confirmStep = useMutation({
    mutationFn: ({ stepKey, image }: { stepKey: string; image: string }) => consentFormsApi.confirmStep(id, stepKey, image),
    onSuccess: (updated) => {
      queryClient.setQueryData(consentFormKeys.detail(id), updated)
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      setStaffSign(null)
      message.success('Đã xác nhận bước của bạn.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const replace = useMutation({
    mutationFn: (source: ConsentFormDetail) =>
      consentFormsApi.create({ patientId: source.patient.id, templateId: source.templateId, replacesFormId: source.id }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      message.success(`Đã lập phiếu thay thế ${created.formNo}; nội dung được chép từ phiếu cũ.`)
      navigate(`/phieu-dong-thuan/${created.id}`)
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  if (isPending) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }

  if (isError) {
    return <Alert type="error" showIcon title={errorMessage(error)} />
  }

  const nextStep = form.steps.find((step) => step.isNext)

  return (
    <>
      <PageHeader
        title={form.templateName}
        extra={
          <Space wrap>
            {form.actions.canStartSigning && (
              <Button type="primary" icon={<EditOutlined />} onClick={() => setSigningOpen(true)}>
                Ký trên điện thoại
              </Button>
            )}
            {form.actions.canConfirmStep && nextStep && (
              <Button type="primary" icon={<CheckCircleOutlined />} onClick={() => setStaffSign('step')}>
                Xác nhận: {nextStep.label}
              </Button>
            )}
            {form.actions.canConfirm && (
              <Button type="primary" icon={<CheckCircleOutlined />} onClick={() => setStaffSign('complete')}>
                Xác nhận và hoàn tất
              </Button>
            )}
            <Button icon={<FilePdfOutlined />} onClick={() => setPdfOpen(true)}>
              {form.signedPdfSha256 ? 'Xem PDF đã ký' : 'Xem PDF'}
            </Button>
            {canManage && form.actions.canReturnToDraft && (
              <Popconfirm
                title="Trả phiếu về nháp?"
                description={
                  form.signatures.length > 0
                    ? 'Chữ ký của người bệnh sẽ bị bỏ; sau khi sửa phải ký lại.'
                    : 'Nội dung được mở khóa để sửa; sau đó trình ký lại.'
                }
                okText="Trả về nháp"
                cancelText="Đóng"
                onConfirm={() => returnToDraft.mutate()}
              >
                <Button icon={<RollbackOutlined />} loading={returnToDraft.isPending}>
                  Trả về nháp
                </Button>
              </Popconfirm>
            )}
            {canManage && form.actions.canCancel && (
              <Button danger icon={<StopOutlined />} onClick={() => setCancelOpen(true)}>
                Hủy phiếu
              </Button>
            )}
            {form.actions.canRevoke && (
              <Button danger icon={<StopOutlined />} onClick={() => setRevokeOpen(true)}>
                Thu hồi
              </Button>
            )}
            {form.actions.canReplace && (
              <Button icon={<CopyOutlined />} loading={replace.isPending} onClick={() => replace.mutate(form)}>
                Lập phiếu thay thế
              </Button>
            )}
          </Space>
        }
      />
      <Flex vertical gap={16}>
        <FormSummary form={form} />
        {canManage && form.actions.canEdit ? <DraftEditor form={form} /> : <ReadOnlyContent form={form} />}
        <FormHistory form={form} />
      </Flex>
      <PdfPreviewModal form={form} open={pdfOpen} onClose={() => setPdfOpen(false)} />
      {cancelOpen && <CancelFormModal form={form} onClose={() => setCancelOpen(false)} />}
      {signingOpen && <StartSigningModal form={form} onClose={() => setSigningOpen(false)} />}
      {revokeOpen && <RevokeFormModal form={form} onClose={() => setRevokeOpen(false)} />}
      {staffSign === 'step' && nextStep && (
        <StaffSignModal
          title={`Ký xác nhận: ${nextStep.label}`}
          description="Ký vào khung bên dưới để xác nhận phần của bạn. Chữ ký được in vào ô của bạn trên phiếu, kèm họ tên và thời điểm theo tài khoản đang đăng nhập. Mỗi bước cần một người khác nhau."
          okText="Ký và xác nhận"
          loading={confirmStep.isPending}
          onSign={(image) => confirmStep.mutate({ stepKey: nextStep.key, image })}
          onClose={() => setStaffSign(null)}
        />
      )}
      {staffSign === 'complete' && (
        <StaffSignModal
          title="Ký xác nhận và hoàn tất phiếu"
          description="Ký vào khung bên dưới để xác nhận. Sau khi xác nhận, phiếu bị khóa và PDF đã ký được lưu kèm mã băm."
          okText="Ký và hoàn tất"
          loading={complete.isPending}
          onSign={(image) => complete.mutate(image)}
          onClose={() => setStaffSign(null)}
        />
      )}
    </>
  )
}

function FormSummary({ form }: { form: ConsentFormDetail }) {
  return (
    <Card>
      <Descriptions size="small" column={{ xs: 1, md: 3 }}>
        <Descriptions.Item label="Số phiếu">
          <Typography.Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
            {form.formNo}
          </Typography.Text>
        </Descriptions.Item>
        <Descriptions.Item label="Trạng thái">
          <FormStatusTag status={form.status} />
        </Descriptions.Item>
        <Descriptions.Item label="Mẫu phiếu">
          {form.templateCode} · phiên bản {form.templateVersionNo}
        </Descriptions.Item>
        <Descriptions.Item label="Người bệnh">
          <Link to={`/nguoi-benh/${form.patient.id}`}>{form.patient.fullName}</Link>
        </Descriptions.Item>
        <Descriptions.Item label="Số CCCD">{maskCitizenId(form.patient.citizenId)}</Descriptions.Item>
        <Descriptions.Item label="Khoa/phòng">{form.departmentName}</Descriptions.Item>
        <Descriptions.Item label="Người ký phía người bệnh">
          {form.representative
            ? `${form.representative.fullName} (${relationshipLabels[form.representative.relationship]})`
            : 'Người bệnh tự ký'}
        </Descriptions.Item>
        <Descriptions.Item label="Người lập">{form.createdByName}</Descriptions.Item>
        <Descriptions.Item label="Lập lúc">{formatDateTime(form.createdAtUtc)}</Descriptions.Item>
        {form.submittedAtUtc && <Descriptions.Item label="Trình ký lúc">{formatDateTime(form.submittedAtUtc)}</Descriptions.Item>}
        {form.decision && <Descriptions.Item label="Ý kiến người bệnh">{decisionLabels[form.decision]}</Descriptions.Item>}
        {form.replacesForm && (
          <Descriptions.Item label="Thay thế cho phiếu">
            <Link to={`/phieu-dong-thuan/${form.replacesForm.id}`}>{form.replacesForm.formNo}</Link>
          </Descriptions.Item>
        )}
        {form.replacedBy && (
          <Descriptions.Item label="Phiếu thay thế">
            <Link to={`/phieu-dong-thuan/${form.replacedBy.id}`}>{form.replacedBy.formNo}</Link>
          </Descriptions.Item>
        )}
        {form.cancelReason && (
          <Descriptions.Item label="Lý do hủy" span="filled">
            {form.cancelReason} ({formatDateTime(form.cancelledAtUtc)})
          </Descriptions.Item>
        )}
        {form.revokeReason && (
          <Descriptions.Item label="Lý do thu hồi" span="filled">
            {form.revokeReason} ({formatDateTime(form.revokedAtUtc)})
          </Descriptions.Item>
        )}
      </Descriptions>
      {form.notices.map((notice) => (
        <Alert key={notice} type="warning" showIcon style={{ marginTop: 12 }} title={notice} />
      ))}
      <StatusNotice form={form} />
    </Card>
  )
}

/** Tiến trình các bước xác nhận theo vai trò (mẫu có `signatures.steps`). */
function StepProgress({ form }: { form: ConsentFormDetail }) {
  return (
    <ol aria-label="Các bước xác nhận" style={{ margin: '4px 0 8px', paddingInlineStart: 20 }}>
      {form.steps.map((step) => (
        <li key={step.key}>
          {step.label}:{' '}
          {step.confirmation ? (
            <Typography.Text type="success">
              {step.confirmation.signerName} đã xác nhận lúc {formatDateTime(step.confirmation.signedAtUtc)}
            </Typography.Text>
          ) : (
            <Typography.Text type="secondary">{step.isNext ? 'đang chờ' : 'chưa đến lượt'} ({step.roleName})</Typography.Text>
          )}
        </li>
      ))}
      <li>
        Xác nhận cuối ({signerRoleLabel(form.hospitalSignerRoleCode)}):{' '}
        <Typography.Text type="secondary">{form.steps.every((s) => s.confirmation) ? 'đang chờ' : 'chưa đến lượt'}</Typography.Text>
      </li>
    </ol>
  )
}

/** Bước tiếp theo của phiếu đang chờ ký / trạng thái cuối. */
function StatusNotice({ form }: { form: ConsentFormDetail }) {
  const patientSignature = form.signatures.find((s) => s.method === 'Handwritten')
  const pendingStep = form.steps.find((step) => step.isNext)
  if (form.status === 'PendingSignature' && !patientSignature) {
    return (
      <Alert
        type="info"
        showIcon
        style={{ marginTop: 12 }}
        title="Phiếu đang chờ ký"
        description="Nội dung và thông tin người bệnh đã được khóa. Bấm “Ký trên điện thoại” để người bệnh (hoặc người đại diện) ký; muốn sửa nội dung, hãy trả phiếu về nháp."
      />
    )
  }
  if (form.status === 'PendingSignature' && patientSignature) {
    return (
      <Alert
        type="success"
        showIcon
        style={{ marginTop: 12 }}
        title={`${patientSignature.signerName} đã ký lúc ${formatDateTime(patientSignature.signedAtUtc)}${
          form.decision ? ` (${decisionLabels[form.decision]})` : ''
        }`}
        description={
          <>
            {form.steps.length > 0 && <StepProgress form={form} />}
            {pendingStep
              ? form.actions.canConfirmStep
                ? `Bấm “Xác nhận: ${pendingStep.label}” và ký vào khung để xác nhận phần của bạn.`
                : `Chờ nhân viên có vai trò ${pendingStep.roleName} xác nhận bước “${pendingStep.label}”.`
              : form.actions.canConfirm
                ? 'Bấm “Xác nhận và hoàn tất”, ký vào khung để xác nhận và khóa phiếu.'
                : `Chờ nhân viên có vai trò ${signerRoleLabel(form.hospitalSignerRoleCode)} xác nhận bằng tài khoản.`}
          </>
        }
      />
    )
  }
  if (form.status === 'Completed') {
    return (
      <Alert
        type="success"
        showIcon
        style={{ marginTop: 12 }}
        title="Phiếu đã hoàn tất"
        description="Phiếu đã khóa, không sửa được. PDF đã ký được lưu kèm mã băm; muốn thay đổi thì thu hồi rồi lập phiếu thay thế."
      />
    )
  }
  return null
}

function DraftEditor({ form }: { form: ConsentFormDetail }) {
  const [antForm] = Form.useForm<FormData>()
  const [signerKind, setSignerKind] = useState<SignerKind>(form.signerKind)
  const [representativeId, setRepresentativeId] = useState<string | null>(form.representative?.id ?? null)
  const [submitErrors, setSubmitErrors] = useState<Record<string, string[]>>()
  const patient = usePatient(form.patient.id)
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const save = () =>
    consentFormsApi.update(form.id, {
      // true: lấy cả giá trị của trường đang ẩn (máy chủ giữ lại trong bản nháp).
      data: antForm.getFieldsValue(true),
      signerKind,
      representativeId: signerKind === 'Representative' ? representativeId : null,
    })

  const onSaved = (updated: ConsentFormDetail) => {
    queryClient.setQueryData(consentFormKeys.detail(form.id), updated)
    void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
  }

  const saveDraft = useMutation({
    mutationFn: save,
    onSuccess: (updated) => {
      onSaved(updated)
      message.success('Đã lưu nháp.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const submit = useMutation({
    mutationFn: async () => {
      await save()
      return consentFormsApi.submit(form.id)
    },
    onSuccess: (updated) => {
      setSubmitErrors(undefined)
      onSaved(updated)
      message.success('Đã trình ký. Nội dung phiếu đã được khóa.')
    },
    onError: (e) => {
      if (e instanceof ApiError && e.fieldErrors) {
        setSubmitErrors(e.fieldErrors)
      }
      message.error(errorMessage(e))
    },
  })

  const busy = saveDraft.isPending || submit.isPending
  const missing = Object.keys(submitErrors ?? {}).length

  // Người dùng sửa trường nào thì bỏ lỗi máy chủ của trường đó (lỗi cũ không còn đúng).
  const clearErrors = (keys: string[]) =>
    setSubmitErrors((current) => {
      if (!current || !keys.some((key) => key in current)) {
        return current
      }
      const rest = Object.fromEntries(Object.entries(current).filter(([key]) => !keys.includes(key)))
      return Object.keys(rest).length > 0 ? rest : undefined
    })

  return (
    <Card
      title="Nội dung phiếu"
      extra={
        <Space wrap>
          <Button icon={<SaveOutlined />} loading={saveDraft.isPending} disabled={busy} onClick={() => saveDraft.mutate()}>
            Lưu nháp
          </Button>
          <Button type="primary" icon={<SendOutlined />} loading={submit.isPending} disabled={busy} onClick={() => submit.mutate()}>
            Trình ký
          </Button>
        </Space>
      }
    >
      <Flex vertical gap={8} style={{ marginBottom: 16 }}>
        <Typography.Text strong>Người ký phía người bệnh</Typography.Text>
        <Radio.Group
          value={signerKind}
          onChange={(e) => {
            setSignerKind(e.target.value as SignerKind)
            clearErrors(['representativeId'])
          }}
          options={[
            { value: 'Patient', label: 'Người bệnh tự ký' },
            { value: 'Representative', label: 'Người đại diện ký thay', disabled: !patient.data?.representatives.length },
          ]}
        />
        {signerKind === 'Representative' && (
          <Select
            placeholder="Chọn người đại diện"
            aria-label="Người đại diện ký thay"
            value={representativeId ?? undefined}
            onChange={(value: string) => {
              setRepresentativeId(value)
              clearErrors(['representativeId'])
            }}
            style={{ maxWidth: 420 }}
            options={patient.data?.representatives.map((r) => ({
              value: r.id,
              label: `${r.fullName} – ${relationshipLabels[r.relationship]}`,
            }))}
          />
        )}
        {submitErrors?.representativeId && <Typography.Text type="danger">{submitErrors.representativeId.join(' ')}</Typography.Text>}
        {patient.data && patient.data.representatives.length === 0 && (
          <Typography.Text type="secondary">
            Người bệnh chưa có người đại diện. Thêm ở <Link to={`/nguoi-benh/${form.patient.id}`}>hồ sơ người bệnh</Link>.
          </Typography.Text>
        )}
      </Flex>
      {missing > 0 && (
        <Alert type="error" showIcon style={{ marginBottom: 16 }} title={`Còn ${missing} mục chưa đạt yêu cầu; xem các ô được đánh dấu đỏ.`} />
      )}
      <DynamicForm
        definition={form.definition}
        form={antForm}
        initialValues={form.data}
        errors={submitErrors}
        disabled={busy}
        onValuesChange={(changed) => clearErrors(Object.keys(changed))}
      />
    </Card>
  )
}

function ReadOnlyContent({ form }: { form: ConsentFormDetail }) {
  const [antForm] = Form.useForm<FormData>()
  return (
    <Card title="Nội dung phiếu">
      <DynamicForm definition={form.definition} form={antForm} initialValues={form.data} disabled />
    </Card>
  )
}

/** Đường dẫn tạm cho dữ liệu nhị phân; tự thu hồi khi dữ liệu đổi hoặc component bị gỡ. */
function useObjectUrl(blob: Blob | undefined): string | null {
  const url = useMemo(() => (blob ? URL.createObjectURL(blob) : null), [blob])
  useEffect(() => () => {
    if (url) {
      URL.revokeObjectURL(url)
    }
  }, [url])
  return url
}

/** Tải PDF có xác thực (chỉ khi mở) rồi hiển thị trong khung xem. */
function PdfPreviewModal({ form, open, onClose }: { form: ConsentFormDetail; open: boolean; onClose: () => void }) {
  const pdf = useQuery({
    queryKey: ['consent-form-pdf', form.id, form.updatedAtUtc],
    queryFn: () => consentFormsApi.pdf(form.id),
    enabled: open,
    gcTime: 0,
  })
  const url = useObjectUrl(open ? pdf.data : undefined)
  const error = pdf.isError ? errorMessage(pdf.error) : null

  return (
    <Modal
      open={open}
      onCancel={onClose}
      title={`PDF phiếu ${form.formNo}`}
      width="min(1000px, 96vw)"
      footer={
        url && (
          <a href={url} download={`${form.formNo}.pdf`}>
            <Button icon={<FilePdfOutlined />}>Tải PDF</Button>
          </a>
        )
      }
      destroyOnHidden
    >
      {error ? (
        <Alert type="error" showIcon title={error} />
      ) : url ? (
        <iframe title={`PDF phiếu ${form.formNo}`} src={url} style={{ width: '100%', height: '75vh', border: 0 }} />
      ) : (
        <Spin style={{ display: 'block', padding: 48 }} />
      )}
    </Modal>
  )
}

function CancelFormModal({ form, onClose }: { form: ConsentFormDetail; onClose: () => void }) {
  const [reason, setReason] = useState('')
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const cancel = useMutation({
    mutationFn: () => consentFormsApi.cancel(form.id, reason.trim()),
    onSuccess: (updated) => {
      queryClient.setQueryData(consentFormKeys.detail(form.id), updated)
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      message.success('Đã hủy phiếu.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  const valid = reason.trim().length >= 5

  return (
    <Modal
      open
      title={`Hủy phiếu ${form.formNo}`}
      okText="Hủy phiếu"
      cancelText="Đóng"
      okButtonProps={{ danger: true, disabled: !valid, loading: cancel.isPending }}
      onOk={() => cancel.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph>Phiếu đã hủy không dùng được nữa; muốn làm lại thì lập phiếu mới.</Typography.Paragraph>
      <Input.TextArea
        aria-label="Lý do hủy"
        placeholder="Lý do hủy (ít nhất 5 ký tự)"
        value={reason}
        maxLength={500}
        showCount
        onChange={(e) => setReason(e.target.value)}
        autoSize={{ minRows: 3 }}
      />
    </Modal>
  )
}
