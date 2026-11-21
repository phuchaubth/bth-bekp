import { ReloadOutlined, ToolOutlined } from '@ant-design/icons'
import { Button, Card, Flex, Typography } from 'antd'
import dayjs from 'dayjs'
import type { ReactNode } from 'react'
import type { MaintenanceNotice } from '../api/maintenanceStore'
import { BRAND } from '../app/theme'

/** Màn hình bảo trì tự hỏi lại máy chủ sau bấy nhiêu giây. */
export const MAINTENANCE_RECHECK_SECONDS = 30

interface MaintenanceScreenProps {
  notice: MaintenanceNotice
  /** Người bệnh dùng máy (kiosk, ký trên máy tính bảng, ký qua mã QR): lời mời chờ, không có nút của nhân viên. */
  forPatient: boolean
  checking: boolean
  onRecheck: () => void
  /** Nút của nhân viên: đăng nhập bằng tài khoản quản trị, đăng xuất. */
  actions?: ReactNode
}

function explanation(notice: MaintenanceNotice, forPatient: boolean): string {
  if (forPatient) {
    return 'Mời ông/bà chờ trong giây lát hoặc liên hệ nhân viên bệnh viện.'
  }
  if (notice.kind === 'unavailable') {
    return 'Máy chủ đang tạm dừng để cập nhật phần mềm hoặc bảo trì. Trang sẽ tự tải lại khi máy chủ chạy lại; thao tác chưa lưu có thể phải làm lại.'
  }
  return 'Trong lúc bảo trì chưa thao tác được. Trang đang mở và dữ liệu đang nhập vẫn được giữ: bảo trì xong, màn hình này tự đóng để làm tiếp (thao tác chưa lưu được thì bấm lưu lại).'
}

/**
 * Màn hình "Hệ thống đang bảo trì" (quyết định 53), che toàn bộ trang đang mở (trang vẫn giữ nguyên bên dưới): lý do và giờ dự
 * kiến xong do quản trị ghi, tự hỏi lại máy chủ sau mỗi {@link MAINTENANCE_RECHECK_SECONDS} giây.
 */
export function MaintenanceScreen({ notice, forPatient, checking, onRecheck, actions }: MaintenanceScreenProps) {
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="bao-tri-tieu-de"
      style={{
        position: 'fixed',
        inset: 0,
        // Trên mọi lớp của Ant Design (hộp thoại, thông báo, ngăn kéo).
        zIndex: 2100,
        overflow: 'auto',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
        background: `linear-gradient(135deg, #073b37 0%, ${BRAND} 100%)`,
      }}
    >
      <Card style={{ width: '100%', maxWidth: 520 }}>
        <Flex vertical align="center" gap={12} style={{ textAlign: 'center' }}>
          <ToolOutlined style={{ fontSize: 40, color: BRAND }} aria-hidden />
          <Typography.Title id="bao-tri-tieu-de" level={3} style={{ margin: 0 }}>
            Hệ thống đang bảo trì
          </Typography.Title>
          {notice.reason && (
            <Typography.Paragraph strong style={{ fontSize: 16, marginBottom: 0 }}>
              {notice.reason}
            </Typography.Paragraph>
          )}
          {notice.expectedEndAtUtc && (
            <Typography.Text>Dự kiến xong lúc {dayjs(notice.expectedEndAtUtc).format('HH:mm [ngày] DD/MM/YYYY')}.</Typography.Text>
          )}
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {explanation(notice, forPatient)} Tự kiểm tra lại sau mỗi {MAINTENANCE_RECHECK_SECONDS} giây.
          </Typography.Paragraph>
          <Button type="primary" size="large" icon={<ReloadOutlined />} loading={checking} onClick={onRecheck} autoFocus>
            Kiểm tra lại
          </Button>
          {!forPatient && actions}
        </Flex>
      </Card>
    </div>
  )
}
