import { describe, expect, it } from 'vitest'
import type { TemplateDefinition } from '../../api/consentTemplates'
import { computeFormula, evaluate } from './templateLogic'

// Cùng các trường hợp với ComputedFormulasTests (backend) để màn hình và máy chủ tính giống nhau.
describe('computeFormula: bmi', () => {
  it.each([
    ['50', '160', '19,5'],
    ['58,5', '165', '21,5'],
    ['58.5', '165.0', '21,5'],
    ['70', '175', '22,9'],
    ['45', '170', '15,6'],
    ['100', '160', '39,1'],
  ])('cân nặng %s kg, chiều cao %s cm → %s', (weight, height, expected) => {
    expect(computeFormula('bmi', [weight, height])).toBe(expected)
  })

  it.each([
    [undefined, '160'],
    ['50', undefined],
    ['', '160'],
    ['abc', '160'],
    ['50', '0'],
    ['-50', '160'],
  ])('thiếu hoặc sai đầu vào (%s, %s) → trống', (weight, height) => {
    expect(computeFormula('bmi', [weight, height])).toBeNull()
  })

  it('công thức lạ → trống', () => {
    expect(computeFormula('bsa', ['50', '160'])).toBeNull()
  })
})

describe('evaluate: trường tự tính', () => {
  const definition: TemplateDefinition = {
    title: 'Thử',
    sections: [
      {
        blocks: [
          { type: 'text', key: 'height', label: 'Chiều cao' },
          { type: 'text', key: 'weight', label: 'Cân nặng' },
          { type: 'computed', key: 'bmi', label: 'BMI', formula: 'bmi', inputs: ['weight', 'height'] },
          {
            type: 'choice',
            key: 'bmiScore',
            label: 'Mức BMI',
            valueFrom: 'bmi',
            options: [
              { value: 'atLeast20', label: '≥ 20', score: 0, min: 20 },
              { value: 'from18_5to20', label: '18,5 – 20,0', score: 1, min: 18.5, max: 20 },
              { value: 'below18_5', label: '< 18,5', score: 2, max: 18.5 },
            ],
          },
        ],
      },
    ],
  }

  it('tính từ giá trị đã nhập, bỏ qua giá trị bmi gõ tay', () => {
    const result = evaluate(definition, { height: '160', weight: '50', bmi: '99' })
    expect(result.computed.bmi).toBe('19,5')
    expect(result.visible.has('bmi')).toBe(true)
  })

  // Cùng ngưỡng với Bm10_AdultBmiScore_IsSelectedFromComputedBmi_IgnoringUserChoice (backend).
  it.each([
    ['64', 'atLeast20'],
    ['51,2', 'atLeast20'],
    ['51', 'from18_5to20'],
    ['47,4', 'from18_5to20'],
    ['47,3', 'from18_5to20'],
    ['47,2', 'below18_5'],
  ])('cân nặng %s kg (cao 160 cm) → mức %s, bỏ qua lựa chọn tay', (weight, expected) => {
    expect(evaluate(definition, { height: '160', weight, bmiScore: 'below18_5' }).derived.bmiScore).toBe(expected)
  })

  it('chưa đủ dữ liệu thì chưa có giá trị', () => {
    const result = evaluate(definition, { height: '160', bmiScore: 'below18_5' })
    expect(result.computed.bmi).toBeUndefined()
    // Chưa có BMI thì không tự chọn: giữ lựa chọn tay.
    expect(result.derived.bmiScore).toBeUndefined()
  })
})
