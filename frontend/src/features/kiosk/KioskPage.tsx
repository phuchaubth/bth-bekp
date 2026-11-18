import './kiosk.css'
import { Modal } from 'antd'
import { useCallback, useMemo, useState } from 'react'
import type { ConsentDecision } from '../../api/consentForms'
import { useKioskConfig, type KioskIdentity, type KioskIdentityRequest, type KioskSubmitResult } from '../../api/kiosk'
import { useAuth } from '../../auth/authContext'
import { DoneScreen, PreviousAnswersScreen, QuestionScreen, SignScreen } from './AnswerScreens'
import { ConfirmScreen, LookupScreen, ManualScreen, ScanScreen, StartScreen, type IdentifyMethod } from './IdentifyScreens'
import { KioskHeader, KioskSteps } from './KioskLayout'
import { kioskQuestions, questionKey, type KioskAnswers, type KioskDecisions } from './kioskQuestions'
import { kioskErrorMessage, useIdleReset } from './kioskSession'

type Step = 'start' | IdentifyMethod | 'confirm' | 'previous' | 'questions' | 'sign' | 'done'

/** Quyết định 24: 60 giây không chạm thì tự xóa thông tin, về trang đầu. */
const IDLE_MS = 60_000

/** Một lượt người bệnh dùng kiosk; về trang đầu là xóa hết. */
interface Visit {
  request: KioskIdentityRequest | null
  identity: KioskIdentity | null
  phone: string
  answers: KioskAnswers
  decisions: KioskDecisions
  questionIndex: number
  /** Quyết định 27: người bệnh chọn giữ câu trả lời của phiếu lần trước (null: chưa hỏi hoặc không có phiếu lần trước). */
  keepPrevious: boolean | null
  result: KioskSubmitResult | null
}

const EMPTY_VISIT: Visit = {
  request: null,
  identity: null,
  phone: '',
  answers: {},
  decisions: {},
  questionIndex: 0,
  keepPrevious: null,
  result: null,
}

const hasPrevious = (identity: KioskIdentity | null) => (identity?.inherited?.length ?? 0) > 0

const STEP_NUMBER: Record<Step, number> = {
  start: -1,
  scan: 0,
  lookup: 0,
  manual: 0,
  confirm: 0,
  previous: 1,
  questions: 1,
  sign: 2,
  done: 3,
}

const today = () => new Intl.DateTimeFormat('vi-VN', { weekday: 'long', day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date())

/**
 * Kiosk cho người bệnh tự làm phiếu (quyết định 24), màn hình 32 inch dọc: nhận diện (quét CCCD, nhập mã y tế/CCCD kèm
 * ngày sinh, hoặc tự khai) → trả lời từng câu → ký một lần cho cả bộ → mời đến quầy. Máy dùng tài khoản vai trò Kiosk;
 * nhân viên giữ logo 3 giây để đăng xuất máy.
 */
export function KioskPage() {
  const config = useKioskConfig()
  const { logout } = useAuth()
  const [step, setStep] = useState<Step>('start')
  const [visit, setVisit] = useState<Visit>(EMPTY_VISIT)
  const [staffOpen, setStaffOpen] = useState(false)

  const restart = useCallback(() => {
    setVisit(EMPTY_VISIT)
    setStep('start')
  }, [])
  const secondsLeft = useIdleReset(step !== 'start' && step !== 'done', IDLE_MS, restart)

  const templates = useMemo(() => config.data?.templates ?? [], [config.data])
  const prefilled = useMemo(() => {
    const sources = new Set(visit.identity?.prefilledSources ?? [])
    if (visit.phone) {
      sources.add('patient.phoneNumber')
    }
    return sources
  }, [visit.identity, visit.phone])
  // Câu đã trả lời ở phiếu lần trước, theo mẫu: chỉ bỏ qua khi người bệnh chọn giữ như lần trước.
  const kept = useMemo(
    () =>
      new Map(
        visit.keepPrevious ? (visit.identity?.inherited ?? []).map((t) => [t.templateId, new Set(t.keys)] as const) : [],
      ),
    [visit.identity, visit.keepPrevious],
  )
  const questions = useMemo(
    () => kioskQuestions(templates, prefilled, visit.answers, kept),
    [templates, prefilled, visit.answers, kept],
  )
  const questionIndex = Math.min(visit.questionIndex, Math.max(questions.length - 1, 0))
  // Không còn câu nào phải hỏi (mọi thông tin đã có trong hồ sơ): sang thẳng bước ký.
  const shown: Step = step === 'questions' && questions.length === 0 ? 'sign' : step

  const identified = (request: KioskIdentityRequest, identity: KioskIdentity) => {
    setVisit({ ...EMPTY_VISIT, request, identity })
    setStep(request.method !== 'SelfReported' ? 'confirm' : hasPrevious(identity) ? 'previous' : 'questions')
  }

  /** Bước trước câu hỏi đầu tiên: hỏi giữ như lần trước (nếu có phiếu lần trước), rồi xác nhận thông tin hay tự khai. */
  const beforeQuestions = (): Step =>
    hasPrevious(visit.identity) ? 'previous' : visit.request?.method === 'SelfReported' ? 'manual' : 'confirm'

  const backFromQuestions = () => {
    if (questionIndex > 0) {
      setVisit((v) => ({ ...v, questionIndex: questionIndex - 1 }))
    } else {
      setStep(beforeQuestions())
    }
  }

  const backFromSign = () => {
    if (questions.length > 0) {
      setVisit((v) => ({ ...v, questionIndex: questions.length - 1 }))
      setStep('questions')
    } else {
      setStep(beforeQuestions())
    }
  }

  const startQuestions = (keepPrevious: boolean | null) => {
    setVisit((v) => ({ ...v, keepPrevious, questionIndex: 0 }))
    setStep('questions')
  }

  let content
  if (config.isPending) {
    content = (
      <main className="kiosk-main kiosk-message">
        <h1>Đang chuẩn bị máy…</h1>
      </main>
    )
  } else if (config.isError || !config.data) {
    content = (
      <main className="kiosk-main kiosk-message">
        <h1>Kiosk tạm chưa dùng được</h1>
        <p className="kiosk-error">{kioskErrorMessage(config.error)}</p>
        <button type="button" className="kiosk-btn" onClick={() => void config.refetch()}>
          Thử lại
        </button>
      </main>
    )
  } else if (shown === 'start') {
    content = <StartScreen config={config.data} onChoose={(method) => setStep(method)} />
  } else if (shown === 'scan') {
    content = <ScanScreen onIdentified={identified} onBack={restart} />
  } else if (shown === 'lookup') {
    content = <LookupScreen onIdentified={identified} onBack={restart} />
  } else if (shown === 'manual') {
    content = <ManualScreen initial={visit.request} onIdentified={identified} onBack={restart} />
  } else if (shown === 'confirm' && visit.identity) {
    content = (
      <ConfirmScreen
        identity={visit.identity}
        phone={visit.phone}
        onPhoneChange={(phone) => setVisit((v) => ({ ...v, phone }))}
        onConfirm={() => (hasPrevious(visit.identity) ? setStep('previous') : startQuestions(null))}
        onReject={restart}
      />
    )
  } else if (shown === 'previous') {
    content = (
      <PreviousAnswersScreen
        onKeep={() => startQuestions(true)}
        onChange={() => startQuestions(false)}
        onBack={() => setStep(visit.request?.method === 'SelfReported' ? 'manual' : 'confirm')}
      />
    )
  } else if (shown === 'questions') {
    content = (
      <QuestionScreen
        key={questionKey(questions[questionIndex])}
        templates={templates}
        questions={questions}
        index={questionIndex}
        answers={visit.answers}
        decisions={visit.decisions}
        prefilled={prefilled}
        onAnswer={(templateId, key, value) =>
          setVisit((v) => ({ ...v, answers: { ...v.answers, [templateId]: { ...v.answers[templateId], [key]: value } } }))
        }
        onDecision={(templateId, decision: ConsentDecision) =>
          setVisit((v) => ({ ...v, decisions: { ...v.decisions, [templateId]: decision } }))
        }
        onNext={() => {
          if (questionIndex + 1 < questions.length) {
            setVisit((v) => ({ ...v, questionIndex: questionIndex + 1 }))
          } else {
            setStep('sign')
          }
        }}
        onBack={backFromQuestions}
      />
    )
  } else if (shown === 'sign' && visit.request && visit.identity) {
    content = (
      <SignScreen
        config={config.data}
        signerName={visit.identity.fullName}
        request={visit.request}
        phone={visit.phone}
        answers={visit.answers}
        decisions={visit.decisions}
        prefilled={prefilled}
        kept={visit.keepPrevious ? kept : undefined}
        onBack={backFromSign}
        onDone={(result) => {
          setVisit((v) => ({ ...v, result }))
          setStep('done')
        }}
      />
    )
  } else if (shown === 'done' && visit.result) {
    content = <DoneScreen result={visit.result} onRestart={restart} />
  } else {
    content = <StartScreen config={config.data} onChoose={(method) => setStep(method)} />
  }

  return (
    <div className="kiosk" lang="vi">
      <KioskHeader
        departmentName={config.data?.departmentName}
        onStaffHold={() => setStaffOpen(true)}
        right={
          shown === 'start' || shown === 'done' ? (
            <span className="kiosk-header-note">{today()}</span>
          ) : (
            <button type="button" className="kiosk-btn small" style={{ marginLeft: 'auto' }} onClick={restart}>
              Làm lại từ đầu
            </button>
          )
        }
      />
      {STEP_NUMBER[shown] >= 0 && <KioskSteps current={STEP_NUMBER[shown]} />}
      {content}
      {secondsLeft !== null && (
        <div className="kiosk-idle" role="status">
          Màn hình sẽ tự về trang đầu sau {secondsLeft} giây. Chạm vào màn hình để tiếp tục.
        </div>
      )}
      <Modal
        open={staffOpen}
        title="Đăng xuất máy kiosk?"
        okText="Đăng xuất"
        cancelText="Hủy"
        okButtonProps={{ danger: true }}
        onOk={() => {
          setStaffOpen(false)
          void logout()
        }}
        onCancel={() => setStaffOpen(false)}
      >
        Dành cho nhân viên. Sau khi đăng xuất, cần đăng nhập lại tài khoản kiosk thì người bệnh mới dùng tiếp được.
      </Modal>
    </div>
  )
}
