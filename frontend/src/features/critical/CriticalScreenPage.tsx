import { CheckCircleFilled, CloseCircleFilled, SoundOutlined, WifiOutlined } from '@ant-design/icons'
import { Button } from 'antd'
import dayjs from 'dayjs'
import { useState, type CSSProperties, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { useCriticalAlerts, useCriticalSettings, type CriticalAlertItem, type CriticalAlerts } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import {
  clockState,
  countdownText,
  isDisplayOnly,
  playChime,
  secondsLeft,
  timeOf,
  unlockChime,
  useAlertChime,
  useNow,
  useServerNow,
  type ClockState,
} from './criticalUtils'
import './critical.css'

/** Màu trên nền tối (đủ tương phản cho chữ nhìn từ xa); luôn kèm chữ, không chỉ dựa vào màu. */
const DARK = {
  bg: '#0b1517',
  panel: '#13252a',
  border: '#24414a',
  text: '#eef6f5',
  muted: '#a9c1c0',
  ok: '#5fd3c5',
  warn: '#ffb74d',
  late: '#ff8a80',
}

const CLOCK: Record<ClockState, string> = { ok: DARK.ok, warn: DARK.warn, late: DARK.late }

/** Quá lâu chưa cập nhật được (mạng, máy chủ, phiên đăng nhập): báo mất kết nối, không để màn hình hiện số cũ như số mới. */
const staleAfterMs = (pollSeconds: number) => Math.max(3 * pollSeconds, 45) * 1000

function Panel({ title, count, color, children }: { title: string; count?: number; color?: string; children: ReactNode }) {
  return (
    <section
      aria-label={title}
      style={{ background: DARK.panel, border: `1px solid ${DARK.border}`, borderRadius: 16, padding: 20, display: 'flex', flexDirection: 'column', gap: 14, minHeight: 0 }}
    >
      <header style={{ display: 'flex', alignItems: 'center', gap: 12, fontSize: 24, fontWeight: 600 }}>
        <span>{title}</span>
        {count !== undefined && (
          <span style={{ background: color, color: '#0b1517', borderRadius: 999, padding: '0 14px', fontSize: 22, fontWeight: 700 }}>{count}</span>
        )}
      </header>
      {children}
    </section>
  )
}

function Countdown({ dueUtc, nowMs, warnMinutes, label }: { dueUtc: string; nowMs: number; warnMinutes: number; label: string }) {
  const seconds = secondsLeft(dueUtc, nowMs)
  const state = clockState(seconds, warnMinutes)
  return (
    <div role="timer" aria-live="off" style={{ textAlign: 'right', color: CLOCK[state], fontVariantNumeric: 'tabular-nums' }}>
      <div style={{ fontSize: 15 }}>{label}</div>
      <div style={{ fontSize: 34, fontWeight: 700, lineHeight: 1.1 }}>{countdownText(seconds)}</div>
    </div>
  )
}

function AlertCard({ item, nowMs, warnMinutes, variant }: { item: CriticalAlertItem; nowMs: number; warnMinutes: number; variant: 'new' | 'doctor' }) {
  const late = secondsLeft(item.actionDueAtUtc, nowMs) < 0
  const border = variant === 'new' ? '#d32f2f' : late ? DARK.late : DARK.border
  return (
    <article
      aria-label={`${item.patientName} · ${item.testName}`}
      style={{ border: `2px solid ${border}`, borderRadius: 14, padding: 16, background: variant === 'new' ? '#2a1416' : '#0f1f23' }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 15, color: variant === 'new' ? '#ffcdd2' : late ? DARK.late : DARK.muted, fontWeight: 600 }}>
            {variant === 'new'
              ? `MỚI · báo lúc ${timeOf(item.notifiedAtUtc)}`
              : late
                ? 'QUÁ HẠN · nhắc lại, báo trực tiếp bác sĩ'
                : `${item.acknowledgedByName ?? ''} nhận lúc ${timeOf(item.acknowledgedAtUtc)}`}
          </div>
          <div style={{ fontSize: 30, fontWeight: 700, lineHeight: 1.2 }}>{item.patientName}</div>
          <div style={{ fontSize: 16, color: DARK.muted }}>
            Năm sinh {item.patientBirthYear} · mã {item.patientCodeTail}
          </div>
        </div>
        <Countdown dueUtc={item.actionDueAtUtc} nowMs={nowMs} warnMinutes={warnMinutes} label="BS xử trí" />
      </div>
      <div style={{ marginTop: 8, fontSize: 24 }}>
        <span style={{ fontWeight: 600 }}>{item.testName}</span>{' '}
        <span style={{ color: '#ff8a80', fontWeight: 700 }}>
          {item.value}
          {item.unit ? ` ${item.unit}` : ''}
        </span>
      </div>
      <div style={{ fontSize: 15, color: DARK.muted, marginTop: 4 }}>
        {item.sendingDepartmentName} · {item.notifiedByName} báo → {item.receiverName} nghe máy
      </div>
    </article>
  )
}

/**
 * Màn hình theo dõi kết quả nguy cấp tại khoa (quyết định 60): TV đặt ở khoa, chỉ để theo dõi (không có nút thao tác). Tự cập
 * nhật theo chu kỳ cấu hình; có hồ sơ mới thì dải đỏ chớp tắt và chuông kêu, nhắc lại tới khi khoa xác nhận; mất kết nối thì báo
 * rõ trên màn hình. Tài khoản màn hình theo dõi thấy tên viết tắt (trừ khi cấu hình cho hiện họ tên đầy đủ).
 */
export function CriticalScreenPage() {
  const navigate = useNavigate()
  const { user, logout } = useAuth()
  const alerts = useCriticalAlerts(true)
  const settings = useCriticalSettings()
  const data: CriticalAlerts | undefined = alerts.data
  const nowMs = useServerNow(data?.serverTimeUtc, alerts.dataUpdatedAt)
  const clientNow = useNow()
  const [audioOn, setAudioOn] = useState(false)
  const s = data?.settings
  const pending = data?.awaitingAcknowledgement ?? []
  const doctor = data?.awaitingAction ?? []
  const recent = data?.recentlyCompleted ?? []
  const soundEnabled = Boolean(s?.soundEnabled)
  useAlertChime(
    pending.map((p) => p.id),
    soundEnabled && audioOn,
    s?.soundRepeatSeconds ?? 0,
  )
  const displayOnly = isDisplayOnly(user?.permissions)
  const stale = alerts.isError || (alerts.dataUpdatedAt > 0 && clientNow - alerts.dataUpdatedAt > staleAfterMs(s?.pollSeconds ?? 15))
  const warn = s?.warnMinutes ?? 3
  const nearest = pending.map((p) => p.actionDueAtUtc).sort()[0]
  const initials = displayOnly && settings.data?.values.displayFullName === false

  const enableSound = () => {
    void unlockChime().then((ok) => {
      setAudioOn(ok)
      if (ok) playChime()
    })
  }

  const page: CSSProperties = {
    minHeight: '100vh',
    background: DARK.bg,
    color: DARK.text,
    padding: '24px 32px',
    boxSizing: 'border-box',
    display: 'flex',
    flexDirection: 'column',
    gap: 18,
    fontSize: 18,
    position: 'relative',
  }

  return (
    <main style={page} onClick={() => !audioOn && soundEnabled && enableSound()}>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 24, flexWrap: 'wrap' }}>
        <div>
          <div style={{ fontSize: 34, fontWeight: 700 }}>{data?.receivingDepartmentNames.join(', ') || 'Kết quả nguy cấp'}</div>
          <div style={{ color: DARK.muted }}>
            Màn hình theo dõi kết quả nguy cấp · chỉ để theo dõi{initials ? ' · tên viết tắt để giữ bí mật thông tin người bệnh' : ''}
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 24, flexWrap: 'wrap' }}>
          <span style={{ color: stale ? DARK.late : DARK.ok }}>
            <WifiOutlined />{' '}
            {stale ? 'Mất kết nối' : alerts.dataUpdatedAt > 0 ? `Đã cập nhật ${dayjs(alerts.dataUpdatedAt).format('HH:mm:ss')}` : 'Đang tải…'}
          </span>
          {soundEnabled ? (
            <Button
              ghost
              icon={<SoundOutlined />}
              onClick={(e) => {
                e.stopPropagation()
                enableSound()
              }}
            >
              {audioOn ? 'Chuông bật · thử chuông' : 'Bấm để bật chuông'}
            </Button>
          ) : (
            <span style={{ color: DARK.muted }}>Chuông tắt (thông số cấu hình)</span>
          )}
          <span style={{ fontSize: 40, fontWeight: 700, fontVariantNumeric: 'tabular-nums' }}>{dayjs(nowMs).format('HH:mm:ss')}</span>
        </div>
      </header>

      {pending.length > 0 && (
        <div
          role="status"
          className={s?.flashEnabled ? 'critical-flash' : undefined}
          style={{
            background: '#9b1c1c',
            animationDuration: `${s?.flashPeriodMs ?? 1200}ms`,
            borderRadius: 14,
            padding: '14px 24px',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: 16,
            fontSize: 30,
            fontWeight: 700,
            color: '#fff',
          }}
        >
          <span>KẾT QUẢ NGUY CẤP MỚI · {pending.length} hồ sơ chờ khoa xác nhận</span>
          {nearest && <span style={{ fontVariantNumeric: 'tabular-nums' }}>Bác sĩ {countdownText(secondsLeft(nearest, nowMs)).toLowerCase()}</span>}
        </div>
      )}

      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 5fr) minmax(0, 5fr) minmax(0, 3fr)', gap: 18, flex: 1, minHeight: 0 }}>
        <Panel title="Chờ khoa xác nhận" count={pending.length} color="#ff8a80">
          {pending.length === 0 ? (
            <div style={{ color: DARK.muted }}>Không có kết quả nguy cấp chờ khoa xác nhận.</div>
          ) : (
            pending.map((item) => <AlertCard key={item.id} item={item} nowMs={nowMs} warnMinutes={warn} variant="new" />)
          )}
          <div style={{ color: DARK.muted, fontSize: 15, marginTop: 'auto' }}>
            Người nghe máy bấm Đã nhận cuộc gọi bằng tài khoản của mình
            {soundEnabled && (s?.soundRepeatSeconds ?? 0) > 0 ? `; chuông nhắc lại mỗi ${s?.soundRepeatSeconds} giây tới khi khoa xác nhận.` : '.'}
          </div>
        </Panel>
        <Panel title="Chờ bác sĩ xử trí" count={doctor.length} color={DARK.warn}>
          {doctor.length === 0 ? (
            <div style={{ color: DARK.muted }}>Không có hồ sơ chờ bác sĩ.</div>
          ) : (
            doctor.map((item) => <AlertCard key={item.id} item={item} nowMs={nowMs} warnMinutes={warn} variant="doctor" />)
          )}
        </Panel>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 18, minHeight: 0 }}>
          <Panel title="Hôm nay">
            <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap' }}>
              <div>
                <div style={{ fontSize: 44, fontWeight: 700 }}>{data?.todayReceived ?? 0}</div>
                <div style={{ color: DARK.muted }}>kết quả nguy cấp</div>
              </div>
              <div>
                <div style={{ fontSize: 44, fontWeight: 700 }}>
                  {data?.todayActedOnTime ?? 0}/{data?.todayActed ?? 0}
                </div>
                <div style={{ color: DARK.muted }}>BS xử trí đúng hạn</div>
              </div>
            </div>
          </Panel>
          <Panel title="Hoàn tất gần đây">
            {recent.length === 0 ? (
              <div style={{ color: DARK.muted }}>Chưa có hồ sơ hoàn tất.</div>
            ) : (
              recent.map((item) => (
                <div key={item.id} style={{ borderBottom: `1px solid ${DARK.border}`, paddingBottom: 8 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                    <strong>{item.patientName}</strong>
                    <span style={{ color: DARK.muted }}>{timeOf(item.actionAtUtc)}</span>
                  </div>
                  <div style={{ color: DARK.muted, fontSize: 15 }}>
                    {item.testName} {item.value}
                    {item.unit ? ` ${item.unit}` : ''}
                  </div>
                  <div style={{ fontSize: 15, color: item.actedOnTime ? DARK.ok : DARK.late }}>
                    {item.actedOnTime ? <CheckCircleFilled /> : <CloseCircleFilled />} BS {item.actedOnTime ? 'xử trí đúng hạn' : 'xử trí quá hạn'}
                    {item.actionByName ? ` · ${item.actionByName}` : ''}
                  </div>
                </div>
              ))
            )}
          </Panel>
        </div>
      </div>

      <footer style={{ display: 'flex', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap', color: DARK.muted, fontSize: 15 }}>
        <span>Xác nhận và ghi xử trí bằng tài khoản cá nhân trên máy tính hoặc điện thoại; màn hình này không có nút thao tác.</span>
        <span>
          Tự cập nhật mỗi {s?.pollSeconds ?? 15} giây · {s?.flashEnabled ? `chớp tắt ${((s?.flashPeriodMs ?? 1200) / 1000).toLocaleString('vi-VN')} giây một lần` : 'không chớp tắt'}
          {' · '}
          {displayOnly ? (
            <Button type="link" size="small" style={{ color: DARK.muted, padding: 0 }} onClick={() => void logout()}>
              Đăng xuất
            </Button>
          ) : (
            <Button type="link" size="small" style={{ color: DARK.muted, padding: 0 }} onClick={() => navigate('/ket-qua-nguy-cap/khoa-nhan')}>
              Về trang làm việc
            </Button>
          )}
        </span>
      </footer>

      {stale && (
        <div
          role="alert"
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(5, 10, 12, 0.88)',
            display: 'grid',
            placeItems: 'center',
            textAlign: 'center',
            padding: 32,
            zIndex: 10,
          }}
        >
          <div>
            <div style={{ fontSize: 44, fontWeight: 800, color: DARK.late }}>MẤT KẾT NỐI · MÀN HÌNH KHÔNG CÒN CẬP NHẬT</div>
            <div style={{ fontSize: 24, marginTop: 16 }}>
              {alerts.dataUpdatedAt > 0 ? `Cập nhật lần cuối ${dayjs(alerts.dataUpdatedAt).format('HH:mm:ss')}. ` : ''}Kết quả nguy cấp vẫn được báo
              qua điện thoại; kiểm tra mạng hoặc đăng nhập lại.
            </div>
          </div>
        </div>
      )}
    </main>
  )
}
