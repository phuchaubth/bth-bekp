import {
  ApartmentOutlined,
  CheckCircleFilled,
  ClockCircleOutlined,
  CloseCircleFilled,
  ExclamationCircleFilled,
  PhoneOutlined,
  SoundOutlined,
  StopOutlined,
} from '@ant-design/icons'
import { Space, Tabs, Tag, Typography } from 'antd'
import type { CSSProperties, ReactNode } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { CRITICAL_STATUS_LABELS, type CriticalStatus } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import { Permissions, hasAnyPermission } from '../../auth/permissions'
import { clockState, countdownText, secondsLeft, type ClockState } from './criticalUtils'

/** Màu trạng thái (luôn kèm biểu tượng và chữ, không dùng màu riêng lẻ). */
const CLOCK_COLORS: Record<ClockState, string> = { ok: '#0d7c74', warn: '#b26a00', late: '#c62828' }

const STATUS_TAGS: Record<CriticalStatus, { color: string; icon: ReactNode }> = {
  AwaitingNotification: { color: 'warning', icon: <PhoneOutlined /> },
  AwaitingAcknowledgement: { color: 'error', icon: <SoundOutlined /> },
  AwaitingAction: { color: 'processing', icon: <ClockCircleOutlined /> },
  Completed: { color: 'success', icon: <CheckCircleFilled /> },
  Cancelled: { color: 'default', icon: <StopOutlined /> },
}

export function CriticalStatusTag({ status }: { status: CriticalStatus }) {
  const { color, icon } = STATUS_TAGS[status]
  return (
    <Tag color={color} icon={icon}>
      {CRITICAL_STATUS_LABELS[status]}
    </Tag>
  )
}

/** Đã báo khoa nhận trên phần mềm ("Lưu, gọi sau"), khoa báo chưa gọi xác nhận người nhận thông tin (quyết định 68). */
export function CallPendingTag({ pending }: { pending: boolean }) {
  if (!pending) return null
  return (
    <Tag color="orange" icon={<PhoneOutlined />}>
      Chưa gọi xác nhận
    </Tag>
  )
}

/** Khoa của người bệnh (khoa nhận) trên thẻ hồ sơ, cho tài khoản xem nhiều khoa nhận ra nhanh hồ sơ của khoa nào. */
export function PatientDepartmentTag({ name }: { name: string | null | undefined }) {
  if (!name) return null
  return (
    <Tag color="geekblue" icon={<ApartmentOutlined />} style={{ fontWeight: 600 }}>
      {name}
    </Tag>
  )
}

/** Đúng hạn hay quá hạn của một bước đã xong (báo, bác sĩ xử trí). */
export function OnTimeTag({ onTime, minutes, label }: { onTime: boolean | null; minutes?: number; label?: string }) {
  if (onTime === null) return null
  const text = `${label ? `${label} ` : ''}${minutes !== undefined ? `${minutes} phút, ` : ''}${onTime ? 'đúng hạn' : 'quá hạn'}`
  return (
    <Tag color={onTime ? 'success' : 'error'} icon={onTime ? <CheckCircleFilled /> : <CloseCircleFilled />}>
      {text}
    </Tag>
  )
}

const CLOCK_ICONS: Record<ClockState, ReactNode> = {
  ok: <ClockCircleOutlined />,
  warn: <ExclamationCircleFilled />,
  late: <CloseCircleFilled />,
}

/**
 * Đồng hồ đếm ngược tới hạn theo giờ máy chủ: còn dưới mốc cảnh báo thì cam, quá hạn thì đỏ (kèm biểu tượng và chữ).
 * `label` là bước đang chờ ("Hạn báo", "BS xử trí").
 */
export function DeadlineClock({
  dueUtc,
  nowMs,
  warnMinutes,
  label,
  size = 'default',
}: {
  dueUtc: string
  nowMs: number
  warnMinutes: number
  label?: string
  size?: 'default' | 'large'
}) {
  const seconds = secondsLeft(dueUtc, nowMs)
  const state = clockState(seconds, warnMinutes)
  const style: CSSProperties = {
    color: CLOCK_COLORS[state],
    fontWeight: 600,
    fontVariantNumeric: 'tabular-nums',
    fontSize: size === 'large' ? 22 : undefined,
    whiteSpace: 'nowrap',
  }
  return (
    <span role="timer" aria-live="off" style={style}>
      <Space size={6}>
        {CLOCK_ICONS[state]}
        <span>
          {label ? `${label}: ` : ''}
          {countdownText(seconds)}
        </span>
      </Space>
    </span>
  )
}

/** Tên xét nghiệm và giá trị kết quả (kết quả dạng chữ không có đơn vị). */
export function ResultValue({ testName, value, unit }: { testName: string; value: string; unit: string | null }) {
  return (
    <span>
      <Typography.Text strong>{testName}</Typography.Text>{' '}
      <Typography.Text strong style={{ color: '#c62828' }}>
        {value}
        {unit ? ` ${unit}` : ''}
      </Typography.Text>
    </span>
  )
}

interface SectionLink {
  path: string
  label: string
  permission: readonly string[]
}

const SECTION_LINKS: SectionLink[] = [
  { path: '/ket-qua-nguy-cap/khoa-bao', label: 'Khoa tôi báo', permission: [Permissions.CriticalRecord, Permissions.CriticalMonitor] },
  {
    path: '/ket-qua-nguy-cap/khoa-nhan',
    label: 'Gửi đến khoa tôi',
    permission: [Permissions.CriticalReceive, Permissions.CriticalTreat, Permissions.CriticalMonitor],
  },
  {
    path: '/ket-qua-nguy-cap/so',
    label: 'Sổ kết quả nguy cấp',
    permission: [Permissions.CriticalRecord, Permissions.CriticalReceive, Permissions.CriticalTreat, Permissions.CriticalMonitor],
  },
  {
    path: '/ket-qua-nguy-cap/dashboard',
    label: 'Dashboard',
    permission: [Permissions.CriticalRecord, Permissions.CriticalReceive, Permissions.CriticalTreat, Permissions.CriticalMonitor],
  },
  { path: '/ket-qua-nguy-cap/danh-muc', label: 'Danh mục và cấu hình', permission: [Permissions.CriticalManage] },
]

/** Thanh chuyển nhanh giữa các trang kết quả nguy cấp mà tài khoản được dùng. */
export function CriticalSectionNav() {
  const { hasPermission } = useAuth()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const links = SECTION_LINKS.filter((link) => hasAnyPermission(link.permission, hasPermission))
  if (links.length < 2) return null
  const active = links.find((link) => pathname.startsWith(link.path))?.path
  return (
    <Tabs
      size="small"
      activeKey={active}
      onChange={(key) => navigate(key)}
      items={links.map((link) => ({ key: link.path, label: link.label }))}
      style={{ marginBottom: 8 }}
    />
  )
}
