import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Card, Flex, Switch, Typography } from 'antd'
import { encounterSettingsApi, settingsKeys, useEncounterSettings } from '../../../api/settings'
import { errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'

/**
 * Bật/tắt gắn phiếu với lượt tiếp nhận HIS khi ký hoàn tất (quyết định 25). Có hiệu lực ngay, không cần khởi động lại: HIS lỗi
 * thì tắt để nhân viên hoàn tất phiếu tiếp, HIS ổn định thì bật lại. Chỉ bật được khi đã cấu hình API thông tin người bệnh.
 */
export function EncounterSettingsCard() {
  const settings = useEncounterSettings()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (enabled: boolean) => encounterSettingsApi.update(enabled),
    onSuccess: (saved) => {
      queryClient.setQueryData(settingsKeys.encounters, saved)
      message.success(
        saved.enabled
          ? 'Đã bật: ký hoàn tất phiếu phải chọn lượt tiếp nhận.'
          : 'Đã tắt: ký hoàn tất phiếu không cần chọn lượt tiếp nhận.',
      )
    },
    onError: (error) => message.error(errorMessage(error)),
  })
  const data = settings.data

  return (
    <Card title="Gắn phiếu với lượt tiếp nhận (HIS)" loading={settings.isPending}>
      {settings.isError && <Alert type="error" showIcon title={errorMessage(settings.error)} />}
      {data && (
        <Flex vertical gap={12}>
          <Flex align="center" gap={12}>
            <Switch
              checked={data.enabled}
              disabled={!data.available}
              loading={save.isPending}
              aria-label="Bắt buộc chọn lượt tiếp nhận khi ký hoàn tất phiếu"
              onChange={(checked) => save.mutate(checked)}
            />
            <Typography.Text strong>Bắt buộc chọn lượt tiếp nhận khi ký hoàn tất phiếu</Typography.Text>
          </Flex>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            Khi bật, người ký hoàn tất (tiếp nhận, bác sĩ…) phải chọn lượt tiếp nhận của người bệnh trên HIS; số tiếp nhận và người tiếp nhận
            được in ở chân phiếu để đối soát. <strong>HIS lỗi thì tắt</strong> để nhân viên hoàn tất phiếu tiếp (phiếu hoàn tất lúc tắt không gắn
            lượt tiếp nhận), HIS ổn định thì bật lại. Có hiệu lực ngay, không cần khởi động lại.
          </Typography.Paragraph>
          {!data.available && (
            <Alert
              type="info"
              showIcon
              title="Chưa cấu hình API thông tin người bệnh nên chưa bật được."
              description="Phòng CNTT cấu hình API thông tin người bệnh và câu truy vấn lượt tiếp nhận theo hướng dẫn triển khai (mục 5.5, 5.6)."
            />
          )}
          <Typography.Text type="secondary">
            {data.updatedAtUtc ? `Đổi lần cuối ${formatDateTime(data.updatedAtUtc)}.` : 'Chưa bật lần nào.'} Mọi thay đổi được ghi nhật ký hệ
            thống.
          </Typography.Text>
        </Flex>
      )}
    </Card>
  )
}
