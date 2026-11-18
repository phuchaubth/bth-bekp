import { Alert, Descriptions } from 'antd'
import type { HisPatient, HisStatus } from '../../api/patients'
import { formatDate, formatDateTime } from '../../utils/format'
import { maskInsurance } from './cccd'

/** Hiển thị kết quả tra cứu HIS (FPT eHospital) sau khi quét CCCD. Chưa kết nối HIS thì không hiển thị gì. */
export function HisInfo({ status, patient }: { status: HisStatus; patient: HisPatient | null }) {
  if (status === 'Disabled') {
    return null
  }

  if (status === 'Unavailable') {
    return (
      <Alert
        type="warning"
        showIcon
        title="Không kết nối được HIS"
        description="Bạn vẫn có thể nhập thông tin thủ công. Hồ sơ sẽ ở trạng thái “Chưa đối chiếu HIS” và được đối chiếu lại ở lần lưu sau."
      />
    )
  }

  if (status === 'NotFound' || !patient) {
    return (
      <Alert
        type="info"
        showIcon
        title="Chưa có thông tin người bệnh này trên HIS"
        description="Kiểm tra lại việc tiếp nhận trên HIS nếu người bệnh đã đăng ký khám."
      />
    )
  }

  const encounter = patient.currentEncounter
  return (
    <Alert
      type="success"
      showIcon
      title="Đã tìm thấy người bệnh trên HIS"
      description={
        <Descriptions size="small" column={{ xs: 1, sm: 2 }} style={{ marginTop: 8 }}>
          <Descriptions.Item label="Mã người bệnh">{patient.patientCode}</Descriptions.Item>
          <Descriptions.Item label="Đối tượng">{patient.coverageType}</Descriptions.Item>
          {patient.healthInsuranceNumber && (
            <Descriptions.Item label="Mã thẻ BHYT">
              {maskInsurance(patient.healthInsuranceNumber)}
              {patient.healthInsuranceValidTo ? ` (hạn ${formatDate(patient.healthInsuranceValidTo)})` : ''}
            </Descriptions.Item>
          )}
          {encounter && (
            <>
              <Descriptions.Item label="Đợt khám">{encounter.encounterCode}</Descriptions.Item>
              <Descriptions.Item label="Nơi khám">
                {[encounter.clinicName, encounter.departmentName].filter(Boolean).join(' – ') || '—'}
              </Descriptions.Item>
              <Descriptions.Item label="Tiếp nhận lúc">{formatDateTime(encounter.admittedAtUtc)}</Descriptions.Item>
            </>
          )}
        </Descriptions>
      }
    />
  )
}
