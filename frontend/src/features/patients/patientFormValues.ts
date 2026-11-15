import dayjs, { type Dayjs } from 'dayjs'
import type { Gender, PatientDetail, PatientInput, SourcePatient } from '../../api/patients'
import type { CccdCard } from './cccdQrParser'

/** Giá trị của form người bệnh (ngày dùng Dayjs cho DatePicker). */
export interface PatientFormValues {
  fullName: string
  dateOfBirth?: Dayjs
  gender?: Gender
  citizenId?: string
  oldIdNumber?: string
  citizenIdIssueDate?: Dayjs
  permanentAddress?: string
  currentAddress?: string
  phoneNumber?: string
  ethnicity?: string
  occupation?: string
  nationality?: string
  healthInsuranceNumber?: string
  patientCode?: string
}

const toDayjs = (value: string | null | undefined) => (value ? dayjs(value) : undefined)
const text = (value: string | null | undefined) => value ?? undefined
const orNull = (value: string | undefined) => (value && value.trim() ? value.trim() : null)

export function patientToFormValues(patient: PatientDetail): PatientFormValues {
  return {
    fullName: patient.fullName,
    dateOfBirth: toDayjs(patient.dateOfBirth),
    gender: patient.gender,
    citizenId: text(patient.citizenId),
    oldIdNumber: text(patient.oldIdNumber),
    citizenIdIssueDate: toDayjs(patient.citizenIdIssueDate),
    permanentAddress: text(patient.permanentAddress),
    currentAddress: text(patient.currentAddress),
    phoneNumber: text(patient.phoneNumber),
    ethnicity: text(patient.ethnicity),
    occupation: text(patient.occupation),
    nationality: text(patient.nationality),
    healthInsuranceNumber: text(patient.healthInsuranceNumber),
    patientCode: text(patient.patientCode),
  }
}

/** Các trường in trên thẻ CCCD (ghi đè lên dữ liệu đang có trong form). */
export function cardToFormValues(card: CccdCard): Partial<PatientFormValues> {
  return {
    fullName: card.fullName,
    dateOfBirth: dayjs(card.dateOfBirth),
    gender: card.gender,
    citizenId: card.citizenId,
    oldIdNumber: card.oldIdNumber ?? undefined,
    citizenIdIssueDate: card.issueDate ? dayjs(card.issueDate) : undefined,
    permanentAddress: card.permanentAddress || undefined,
  }
}

/** Thông tin lấy từ hệ thống bệnh viện: chỉ các trường có giá trị (ghi đè lên dữ liệu đang có trong form). */
export function sourceToFormValues(patient: SourcePatient): Partial<PatientFormValues> {
  const values: Partial<PatientFormValues> = {
    fullName: text(patient.fullName),
    dateOfBirth: toDayjs(patient.dateOfBirth),
    gender: patient.gender ?? undefined,
    citizenId: text(patient.citizenId),
    citizenIdIssueDate: toDayjs(patient.citizenIdIssueDate),
    oldIdNumber: text(patient.oldIdNumber),
    permanentAddress: text(patient.permanentAddress),
    currentAddress: text(patient.currentAddress),
    phoneNumber: text(patient.phoneNumber),
    ethnicity: text(patient.ethnicity),
    occupation: text(patient.occupation),
    nationality: text(patient.nationality),
    healthInsuranceNumber: text(patient.healthInsuranceNumber),
    patientCode: text(patient.patientCode),
  }
  return Object.fromEntries(Object.entries(values).filter(([, value]) => value !== undefined)) as Partial<PatientFormValues>
}

export function formValuesToInput(values: PatientFormValues): PatientInput {
  return {
    fullName: values.fullName.trim(),
    dateOfBirth: values.dateOfBirth!.format('YYYY-MM-DD'),
    gender: values.gender!,
    citizenId: orNull(values.citizenId),
    oldIdNumber: orNull(values.oldIdNumber),
    citizenIdIssueDate: values.citizenIdIssueDate ? values.citizenIdIssueDate.format('YYYY-MM-DD') : null,
    permanentAddress: orNull(values.permanentAddress),
    currentAddress: orNull(values.currentAddress),
    phoneNumber: orNull(values.phoneNumber),
    ethnicity: orNull(values.ethnicity),
    occupation: orNull(values.occupation),
    nationality: orNull(values.nationality),
    healthInsuranceNumber: orNull(values.healthInsuranceNumber)?.toUpperCase() ?? null,
    patientCode: orNull(values.patientCode),
  }
}

/**
 * Tên hiển thị các trường của form, để báo những ô còn trống sau khi lấy thông tin tự động. Không báo "Địa chỉ hiện tại"
 * (để trống nghĩa là giống nơi thường trú) và số CMND cũ (người có CCCD mới thường không có).
 */
const fieldLabels: [keyof PatientFormValues, string, boolean][] = [
  ['fullName', 'Họ và tên', true],
  ['dateOfBirth', 'Ngày sinh', true],
  ['gender', 'Giới tính', true],
  ['citizenId', 'Số CCCD', false],
  ['citizenIdIssueDate', 'Ngày cấp CCCD', false],
  ['permanentAddress', 'Nơi thường trú', false],
  ['phoneNumber', 'Số điện thoại', false],
  ['ethnicity', 'Dân tộc', false],
  ['nationality', 'Quốc tịch', false],
  ['occupation', 'Nghề nghiệp', false],
  ['healthInsuranceNumber', 'Mã thẻ BHYT', false],
  ['patientCode', 'Mã người bệnh', false],
]

export interface MissingFields {
  required: string[]
  optional: string[]
}

export function missingFields(values: Partial<PatientFormValues>): MissingFields {
  const empty = fieldLabels.filter(([name]) => {
    const value = values[name]
    return value === undefined || value === null || (typeof value === 'string' && !value.trim())
  })
  return {
    required: empty.filter(([, , required]) => required).map(([, label]) => label),
    optional: empty.filter(([, , required]) => !required).map(([, label]) => label),
  }
}

/** Chỉ các giá trị điền được vào ô đang trống (không ghi đè dữ liệu đã có, ví dụ vừa quét từ CCCD). */
export function onlyEmpty(current: Partial<PatientFormValues>, incoming: Partial<PatientFormValues>): Partial<PatientFormValues> {
  return Object.fromEntries(
    Object.entries(incoming).filter(([name]) => {
      const value = current[name as keyof PatientFormValues]
      return value === undefined || value === null || (typeof value === 'string' && !value.trim())
    }),
  ) as Partial<PatientFormValues>
}
