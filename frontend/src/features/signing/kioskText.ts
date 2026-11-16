/** Câu nhắc còn thiếu gì trước khi xác nhận chữ ký, ví dụ "Chọn Đồng ý hoặc Từ chối, ghi lời xác nhận và ký vào khung trên để xác nhận." */
export function readyHint(decisionStep: string | null, needsStatement: boolean) {
  const steps = [decisionStep, needsStatement ? 'ghi lời xác nhận' : null].filter((step): step is string => !!step)
  const text = steps.length > 0 ? `${steps.join(', ')} và ký vào khung trên để xác nhận.` : 'ký vào khung trên để xác nhận.'
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/** Câu sau khi ký xong: trên máy của bệnh viện thì đưa máy lại; trên điện thoại riêng của người bệnh thì đóng trang. */
export const signedHint = (remote: boolean) =>
  remote ? 'Cảm ơn ông/bà. Có thể đóng trang này; nhân viên y tế sẽ xác nhận phiếu.' : 'Vui lòng đưa máy lại cho nhân viên y tế.'

/** Phiên ký kết thúc (hết hạn hoặc nhân viên đã mở phiên khác). */
export const endedHint = (remote: boolean) =>
  remote
    ? 'Phiên ký đã hết hạn hoặc nhân viên đã hủy mã. Ông/bà nhờ nhân viên tạo mã mới.'
    : 'Phiên ký đã hết hạn hoặc đã được thay bằng phiên khác. Nhân viên đăng nhập lại để mở phiên mới.'
