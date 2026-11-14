import dayjs, { type Dayjs } from 'dayjs'
import type { Gender, PatientDetail, PatientInput } from '../../api/patients'
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
