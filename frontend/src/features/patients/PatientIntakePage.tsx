import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Divider, Flex, Form, Space, Spin, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { useFeatures } from '../../api/features'
import { ApiError } from '../../api/http'
import { patientKeys, patientsApi, type PatientLookupResult, type PatientSourceLookupResult } from '../../api/patients'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { formatDate } from '../../utils/format'
import { maskCitizenId } from './cccd'
import { parseCccdQr } from './cccdQrParser'
import { HisInfo } from './HisInfo'
import { PatientForm } from './PatientForm'
import {
  cardToFormValues,
  formValuesToInput,
  missingFields,
  onlyEmpty,
  patientToFormValues,
  sourceToFormValues,
  type MissingFields,
  type PatientFormValues,
} from './patientFormValues'
import { QrScanInput } from './QrScanInput'
import { SourceLookup } from './SourceLookup'

/**
 * Màn hình 4: tiếp nhận người bệnh. Quét QR CCCD → tự điền thông tin → tra cứu hồ sơ đã có (và HIS nếu đã kết nối) →
 * bổ sung thông tin còn thiếu → lưu. Khi lập phiếu (Sprint 3), bước này được dùng lại làm bước 1.
 */
export function PatientIntakePage() {
  const [form] = Form.useForm<PatientFormValues>()
  const navigate = useNavigate()
  // Mở từ màn hình lập phiếu: lưu xong quay lại lập phiếu cho người bệnh này.
  const [params] = useSearchParams()
  const thenCreateForm = params.get('tiepTheo') === 'lap-phieu'
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const { his, patientSource } = useFeatures()
  const [scanErrors, setScanErrors] = useState<string[]>([])
  const [scanWarnings, setScanWarnings] = useState<string[]>([])
  const [lookup, setLookup] = useState<PatientLookupResult | null>(null)
  const [updatingPatientId, setUpdatingPatientId] = useState<string | null>(null)
  const [duplicateId, setDuplicateId] = useState<string | null>(null)
  // Kết quả lấy thông tin từ hệ thống bệnh viện: các ô còn trống cần nhập thêm.
  const [sourceFilled, setSourceFilled] = useState<MissingFields | null>(null)

  const showMissing = () => setSourceFilled(missingFields(form.getFieldsValue(true) as Partial<PatientFormValues>))

  // Quét CCCD khi đã cấu hình nguồn: lấy thêm thông tin từ hệ thống bệnh viện, chỉ điền vào ô còn trống (thẻ được ưu tiên).
  const sourceByCard = useMutation({
    mutationFn: (citizenId: string) => patientsApi.lookupSource({ citizenId }),
    onSuccess: (result) => {
      if (result.status === 'Found' && result.patient) {
        form.setFieldsValue(onlyEmpty(form.getFieldsValue(true) as Partial<PatientFormValues>, sourceToFormValues(result.patient)))
        showMissing()
      }
    },
  })

  const lookupMutation = useMutation({
    mutationFn: (citizenId: string) => patientsApi.lookup(citizenId),
    onSuccess: (result) => {
      setLookup(result)
      const his = result.hisPatient
      if (result.hisStatus === 'Found' && his) {
        // Chỉ điền vào ô còn trống, không ghi đè dữ liệu người dùng đã nhập.
        if (!form.getFieldValue('patientCode')) {
          form.setFieldValue('patientCode', his.patientCode)
        }
        if (his.healthInsuranceNumber && !form.getFieldValue('healthInsuranceNumber')) {
          form.setFieldValue('healthInsuranceNumber', his.healthInsuranceNumber)
        }
      }
    },
    onError: (error) => message.error(errorMessage(error)),
  })

  const save = useMutation({
    mutationFn: (values: PatientFormValues) => {
      const input = formValuesToInput(values)
      return updatingPatientId ? patientsApi.update(updatingPatientId, input) : patientsApi.create(input)
    },
    onSuccess: async (patient) => {
      message.success(updatingPatientId ? 'Đã cập nhật hồ sơ người bệnh.' : 'Đã tạo hồ sơ người bệnh.')
      await queryClient.invalidateQueries({ queryKey: patientKeys.all })
      navigate(thenCreateForm ? `/phieu-dong-thuan/tao-moi?patientId=${patient.id}` : `/nguoi-benh/${patient.id}`)
    },
    onError: (error) => {
      if (error instanceof ApiError && error.code === 'patients.duplicate_citizen_id') {
        const existingId = error.problem?.existingPatientId
        setDuplicateId(typeof existingId === 'string' ? existingId : null)
        message.error(error.message)
        return
      }
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  const onScan = (raw: string) => {
    const result = parseCccdQr(raw)
    if (!result.ok) {
      setScanErrors(result.errors)
      setScanWarnings([])
      return
    }

    setScanErrors([])
    setScanWarnings(result.warnings)
    setSourceFilled(null)
    setLookup(null)
    setUpdatingPatientId(null)
    setDuplicateId(null)
    form.resetFields()
    form.setFieldsValue(cardToFormValues(result.card))
    lookupMutation.mutate(result.card.citizenId)
    if (patientSource) {
      sourceByCard.mutate(result.card.citizenId)
    }
  }

  // Thông tin từ hệ thống bệnh viện: điền vào form; nếu đã có hồ sơ (cùng CCCD) thì gợi ý mở/cập nhật hồ sơ đó.
  const onSourceFound = (result: PatientSourceLookupResult) => {
    setScanErrors([])
    setScanWarnings([])
    setUpdatingPatientId(null)
    setDuplicateId(null)
    form.resetFields()
    form.setFieldsValue(sourceToFormValues(result.patient!))
    setLookup({ existingPatient: result.existingPatient, hisStatus: 'Disabled', hisPatient: null })
    showMissing()
  }

  const updateExistingRecord = () => {
    const existing = lookup?.existingPatient
    if (!existing) {
      return
    }
    // Dữ liệu trên thẻ (vừa quét) được ưu tiên, các thông tin bổ sung giữ theo hồ sơ cũ.
    const fromCard = Object.fromEntries(
      Object.entries(form.getFieldsValue(['fullName', 'dateOfBirth', 'gender', 'citizenId', 'oldIdNumber', 'citizenIdIssueDate', 'permanentAddress']))
        .filter(([, value]) => value !== undefined && value !== null && value !== ''),
    )
    form.setFieldsValue({ ...patientToFormValues(existing), ...fromCard })
    setUpdatingPatientId(existing.id)
  }

  const existing = lookup?.existingPatient

  return (
    <>
      <PageHeader title="Tiếp nhận người bệnh" />
      <Space orientation="vertical" size={16} style={{ width: '100%' }}>
        <Card title={patientSource ? '1. Lấy thông tin người bệnh' : '1. Quét CCCD'}>
          {patientSource && (
            <>
              <Typography.Paragraph type="secondary">
                Người bệnh đã có trên hệ thống bệnh viện: nhập mã người bệnh hoặc số CCCD để lấy thông tin hành chính, không
                cần nhập lại.
              </Typography.Paragraph>
              <SourceLookup onFound={onSourceFound} />
              <Divider plain>hoặc quét CCCD</Divider>
            </>
          )}
          <QrScanInput onScan={onScan} autoFocus={!patientSource} />
          <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
            Người bệnh không có CCCD (trẻ em, người nước ngoài…): nhập trực tiếp vào phần thông tin bên dưới.
          </Typography.Paragraph>
          {scanErrors.length > 0 && (
            <Alert
              type="error"
              showIcon
              style={{ marginTop: 12 }}
              title="Không đọc được mã QR"
              description={<ul style={{ margin: 0, paddingLeft: 20 }}>{scanErrors.map((e) => <li key={e}>{e}</li>)}</ul>}
            />
          )}
          {scanWarnings.length > 0 && (
            <Alert
              type="warning"
              showIcon
              style={{ marginTop: 12 }}
              title="Cần kiểm tra lại"
              description={<ul style={{ margin: 0, paddingLeft: 20 }}>{scanWarnings.map((w) => <li key={w}>{w}</li>)}</ul>}
            />
          )}
          {(lookupMutation.isPending || sourceByCard.isPending) && (
            <Flex gap={8} align="center" style={{ marginTop: 12 }}>
              <Spin size="small" /> {his || patientSource ? 'Đang tra cứu hồ sơ và hệ thống bệnh viện…' : 'Đang tra cứu hồ sơ…'}
            </Flex>
          )}
          {sourceFilled && (
            <Alert
              type={sourceFilled.required.length > 0 ? 'warning' : 'success'}
              showIcon
              style={{ marginTop: 12 }}
              title="Đã lấy thông tin từ hệ thống bệnh viện"
              description={
                sourceFilled.required.length > 0
                  ? `Cần nhập thêm (bắt buộc): ${sourceFilled.required.join(', ')}.`
                    + (sourceFilled.optional.length > 0 ? ` Còn trống: ${sourceFilled.optional.join(', ')}.` : '')
                  : sourceFilled.optional.length > 0
                    ? `Chỉ cần bổ sung nếu có: ${sourceFilled.optional.join(', ')}. Kiểm tra lại rồi lưu hồ sơ.`
                    : 'Đã đủ thông tin. Kiểm tra lại rồi lưu hồ sơ.'
              }
            />
          )}
          {existing && (
            <Alert
              type={updatingPatientId ? 'success' : 'warning'}
              showIcon
              style={{ marginTop: 12 }}
              title={updatingPatientId ? 'Đang cập nhật hồ sơ đã có' : 'Người bệnh đã có hồ sơ trong hệ thống'}
              description={`${existing.fullName} – sinh ngày ${formatDate(existing.dateOfBirth)} – CCCD ${maskCitizenId(existing.citizenId)}${existing.patientCode ? ` – mã ${existing.patientCode}` : ''}`}
              action={
                !updatingPatientId && (
                  <Space orientation="vertical">
                    <Button size="small" type="primary" onClick={updateExistingRecord}>
                      Cập nhật hồ sơ theo CCCD
                    </Button>
                    <Button
                      size="small"
                      onClick={() =>
                        navigate(thenCreateForm ? `/phieu-dong-thuan/tao-moi?patientId=${existing.id}` : `/nguoi-benh/${existing.id}`)
                      }
                    >
                      {thenCreateForm ? 'Lập phiếu cho người bệnh này' : 'Mở hồ sơ'}
                    </Button>
                  </Space>
                )
              }
            />
          )}
          {lookup && lookup.hisStatus !== 'Disabled' && (
            <div style={{ marginTop: 12 }}>
              <HisInfo status={lookup.hisStatus} patient={lookup.hisPatient} />
            </div>
          )}
        </Card>

        <Card
          title={updatingPatientId ? '2. Cập nhật thông tin người bệnh' : '2. Thông tin người bệnh'}
          extra={
            <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
              {thenCreateForm ? 'Lưu và lập phiếu' : updatingPatientId ? 'Lưu thay đổi' : 'Lưu hồ sơ'}
            </Button>
          }
        >
          {duplicateId && (
            <Alert
              type="error"
              showIcon
              style={{ marginBottom: 16 }}
              title="Số CCCD này đã có hồ sơ"
              description={<Link to={`/nguoi-benh/${duplicateId}`}>Mở hồ sơ đã có</Link>}
            />
          )}
          <PatientForm form={form} onFinish={(values) => save.mutate(values)} disabled={save.isPending} />
        </Card>
      </Space>
    </>
  )
}
