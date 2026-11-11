import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Form, Space, Spin, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError } from '../../api/http'
import { patientKeys, patientsApi, type PatientLookupResult } from '../../api/patients'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { formatDate } from '../../utils/format'
import { maskCitizenId } from './cccd'
import { parseCccdQr } from './cccdQrParser'
import { HisInfo } from './HisInfo'
import { PatientForm } from './PatientForm'
import { cardToFormValues, formValuesToInput, patientToFormValues, type PatientFormValues } from './patientFormValues'
import { QrScanInput } from './QrScanInput'

/**
 * Màn hình 4: tiếp nhận người bệnh. Quét QR CCCD → tự điền thông tin → tra cứu hồ sơ đã có và HIS →
 * bổ sung thông tin còn thiếu → lưu. Khi lập phiếu (Sprint 3), bước này được dùng lại làm bước 1.
 */
export function PatientIntakePage() {
  const [form] = Form.useForm<PatientFormValues>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [scanErrors, setScanErrors] = useState<string[]>([])
  const [scanWarnings, setScanWarnings] = useState<string[]>([])
  const [lookup, setLookup] = useState<PatientLookupResult | null>(null)
  const [updatingPatientId, setUpdatingPatientId] = useState<string | null>(null)
  const [duplicateId, setDuplicateId] = useState<string | null>(null)

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
      navigate(`/nguoi-benh/${patient.id}`)
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
    setLookup(null)
    setUpdatingPatientId(null)
    setDuplicateId(null)
    form.resetFields()
    form.setFieldsValue(cardToFormValues(result.card))
    lookupMutation.mutate(result.card.citizenId)
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
        <Card title="1. Quét CCCD">
          <QrScanInput onScan={onScan} autoFocus />
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
          {lookupMutation.isPending && (
            <Flex gap={8} align="center" style={{ marginTop: 12 }}>
              <Spin size="small" /> Đang tra cứu hồ sơ và HIS…
            </Flex>
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
                    <Button size="small" onClick={() => navigate(`/nguoi-benh/${existing.id}`)}>
                      Mở hồ sơ
                    </Button>
                  </Space>
                )
              }
            />
          )}
          {lookup && (
            <div style={{ marginTop: 12 }}>
              <HisInfo status={lookup.hisStatus} patient={lookup.hisPatient} />
            </div>
          )}
        </Card>

        <Card
          title={updatingPatientId ? '2. Cập nhật thông tin người bệnh' : '2. Thông tin người bệnh'}
          extra={
            <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
              {updatingPatientId ? 'Lưu thay đổi' : 'Lưu hồ sơ'}
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
