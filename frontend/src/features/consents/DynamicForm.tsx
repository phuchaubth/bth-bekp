import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { Button, Checkbox, DatePicker, Flex, Form, Input, Radio, Tag, Typography, type FormInstance } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo } from 'react'
import type { FormData, ScoreResult } from '../../api/consentForms'
import type { TemplateBlock, TemplateDefinition, TemplateOption } from '../../api/consentTemplates'
import { Bilingual } from './Bilingual'
import { evaluate, levelIndex } from './templateLogic'

interface Props {
  definition: TemplateDefinition
  form: FormInstance<FormData>
  initialValues: FormData
  disabled?: boolean
  /** Lỗi theo từng trường do máy chủ trả về khi trình ký. */
  errors?: Record<string, string[]>
  /** Gọi khi người dùng sửa trường (ví dụ để bỏ lỗi máy chủ của trường vừa sửa). */
  onValuesChange?: (changed: FormData) => void
}

const DATE = 'YYYY-MM-DD'
const DATE_TIME = 'YYYY-MM-DDTHH:mm'
const levelColors = ['green', 'gold', 'red']

/** Hiển thị và nhập liệu một phiếu theo định nghĩa mẫu (dùng chung cho soạn phiếu, xem phiếu và xem trước mẫu). */
export function DynamicForm({ definition, form, initialValues, disabled = false, errors, onValuesChange }: Props) {
  // preserve: tính cả giá trị của trường đang ẩn (giữ lại khi lưu nháp, giống máy chủ).
  const watched = Form.useWatch((values: FormData) => values, { form, preserve: true })
  const values = watched ?? initialValues
  const { visible, scores, computed, derived } = useMemo(() => evaluate(definition, values ?? {}), [definition, values])

  // Câu hỏi tự chọn theo trường khác (ví dụ mức BMI): ghi lựa chọn vào biểu mẫu để lưu nháp và tính điểm như máy chủ.
  useEffect(() => {
    if (disabled) return
    for (const [key, value] of Object.entries(derived)) {
      if (form.getFieldValue(key) !== value) {
        form.setFieldValue(key, value)
      }
    }
  }, [derived, disabled, form])

  return (
    <Form<FormData>
      form={form}
      layout="vertical"
      initialValues={initialValues}
      disabled={disabled}
      requiredMark="optional"
      onValuesChange={onValuesChange ? (changed: FormData) => onValuesChange(changed) : undefined}
    >
      {definition.sections.map((section, s) => (
        <section key={section.title ?? s} style={{ marginBottom: 8 }}>
          {section.title && (
            <Typography.Title level={5} style={{ marginTop: 8 }}>
              <Bilingual text={section.title} />
            </Typography.Title>
          )}
          {section.blocks.map((block, b) => {
            const key = block.key ?? `${s}-${b}`
            if (block.type === 'paragraph') {
              return (
                <Typography.Paragraph key={key} style={{ textAlign: 'justify' }}>
                  <Bilingual text={block.text} />
                </Typography.Paragraph>
              )
            }
            if (block.type === 'list') {
              const ListTag = block.layout === 'ordered' ? 'ol' : 'ul'
              return (
                <ListTag key={key} style={{ paddingLeft: 22, marginTop: 0 }}>
                  {block.items?.map((item) => (
                    <li key={item} style={{ marginBottom: 4 }}>
                      <Bilingual text={item} />
                    </li>
                  ))}
                </ListTag>
              )
            }
            if (block.type === 'score') {
              return <ScoreLine key={key} block={block} score={scores[block.key!]} />
            }
            if (!visible.has(block.key!)) {
              return null
            }
            if (block.type === 'computed') {
              return <ComputedField key={key} block={block} value={computed[block.key!]} error={errors?.[block.key!]} />
            }
            return (
              <Field
                key={key}
                block={block}
                error={errors?.[block.key!]}
                derivedFrom={derived[block.key!] && block.valueFrom ? sourceText(definition, block.valueFrom, computed, values) : undefined}
              />
            )
          })}
        </section>
      ))}
    </Form>
  )
}

function Field({ block, error, derivedFrom }: { block: TemplateBlock; error?: string[]; derivedFrom?: string }) {
  const common = {
    name: block.key!,
    label: block.type === 'confirm' ? undefined : <Bilingual text={block.label} />,
    required: block.required,
    validateStatus: error ? ('error' as const) : undefined,
    help: error?.join(' '),
    // Tự chọn theo trường khác: ghi rõ căn cứ (ví dụ "Tự chọn theo BMI 21,5").
    extra: derivedFrom ? `Tự chọn theo ${derivedFrom}.` : undefined,
  }

  switch (block.type) {
    case 'text':
      return (
        <Form.Item {...common}>
          <Input maxLength={block.maxLength} autoComplete="off" />
        </Form.Item>
      )
    case 'date':
      return (
        <Form.Item
          {...common}
          getValueProps={(value?: string) => ({ value: value ? dayjs(value, DATE) : undefined })}
          normalize={(value?: Dayjs | null) => (value ? value.format(DATE) : undefined)}
        >
          <DatePicker format="DD/MM/YYYY" placeholder="dd/mm/yyyy" style={{ width: 200 }} />
        </Form.Item>
      )
    case 'datetime':
      return (
        <Form.Item
          {...common}
          getValueProps={(value?: string) => ({ value: value ? dayjs(value, DATE_TIME) : undefined })}
          normalize={(value?: Dayjs | null) => (value ? value.format(DATE_TIME) : undefined)}
        >
          <DatePicker showTime={{ format: 'HH:mm' }} format="HH:mm DD/MM/YYYY" placeholder="giờ:phút ngày" style={{ width: 220 }} />
        </Form.Item>
      )
    case 'choice':
      return (
        <Form.Item {...common}>
          <Radio.Group disabled={derivedFrom ? true : undefined}>
            <Flex vertical={block.layout !== 'inline'} wrap gap={block.layout === 'inline' ? 16 : 4}>
              {block.options?.map((option) => (
                <Radio key={option.value} value={option.value}>
                  {optionLabel(option)}
                </Radio>
              ))}
            </Flex>
          </Radio.Group>
        </Form.Item>
      )
    case 'multiChoice':
      return (
        <Form.Item {...common}>
          <Checkbox.Group>
            <Flex vertical={block.layout !== 'inline'} wrap gap={block.layout === 'inline' ? 16 : 4}>
              {block.options?.map((option) => (
                <Checkbox key={option.value} value={option.value}>
                  <Bilingual text={option.label} />
                </Checkbox>
              ))}
            </Flex>
          </Checkbox.Group>
        </Form.Item>
      )
    case 'confirm':
      return (
        <Form.Item {...common} valuePropName="checked" normalize={(checked: boolean) => (checked ? true : undefined)}>
          <Checkbox>
            <Bilingual text={block.label} />
          </Checkbox>
        </Form.Item>
      )
    case 'repeater':
      return <Rows block={block} error={error} />
    default:
      return null
  }
}

/** Căn cứ tự chọn, ví dụ "Chỉ số khối cơ thể 21,5" (nhãn tiếng Việt của trường nguồn và giá trị). */
function sourceText(definition: TemplateDefinition, key: string, computed: Record<string, string>, values?: FormData) {
  const label = definition.sections.flatMap((s) => s.blocks).find((b) => b.key === key)?.label ?? key
  const value = computed[key] ?? String(values?.[key] ?? '')
  return `${label.split('(')[0].trim()} ${value}`
}

const formulaHints: Record<string, string> = {
  bmi: 'Tự tính = cân nặng (kg) / [chiều cao (m)]². Nhập chiều cao và cân nặng ở trên.',
}

/** Trường tự tính (ví dụ BMI): chỉ hiển thị, không nhập; máy chủ tự tính lại khi lưu. */
function ComputedField({ block, value, error }: { block: TemplateBlock; value?: string; error?: string[] }) {
  return (
    <Form.Item
      label={<Bilingual text={block.label} />}
      htmlFor={`computed-${block.key}`}
      extra={block.formula ? formulaHints[block.formula] : undefined}
      validateStatus={error ? 'error' : undefined}
      help={error?.join(' ')}
    >
      <Input
        id={`computed-${block.key}`}
        readOnly
        value={value ?? ''}
        placeholder="Chưa đủ dữ liệu để tính"
        style={{ width: 200, fontVariantNumeric: 'tabular-nums' }}
      />
    </Form.Item>
  )
}

function optionLabel(option: TemplateOption) {
  // == null: thuộc tính trống có thể vắng mặt hoặc là null.
  return option.score == null ? (
    <Bilingual text={option.label} />
  ) : (
    <>
      <Bilingual text={option.label} />{' '}
      <Typography.Text type="secondary" style={{ fontVariantNumeric: 'tabular-nums' }}>
        ({option.score} điểm)
      </Typography.Text>
    </>
  )
}

function Rows({ block, error }: { block: TemplateBlock; error?: string[] }) {
  const columns = block.columns ?? []
  return (
    <Form.Item label={<Bilingual text={block.label} />} required={block.required} validateStatus={error ? 'error' : undefined} help={error?.join(' ')}>
      <Form.List name={block.key!}>
        {(rows, { add, remove }) => (
          <Flex vertical gap={8}>
            {rows.map((row, index) => (
              <Flex key={row.key} gap={8} wrap align="start">
                <Typography.Text type="secondary" style={{ width: 20, paddingTop: 5 }}>
                  {index + 1}.
                </Typography.Text>
                {columns.map((column) => (
                  <Form.Item key={column.key} name={[row.name, column.key]} noStyle>
                    <Input placeholder={column.label} aria-label={`${column.label} (dòng ${index + 1})`} maxLength={column.maxLength} style={{ flex: '1 1 160px', minWidth: 0 }} />
                  </Form.Item>
                ))}
                <Button type="text" icon={<DeleteOutlined />} aria-label={`Xóa dòng ${index + 1}`} onClick={() => remove(row.name)} />
              </Flex>
            ))}
            <Button
              icon={<PlusOutlined />}
              onClick={() => add({})}
              disabled={block.maxItems != null && rows.length >= block.maxItems}
              style={{ alignSelf: 'flex-start' }}
            >
              Thêm dòng
            </Button>
          </Flex>
        )}
      </Form.List>
    </Form.Item>
  )
}

function ScoreLine({ block, score }: { block: TemplateBlock; score?: ScoreResult }) {
  const index = levelIndex(block, score?.levelCode ?? null)
  return (
    <Flex align="center" gap={8} wrap style={{ margin: '4px 0 16px' }} role="status" aria-label={block.label}>
      <Typography.Text strong>
        <Bilingual text={`${block.label}:`} />
      </Typography.Text>
      {score?.total == null ? (
        <Typography.Text type="secondary">trả lời đủ các câu để tính điểm</Typography.Text>
      ) : (
        <>
          <Typography.Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
            {score.total} điểm
          </Typography.Text>
          <Tag color={levelColors[Math.min(index, levelColors.length - 1)] ?? 'default'}>
            <Bilingual text={score.levelLabel} />
          </Tag>
        </>
      )}
    </Flex>
  )
}
