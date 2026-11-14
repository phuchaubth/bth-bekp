import { CheckOutlined } from '@ant-design/icons'
import { Button, Flex, Input, Typography } from 'antd'
import { STATEMENT_MAX_LENGTH, type TemplateStatement } from '../../api/consentTemplates'
import { Bilingual } from '../consents/Bilingual'

interface Props {
  config: TemplateStatement
  value: string
  onChange: (value: string) => void
  disabled?: boolean
}

/**
 * Ô lời xác nhận của người ký ở khung ký (ví dụ "Tôi đã nghe, hiểu và đồng ý"): bấm một câu gợi ý để điền nhanh, vẫn
 * sửa hoặc tự gõ được. Chữ nâu đậm, nghiêng như trên PDF.
 */
export function StatementBox({ config, value, onChange, disabled }: Props) {
  const label = config.label ?? 'Lời xác nhận của người ký'
  const suggestions = config.suggestions ?? []
  return (
    <Flex vertical gap={8}>
      <label htmlFor="signer-statement" style={{ fontWeight: 600 }}>
        <Bilingual text={label} />
        {config.required && (
          <Typography.Text type="danger" aria-hidden="true">
            {' '}
            *
          </Typography.Text>
        )}
      </label>
      {suggestions.length > 0 && (
        <Flex gap={8} wrap role="group" aria-label="Chọn nhanh lời xác nhận">
          {suggestions.map((suggestion) => {
            const selected = value.trim() === suggestion
            return (
              <Button
                key={suggestion}
                size="large"
                shape="round"
                type={selected ? 'primary' : 'default'}
                icon={selected ? <CheckOutlined /> : undefined}
                aria-pressed={selected}
                disabled={disabled}
                onClick={() => onChange(suggestion)}
                style={{ height: 'auto', minHeight: 44, whiteSpace: 'normal', textAlign: 'left' }}
              >
                {suggestion}
              </Button>
            )
          })}
        </Flex>
      )}
      {suggestions.length > 0 && (
        <Typography.Text type="secondary">
          Không đúng ý ông/bà? Ghi nội dung của mình vào ô dưới (có thể sửa câu đã chọn).
        </Typography.Text>
      )}
      <Input.TextArea
        id="signer-statement"
        className="signer-statement"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        maxLength={STATEMENT_MAX_LENGTH}
        autoSize={{ minRows: 2, maxRows: 4 }}
        placeholder={suggestions.length > 0 ? 'Bấm câu gợi ý ở trên hoặc tự ghi nội dung' : 'Ví dụ: Tôi đã nghe, hiểu và đồng ý'}
        disabled={disabled}
        style={{ fontSize: 17 }}
      />
    </Flex>
  )
}
