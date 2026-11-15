import { CheckCircleFilled, CheckCircleOutlined, EditOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Space, Typography } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router'
import {
  consentFormKeys,
  consentFormsApi,
  type ConsentFormBundle,
  type ConsentFormDetail,
  type StaffSignatureInput,
} from '../../api/consentForms'
import { errorMessage } from '../../utils/formErrors'
import { FormStatusTag } from './FormStatusTag'
import { StaffSignModal, StartBundleSigningModal } from './SigningActions'

/**
 * Thanh bộ phiếu (quyết định 23) trên trang phiếu: các phiếu của bộ và trạng thái, chuyển nhanh giữa các phiếu, ký cả bộ
 * một lần trên điện thoại và xác nhận một lần các phiếu đến lượt mình.
 */
export function BundleBar({ form, bundle }: { form: ConsentFormDetail; bundle: ConsentFormBundle }) {
  const [signingOpen, setSigningOpen] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const toSign = bundle.forms.filter((item) => item.actions.canStartSigning)
  const myTurn = bundle.forms.filter((item) => item.actions.canConfirmStep || item.actions.canConfirm)

  const confirm = useMutation({
    mutationFn: (signature: StaffSignatureInput) => consentFormsApi.confirmBundle(bundle.bundleId, signature),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      setConfirmOpen(false)
      const completed = result.confirmed.filter((item) => item.action === 'completed').length
      message.success(
        `Đã xác nhận ${result.confirmed.length} phiếu${completed > 0 ? ` (hoàn tất ${completed} phiếu)` : ''}: ${result.confirmed
          .map((item) => item.formNo)
          .join(', ')}.`,
      )
      result.failed.forEach((item) => message.warning(`${item.formNo}: ${item.message}`))
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  return (
    <Card
      size="small"
      title={`Bộ phiếu của ${bundle.patientName} (${bundle.forms.length} phiếu)`}
      extra={
        <Space wrap>
          {bundle.canStartSigning && (
            <Button type="primary" icon={<EditOutlined />} onClick={() => setSigningOpen(true)}>
              Ký cả bộ trên điện thoại ({toSign.length} phiếu)
            </Button>
          )}
          {bundle.canConfirm && (
            <Button type="primary" icon={<CheckCircleOutlined />} onClick={() => setConfirmOpen(true)}>
              Xác nhận cả bộ ({myTurn.length} phiếu)
            </Button>
          )}
        </Space>
      }
    >
      <Flex vertical gap={8}>
        <ol style={{ margin: 0, paddingLeft: 20 }} aria-label="Các phiếu trong bộ">
          {bundle.forms.map((item) => (
            <li key={item.id} style={{ marginBottom: 4 }}>
              <Flex gap={8} wrap align="center">
                {item.id === form.id ? (
                  <Typography.Text strong aria-current="page">
                    {item.templateName}
                  </Typography.Text>
                ) : (
                  <Link to={`/phieu-dong-thuan/${item.id}`}>{item.templateName}</Link>
                )}
                <Typography.Text type="secondary">{item.formNo}</Typography.Text>
                <FormStatusTag status={item.status} />
                {item.patientSigned && item.status === 'PendingSignature' && (
                  <Typography.Text type="success">
                    <CheckCircleFilled /> Người bệnh đã ký{item.nextStepLabel ? ` · chờ ${item.nextStepLabel}` : ''}
                  </Typography.Text>
                )}
              </Flex>
            </li>
          ))}
        </ol>
        {bundle.notices.map((notice) => (
          <Alert key={notice} type="info" showIcon title={notice} />
        ))}
      </Flex>
      {signingOpen && (
        <StartBundleSigningModal
          bundleId={bundle.bundleId}
          count={toSign.length}
          byRepresentative={form.signerKind === 'Representative'}
          onClose={() => setSigningOpen(false)}
        />
      )}
      {confirmOpen && (
        <StaffSignModal
          title="Xác nhận cả bộ phiếu"
          description={`Bạn sẽ ký xác nhận ${myTurn.length} phiếu đang đến lượt mình: ${myTurn
            .map((item) => `${item.templateName} (${item.formNo})`)
            .join('; ')}. Chữ ký được in vào ô của bạn trên từng phiếu.`}
          okText="Ký và xác nhận cả bộ"
          loading={confirm.isPending}
          onSign={(signature) => confirm.mutate(signature)}
          onClose={() => setConfirmOpen(false)}
        />
      )}
    </Card>
  )
}
