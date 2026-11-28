import { Tag } from 'antd'
import { QC_STATUS_LABELS, type QcBookStatus } from '../../api/qc'

const STATUS_COLORS: Record<QcBookStatus, string> = { Open: 'processing', Submitted: 'warning', Confirmed: 'success' }

export function QcStatusTag({ status }: { status: QcBookStatus }) {
  return <Tag color={STATUS_COLORS[status]}>{QC_STATUS_LABELS[status]}</Tag>
}

/** Đánh giá kết quả như sổ giấy: "Đạt/ Pass" hoặc "Không đạt/ Fail" (kèm màu, không chỉ dựa vào màu). */
export function QcEvaluationTag({ passed }: { passed: boolean }) {
  return <Tag color={passed ? 'success' : 'error'}>{passed ? 'Đạt/ Pass' : 'Không đạt/ Fail'}</Tag>
}
