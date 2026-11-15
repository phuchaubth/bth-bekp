import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Select, Typography } from 'antd'
import { useState } from 'react'
import { useConsentTemplates } from '../../../api/consentTemplates'
import { kioskKeys, kioskSettingsApi, useKioskSettings } from '../../../api/kiosk'
import { errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

/**
 * Chọn các mẫu người bệnh tự làm trên kiosk (quyết định 24), theo thứ tự làm. Máy chủ chỉ nhận mẫu đang dùng, đã phát hành
 * và người bệnh tự trả lời được.
 */
export function KioskSettingsCard() {
  const settings = useKioskSettings()
  const templates = useConsentTemplates(false)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [selected, setSelected] = useState<string[] | null>(null)
  const value = selected ?? settings.data?.templates.map((t) => t.id) ?? []

  const save = useMutation({
    mutationFn: () => kioskSettingsApi.update(value),
    onSuccess: (saved) => {
      queryClient.setQueryData(kioskKeys.settings, saved)
      setSelected(null)
      message.success(saved.templates.length > 0 ? 'Đã lưu các mẫu dùng trên kiosk.' : 'Đã bỏ hết mẫu: kiosk mời người bệnh đến quầy.')
    },
    onError: (error) => message.error(errorMessage(error)),
  })

  const options = (templates.data ?? [])
    .filter((t) => t.publishedVersionNo != null)
    .map((t) => ({ value: t.id, label: `${t.code} – ${t.name}` }))
  const problems = (settings.data?.templates ?? []).filter((t) => t.problems.length > 0)

  return (
    <Card title="Kiosk người bệnh tự làm phiếu" loading={settings.isPending}>
      <Typography.Paragraph type="secondary">
        Chọn các mẫu người bệnh tự làm trên kiosk, <strong>theo thứ tự làm</strong>. Chỉ dùng mẫu người bệnh tự trả lời được (không có bảng
        nhiều dòng hay ngày giờ); phiếu nhiều mục chuyên môn (ví dụ BM10) để điều dưỡng làm.
      </Typography.Paragraph>
      {problems.map((t) => (
        <Alert key={t.id} type="warning" showIcon style={{ marginBottom: 12 }} title={`${t.code}: ${t.problems.join(' ')}`} />
      ))}
      <Flex vertical gap={12}>
        <Select
          mode="multiple"
          aria-label="Mẫu dùng trên kiosk"
          placeholder="Chọn mẫu phiếu"
          loading={templates.isPending}
          options={options}
          value={value}
          onChange={setSelected}
          optionFilterProp="label"
          style={{ width: '100%' }}
        />
        <Flex justify="space-between" align="center" wrap gap={8}>
          <Typography.Text type="secondary">
            {settings.data?.updatedAtUtc ? `Cập nhật lần cuối ${formatDateTime(settings.data.updatedAtUtc)}` : 'Chưa chọn mẫu nào.'}
          </Typography.Text>
          <Button type="primary" disabled={selected === null} loading={save.isPending} onClick={() => save.mutate()}>
            Lưu mẫu kiosk
          </Button>
        </Flex>
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          Máy kiosk đăng nhập bằng tài khoản vai trò <strong>Kiosk người bệnh</strong> (tạo ở Quản trị → Người dùng, thuộc khoa đặt máy). Tài
          khoản này chỉ dùng được màn hình kiosk.
        </Typography.Paragraph>
      </Flex>
    </Card>
  )
}
