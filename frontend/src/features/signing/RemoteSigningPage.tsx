import { Alert, Button, Card, Flex, Input, Result, Spin, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { ApiError } from '../../api/http'
import { remoteSigningApi, type RemoteSigningSession } from '../../api/remoteSigning'
import { BrandMark } from '../../components/layout/BrandMark'
import { errorMessage } from '../../utils/formErrors'
import { birthDateError, dateDigitsToIso, DATE_DIGITS } from '../kiosk/kioskDates'
import { SigningKioskPage } from './SigningKioskPage'
import type { SigningSession } from './signingSession'

const STORAGE_KEY = 'consent.remoteSigning'

/** Mã và khóa thiết bị giữ trong bộ nhớ tạm của thẻ trình duyệt: tải lại trang thì ký tiếp, đóng thẻ là mất. */
interface Stored {
  code: string
  deviceKey?: string
}

function readStored(): Stored | null {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Stored) : null
  } catch {
    return null
  }
}

function writeStored(value: Stored | null) {
  try {
    if (value) {
      window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value))
    } else {
      window.sessionStorage.removeItem(STORAGE_KEY)
    }
  } catch {
    // Trình duyệt chặn bộ nhớ tạm: vẫn ký được, chỉ là tải lại trang thì phải nhờ nhân viên tạo mã mới.
  }
}

/**
 * Lấy mã sau dấu "#" rồi xóa khỏi thanh địa chỉ (không nằm lại trong lịch sử trình duyệt, không lộ khi chụp màn hình); mã
 * được giữ trong bộ nhớ tạm của thẻ để tải lại trang vẫn dùng được.
 */
function takeCode(): string | null {
  const fromHash = window.location.hash.slice(1)
  if (!fromHash) {
    return readStored()?.code ?? null
  }
  const stored = readStored()
  writeStored(stored?.code === fromHash ? stored : { code: fromHash })
  window.history.replaceState(null, '', window.location.pathname)
  return fromHash
}

/** "12031985" → "12/03/1985" trong lúc gõ. */
const formatDigits = (digits: string) => [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4)].filter(Boolean).join('/')

function toSession(result: RemoteSigningSession): SigningSession {
  return {
    token: result.token,
    expiresAtUtc: result.expiresAtUtc,
    form: result.forms[0],
    bundle: result.bundleId ? { bundleId: result.bundleId, forms: result.forms } : undefined,
    staffUserName: '',
    remote: true,
    // Ký xong: xóa mã và khóa thiết bị khỏi máy của người bệnh.
    onSigned: () => writeStored(null),
  }
}

/**
 * Trang ký trên điện thoại riêng của người bệnh (quyết định 32), mở từ mã QR trên màn hình nhân viên, không cần đăng nhập.
 * Người bệnh (hoặc người đại diện) nhập ngày sinh của người bệnh để mở phiếu, rồi đọc và ký như trên máy của bệnh viện.
 */
export function RemoteSigningPage() {
  const [code] = useState(takeCode)
  const [session, setSession] = useState<SigningSession | null>(null)
  const [digits, setDigits] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [problem, setProblem] = useState<string | null>(
    code ? null : 'Liên kết ký không đúng. Ông/bà quét lại mã QR trên màn hình của nhân viên y tế.',
  )
  // Điện thoại đã mở mã này trước đó (tải lại trang): dùng khóa thiết bị, không phải nhập lại ngày sinh.
  const [resumeKey] = useState(() => {
    const stored = readStored()
    return code && stored?.code === code ? (stored.deviceKey ?? null) : null
  })
  const [busy, setBusy] = useState(!!resumeKey)

  useEffect(() => {
    if (!code || !resumeKey) {
      return
    }
    let active = true
    remoteSigningApi
      .claim({ code, deviceKey: resumeKey })
      .then((result) => {
        if (active) setSession(toSession(result))
      })
      .catch((e: unknown) => {
        if (active) {
          writeStored(null)
          setProblem(errorMessage(e))
        }
      })
      .finally(() => {
        if (active) setBusy(false)
      })
    return () => {
      active = false
    }
  }, [code, resumeKey])

  const open = async (input: { dateOfBirth: string }) => {
    if (!code) {
      return
    }
    setBusy(true)
    setFieldError(null)
    try {
      const result = await remoteSigningApi.claim({ code, ...input })
      if (result.deviceKey) {
        writeStored({ code, deviceKey: result.deviceKey })
      }
      setSession(toSession(result))
    } catch (e) {
      if (e instanceof ApiError && e.code === 'remote_signing.wrong_birth_date') {
        const remaining = e.problem?.remainingAttempts
        setFieldError(typeof remaining === 'number' ? `${e.message} Còn ${remaining} lần thử.` : e.message)
      } else if (e instanceof ApiError && e.status === 400) {
        setFieldError(errorMessage(e))
      } else {
        writeStored(null)
        setProblem(errorMessage(e))
      }
    } finally {
      setBusy(false)
    }
  }

  if (session) {
    return <SigningKioskPage session={session} />
  }

  const submit = () => {
    const error = birthDateError(digits)
    if (error) {
      setFieldError(error)
      return
    }
    void open({ dateOfBirth: dateDigitsToIso(digits)! })
  }

  return (
    <div style={{ minHeight: '100vh', background: '#f1f5f9', padding: '24px 12px', overscrollBehaviorY: 'contain' }}>
      <Flex vertical gap={16} style={{ maxWidth: 480, margin: '0 auto' }}>
        <Flex align="center" gap={12}>
          <BrandMark size={40} />
          <Typography.Title level={4} style={{ margin: 0 }}>
            Ký phiếu trên điện thoại
          </Typography.Title>
        </Flex>
        {problem ? (
          <Card>
            <Result status="warning" title="Không mở được phiếu" subTitle={problem} />
          </Card>
        ) : busy && !digits ? (
          <Card>
            <Flex justify="center" style={{ padding: 32 }}>
              <Spin />
            </Flex>
          </Card>
        ) : (
          <Card>
            <form
              onSubmit={(event) => {
                event.preventDefault()
                submit()
              }}
            >
              <Flex vertical gap={16}>
                <Typography.Paragraph style={{ margin: 0, fontSize: 16 }}>
                  Để bảo vệ thông tin, vui lòng nhập <strong>ngày sinh của người bệnh</strong> (người đại diện ký thay cũng nhập
                  ngày sinh của người bệnh).
                </Typography.Paragraph>
                <label htmlFor="remote-birth-date" style={{ fontWeight: 600 }}>
                  Ngày sinh (ngày/tháng/năm)
                </label>
                <Input
                  id="remote-birth-date"
                  size="large"
                  inputMode="numeric"
                  autoComplete="off"
                  placeholder="__/__/____"
                  value={formatDigits(digits)}
                  status={fieldError ? 'error' : undefined}
                  onChange={(event) => {
                    setDigits(event.target.value.replace(/\D/g, '').slice(0, DATE_DIGITS))
                    setFieldError(null)
                  }}
                  style={{ fontSize: 22, letterSpacing: 1 }}
                />
                {fieldError && <Alert type="error" showIcon title={fieldError} />}
                <Button type="primary" htmlType="submit" size="large" block loading={busy}>
                  Mở phiếu
                </Button>
                <Typography.Text type="secondary">
                  Nhập sai 5 lần thì mã bị hủy; khi đó ông/bà nhờ nhân viên y tế tạo mã mới.
                </Typography.Text>
              </Flex>
            </form>
          </Card>
        )}
      </Flex>
    </div>
  )
}
