import { ConfigProvider, Form, Modal } from 'antd'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { ConsentDecision } from '../../api/consentForms'
import type { TemplateBlock } from '../../api/consentTemplates'
import {
  kioskApi,
  type KioskConfig,
  type KioskIdentityRequest,
  type KioskSubmitResult,
  type KioskTemplate,
} from '../../api/kiosk'
import { Bilingual } from '../consents/Bilingual'
import { DynamicForm } from '../consents/DynamicForm'
import { SignaturePad, type SignaturePadHandle } from '../signing/SignaturePad'
import { dateDigitsToIso, DATE_DIGITS, formatDateDigits } from './kioskDates'
import { NumberPad, TextKeyboard } from './KioskKeyboards'
import { KioskFooter } from './KioskLayout'
import { kioskErrorMessage } from './kioskSession'
import {
  asksDecision,
  keyboardFor,
  questionKey,
  validateAnswer,
  type KioskAnswers,
  type KioskDecisions,
  type KioskQuestion,
} from './kioskQuestions'

const CUSTOM_STATEMENT = '__custom__'

/** Nội dung phiếu (chỉ xem) với câu trả lời hiện có; trường lấy từ hồ sơ ghi "Đã có trong hồ sơ". */
export function FormPreview({
  template,
  values,
  prefilled,
  onClose,
}: {
  template: KioskTemplate
  values: Record<string, unknown>
  prefilled: ReadonlySet<string>
  onClose: () => void
}) {
  const [form] = Form.useForm()
  const fromRecord = Object.fromEntries(
    template.definition.sections
      .flatMap((section) => section.blocks)
      .filter((block) => block.key && block.prefill && prefilled.has(block.prefill))
      .map((block) => [block.key!, '(Đã có trong hồ sơ)']),
  )
  return (
    <Modal
      open
      title={template.name}
      width="92vw"
      onCancel={onClose}
      footer={
        <button type="button" className="kiosk-btn primary block" onClick={onClose}>
          Đóng
        </button>
      }
    >
      <ConfigProvider theme={{ token: { fontSize: 20 } }}>
        <DynamicForm definition={template.definition} form={form} initialValues={{ ...fromRecord, ...values } as never} disabled />
      </ConfigProvider>
    </Modal>
  )
}

/** Các phiếu và tình trạng trả lời (đã trả lời / đang làm / chưa làm). */
function FormsPanel({ templates, currentIndex }: { templates: KioskTemplate[]; currentIndex: number }) {
  return (
    <section className="kiosk-card" aria-labelledby="kiosk-forms-title" style={{ padding: 'calc(var(--k) * 24) calc(var(--k) * 28)' }}>
      <h2 id="kiosk-forms-title" style={{ marginBottom: 'calc(var(--k) * 14)', fontSize: 'calc(var(--k) * 26)' }}>
        Phiếu của ông/bà hôm nay ({templates.length})
      </h2>
      <ol className="kiosk-forms">
        {templates.map((template, index) => (
          <li
            key={template.id}
            className={index < currentIndex ? 'answered' : index === currentIndex ? 'current' : undefined}
            aria-current={index === currentIndex ? 'true' : undefined}
          >
            <span className="mark" aria-hidden="true">
              {index < currentIndex ? '✓' : ''}
            </span>
            <strong>{template.name}</strong>
            <span className="state">{index < currentIndex ? 'Đã trả lời' : index === currentIndex ? 'Đang làm' : 'Chưa làm'}</span>
          </li>
        ))}
      </ol>
      <p className="kiosk-hint" style={{ marginTop: 'calc(var(--k) * 12)', color: '#8a5400', fontSize: 'calc(var(--k) * 21)' }}>
        Các phiếu chuyên môn khác (nếu có) sẽ do điều dưỡng làm cùng ông/bà tại phòng khám.
      </p>
    </section>
  )
}

const capitalizes = (block: TemplateBlock) => /name|address|ten|diachi/i.test(block.key ?? '')

/** Một câu hỏi mỗi màn hình: chọn bằng nút lớn, chữ nhập bằng bàn phím màn hình. */
export function QuestionScreen({
  templates,
  questions,
  index,
  answers,
  decisions,
  prefilled,
  onAnswer,
  onDecision,
  onNext,
  onBack,
}: {
  templates: KioskTemplate[]
  questions: KioskQuestion[]
  index: number
  answers: KioskAnswers
  decisions: KioskDecisions
  prefilled: ReadonlySet<string>
  onAnswer: (templateId: string, key: string, value: unknown) => void
  onDecision: (templateId: string, decision: ConsentDecision) => void
  onNext: () => void
  onBack: () => void
}) {
  const question = questions[index]
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState(false)
  const key = questionKey(question)
  // Màn hình được dựng lại cho mỗi câu (key theo câu hỏi), nên nháp ngày lấy từ câu trả lời đã có ngay khi mở câu.
  const [dateDraft, setDateDraft] = useState(() => {
    const saved = question.kind === 'field' ? answers[question.template.id]?.[question.block.key!] : null
    return typeof saved === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(saved) ? saved.split('-').reverse().join('') : ''
  })

  const templateQuestions = questions.filter((q) => q.template.id === question.template.id)
  const position = templateQuestions.findIndex((q) => questionKey(q) === key) + 1
  const values = answers[question.template.id] ?? {}
  const value = question.kind === 'field' ? values[question.block.key!] : decisions[question.template.id]

  const next = () => {
    const problem =
      question.kind === 'field'
        ? validateAnswer(question.block, value)
        : value
          ? null
          : 'Vui lòng chọn một câu trả lời.'
    if (problem) {
      setError(problem)
      return
    }
    onNext()
  }

  const set = (next: unknown) => {
    setError(null)
    if (question.kind === 'field') {
      onAnswer(question.template.id, question.block.key!, next)
    }
  }

  const signatures = question.template.definition.signatures
  const label =
    question.kind === 'field'
      ? (question.block.label ?? question.block.key!)
      : (signatures?.decisionLabel ?? 'Ông/bà có đồng ý với nội dung phiếu này không?')

  let body: ReactNode = null
  let keyboard: ReactNode = null
  if (question.kind === 'decision') {
    body = (
      <>
        <button type="button" className="kiosk-btn small" style={{ alignSelf: 'flex-start' }} onClick={() => setPreview(true)}>
          Xem nội dung phiếu
        </button>
        <div className="kiosk-choices single kiosk-bottom" role="radiogroup" aria-label={label}>
          {(['Agreed', 'Refused'] as const).map((decision) => (
            <button
              key={decision}
              type="button"
              role="radio"
              aria-checked={value === decision}
              className={`kiosk-choice${decision === 'Refused' ? ' refuse' : ''}`}
              onClick={() => {
                setError(null)
                onDecision(question.template.id, decision)
              }}
            >
              {value === decision ? '✓ ' : ''}
              <Bilingual text={decision === 'Agreed' ? (signatures?.agreeLabel ?? 'Đồng ý') : (signatures?.refuseLabel ?? 'Không đồng ý')} />
            </button>
          ))}
        </div>
      </>
    )
  } else {
    const { block } = question
    if (block.type === 'choice' || block.type === 'multiChoice') {
      const multiple = block.type === 'multiChoice'
      const selected = multiple ? (Array.isArray(value) ? (value as string[]) : []) : value
      const long = (block.options ?? []).some((option) => option.label.length > 28)
      body = (
        <div
          className={`kiosk-choices kiosk-bottom${long ? ' single' : ''}`}
          role={multiple ? 'group' : 'radiogroup'}
          aria-label={block.label ?? block.key}
        >
          {(block.options ?? []).map((option) => {
            const isOn = multiple ? (selected as string[]).includes(option.value) : selected === option.value
            return (
              <button
                key={option.value}
                type="button"
                role={multiple ? undefined : 'radio'}
                aria-checked={multiple ? undefined : isOn}
                aria-pressed={multiple ? isOn : undefined}
                className="kiosk-choice"
                onClick={() =>
                  set(
                    multiple
                      ? isOn
                        ? (selected as string[]).filter((v) => v !== option.value)
                        : [...(selected as string[]), option.value]
                      : option.value,
                  )
                }
              >
                {isOn ? '✓ ' : ''}
                <Bilingual text={option.label} />
              </button>
            )
          })}
        </div>
      )
    } else if (block.type === 'confirm') {
      body = (
        <div className="kiosk-choices single kiosk-bottom">
          <button type="button" aria-pressed={value === true} className="kiosk-choice" onClick={() => set(value !== true)}>
            {value === true ? '✓ Tôi xác nhận' : 'Chạm để xác nhận'}
          </button>
        </div>
      )
    } else if (block.type === 'date') {
      body = (
        <input
          aria-label={block.label ?? block.key}
          className="kiosk-input large active"
          inputMode="none"
          readOnly
          value={formatDateDigits(dateDraft)}
        />
      )
      const update = (digits: string) => {
        setDateDraft(digits)
        set(digits.length === DATE_DIGITS ? (dateDigitsToIso(digits) ?? digits) : '')
      }
      keyboard = (
        <NumberPad
          onDigit={(digit) => update((dateDraft + digit).slice(0, DATE_DIGITS))}
          onBackspace={() => update(dateDraft.slice(0, -1))}
          extraKey={{ label: 'Tiếp tục', onPress: next }}
        />
      )
    } else {
      const text = typeof value === 'string' ? value : ''
      const kind = keyboardFor(block)
      body = (
        <input
          aria-label={block.label ?? block.key}
          className="kiosk-input large active"
          inputMode="none"
          autoComplete="off"
          value={text}
          maxLength={block.maxLength}
          onChange={(e) => set(e.target.value)}
        />
      )
      keyboard =
        kind === 'number' ? (
          <NumberPad
            onDigit={(digit) => set((text + digit).slice(0, block.maxLength ?? 20))}
            onBackspace={() => set(text.slice(0, -1))}
            extraKey={{ label: 'Tiếp tục', onPress: next }}
          />
        ) : (
          <TextKeyboard
            value={text}
            onChange={set}
            variant={kind === 'code' ? 'code' : 'text'}
            capitalizeWords={capitalizes(block)}
            maxLength={block.maxLength}
            doneLabel="Tiếp tục ›"
            onDone={next}
          />
        )
    }
  }

  return (
    <>
      <main className="kiosk-main">
        <FormsPanel templates={templates} currentIndex={question.templateIndex} />
        <div>
          <p className="kiosk-hint">
            <strong style={{ color: 'var(--brand-dark)' }}>
              Câu {position}/{templateQuestions.length}
            </strong>{' '}
            · {question.template.name}
          </p>
          <div className="kiosk-progress" aria-hidden="true">
            <div style={{ width: `${(position / templateQuestions.length) * 100}%` }} />
          </div>
        </div>
        <h1 className="kiosk-question" id="kiosk-question">
          <Bilingual text={label} />
        </h1>
        {body}
        {error && (
          <div className="kiosk-error" role="alert">
            {error}
          </div>
        )}
        {keyboard && <div className="kiosk-bottom">{keyboard}</div>}
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onBack}>
          Quay lại
        </button>
        <button type="button" className="kiosk-btn primary" onClick={next}>
          Tiếp tục
        </button>
      </KioskFooter>
      {preview && (
        <FormPreview template={question.template} values={values} prefilled={prefilled} onClose={() => setPreview(false)} />
      )}
    </>
  )
}

/**
 * Kế thừa phiếu lần trước (quyết định 27): một câu hỏi thay cho các câu đã trả lời lần trước. Không hiện lại câu trả lời cũ
 * (giữ kín thông tin); câu Đồng ý/Không đồng ý và chữ ký vẫn làm lại.
 */
export function PreviousAnswersScreen({ onKeep, onChange, onBack }: { onKeep: () => void; onChange: () => void; onBack: () => void }) {
  return (
    <>
      <main className="kiosk-main">
        <h1>Thông tin của ông/bà có thay đổi so với lần trước không?</h1>
        <p className="kiosk-hint">
          Ông/bà đã làm các phiếu này gần đây. Nếu không có gì thay đổi, máy dùng lại câu trả lời lần trước; ông/bà chỉ cần trả lời
          các câu còn lại và ký.
        </p>
        <div className="kiosk-choices single kiosk-bottom" role="group" aria-label="Thông tin so với lần trước">
          <button type="button" className="kiosk-choice" onClick={onKeep}>
            Không thay đổi, giữ như lần trước
          </button>
          <button type="button" className="kiosk-choice" onClick={onChange}>
            Có thay đổi, trả lời lại
          </button>
        </div>
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onBack}>
          Quay lại
        </button>
      </KioskFooter>
    </>
  )
}

/** Ký một lần cho cả bộ: xem lại phiếu, chọn lời xác nhận (chữ đỏ đậm), ký vào khung. */
export function SignScreen({
  config,
  signerName,
  request,
  phone,
  answers,
  decisions,
  prefilled,
  kept,
  onBack,
  onDone,
}: {
  config: KioskConfig
  signerName: string
  request: KioskIdentityRequest
  phone: string
  answers: KioskAnswers
  decisions: KioskDecisions
  prefilled: ReadonlySet<string>
  /** Người bệnh chọn giữ như lần trước (quyết định 27): các câu đã trả lời lần trước, theo mẫu. */
  kept?: ReadonlyMap<string, ReadonlySet<string>>
  onBack: () => void
  onDone: (result: KioskSubmitResult) => void
}) {
  const statementConfig = config.statement
  const suggestions = statementConfig?.suggestions ?? []
  const [choice, setChoice] = useState<string>(suggestions[0] ?? (statementConfig ? CUSTOM_STATEMENT : ''))
  const [custom, setCustom] = useState('')
  const [hasInk, setHasInk] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<KioskTemplate | null>(null)
  const pad = useRef<SignaturePadHandle>(null)
  const statement = choice === CUSTOM_STATEMENT ? custom.trim() : choice
  const count = config.templates.length
  const ready = hasInk && (!statementConfig?.required || statement.length > 0)

  const summary = (template: KioskTemplate) => {
    if (asksDecision(template)) {
      const decision = decisions[template.id]
      const signatures = template.definition.signatures
      return decision === 'Refused' ? `✗ ${signatures?.refuseLabel ?? 'Không đồng ý'}` : `✓ ${signatures?.agreeLabel ?? 'Đồng ý'}`
    }
    const answered = Object.values(answers[template.id] ?? {}).filter((v) => v !== '' && v != null).length
    if (kept?.has(template.id)) {
      return answered > 0 ? `✓ Đã trả lời ${answered} câu, còn lại giữ như lần trước` : '✓ Giữ như lần trước'
    }
    return answered > 0 ? `✓ Đã trả lời ${answered} câu` : '✓ Thông tin lấy từ hồ sơ'
  }

  const submit = async () => {
    if (!ready || !pad.current || busy) {
      return
    }
    setBusy(true)
    setError(null)
    try {
      const result = await kioskApi.submit({
        identity: request,
        phoneNumber: phone || null,
        forms: config.templates.map((template) => ({
          templateId: template.id,
          data: answers[template.id] ?? {},
          decision: asksDecision(template) ? (decisions[template.id] ?? null) : null,
        })),
        statement,
        image: pad.current.toPngBase64(),
        ...(kept ? { keepPrevious: true } : {}),
      })
      onDone(result)
    } catch (failure) {
      setError(kioskErrorMessage(failure))
      setBusy(false)
    }
  }

  return (
    <>
      <main className="kiosk-main">
        <div>
          <h1>Ký tên cho các phiếu</h1>
          <p className="kiosk-lead">
            Ông/bà chỉ cần ký <strong>một lần</strong> cho cả {count} phiếu dưới đây.
          </p>
        </div>
        <ul className="kiosk-forms kiosk-sign-list">
          {config.templates.map((template) => (
            <li key={template.id}>
              <span>
                <strong>{template.name}</strong>
                <span>{summary(template)}</span>
              </span>
              <button type="button" className="kiosk-btn small" onClick={() => setPreview(template)}>
                Xem nội dung
              </button>
            </li>
          ))}
        </ul>

        {statementConfig && (
          <div className="kiosk-field">
            <span className="label" id="kiosk-statement-label">
              Lời xác nhận của ông/bà {statementConfig.required && <span className="kiosk-required">*</span>}
            </span>
            <div className="kiosk-statement-chips" role="group" aria-labelledby="kiosk-statement-label">
              {[...suggestions, CUSTOM_STATEMENT].map((item) => (
                <button key={item} type="button" aria-pressed={choice === item} onClick={() => setChoice(item)}>
                  {choice === item ? '✓ ' : ''}
                  {item === CUSTOM_STATEMENT ? 'Tự nhập nội dung khác' : item}
                </button>
              ))}
            </div>
            {choice === CUSTOM_STATEMENT && (
              <>
                <input
                  aria-label="Lời xác nhận tự nhập"
                  className="kiosk-input active"
                  inputMode="none"
                  autoComplete="off"
                  maxLength={300}
                  value={custom}
                  onChange={(e) => setCustom(e.target.value)}
                />
                <TextKeyboard value={custom} onChange={setCustom} maxLength={300} />
              </>
            )}
          </div>
        )}

        <div className="kiosk-signature kiosk-bottom">
          {statement && <div className="kiosk-statement">{statement}</div>}
          <SignaturePad ref={pad} onInkChange={setHasInk} aspectRatio="5 / 2" disabled={busy} />
          <div className="kiosk-signer">{signerName}</div>
        </div>
        <p className="kiosk-hint">
          Chữ ký này dùng chung cho cả {count} phiếu. Mỗi phiếu in ra đều ghi “Ký chung một lần cho {count} phiếu”.
        </p>
        {error && (
          <div className="kiosk-error" role="alert">
            {error}
          </div>
        )}
      </main>
      <KioskFooter>
        <button type="button" className="kiosk-btn" onClick={onBack} disabled={busy}>
          Quay lại
        </button>
        <button type="button" className="kiosk-btn primary" disabled={!ready || busy} onClick={() => void submit()}>
          {busy ? 'Đang lưu…' : count > 1 ? `Ký xác nhận cho cả ${count} phiếu` : 'Ký xác nhận'}
        </button>
      </KioskFooter>
      {preview && (
        <FormPreview template={preview} values={answers[preview.id] ?? {}} prefilled={prefilled} onClose={() => setPreview(null)} />
      )}
    </>
  )
}

const DONE_SECONDS = 15

/** Hoàn tất: mời đến quầy tiếp đón; tự về trang đầu sau 15 giây. */
export function DoneScreen({ result, onRestart }: { result: KioskSubmitResult; onRestart: () => void }) {
  const [left, setLeft] = useState(DONE_SECONDS)
  useEffect(() => {
    const timer = window.setInterval(() => setLeft((value) => value - 1), 1000)
    return () => window.clearInterval(timer)
  }, [])
  useEffect(() => {
    if (left <= 0) {
      onRestart()
    }
  }, [left, onRestart])

  const numbers = result.forms.map((form) => form.formNo)
  const range = numbers.length > 1 ? `${numbers[0]} → ${numbers[numbers.length - 1].split('-').pop()}` : numbers[0]
  return (
    <main className="kiosk-main kiosk-done">
      <div className="kiosk-done-icon" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="#0d7c74" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round">
          <path d="M5 12.5l4.5 4.5L19 7.5" />
        </svg>
      </div>
      <h1>Ông/bà đã ký xong {result.forms.length} phiếu</h1>
      <p className="kiosk-lead">
        Mời ông/bà đến <strong>quầy tiếp đón</strong> để nhân viên kiểm tra và xác nhận.
      </p>
      <section className="kiosk-ticket" aria-label="Thông tin đưa nhân viên">
        <div>
          Người bệnh <strong>{result.patientName}</strong>
        </div>
        {result.patientCode && (
          <div>
            Mã y tế <strong>{result.patientCode}</strong>
          </div>
        )}
        <div>
          Số phiếu <strong>{range}</strong>
        </div>
      </section>
      <p className="kiosk-hint">Muốn thay đổi nội dung, ông/bà báo nhân viên tại quầy.</p>
      <div className="kiosk-bottom" style={{ width: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'calc(var(--k) * 18)' }}>
        <button type="button" className="kiosk-btn primary block" onClick={onRestart}>
          Về trang đầu
        </button>
        <div className="kiosk-countdown" aria-hidden="true">
          <div style={{ width: `${(left / DONE_SECONDS) * 100}%` }} />
        </div>
        <span className="kiosk-hint">
          Màn hình tự về trang đầu sau <strong>{Math.max(left, 0)} giây</strong> và xóa thông tin của ông/bà khỏi màn hình.
        </span>
      </div>
    </main>
  )
}
