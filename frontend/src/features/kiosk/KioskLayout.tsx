import { useRef, type ReactNode } from 'react'
import { KIOSK_STEPS } from './kioskSession'

/** Giữ logo lâu (giây) để mở hộp đăng xuất dành cho nhân viên. */
const STAFF_HOLD_MS = 3000

export function KioskHeader({
  departmentName,
  right,
  onStaffHold,
}: {
  departmentName?: string
  right?: ReactNode
  onStaffHold?: () => void
}) {
  const timer = useRef<number | null>(null)
  const start = () => {
    if (onStaffHold) {
      timer.current = window.setTimeout(onStaffHold, STAFF_HOLD_MS)
    }
  }
  const cancel = () => {
    if (timer.current !== null) {
      window.clearTimeout(timer.current)
      timer.current = null
    }
  }
  return (
    <header className="kiosk-header">
      <button
        type="button"
        className="kiosk-logo"
        aria-label="Biểu tượng bệnh viện (nhân viên: giữ 3 giây để đăng xuất máy)"
        onPointerDown={start}
        onPointerUp={cancel}
        onPointerLeave={cancel}
        onContextMenu={(e) => e.preventDefault()}
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M9 3h6v6h6v6h-6v6H9v-6H3V9h6z" />
        </svg>
      </button>
      <div className="kiosk-hospital">
        <strong>Làm phiếu tự phục vụ</strong>
        <span>{departmentName ? `${departmentName} · ` : ''}Kiosk cho người bệnh</span>
      </div>
      {right}
    </header>
  )
}

/** Thanh bốn bước: bước trước đã xong, bước hiện tại tô đậm. */
export function KioskSteps({ current }: { current: number }) {
  return (
    <ol className="kiosk-steps" aria-label="Các bước">
      {KIOSK_STEPS.map((label, index) => (
        <li
          key={label}
          className={index < current ? 'done' : index === current ? 'active' : undefined}
          aria-current={index === current ? 'step' : undefined}
        >
          {index < current ? '✓ ' : ''}
          {index + 1}. {label}
        </li>
      ))}
    </ol>
  )
}

export function KioskFooter({ children }: { children: ReactNode }) {
  return <footer className="kiosk-footer">{children}</footer>
}
