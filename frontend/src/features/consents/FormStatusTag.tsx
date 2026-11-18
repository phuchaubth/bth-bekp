import { Tag } from 'antd'
import { formStatusColors, formStatusLabels, type ConsentFormStatus } from '../../api/consentForms'

export function FormStatusTag({ status }: { status: ConsentFormStatus }) {
  return <Tag color={formStatusColors[status]}>{formStatusLabels[status]}</Tag>
}
