import { useMutation } from '@tanstack/react-query'
import { Alert, Button, Form, Input, Radio, Space } from 'antd'
import { useState } from 'react'
import { patientsApi, type PatientSourceLookupResult, type PatientSourceQuery } from '../../api/patients'
import { errorMessage } from '../../utils/formErrors'

type LookupBy = 'patientCode' | 'citizenId'

const notices: Partial<Record<PatientSourceLookupResult['status'], string>> = {
  NotFound: 'Không tìm thấy người bệnh trong hệ thống bệnh viện. Vui lòng kiểm tra lại hoặc nhập trực tiếp.',
  Unavailable: 'Chưa kết nối được hệ thống thông tin người bệnh của bệnh viện. Vui lòng thử lại sau hoặc nhập trực tiếp.',
  Disabled: 'Chưa cấu hình kết nối hệ thống thông tin người bệnh.',
}

/** Lấy thông tin hành chính từ hệ thống bệnh viện theo mã người bệnh hoặc số CCCD. */
export function SourceLookup({ onFound }: { onFound: (result: PatientSourceLookupResult) => void }) {
  const [by, setBy] = useState<LookupBy>('patientCode')
  const [value, setValue] = useState('')
  const [inputError, setInputError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const lookup = useMutation({
    mutationFn: (query: PatientSourceQuery) => patientsApi.lookupSource(query),
    onSuccess: (result) => {
      if (result.status === 'Found' && result.patient) {
        setNotice(null)
        onFound(result)
      } else {
        setNotice(notices[result.status] ?? null)
      }
    },
    onError: (error) => setNotice(errorMessage(error)),
  })

  const submit = () => {
    const trimmed = value.trim()
    if (!trimmed) {
      setInputError(by === 'patientCode' ? 'Nhập mã người bệnh.' : 'Nhập số CCCD.')
      return
    }
    if (by === 'citizenId' && !/^\d{12}$/.test(trimmed)) {
      setInputError('Số CCCD gồm 12 chữ số.')
      return
    }
    setInputError(null)
    setNotice(null)
    lookup.mutate(by === 'patientCode' ? { patientCode: trimmed } : { citizenId: trimmed })
  }

  return (
    <Space orientation="vertical" size={12} style={{ width: '100%' }}>
      <Radio.Group
        optionType="button"
        value={by}
        onChange={(event) => {
          setBy(event.target.value as LookupBy)
          setInputError(null)
        }}
        options={[
          { label: 'Mã người bệnh', value: 'patientCode' },
          { label: 'Số CCCD', value: 'citizenId' },
        ]}
      />
      <Form.Item
        style={{ marginBottom: 0 }}
        validateStatus={inputError ? 'error' : undefined}
        help={inputError ?? undefined}
      >
        <Space.Compact style={{ width: '100%', maxWidth: 520 }}>
          <Input
            aria-label={by === 'patientCode' ? 'Mã người bệnh cần tra' : 'Số CCCD cần tra'}
            placeholder={by === 'patientCode' ? 'Nhập mã người bệnh' : 'Nhập 12 chữ số CCCD'}
            inputMode={by === 'citizenId' ? 'numeric' : undefined}
            maxLength={by === 'patientCode' ? 50 : 12}
            value={value}
            onChange={(event) => setValue(event.target.value)}
            onPressEnter={submit}
            autoComplete="off"
            autoFocus
          />
          <Button type="primary" loading={lookup.isPending} onClick={submit}>
            Lấy thông tin
          </Button>
        </Space.Compact>
      </Form.Item>
      {notice && <Alert type="warning" showIcon title={notice} />}
    </Space>
  )
}
