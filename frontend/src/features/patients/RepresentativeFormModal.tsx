import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Col, DatePicker, Form, Input, Modal, Radio, Row, Select } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { patientKeys, patientsApi, type Gender, type Relationship, type Representative } from '../../api/patients'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { citizenIdMismatch, isValidCitizenId } from './cccd'
import { parseCccdQr } from './cccdQrParser'
import { relationshipLabels } from './labels'
import { QrScanInput } from './QrScanInput'

interface Values {
  fullName: string
  relationship: Relationship
  citizenId?: string
  dateOfBirth?: Dayjs
  gender?: Gender
  phoneNumber?: string
  address?: string
}

interface Props {
  patientId: string
  /** null: thêm mới. */
  representative: Representative | null
  onClose: () => void
}

/** Thêm/sửa người đại diện hợp pháp/người ký thay; có thể quét CCCD của người đó. */
export function RepresentativeFormModal({ patientId, representative, onClose }: Props) {
  const [form] = Form.useForm<Values>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [scanError, setScanError] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input = {
        fullName: values.fullName.trim(),
        relationship: values.relationship,
        citizenId: values.citizenId?.trim() || null,
        dateOfBirth: values.dateOfBirth ? values.dateOfBirth.format('YYYY-MM-DD') : null,
        gender: values.gender ?? null,
        phoneNumber: values.phoneNumber?.trim() || null,
        address: values.address?.trim() || null,
      }
      return representative
        ? patientsApi.updateRepresentative(patientId, representative.id, input)
        : patientsApi.addRepresentative(patientId, input)
    },
    onSuccess: async (patient) => {
      queryClient.setQueryData(patientKeys.detail(patientId), patient)
      await queryClient.invalidateQueries({ queryKey: patientKeys.all })
      message.success(representative ? 'Đã cập nhật người đại diện.' : 'Đã thêm người đại diện.')
      onClose()
    },
    onError: (error) => {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  const onScan = (raw: string) => {
    const result = parseCccdQr(raw)
    if (!result.ok) {
      setScanError(result.errors.join(' '))
      return
    }
    setScanError(null)
    form.setFieldsValue({
      fullName: result.card.fullName,
      citizenId: result.card.citizenId,
      dateOfBirth: dayjs(result.card.dateOfBirth),
      gender: result.card.gender,
      address: result.card.permanentAddress || undefined,
    })
  }

  return (
    <Modal
      open
      width={680}
      title={representative ? 'Sửa người đại diện' : 'Thêm người đại diện / người ký thay'}
      okText="Lưu"
      cancelText="Hủy"
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
    >
      <div style={{ marginBottom: 16 }}>
        <QrScanInput onScan={onScan} />
        {scanError && <Alert type="error" showIcon title={scanError} style={{ marginTop: 8 }} />}
      </div>
      <Form<Values>
        form={form}
        layout="vertical"
        onFinish={(values) => save.mutate(values)}
        initialValues={
          representative
            ? {
                fullName: representative.fullName,
                relationship: representative.relationship,
                citizenId: representative.citizenId ?? undefined,
                dateOfBirth: representative.dateOfBirth ? dayjs(representative.dateOfBirth) : undefined,
                gender: representative.gender ?? undefined,
                phoneNumber: representative.phoneNumber ?? undefined,
                address: representative.address ?? undefined,
              }
            : undefined
        }
      >
        <Row gutter={16}>
          <Col xs={24} md={14}>
            <Form.Item label="Họ và tên" name="fullName" rules={[{ required: true, message: 'Vui lòng nhập họ tên người đại diện.' }]}>
              <Input />
            </Form.Item>
          </Col>
          <Col xs={24} md={10}>
            <Form.Item label="Quan hệ với người bệnh" name="relationship" rules={[{ required: true, message: 'Vui lòng chọn quan hệ.' }]}>
              <Select
                options={Object.entries(relationshipLabels).map(([value, label]) => ({ value, label }))}
                placeholder="Chọn quan hệ"
              />
            </Form.Item>
          </Col>
          <Col xs={24} md={10}>
            <Form.Item
              label="Số CCCD"
              name="citizenId"
              dependencies={['dateOfBirth', 'gender']}
              rules={[
                ({ getFieldValue }) => ({
                  validator: (_, value?: string) => {
                    if (!value) {
                      return Promise.resolve()
                    }
                    if (!isValidCitizenId(value)) {
                      return Promise.reject(new Error('Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.'))
                    }
                    const dob: Dayjs | undefined = getFieldValue('dateOfBirth')
                    const gender = getFieldValue('gender')
                    const mismatch = dob && gender ? citizenIdMismatch(value, dob.year(), gender) : null
                    return mismatch ? Promise.reject(new Error(mismatch)) : Promise.resolve()
                  },
                }),
              ]}
            >
              <Input inputMode="numeric" maxLength={12} autoComplete="off" />
            </Form.Item>
          </Col>
          <Col xs={12} md={7}>
            <Form.Item label="Ngày sinh" name="dateOfBirth">
              <DatePicker format="DD/MM/YYYY" disabledDate={(d) => d.isAfter(dayjs(), 'day')} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col xs={12} md={7}>
            <Form.Item label="Giới tính" name="gender">
              <Radio.Group options={[{ value: 'Male', label: 'Nam' }, { value: 'Female', label: 'Nữ' }]} />
            </Form.Item>
          </Col>
          <Col xs={24} md={10}>
            <Form.Item
              label="Số điện thoại"
              name="phoneNumber"
              rules={[{ pattern: /^(0|\+84)[0-9]{9,10}$/, message: 'Số điện thoại không hợp lệ (ví dụ 0912345678).' }]}
            >
              <Input inputMode="tel" autoComplete="off" />
            </Form.Item>
          </Col>
          <Col xs={24} md={14}>
            <Form.Item label="Địa chỉ" name="address" rules={[{ max: 500, message: 'Địa chỉ tối đa 500 ký tự.' }]}>
              <Input />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </Modal>
  )
}
