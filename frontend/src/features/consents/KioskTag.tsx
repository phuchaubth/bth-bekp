import { Tag } from 'antd'
import { kioskIdentityLabels, type KioskIdentityMethod } from '../../api/consentForms'

/** Phiếu người bệnh tự làm trên kiosk (quyết định 24): tự khai thì tô cam để nhân viên đối chiếu kỹ giấy tờ. */
export function KioskTag({ method }: { method?: KioskIdentityMethod | null }) {
  if (!method) {
    return null
  }
  return <Tag color={method === 'SelfReported' ? 'orange' : 'cyan'}>{kioskIdentityLabels[method]}</Tag>
}
