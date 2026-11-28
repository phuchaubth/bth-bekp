import { AudioMutedOutlined, ExclamationCircleFilled, SoundOutlined } from '@ant-design/icons'
import { Button, Space } from 'antd'
import { useEffect, useState, type CSSProperties } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { useCriticalAlerts } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import {
  chimeReady,
  countdownText,
  departmentList,
  isDisplayOnly,
  readMuted,
  secondsLeft,
  seesManyDepartments,
  timeOf,
  unlockChime,
  useAlertChime,
  useServerNow,
  writeMuted,
} from './criticalUtils'
import './critical.css'

const BAND: CSSProperties = {
  color: '#fff',
  padding: '8px 24px',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  flexWrap: 'wrap',
  fontSize: 14,
}

/**
 * Dải cảnh báo kết quả nguy cấp trên mọi trang (quyết định 60). Khoa nhận (người nghe máy, bác sĩ): dải đỏ chớp tắt khi có hồ sơ
 * chờ khoa xác nhận, chuông khi có hồ sơ mới và nhắc lại theo cấu hình (tắt tiếng được trên máy này); dải cam khi có hồ sơ bác
 * sĩ quá hạn xử trí. Khoa báo: dải cam khi có hồ sơ quá hạn báo. Tự cập nhật theo chu kỳ cấu hình.
 */
export function CriticalAlertBanner() {
  const { user, hasPermission } = useAuth()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const staff = hasPermission(Permissions.CriticalReceive) || hasPermission(Permissions.CriticalTreat)
  const records = hasPermission(Permissions.CriticalRecord)
  const enabled = (staff || records) && !isDisplayOnly(user?.permissions)
  // Tài khoản xem nhiều khoa: dải ghi khoa của người bệnh (quyết định 63).
  const many = seesManyDepartments(user, hasPermission)
  const alerts = useCriticalAlerts(enabled)
  const data = alerts.data
  const nowMs = useServerNow(data?.serverTimeUtc, alerts.dataUpdatedAt)
  const [muted, setMuted] = useState(readMuted)
  const [audioOn, setAudioOn] = useState(chimeReady)
  const pending = staff ? (data?.awaitingAcknowledgement ?? []) : []
  const soundEnabled = Boolean(data?.settings.soundEnabled)
  useAlertChime(
    pending.map((p) => p.id),
    staff && soundEnabled && !muted && audioOn,
    data?.settings.soundRepeatSeconds ?? 0,
  )

  // Trình duyệt chỉ cho phát âm thanh sau một thao tác: lần bấm, gõ phím đầu tiên trên trang mở khóa chuông. Mở khóa khi bấm
  // xong (click), không phải lúc nhấn xuống: dải cảnh báo đổi chữ giữa lúc nhấn và lúc thả thì lần bấm đó bị mất.
  useEffect(() => {
    if (audioOn || !staff || !enabled) return
    const unlock = () => void unlockChime().then((ok) => ok && setAudioOn(true))
    window.addEventListener('click', unlock, { once: true, capture: true })
    window.addEventListener('keydown', unlock, { once: true, capture: true })
    return () => {
      window.removeEventListener('click', unlock, { capture: true })
      window.removeEventListener('keydown', unlock, { capture: true })
    }
  }, [audioOn, staff, enabled])

  if (!data) return null

  /** Một nút cố định chỗ: chưa mở khóa chuông thì "Bật chuông", rồi "Tắt tiếng" / "Bật tiếng" (dải không đổi chiều cao). */
  const soundButton = () => {
    if (!audioOn && !muted) {
      return { label: 'Bật chuông', icon: <SoundOutlined />, onClick: () => void unlockChime().then((ok) => ok && setAudioOn(true)) }
    }
    return {
      label: muted ? 'Bật tiếng' : 'Tắt tiếng',
      icon: muted ? <AudioMutedOutlined /> : <SoundOutlined />,
      onClick: () => {
        writeMuted(!muted)
        setMuted(!muted)
      },
    }
  }

  if (pending.length > 0) {
    const first = pending[0]
    const left = secondsLeft(first.actionDueAtUtc, nowMs)
    const sound = soundButton()
    return (
      <div
        role="region"
        aria-label="Cảnh báo kết quả nguy cấp"
        className={data.settings.flashEnabled ? 'critical-flash' : undefined}
        style={{ ...BAND, background: '#9b1c1c', animationDuration: `${data.settings.flashPeriodMs}ms` }}
      >
        <Space wrap size={12}>
          <ExclamationCircleFilled style={{ fontSize: 18 }} />
          <strong aria-live="assertive">
            {pending.length} kết quả nguy cấp chờ khoa xác nhận
            {many && pending.length > 1 ? ` · ${departmentList(pending.map((p) => p.receivingDepartmentName))}` : ''}
          </strong>
          <span>
            {first.patientName}
            {many ? ` (${first.receivingDepartmentName})` : ''} · {first.testName} {first.value}
            {first.unit ? ` ${first.unit}` : ''} · {first.sendingDepartmentName} báo lúc {timeOf(first.notifiedAtUtc)} ·{' '}
            <span role="timer" aria-live="off">
              {left < 0 ? `bác sĩ đã quá hạn xử trí ${countdownText(left).slice('Quá hạn '.length)}` : `bác sĩ còn ${countdownText(left).slice('Còn '.length)} để xử trí`}
            </span>
          </span>
        </Space>
        <Space wrap>
          {pathname !== '/ket-qua-nguy-cap/khoa-nhan' && (
            <Button size="small" onClick={() => navigate('/ket-qua-nguy-cap/khoa-nhan')}>
              Xem ngay
            </Button>
          )}
          {soundEnabled && (
            <Button size="small" ghost icon={sound.icon} onClick={sound.onClick} aria-pressed={muted} style={{ minWidth: 112 }}>
              {sound.label}
            </Button>
          )}
        </Space>
      </div>
    )
  }

  const doctorLate = staff ? data.awaitingAction.filter((a) => secondsLeft(a.actionDueAtUtc, nowMs) < 0) : []
  if (doctorLate.length > 0) {
    return (
      <div role="region" aria-label="Cảnh báo kết quả nguy cấp" style={{ ...BAND, background: '#8a4b00' }}>
        <Space wrap size={12}>
          <ExclamationCircleFilled />
          <strong>
            {doctorLate.length} kết quả nguy cấp bác sĩ quá hạn xử trí
            {many ? ` · ${departmentList(doctorLate.map((a) => a.receivingDepartmentName))}` : ''}
          </strong>
          <span>Nhắc lại và báo trực tiếp bác sĩ.</span>
        </Space>
        {pathname !== '/ket-qua-nguy-cap/khoa-nhan' && (
          <Button size="small" onClick={() => navigate('/ket-qua-nguy-cap/khoa-nhan')}>
            Xem ngay
          </Button>
        )}
      </div>
    )
  }

  if (records && data.overdueNotification > 0) {
    return (
      <div role="region" aria-label="Cảnh báo kết quả nguy cấp" style={{ ...BAND, background: '#8a4b00' }}>
        <Space wrap size={12}>
          <ExclamationCircleFilled />
          <strong>{data.overdueNotification} kết quả nguy cấp quá hạn báo</strong>
          <span>Gọi báo khoa điều trị ngay.</span>
        </Space>
        {pathname !== '/ket-qua-nguy-cap/khoa-bao' && (
          <Button size="small" onClick={() => navigate('/ket-qua-nguy-cap/khoa-bao')}>
            Xem ngay
          </Button>
        )}
      </div>
    )
  }

  return null
}
