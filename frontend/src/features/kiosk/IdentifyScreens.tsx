import { useEffect, useRef, useState } from 'react'
import type { Gender } from '../../api/patients'
import { kioskApi, type KioskConfig, type KioskIdentity, type KioskIdentityRequest } from '../../api/kiosk'
import { citizenIdMismatch, isValidCitizenId } from '../patients/cccd'
import { parseCccdQr } from '../patients/cccdQrParser'
import { birthDateError, dateDigitsToIso, DATE_DIGITS, displayDate, formatDateDigits } from './kioskDates'
import { NumberPad, TextKeyboard } from './KioskKeyboards'
import { KioskFooter } from './KioskLayout'
import { kioskErrorMessage } from './kioskSession'

export type IdentifyMethod = 'scan' | 'lookup' | 'manual'

const PHONE_PATTERN = /^(0|\+84)[0-9]{9,10}$/
const genderLabels: Record<Gender, string> = { Male: 'Nam', Female: 'Nữ' }

/** Nhóm 4 chữ số cho dễ đọc: 0790 7000 0123. */
const groupDigits = (value: string) => value.replace(/(\d{4})(?=\d)/g, '$1 ')

/** Mã y tế ở bệnh viện chỉ gồm chữ số: trên kiosk chỉ nhập được số, tối đa bằng độ dài mã của hồ sơ người bệnh. */
const PATIENT_CODE_MAX_LENGTH = 50

/** Gọi nhận diện, quản lý trạng thái đang gửi và lỗi cho từng màn hình. */
function useIdentify(onIdentified: (request: KioskIdentityRequest, identity: KioskIdentity) => void) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const identify = async (request: KioskIdentityRequest) => {
    if (busy) {
      return
    }
    setBusy(true)
    setError(null)
    try {
      onIdentified(request, await kioskApi.identify(request))
    } catch (failure) {
      setError(kioskErrorMessage(failure))
      setBusy(false)
    }
  }
  return { busy, error, setError, identify }
}

/** Màn hình đầu: chọn cách lấy thông tin. */
export function StartScreen({ config, onChoose }: { config: KioskConfig; onChoose: (method: IdentifyMethod) => void }) {
  return (
    <main className="kiosk-main">
      <div className="kiosk-welcome">
        <h1>
          Xin chào!
          <br />
          Ông/bà tự làm phiếu tại đây
        </h1>
        <p className="kiosk-lead">Chỉ 3 bước đơn giản.</p>
      </div>
      <ol className="kiosk-steps-intro" aria-label="Các bước sẽ làm">
        <li>
          <b aria-hidden="true">1</b>
          <strong>Nhận diện</strong>Quét CCCD hoặc nhập mã
        </li>
        <li>
          <b aria-hidden="true">2</b>
          <strong>Trả lời</strong>Chạm để chọn câu trả lời
        </li>
        <li>
          <b aria-hidden="true">3</b>
          <strong>Ký tên</strong>Ký một lần cho tất cả phiếu
        </li>
      </ol>

      {config.ready ? (
        <div className="kiosk-methods kiosk-bottom">
          <h2>Chọn cách lấy thông tin</h2>
          <button type="button" className="kiosk-method featured" onClick={() => onChoose('scan')}>
            <span className="badge">Nhanh nhất</span>
            <svg viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <rect x="3" y="3" width="7" height="7" rx="1" />
              <rect x="14" y="3" width="7" height="7" rx="1" />
              <rect x="3" y="14" width="7" height="7" rx="1" />
              <path d="M14 14h3v3h-3zM20 14v.01M14 20h.01M17 20h3v-3" />
            </svg>
            <span>
              <strong>Quét CCCD</strong>
              <span>Đưa mã QR trên thẻ căn cước vào máy quét. Thông tin được điền sẵn.</span>
            </span>
          </button>
          <button type="button" className="kiosk-method" onClick={() => onChoose('lookup')}>
            <svg viewBox="0 0 24 24" fill="none" stroke="#0d7c74" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <rect x="3" y="4" width="18" height="16" rx="2" />
              <path d="M7 9h.01M11 9h.01M15 9h.01M7 13h.01M11 13h.01M15 13h.01M7 17h10" />
            </svg>
            <span>
              <strong>Nhập mã y tế hoặc số CCCD</strong>
              <span>Lấy thông tin ông/bà đã có ở bệnh viện. Cần nhập thêm ngày sinh để xác nhận.</span>
            </span>
          </button>
          <button type="button" className="kiosk-method" onClick={() => onChoose('manual')}>
            <svg viewBox="0 0 24 24" fill="none" stroke="#0d7c74" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M4 20h4L19 9l-4-4L4 16v4z" />
              <path d="M13 7l4 4" />
            </svg>
            <span>
              <strong>Nhập thông tin</strong>
              <span>Khi ông/bà không mang CCCD hoặc chưa có mã y tế.</span>
            </span>
          </button>
        </div>
      ) : (
        <div className="kiosk-error kiosk-bottom">Kiosk chưa có phiếu nào để làm. Mời ông/bà đến quầy tiếp đón.</div>
      )}

      <div className="kiosk-help">
        <strong>Cần hỗ trợ? Mời ông/bà đến quầy tiếp đón.</strong>
        <span>Thông tin của ông/bà được bảo mật và tự xóa khỏi màn hình khi xong.</span>
      </div>
    </main>
  )
}

/**
 * Quét mã QR trên CCCD bằng máy quét gắn dưới màn hình (máy quét gõ nội dung như bàn phím rồi nhấn Enter vào một ô ẩn
 * luôn giữ con trỏ). Máy chỉ đọc chữ trong mã QR, không chụp ảnh thẻ.
 */
export function ScanScreen({
  onIdentified,
  onBack,
}: {
  onIdentified: (request: KioskIdentityRequest, identity: KioskIdentity) => void
  onBack: () => void
}) {
  const input = useRef<HTMLInputElement>(null)
  const [raw, setRaw] = useState('')
  const { busy, error, setError, identify } = useIdentify(onIdentified)
  const refocus = () => window.setTimeout(() => input.current?.focus(), 0)

  useEffect(() => {
    input.current?.focus()
  }, [])

  const submit = () => {
    const text = raw
    setRaw('')
    const parsed = parseCccdQr(text)
    if (!parsed.ok) {
      setError('Chưa đọc được mã QR trên thẻ. Ông/bà lật thẻ, đưa mã QR lại gần đèn đỏ và giữ yên 1–2 giây.')
      return
    }
    const { card } = parsed
    void identify({
      method: 'CitizenIdCard',
      dateOfBirth: card.dateOfBirth,
      citizenId: card.citizenId,
      card: {
        fullName: card.fullName,
        gender: card.gender,
        permanentAddress: card.permanentAddress || null,
        issueDate: card.issueDate,
        oldIdNumber: card.oldIdNumber,
      },
    })
  }

  return (
    <>
      <main className="kiosk-main" onPointerDown={refocus}>
        <h1>Đưa mã QR trên thẻ căn cước vào máy quét</h1>
        <div className="kiosk-illustration">
          <svg viewBox="0 0 640 560" role="img" aria-label="Hình minh họa: đưa mã QR trên thẻ vào máy quét bên dưới màn hình">
            <rect x="80" y="24" width="420" height="260" rx="22" fill="#e2f3f1" stroke="#0d7c74" strokeWidth="4" />
            <rect x="112" y="60" width="100" height="130" rx="8" fill="#ffffff" stroke="#9fc3c0" strokeWidth="3" />
            <rect x="236" y="70" width="190" height="14" rx="7" fill="#9fc3c0" />
            <rect x="236" y="102" width="150" height="14" rx="7" fill="#9fc3c0" />
            <rect x="236" y="134" width="170" height="14" rx="7" fill="#9fc3c0" />
            <rect x="396" y="180" width="84" height="84" rx="8" fill="#ffffff" stroke="#0d7c74" strokeWidth="4" />
            <path d="M408 192h22v22h-22zM446 192h22v22h-22zM408 230h22v22h-22zM446 230h9v9h-9zM459 243h9v9h-9z" fill="#0d7c74" />
            <path d="M438 282 V 388" fill="none" stroke="#0d7c74" strokeWidth="6" strokeLinecap="round" strokeDasharray="4 16" />
            <path d="M414 372 l24 26 24 -26" fill="none" stroke="#0d7c74" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />
            <rect x="308" y="416" width="260" height="120" rx="18" fill="#16302e" />
            <rect x="338" y="436" width="200" height="14" rx="7" fill="#d9534f" />
            <text x="438" y="504" textAnchor="middle" fontSize="24" fill="#ffffff">
              Máy quét
            </text>
            <text x="284" y="484" textAnchor="end" fontSize="22" fill="#5b7472">
              Bên dưới màn hình
            </text>
          </svg>
        </div>
        <ol className="kiosk-list">
          <li>Lật thẻ sang mặt có mã QR.</li>
          <li>Đưa mã QR lại gần đèn đỏ của máy quét, giữ yên 1–2 giây.</li>
          <li>Nghe tiếng “bíp” là đã đọc xong, máy tự chuyển sang bước sau.</li>
        </ol>
        {error ? (
          <div className="kiosk-error" role="alert">
            {error}
          </div>
        ) : (
          <div className="kiosk-status" role="status">
            {busy ? 'Đang lấy thông tin…' : 'Đang chờ quét thẻ…'}
          </div>
        )}
        <p className="kiosk-hint">Máy chỉ đọc chữ trong mã QR để điền phiếu, không chụp hay lưu ảnh thẻ.</p>
        <input
          ref={input}
          className="kiosk-hidden-input"
          aria-label="Mã QR trên CCCD (máy quét tự nhập)"
          inputMode="none"
          autoComplete="off"
          spellCheck={false}
          value={raw}
          disabled={busy}
          onChange={(e) => setRaw(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              submit()
            }
          }}
          onBlur={refocus}
        />
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onBack}>
          Quay lại
        </button>
      </KioskFooter>
    </>
  )
}

/** Nhập mã y tế hoặc số CCCD (cả hai chỉ gồm chữ số, bàn phím số), kèm ngày sinh để đối chiếu (bắt buộc khớp hồ sơ). */
export function LookupScreen({
  onIdentified,
  onBack,
}: {
  onIdentified: (request: KioskIdentityRequest, identity: KioskIdentity) => void
  onBack: () => void
}) {
  const [kind, setKind] = useState<'patientCode' | 'citizenId'>('citizenId')
  const [code, setCode] = useState('')
  const [dob, setDob] = useState('')
  const [active, setActive] = useState<'code' | 'dob'>('code')
  const { busy, error, setError, identify } = useIdentify(onIdentified)
  const maxCodeLength = kind === 'citizenId' ? 12 : PATIENT_CODE_MAX_LENGTH

  const switchKind = (next: 'patientCode' | 'citizenId') => {
    setKind(next)
    setCode('')
    setActive('code')
    setError(null)
  }

  const submit = () => {
    const value = code.trim()
    if (!value) {
      setError(kind === 'citizenId' ? 'Vui lòng nhập số CCCD.' : 'Vui lòng nhập mã y tế.')
      setActive('code')
      return
    }
    if (kind === 'citizenId' && !isValidCitizenId(value)) {
      setError('Số CCCD phải gồm 12 chữ số.')
      setActive('code')
      return
    }
    const dobError = birthDateError(dob)
    if (dobError) {
      setError(dobError)
      setActive('dob')
      return
    }
    void identify({
      method: 'LookupCode',
      dateOfBirth: dateDigitsToIso(dob)!,
      patientCode: kind === 'patientCode' ? value : null,
      citizenId: kind === 'citizenId' ? value : null,
    })
  }

  const codeLabel = kind === 'citizenId' ? 'Số CCCD (12 chữ số)' : 'Mã y tế'
  return (
    <>
      <main className="kiosk-main">
        <h1>Nhập mã y tế hoặc số CCCD</h1>
        <div className="kiosk-segmented" role="radiogroup" aria-label="Loại mã">
          <button type="button" role="radio" aria-checked={kind === 'patientCode'} onClick={() => switchKind('patientCode')}>
            {kind === 'patientCode' ? '✓ ' : ''}Mã y tế
          </button>
          <button type="button" role="radio" aria-checked={kind === 'citizenId'} onClick={() => switchKind('citizenId')}>
            {kind === 'citizenId' ? '✓ ' : ''}Số CCCD
          </button>
        </div>
        <div className="kiosk-field">
          <label htmlFor="kiosk-code">{codeLabel}</label>
          <input
            id="kiosk-code"
            className={`kiosk-input large${active === 'code' ? ' active' : ''}`}
            inputMode="none"
            autoComplete="off"
            value={kind === 'citizenId' ? groupDigits(code) : code}
            onFocus={() => setActive('code')}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, maxCodeLength))}
          />
        </div>
        <div className="kiosk-field">
          <label htmlFor="kiosk-dob">Ngày sinh (ngày/tháng/năm)</label>
          <input
            id="kiosk-dob"
            className={`kiosk-input large${active === 'dob' ? ' active' : ''}`}
            inputMode="none"
            autoComplete="off"
            value={formatDateDigits(dob)}
            onFocus={() => setActive('dob')}
            onChange={(e) => setDob(e.target.value.replace(/\D/g, '').slice(0, DATE_DIGITS))}
          />
          <span className="kiosk-hint">Để bảo vệ thông tin của ông/bà, máy chỉ hiện thông tin khi ngày sinh khớp với hồ sơ.</span>
        </div>
        {error && (
          <div className="kiosk-error" role="alert">
            {error}
          </div>
        )}
        <div className="kiosk-bottom">
          {active === 'code' ? (
            <NumberPad
              onDigit={(digit) => setCode((value) => (value + digit).slice(0, maxCodeLength))}
              onBackspace={() => setCode((value) => value.slice(0, -1))}
              extraKey={{ label: 'Ô tiếp', onPress: () => setActive('dob') }}
            />
          ) : (
            <NumberPad
              onDigit={(digit) => setDob((value) => (value + digit).slice(0, DATE_DIGITS))}
              onBackspace={() => setDob((value) => value.slice(0, -1))}
              extraKey={{ label: 'Xóa hết', onPress: () => setDob('') }}
            />
          )}
        </div>
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onBack}>
          Quay lại
        </button>
        <button type="button" className="kiosk-btn primary" disabled={busy} onClick={submit}>
          {busy ? 'Đang lấy thông tin…' : 'Lấy thông tin'}
        </button>
      </KioskFooter>
    </>
  )
}

type ManualField = 'fullName' | 'dob' | 'phone' | 'citizenId' | 'address'

/** Người bệnh tự khai thông tin (không có thẻ hay mã y tế); nhân viên đối chiếu giấy tờ khi xác nhận. */
export function ManualScreen({
  initial,
  onIdentified,
  onBack,
}: {
  /** Thông tin đã khai (khi quay lại từ câu hỏi đầu tiên), để không phải nhập lại. */
  initial?: KioskIdentityRequest | null
  onIdentified: (request: KioskIdentityRequest, identity: KioskIdentity) => void
  onBack: () => void
}) {
  const self = initial?.method === 'SelfReported' ? initial.selfReport : null
  const [fullName, setFullName] = useState(self?.fullName ?? '')
  const [dob, setDob] = useState(self && initial ? initial.dateOfBirth.split('-').reverse().join('') : '')
  const [gender, setGender] = useState<Gender | null>(self?.gender ?? null)
  const [phone, setPhone] = useState(self?.phoneNumber ?? '')
  const [citizenId, setCitizenId] = useState(self ? (initial?.citizenId ?? '') : '')
  const [address, setAddress] = useState(self?.currentAddress ?? '')
  const [active, setActive] = useState<ManualField>('fullName')
  const { busy, error, setError, identify } = useIdentify(onIdentified)
  const order: ManualField[] = ['fullName', 'dob', 'phone', 'citizenId', 'address']
  const next = () => setActive(order[Math.min(order.indexOf(active) + 1, order.length - 1)])

  const submit = () => {
    const fail = (message: string, field?: ManualField) => {
      setError(message)
      if (field) setActive(field)
    }
    if (!fullName.trim()) return fail('Vui lòng nhập họ và tên.', 'fullName')
    const dobError = birthDateError(dob)
    if (dobError) return fail(dobError, 'dob')
    if (!gender) return fail('Vui lòng chọn giới tính.')
    if (!PHONE_PATTERN.test(phone)) return fail('Số điện thoại chưa đúng (ví dụ 0912345678).', 'phone')
    const iso = dateDigitsToIso(dob)!
    if (citizenId) {
      const mismatch = citizenIdMismatch(citizenId, Number(iso.slice(0, 4)), gender)
      if (mismatch) return fail(mismatch, 'citizenId')
    }
    if (!address.trim()) return fail('Vui lòng nhập chỗ ở hiện nay.', 'address')
    void identify({
      method: 'SelfReported',
      dateOfBirth: iso,
      citizenId: citizenId || null,
      selfReport: { fullName: fullName.trim(), gender, phoneNumber: phone, currentAddress: address.trim() },
    })
  }

  const field = (id: ManualField, label: string, value: string, display = value, required = true, wide = false) => (
    <div className={`kiosk-field${wide ? ' wide' : ''}`}>
      <label htmlFor={`kiosk-${id}`}>
        {label} {required ? <span className="kiosk-required">*</span> : <span className="kiosk-hint">(nếu có)</span>}
      </label>
      <input
        id={`kiosk-${id}`}
        className={`kiosk-input${active === id ? ' active' : ''}`}
        inputMode="none"
        autoComplete="off"
        value={display}
        onFocus={() => setActive(id)}
        onChange={(e) => {
          const text = e.target.value
          if (id === 'fullName') setFullName(text)
          else if (id === 'address') setAddress(text)
          else if (id === 'dob') setDob(text.replace(/\D/g, '').slice(0, DATE_DIGITS))
          else if (id === 'phone') setPhone(text.replace(/[^\d+]/g, '').slice(0, 12))
          else setCitizenId(text.replace(/\D/g, '').slice(0, 12))
        }}
      />
    </div>
  )

  const digits = (setter: (update: (value: string) => string) => void, max: number) => (
    <NumberPad
      onDigit={(digit) => setter((value) => (value + digit).slice(0, max))}
      onBackspace={() => setter((value) => value.slice(0, -1))}
      extraKey={{ label: 'Ô tiếp', onPress: next }}
    />
  )

  return (
    <>
      <main className="kiosk-main">
        <div>
          <h1>Nhập thông tin của ông/bà</h1>
          <p className="kiosk-hint">
            Ô có dấu <span className="kiosk-required">*</span> là bắt buộc. Chạm vào ô để nhập.
          </p>
        </div>
        <div className="kiosk-grid-2">
          {field('fullName', 'Họ và tên', fullName, fullName, true, true)}
          {field('dob', 'Ngày sinh', dob, formatDateDigits(dob))}
          <div className="kiosk-field">
            <span className="label" id="kiosk-gender">
              Giới tính <span className="kiosk-required">*</span>
            </span>
            <div className="kiosk-choices" role="radiogroup" aria-labelledby="kiosk-gender">
              {(['Male', 'Female'] as const).map((value) => (
                <button
                  key={value}
                  type="button"
                  role="radio"
                  aria-checked={gender === value}
                  className="kiosk-choice short"
                  onClick={() => setGender(value)}
                >
                  {gender === value ? '✓ ' : ''}
                  {genderLabels[value]}
                </button>
              ))}
            </div>
          </div>
          {field('phone', 'Số điện thoại', phone)}
          {field('citizenId', 'Số CCCD', citizenId, groupDigits(citizenId), false)}
          {field('address', 'Chỗ ở hiện nay', address, address, true, true)}
        </div>
        <p className="kiosk-hint">Nhân viên tiếp đón sẽ đối chiếu lại thông tin khi ông/bà đến quầy.</p>
        {error && (
          <div className="kiosk-error" role="alert">
            {error}
          </div>
        )}
        <div className="kiosk-bottom">
          {active === 'fullName' || active === 'address' ? (
            <TextKeyboard
              value={active === 'fullName' ? fullName : address}
              onChange={active === 'fullName' ? setFullName : setAddress}
              capitalizeWords
              maxLength={active === 'fullName' ? 200 : 500}
              doneLabel={active === 'address' ? 'Xong' : 'Ô tiếp ›'}
              onDone={active === 'address' ? submit : next}
            />
          ) : active === 'dob' ? (
            digits(setDob, DATE_DIGITS)
          ) : active === 'phone' ? (
            digits(setPhone, 11)
          ) : (
            digits(setCitizenId, 12)
          )}
        </div>
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onBack}>
          Quay lại
        </button>
        <button type="button" className="kiosk-btn primary" disabled={busy} onClick={submit}>
          {busy ? 'Đang kiểm tra…' : 'Tiếp tục'}
        </button>
      </KioskFooter>
    </>
  )
}

/** "Đây có phải thông tin của ông/bà?" — thêm số điện thoại nếu hồ sơ chưa có. */
export function ConfirmScreen({
  identity,
  phone,
  onPhoneChange,
  onConfirm,
  onReject,
}: {
  identity: KioskIdentity
  phone: string
  onPhoneChange: (phone: string) => void
  onConfirm: () => void
  onReject: () => void
}) {
  const [error, setError] = useState<string | null>(null)
  const needsPhone = identity.missingFields.includes('phoneNumber')
  const confirm = () => {
    if (needsPhone && !PHONE_PATTERN.test(phone)) {
      setError('Vui lòng nhập số điện thoại (ví dụ 0912345678).')
      return
    }
    onConfirm()
  }
  return (
    <>
      <main className="kiosk-main">
        <span className="kiosk-badge">
          {identity.foundRecord ? '✓ Đã tìm thấy hồ sơ tại bệnh viện' : 'Thông tin đọc từ thẻ CCCD'}
        </span>
        <h1>Đây có phải thông tin của ông/bà?</h1>
        <section className="kiosk-card kiosk-facts" aria-label="Thông tin người bệnh">
          <div className="wide">
            <span>Họ và tên</span>
            <strong className="name">{identity.fullName.toLocaleUpperCase('vi')}</strong>
          </div>
          <div>
            <span>Ngày sinh</span>
            <strong>{displayDate(identity.dateOfBirth)}</strong>
          </div>
          <div>
            <span>Giới tính</span>
            <strong>{genderLabels[identity.gender]}</strong>
          </div>
          {identity.citizenIdMasked && (
            <div>
              <span>Số CCCD</span>
              <strong>{identity.citizenIdMasked}</strong>
            </div>
          )}
          {identity.patientCode && (
            <div>
              <span>Mã y tế</span>
              <strong>{identity.patientCode}</strong>
            </div>
          )}
          <div className="wide">
            <span>Nơi thường trú</span>
            <strong>{identity.permanentAddress ?? (identity.hasAddressOnFile ? 'Đã có trong hồ sơ' : 'Chưa có')}</strong>
          </div>
        </section>
        {needsPhone && (
          <div className="kiosk-field">
            <label htmlFor="kiosk-phone">
              Số điện thoại để bệnh viện liên hệ <span className="kiosk-required">*</span>
            </label>
            <span className="kiosk-hint">Hồ sơ chưa có số điện thoại, ông/bà nhập giúp.</span>
            <input
              id="kiosk-phone"
              className="kiosk-input large active"
              inputMode="none"
              autoComplete="off"
              placeholder="Ví dụ 0912 345 678"
              value={phone}
              onChange={(e) => onPhoneChange(e.target.value.replace(/[^\d+]/g, '').slice(0, 12))}
            />
          </div>
        )}
        {error && (
          <div className="kiosk-error" role="alert">
            {error}
          </div>
        )}
        {needsPhone && (
          <div className="kiosk-bottom">
            <NumberPad
              onDigit={(digit) => onPhoneChange((phone + digit).slice(0, 11))}
              onBackspace={() => onPhoneChange(phone.slice(0, -1))}
              extraKey={{ label: 'Xóa hết', onPress: () => onPhoneChange('') }}
            />
          </div>
        )}
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onReject}>
          Không phải tôi
        </button>
        <button type="button" className="kiosk-btn primary" onClick={confirm}>
          Đúng, tiếp tục
        </button>
      </KioskFooter>
    </>
  )
}
