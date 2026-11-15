/** Câu nhắc còn thiếu gì trước khi xác nhận chữ ký, ví dụ "Chọn Đồng ý hoặc Từ chối, ghi lời xác nhận và ký vào khung trên để xác nhận." */
export function readyHint(decisionStep: string | null, needsStatement: boolean) {
  const steps = [decisionStep, needsStatement ? 'ghi lời xác nhận' : null].filter((step): step is string => !!step)
  const text = steps.length > 0 ? `${steps.join(', ')} và ký vào khung trên để xác nhận.` : 'ký vào khung trên để xác nhận.'
  return text.charAt(0).toUpperCase() + text.slice(1)
}
