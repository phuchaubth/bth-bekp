/** Vai trò có thể xác nhận phiếu phía bệnh viện (mã vai trò hệ thống). */
export const signerRoleOptions = [
  { value: 'DOCTOR', label: 'Bác sĩ' },
  { value: 'NURSE', label: 'Điều dưỡng' },
  { value: 'RECEPTIONIST', label: 'Tiếp đón' },
  { value: 'DEPARTMENT_HEAD', label: 'Trưởng khoa' },
]

export function signerRoleLabel(code: string): string {
  return signerRoleOptions.find((o) => o.value === code)?.label ?? code
}
