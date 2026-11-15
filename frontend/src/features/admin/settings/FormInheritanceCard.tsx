import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, InputNumber, Typography } from 'antd'
import { useState } from 'react'
import {
  FORM_INHERITANCE_MAX_DAYS,
  formInheritanceApi,
  settingsKeys,
  useFormInheritanceSettings,
} from '../../../api/settings'
import { errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

/**
 * Kế thừa phiếu lần trước (quyết định 27): người bệnh làm lại cùng mẫu ở cùng khoa trong số ngày đặt ở đây thì phiếu mới điền
 * sẵn câu trả lời của phiếu đã hoàn tất gần nhất. 0 là tắt. Có hiệu lực ngay cho phiếu lập sau khi lưu.
 */
export function FormInheritanceCard() {
  const settings = useFormInheritanceSettings()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [draft, setDraft] = useState<number | null>(null)
  const saved = settings.data?.days ?? 0
  const days = draft ?? saved
  const save = useMutation({
    mutationFn: () => formInheritanceApi.update(days),
    onSuccess: (result) => {
      queryClient.setQueryData(settingsKeys.formInheritance, result)
      setDraft(null)
      message.success(
        result.days > 0
          ? `Đã lưu: phiếu mới được điền sẵn theo phiếu lần trước trong ${result.days} ngày.`
          : 'Đã tắt kế thừa phiếu lần trước.',
      )
    },
    onError: (error) => message.error(errorMessage(error)),
  })

  return (
    <Card title="Kế thừa phiếu lần trước" loading={settings.isPending}>
      {settings.isError && <Alert type="error" showIcon title={errorMessage(settings.error)} />}
      {settings.data && (
        <Flex vertical gap={12}>
          <Typography.Paragraph style={{ marginBottom: 0 }}>
            Người bệnh làm lại cùng mẫu phiếu ở cùng khoa trong số ngày này thì phiếu mới được điền sẵn câu trả lời của phiếu đã
            hoàn tất gần nhất, để nhập cho nhanh. Nhân viên kiểm tra lại trước khi trình ký; trên kiosk, người bệnh chọn{' '}
            <strong>giữ như lần trước</strong> hoặc trả lời lại. Không kế thừa ý kiến Đồng ý/Không đồng ý, lời xác nhận và chữ ký.
          </Typography.Paragraph>
          <Flex align="center" gap={8} wrap>
            <InputNumber
              min={0}
              max={FORM_INHERITANCE_MAX_DAYS}
              precision={0}
              value={days}
              onChange={(value) => setDraft(typeof value === 'number' ? value : 0)}
              aria-label="Số ngày được kế thừa phiếu lần trước"
              style={{ width: 120 }}
            />
            <Typography.Text>ngày (0 là tắt)</Typography.Text>
            <Button type="primary" loading={save.isPending} disabled={days === saved} onClick={() => save.mutate()}>
              Lưu
            </Button>
          </Flex>
          <Typography.Text type="secondary">
            {settings.data.updatedAtUtc ? `Đổi lần cuối ${formatDateTime(settings.data.updatedAtUtc)}.` : 'Chưa đặt: đang tắt.'} Mọi
            thay đổi được ghi nhật ký hệ thống.
          </Typography.Text>
        </Flex>
      )}
    </Card>
  )
}
