import type { Gender, Relationship } from '../../api/patients'

export const genderLabels: Record<Gender, string> = { Male: 'Nam', Female: 'Nữ' }

export const relationshipLabels: Record<Relationship, string> = {
  Father: 'Cha',
  Mother: 'Mẹ',
  Spouse: 'Vợ/Chồng',
  Child: 'Con',
  Sibling: 'Anh/chị/em ruột',
  Guardian: 'Người giám hộ',
  Relative: 'Người thân khác',
  Other: 'Khác',
}

/** Một số dân tộc thường gặp (vẫn cho phép nhập tự do). */
export const commonEthnicities = [
  'Kinh', 'Tày', 'Thái', 'Mường', 'Khơ Me', 'Hoa', 'Nùng', 'Mông', 'Dao', 'Gia Rai', 'Ê Đê', 'Ba Na', 'Sán Chay',
  'Chăm', 'Cơ Ho', 'Xơ Đăng', 'Sán Dìu', 'Hrê', 'Ra Glai', 'Mnông',
]

export function ageFrom(dateOfBirth: string, today = new Date()): number {
  const [year, month, day] = dateOfBirth.split('-').map(Number)
  let age = today.getFullYear() - year
  if (today.getMonth() + 1 < month || (today.getMonth() + 1 === month && today.getDate() < day)) {
    age--
  }
  return age
}
