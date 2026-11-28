import {
  CheckCircleFilled,
  CloseCircleFilled,
  PauseCircleOutlined,
  PlayCircleOutlined,
  SoundOutlined,
  StopOutlined,
  WifiOutlined,
} from '@ant-design/icons'
import { Button, Select } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useDepartments } from '../../api/admin'
import { accountDepartments } from '../../api/auth'
import { useCriticalAlerts, useCriticalSettings, type CriticalAlertItem, type CriticalAlerts } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import {
  clockState,
  countdownText,
  cycleDepartments,
  departmentList,
  isDisplayOnly,
  playChime,
  secondsLeft,
  seesManyDepartments,
  timeOf,
  unlockChime,
  useAlertChime,
  useNow,
  useServerNow,
  type ClockState,
} from './criticalUtils'
import { CYCLE_SECONDS, useDepartmentCycle } from './departmentCycle'
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

function AlertCard({
  item,
  nowMs,
  warnMinutes,
  variant,
  showDepartment,
}: {
  item: CriticalAlertItem
  nowMs: number
  warnMinutes: number
  variant: 'new' | 'doctor'
  /** Màn hình của nhiều khoa: ghi khoa của người bệnh (quyết định 63). */
  showDepartment: boolean
}) {
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
          {showDepartment && <div style={{ fontSize: 20, fontWeight: 700, color: '#90caf9' }}>{item.receivingDepartmentName}</div>}
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
        {item.sendingDepartmentName} · {item.notifiedByName} {item.callPending ? 'báo trên phần mềm' : `báo → ${item.receiverName ?? ''} nghe máy`}
      </div>
    </article>
  )
}

/** Các khoa có hồ sơ trên màn hình (chờ xác nhận, chờ bác sĩ, vừa xong), không trùng, xếp theo tên. */
const departmentsOnScreen = (data: CriticalAlerts | undefined) =>
  cycleDepartments(
    [data?.awaitingAcknowledgement ?? [], data?.awaitingAction ?? [], data?.recentlyCompleted ?? []].map((list) =>
      list.map((item) => ({ departmentId: item.receivingDepartmentId, name: item.receivingDepartmentName })),
    ),
  )

/**
 * Màn hình theo dõi kết quả nguy cấp tại khoa (quyết định 60): TV đặt ở khoa, chỉ để theo dõi (không có nút thao tác). Tự cập
 * nhật theo chu kỳ cấu hình; có hồ sơ mới thì dải đỏ chớp tắt và chuông kêu, nhắc lại tới khi khoa xác nhận; mất kết nối thì báo
 * rõ trên màn hình. Tài khoản màn hình theo dõi thấy tên viết tắt (trừ khi cấu hình cho hiện họ tên đầy đủ). Quyết định 66: QLCL
 * xem màn hình của mọi khoa; QLCL và tài khoản nhiều khoa chọn từng khoa hoặc chạy lướt từng khoa; dải đỏ, chuông vẫn tính mọi
 * khoa của tài khoản khi đang hiện một khoa, để chạy lướt không làm lỡ hồ sơ mới.
 */
export function CriticalScreenPage() {
  const navigate = useNavigate()
  const { user, logout, hasPermission } = useAuth()
  const everyDepartment = hasPermission(Permissions.CriticalMonitor) || hasPermission(Permissions.ScopeAllDepartments)
  const own = accountDepartments(user)
  const [params] = useSearchParams()
  const departments = useDepartments(true)
  const requested = params.get('khoa') || undefined
  // Khoa đang hiện (bỏ trống là tất cả); tài khoản của khoa chỉ chọn được khoa của mình. Địa chỉ lưu sẵn một khoa đã xóa thì hiện
  // mọi khoa (khi danh sách khoa đã tải), không để màn hình báo mất kết nối.
  const known = (id: string) => (everyDepartment ? (departments.data?.some((d) => d.id === id) ?? true) : own.some((d) => d.id === id))
  const departmentId = requested && known(requested) ? requested : undefined
  // Toàn bộ phạm vi của tài khoản (QLCL: mọi khoa): dải đỏ, chuông, mất kết nối. Một khoa: các cột hồ sơ, số hôm nay.
  const alerts = useCriticalAlerts(true, { allDepartments: everyDepartment })
  const shown = useCriticalAlerts(Boolean(departmentId), { departmentId })
  const settings = useCriticalSettings()
  const data: CriticalAlerts | undefined = alerts.data
  // Lần đầu sang một khoa, trong lúc tải: giữ số liệu đang hiện và làm mờ đi (như dashboard), không để màn hình thoáng hiện cột
  // trống "không có kết quả nguy cấp" hay số của khoa trước như số của khoa mới.
  const loading = Boolean(departmentId) && (shown.isPending || shown.isPlaceholderData)
  const view: CriticalAlerts | undefined = departmentId ? (shown.data ?? data) : data
  const chosenName = departmentId ? (everyDepartment ? departments.data : own)?.find((d) => d.id === departmentId)?.name : undefined
  const nowMs = useServerNow(data?.serverTimeUtc, alerts.dataUpdatedAt)
  const clientNow = useNow()
  const [audioOn, setAudioOn] = useState(false)
  const s = data?.settings
  const waiting = data?.awaitingAcknowledgement ?? []
  const pending = view?.awaitingAcknowledgement ?? []
  const doctor = view?.awaitingAction ?? []
  const recent = view?.recentlyCompleted ?? []
  const soundEnabled = Boolean(s?.soundEnabled)
  useAlertChime(
    waiting.map((p) => p.id),
    soundEnabled && audioOn,
    s?.soundRepeatSeconds ?? 0,
  )
  const displayOnly = isDisplayOnly(user?.permissions)
  const stale =
    alerts.isError ||
    (Boolean(departmentId) && shown.isError) ||
    (alerts.dataUpdatedAt > 0 && clientNow - alerts.dataUpdatedAt > staleAfterMs(s?.pollSeconds ?? 15))
  const warn = s?.warnMinutes ?? 3
  const nearest = waiting.map((p) => p.actionDueAtUtc).sort()[0]
  const initials = displayOnly && settings.data?.values.displayFullName === false
  // Tài khoản xem nhiều khoa (quyết định 63): mỗi hồ sơ ghi khoa của người bệnh (đang hiện một khoa thì không cần).
  const many = seesManyDepartments(user, hasPermission)
  // Chọn khoa, chạy lướt (quyết định 66): QLCL chạy lướt các khoa đang có hồ sơ trên màn hình; tài khoản nhiều khoa chạy lướt các
  // khoa của mình. Tài khoản một khoa không có.
  const choosing = everyDepartment || own.length > 1
  const cycle = everyDepartment ? departmentsOnScreen(data) : choosing ? cycleDepartments([own.map((d) => ({ departmentId: d.id, name: d.name }))]) : []
  const cycling = useDepartmentCycle(cycle, departmentId)
  const position = cycling.positionOf(departmentId)
  const departmentOptions = everyDepartment
    ? (departments.data ?? []).map((d) => ({ value: d.id, label: d.isActive ? d.name : `${d.name} (ngưng)` }))
    : cycle.map((d) => ({ value: d.id, label: d.name }))

  const enableSound = () => {
    void unlockChime().then((ok) => {
      setAudioOn(ok)
      if (ok) playChime()
    })
  }

  // Máy TV đặt trình duyệt cho tự phát âm thanh (tài liệu triển khai, mục 5.14) thì chuông bật ngay khi mở trang, không cần ai
  // bấm sau mỗi lần máy khởi động lại. Trình duyệt chưa cho phép thì chờ lần bấm đầu tiên như cũ.
  useEffect(() => {
    if (!soundEnabled || audioOn) return
    let active = true
    void unlockChime().then((ok) => {
      if (active && ok) setAudioOn(true)
    })
    return () => {
      active = false
    }
  }, [soundEnabled, audioOn])

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
          <div style={{ fontSize: 34, fontWeight: 700 }}>{chosenName || view?.receivingDepartmentNames.join(', ') || 'Kết quả nguy cấp'}</div>
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

      {choosing && (
        <nav
          aria-label="Chọn khoa trên màn hình"
          style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap', color: DARK.muted }}
          onClick={(e) => e.stopPropagation()}
        >
          <Select
            showSearch
            allowClear
            optionFilterProp="label"
            placeholder={everyDepartment ? 'Mọi khoa' : 'Mọi khoa của tôi'}
            style={{ width: 280 }}
            value={departmentId}
            options={departmentOptions}
            onChange={(value?: string) => cycling.choose(value)}
            aria-label="Khoa"
          />
          {cycling.seconds ? (
            <>
              <span role="status" aria-live="polite" style={{ color: DARK.text, fontSize: 20, fontWeight: 600 }}>
                {position > 0 ? `Khoa ${position}/${cycle.length}` : `${cycle.length} khoa`} ·{' '}
                {cycling.paused ? 'đã tạm dừng' : `mỗi khoa ${cycling.seconds} giây`}
              </span>
              <Select
                value={cycling.seconds}
                options={CYCLE_SECONDS.map((seconds) => ({ value: seconds, label: `Mỗi khoa ${seconds} giây` }))}
                onChange={(seconds: number) => cycling.setSeconds(seconds)}
                aria-label="Mỗi khoa"
                style={{ width: 170 }}
              />
              <Button ghost icon={cycling.paused ? <PlayCircleOutlined /> : <PauseCircleOutlined />} onClick={cycling.togglePause}>
                {cycling.paused ? 'Tiếp tục' : 'Tạm dừng'}
              </Button>
              <Button ghost icon={<StopOutlined />} onClick={cycling.stop}>
                Dừng chạy lướt
              </Button>
            </>
          ) : (
            <Button ghost icon={<PlayCircleOutlined />} disabled={cycle.length === 0} onClick={cycling.start}>
              Chạy lướt từng khoa
            </Button>
          )}
        </nav>
      )}

      {waiting.length > 0 && (
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
          <span>
            KẾT QUẢ NGUY CẤP MỚI · {waiting.length} hồ sơ chờ khoa xác nhận
            {many ? ` · ${departmentList(waiting.map((p) => p.receivingDepartmentName))}` : ''}
          </span>
          {nearest && <span style={{ fontVariantNumeric: 'tabular-nums' }}>Bác sĩ {countdownText(secondsLeft(nearest, nowMs)).toLowerCase()}</span>}
        </div>
      )}

      <div
        aria-busy={loading}
        style={{
          display: 'grid',
          gridTemplateColumns: 'minmax(0, 5fr) minmax(0, 5fr) minmax(0, 3fr)',
          gap: 18,
          flex: 1,
          minHeight: 0,
          opacity: loading ? 0.6 : 1,
          transition: 'opacity 0.2s',
        }}
      >
        <Panel title="Chờ khoa xác nhận" count={pending.length} color="#ff8a80">
          {pending.length === 0 ? (
            <div style={{ color: DARK.muted }}>Không có kết quả nguy cấp chờ khoa xác nhận.</div>
          ) : (
            pending.map((item) => (
              <AlertCard key={item.id} item={item} nowMs={nowMs} warnMinutes={warn} variant="new" showDepartment={many && !departmentId} />
            ))
          )}
          <div style={{ color: DARK.muted, fontSize: 15, marginTop: 'auto' }}>
            Người nghe máy bấm Đã nhận cuộc gọi (báo trên phần mềm: Đã nhận thông báo) bằng tài khoản của mình
            {soundEnabled && (s?.soundRepeatSeconds ?? 0) > 0 ? `; chuông nhắc lại mỗi ${s?.soundRepeatSeconds} giây tới khi khoa xác nhận.` : '.'}
          </div>
        </Panel>
        <Panel title="Chờ bác sĩ xử trí" count={doctor.length} color={DARK.warn}>
          {doctor.length === 0 ? (
            <div style={{ color: DARK.muted }}>Không có hồ sơ chờ bác sĩ.</div>
          ) : (
            doctor.map((item) => (
              <AlertCard key={item.id} item={item} nowMs={nowMs} warnMinutes={warn} variant="doctor" showDepartment={many && !departmentId} />
            ))
          )}
        </Panel>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 18, minHeight: 0 }}>
          <Panel title="Hôm nay">
            <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap' }}>
              <div>
                <div style={{ fontSize: 44, fontWeight: 700 }}>{view?.todayReceived ?? 0}</div>
                <div style={{ color: DARK.muted }}>kết quả nguy cấp</div>
              </div>
              <div>
                <div style={{ fontSize: 44, fontWeight: 700 }}>
                  {view?.todayActedOnTime ?? 0}/{view?.todayActed ?? 0}
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
                    {many && !departmentId ? `${item.receivingDepartmentName} · ` : ''}
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
