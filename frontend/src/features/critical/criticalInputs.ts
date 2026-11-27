import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import type { CriticalMethod, CriticalNotificationInput } from '../../api/critical'

/** Giá trị của các ô "Đã báo cho khoa điều trị" trong form (Ant Design). */
export interface NotificationFormValues {
  receivingDepartmentId?: string
  method: CriticalMethod
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

/** Lý do hay gặp khi khoa nhận trả lại lần báo (chọn nhanh, sửa được). */
export const REJECT_REASONS = [
  'Người bệnh không điều trị tại khoa',
  'Người bệnh đã chuyển khoa',
  'Thông tin người bệnh không khớp',
] as const
