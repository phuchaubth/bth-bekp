import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Form, Modal } from 'antd'
import { useEffect, useMemo } from 'react'
import { ApiError } from '../../api/http'
import { patientKeys, patientsApi, type SourcePatient } from '../../api/patients'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { PatientForm } from './PatientForm'
import { formValuesToInput, missingFields, sourceToFormValues, type PatientFormValues } from './patientFormValues'

interface Props {
  /** Thông tin lấy từ hệ thống bệnh viện; null = đóng. */
  source: SourcePatient | null
  onClose: () => void
  /** Hồ sơ đã lưu (hoặc hồ sơ đã có cùng số CCCD). */
  onSaved: (patientId: string) => void
}

/**
 * Lưu nhanh hồ sơ người bệnh ngay trong lúc lập phiếu: thông tin lấy từ hệ thống bệnh viện được điền sẵn, nhân viên chỉ
 * nhập các ô còn thiếu rồi lưu, không phải sang màn hình tiếp nhận.
 */
export function QuickPatientModal({ source, onClose, onSaved }: Props) {
  const [form] = Form.useForm<PatientFormValues>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const initial = useMemo(() => (source ? sourceToFormValues(source) : null), [source])
  const missing = useMemo(() => (initial ? missingFields({ nationality: 'Việt Nam', ...initial }) : null), [initial])

  useEffect(() => {
    if (initial) {
      form.resetFields()
      form.setFieldsValue(initial)
    }
  }, [form, initial])

  const save = useMutation({
    mutationFn: (values: PatientFormValues) => patientsApi.create(formValuesToInput(values)),
    onSuccess: async (patient) => {
      message.success('Đã lưu hồ sơ người bệnh.')
      await queryClient.invalidateQueries({ queryKey: patientKeys.all })
      onSaved(patient.id)
    },
    onError: (error) => {
      // Số CCCD đã có hồ sơ: dùng luôn hồ sơ đó thay vì tạo trùng.
      const existingId = error instanceof ApiError && error.code === 'patients.duplicate_citizen_id' ? error.problem?.existingPatientId : null
      if (typeof existingId === 'string') {
        message.info('Người bệnh đã có hồ sơ: dùng hồ sơ đã có.')
        onSaved(existingId)
        return
      }
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  return (
    <Modal
      open={source !== null}
      title="Lưu hồ sơ người bệnh"
      okText="Lưu và chọn người bệnh"
      cancelText="Hủy"
      onOk={() => form.submit()}
      onCancel={onClose}
      confirmLoading={save.isPending}
      width={960}
      destroyOnHidden
    >
      {missing && (
        <Alert
          type={missing.required.length > 0 ? 'warning' : 'info'}
          showIcon
          style={{ marginBottom: 16 }}
          title="Đã điền sẵn thông tin từ hệ thống bệnh viện"
          description={
            missing.required.length > 0
              ? `Cần nhập thêm (bắt buộc): ${missing.required.join(', ')}.`
              : `Chỉ cần bổ sung nếu có${missing.optional.length > 0 ? `: ${missing.optional.join(', ')}` : ''}. Kiểm tra lại rồi lưu.`
          }
        />
      )}
      <PatientForm form={form} onFinish={(values) => save.mutate(values)} disabled={save.isPending} />
    </Modal>
  )
}
