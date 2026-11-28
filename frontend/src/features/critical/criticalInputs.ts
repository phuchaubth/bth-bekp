import type { FormInstance } from 'antd'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import type { CriticalAppNotificationInput, CriticalCallInput, CriticalCallMethod, CriticalNotificationInput } from '../../api/critical'

/**
 * Giá trị của các ô "Đã báo cho khoa điều trị" trong form (Ant Design); dùng lại cho "Ghi cuộc gọi" (quyết định 68), khi đó
 * `notifiedAt` là giờ gọi và không có ô khoa nhận.
 */
export interface NotificationFormValues {
  receivingDepartmentId?: string
  method: CriticalCallMethod
  notifiedAt?: Dayjs
  receiverName?: string
  /** Người nhận đọc lại: đúng (true), sai (false); chỉ lưu được khi đúng. */
  readBackCorrect?: boolean
  /** Số lần người nhận đọc lại sai trước khi đúng (vẫn lưu trong lịch sử hồ sơ). */
  incorrectReadBacks: number
  repeatedTwice: boolean
}

/** Giờ hiện tại làm tròn xuống phút (ô giờ chỉ chọn tới phút). */
export const nowToMinute = () => dayjs().second(0).millisecond(0)

export function emptyNotification(): NotificationFormValues {
  return { method: 'Phone', notifiedAt: nowToMinute(), readBackCorrect: undefined, incorrectReadBacks: 0, repeatedTwice: false }
}

/** Ô không hiện (ví dụ "nhắc lại 2 lần" ngoài sổ Vi sinh) không có trong giá trị form: gửi giá trị mặc định rõ ràng. */
export function toNotificationInput(values: NotificationFormValues): CriticalNotificationInput {
  return {
    receivingDepartmentId: values.receivingDepartmentId!,
    method: values.method ?? 'Phone',
    notifiedAtUtc: values.notifiedAt!.toISOString(),
    receiverName: values.receiverName?.trim() ?? '',
    readBackCorrect: values.readBackCorrect === true,
    incorrectReadBacks: values.incorrectReadBacks ?? 0,
    repeatedTwice: values.repeatedTwice === true,
  }
}

/** Các ô chỉ cần khi đã gọi ("Lưu, đã báo"): người nhận, giờ báo, đọc lại, nhắc lại 2 lần. */
const CALL_ONLY_FIELDS = ['receiverName', 'notifiedAt', 'readBackCorrect', 'repeatedTwice'] as const

/**
 * Bấm "Lưu, gọi sau": xóa lỗi cũ của các ô chỉ cần khi đã gọi (lỡ bấm "Lưu, đã báo" trước đó), để không tưởng là phải điền.
 */
export function clearCallErrors(form: FormInstance): void {
  form.setFields(CALL_ONLY_FIELDS.map((field) => ({ name: ['notification', field], errors: [] })))
}

/** "Lưu, gọi sau" (quyết định 68): chỉ cần khoa nhận; khoa nhận được báo ngay trên phần mềm. */
export function toAppNotificationInput(values: NotificationFormValues): CriticalAppNotificationInput {
  return { receivingDepartmentId: values.receivingDepartmentId!, method: 'App' }
}

/** Ghi cuộc gọi xác nhận người nhận thông tin: các ô như lần báo, giờ báo là giờ gọi. */
export function toCallInput(values: NotificationFormValues): CriticalCallInput {
  const { notifiedAtUtc, method, receiverName, readBackCorrect, incorrectReadBacks, repeatedTwice } = toNotificationInput(values)
  return { method, calledAtUtc: notifiedAtUtc, receiverName, readBackCorrect, incorrectReadBacks, repeatedTwice }
}

/** Lý do hay gặp khi khoa nhận trả lại lần báo (chọn nhanh, sửa được). */
export const REJECT_REASONS = [
  'Người bệnh không điều trị tại khoa',
  'Người bệnh đã chuyển khoa',
  'Thông tin người bệnh không khớp',
] as const
