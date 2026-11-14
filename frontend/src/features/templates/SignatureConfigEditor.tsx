import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Col, Flex, Form, Input, Radio, Row, Select, Typography } from 'antd'
import { useState } from 'react'
import {
  STATEMENT_MAX_LENGTH,
  STATEMENT_MAX_SUGGESTIONS,
  consentTemplatesApi,
  templateKeys,
  type ConsentTemplateVersion,
  type TemplateDefinition,
  type TemplateSignatures,
} from '../../api/consentTemplates'
import { ApiError } from '../../api/http'
import { errorMessage } from '../../utils/formErrors'
import { signerRoleLabel, signerRoleOptions } from './signerRoles'

/** Giới hạn của máy chủ (TemplateDefinitionValidator, TemplateSignatures.MaxSteps). */
const MAX_STEPS = 5
const MAX_LABEL = 500

interface StepValues {
  key?: string
  roleCode?: string
  label?: string
  hint?: string
}

interface SignatureValues {
  patient?: string
  representative?: string
  hospital?: string
  hospitalHint?: string
  decision: 'required' | 'none'
  decisionLabel?: string
  agreeLabel?: string
  refuseLabel?: string
  steps: StepValues[]
  statementEnabled: boolean
  statementLabel?: string
  statementRequired: boolean
  statementSuggestions: string[]
}

const clean = (value?: string) => (value?.trim() ? value.trim() : undefined)

function toValues(signatures: TemplateSignatures | undefined): SignatureValues {
  return {
    patient: signatures?.patient,
    representative: signatures?.representative,
    hospital: signatures?.hospital,
    hospitalHint: signatures?.hospitalHint,
    decision: signatures?.decision === 'none' ? 'none' : 'required',
    decisionLabel: signatures?.decisionLabel,
    agreeLabel: signatures?.agreeLabel,
    refuseLabel: signatures?.refuseLabel,
    steps: signatures?.steps?.map((s) => ({ ...s })) ?? [],
    statementEnabled: !!signatures?.statement,
    statementLabel: signatures?.statement?.label,
    statementRequired: signatures?.statement?.required ?? true,
    statementSuggestions: signatures?.statement?.suggestions ?? ['Tôi đã nghe, hiểu và đồng ý'],
  }
}

/** Dựng lại khối `signatures`: bỏ ô trống (dùng nhãn mặc định), giữ mã bước cũ, tạo mã cho bước mới. */
function toSignatures(values: SignatureValues): TemplateSignatures | undefined {
  const used = new Set(values.steps.map((s) => s.key).filter(Boolean))
  const nextKey = () => {
    let n = 1
    while (used.has(`step${n}`)) n++
    used.add(`step${n}`)
    return `step${n}`
  }
  const steps = values.steps.map((s) => ({
    key: s.key || nextKey(),
    roleCode: s.roleCode ?? '',
    label: s.label?.trim() ?? '',
    ...(clean(s.hint) ? { hint: clean(s.hint) } : {}),
  }))
  const asks = values.decision === 'required'
  const signatures: TemplateSignatures = {
    patient: clean(values.patient),
    representative: clean(values.representative),
    hospital: clean(values.hospital),
    hospitalHint: clean(values.hospitalHint),
    decision: asks ? undefined : 'none',
    decisionLabel: asks ? clean(values.decisionLabel) : undefined,
    agreeLabel: asks ? clean(values.agreeLabel) : undefined,
    refuseLabel: asks ? clean(values.refuseLabel) : undefined,
    steps: steps.length > 0 ? steps : undefined,
    statement: values.statementEnabled
      ? {
          ...(clean(values.statementLabel) ? { label: clean(values.statementLabel) } : {}),
          ...(values.statementRequired ? { required: true } : {}),
          suggestions: values.statementSuggestions.map((x) => x.trim()).filter(Boolean),
        }
      : undefined,
  }
  const entries = Object.entries(signatures).filter(([, v]) => v !== undefined)
  return entries.length > 0 ? (Object.fromEntries(entries) as TemplateSignatures) : undefined
}

const labelRules = [{ max: MAX_LABEL, message: `Tối đa ${MAX_LABEL} ký tự.` }]

/**
 * Cấu hình ký của phiên bản mẫu bằng biểu mẫu (thay cho sửa phần `signatures` trong JSON): nhãn các ô ký, có hỏi Đồng ý/
 * Không đồng ý hay không, và các bước xác nhận theo vai trò trước bước xác nhận cuối.
 */
export function SignatureConfigEditor({
  version,
  editable,
  finalRoleCode,
}: {
  version: ConsentTemplateVersion
  editable: boolean
  /** Vai trò xác nhận cuối của mẫu (sửa ở "Sửa thông tin mẫu"). */
  finalRoleCode: string
}) {
  const [form] = Form.useForm<SignatureValues>()
  const [problems, setProblems] = useState<string[]>([])
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const decision = Form.useWatch('decision', form)
  const statementEnabled = Form.useWatch('statementEnabled', form)

  const save = useMutation({
    mutationFn: (values: SignatureValues) => {
      const { signatures: _old, ...rest } = version.definition
      const signatures = toSignatures(values)
      const definition: TemplateDefinition = signatures ? { ...rest, signatures } : rest
      return consentTemplatesApi.updateDraft(version.templateId, version.versionNo, definition)
    },
    onSuccess: async (updated) => {
      queryClient.setQueryData(templateKeys.version(version.templateId, version.versionNo), updated)
      await queryClient.invalidateQueries({ queryKey: templateKeys.detail(version.templateId) })
      setProblems([])
      message.success('Đã lưu cấu hình ký của phiên bản nháp.')
    },
    onError: (e) => {
      if (e instanceof ApiError && e.fieldErrors?.definition) {
        setProblems(e.fieldErrors.definition)
      }
      message.error(errorMessage(e))
    },
  })

  return (
    <Form<SignatureValues>
      form={form}
      layout="vertical"
      disabled={!editable}
      initialValues={toValues(version.definition.signatures)}
      onFinish={(values) => save.mutate(values)}
    >
      <Flex vertical gap={12}>
        {!editable && (
          <Alert
            type="info"
            showIcon
            title="Chỉ phiên bản nháp sửa được."
            description="Bấm “Tạo phiên bản mới”, sửa cấu hình ký rồi phát hành. Phiếu đã lập vẫn ký theo phiên bản cũ."
          />
        )}
        {problems.length > 0 && (
          <Alert
            type="error"
            showIcon
            title="Cấu hình chưa hợp lệ"
            description={
              <ul style={{ margin: 0, paddingLeft: 20 }}>
                {problems.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            }
          />
        )}

        <Card size="small" title="Thứ tự ký">
          <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
            Người bệnh (hoặc người đại diện) ký trước trên điện thoại. Sau đó nhân viên đúng vai trò xác nhận lần lượt từng
            bước bên dưới, mỗi bước một người khác nhau. Cuối cùng nhân viên có vai trò{' '}
            <Typography.Text strong>{signerRoleLabel(finalRoleCode)}</Typography.Text> bấm “Xác nhận và hoàn tất” (đổi vai trò
            này ở “Sửa thông tin mẫu”).
          </Typography.Paragraph>
          <Form.List name="steps">
            {(fields, { add, remove, move }) => (
              <Flex vertical gap={8}>
                {fields.length === 0 && <Typography.Text type="secondary">Không có bước xác nhận trung gian.</Typography.Text>}
                {fields.map((field, index) => (
                  <Card
                    key={field.key}
                    size="small"
                    type="inner"
                    title={`Bước ${index + 1}`}
                    extra={
                      editable && (
                        <Flex gap={4}>
                          <Button
                            size="small"
                            type="text"
                            icon={<ArrowUpOutlined />}
                            aria-label={`Đưa bước ${index + 1} lên`}
                            disabled={index === 0}
                            onClick={() => move(index, index - 1)}
                          />
                          <Button
                            size="small"
                            type="text"
                            icon={<ArrowDownOutlined />}
                            aria-label={`Đưa bước ${index + 1} xuống`}
                            disabled={index === fields.length - 1}
                            onClick={() => move(index, index + 1)}
                          />
                          <Button
                            size="small"
                            type="text"
                            danger
                            icon={<DeleteOutlined />}
                            aria-label={`Xóa bước ${index + 1}`}
                            onClick={() => remove(index)}
                          />
                        </Flex>
                      )
                    }
                  >
                    <Form.Item name={[field.name, 'key']} hidden>
                      <Input />
                    </Form.Item>
                    <Row gutter={12}>
                      <Col xs={24} md={8}>
                        <Form.Item
                          label="Vai trò xác nhận"
                          name={[field.name, 'roleCode']}
                          rules={[{ required: true, message: 'Vui lòng chọn vai trò.' }]}
                        >
                          <Select options={signerRoleOptions} placeholder="Chọn vai trò" />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={16}>
                        <Form.Item
                          label="Tên ô ký trên phiếu"
                          name={[field.name, 'label']}
                          rules={[{ required: true, whitespace: true, message: 'Vui lòng nhập tên ô ký.' }, ...labelRules]}
                        >
                          <Input placeholder="Ví dụ: Người tư vấn (Consultant)" />
                        </Form.Item>
                      </Col>
                      <Col span={24}>
                        <Form.Item label="Ghi chú dưới tên ô ký (không bắt buộc)" name={[field.name, 'hint']} rules={labelRules}>
                          <Input placeholder="Ví dụ: (Ký, ghi rõ họ tên)" />
                        </Form.Item>
                      </Col>
                    </Row>
                  </Card>
                ))}
                {editable && (
                  <Button
                    icon={<PlusOutlined />}
                    onClick={() => add({ roleCode: undefined, label: '' })}
                    disabled={fields.length >= MAX_STEPS}
                    style={{ alignSelf: 'flex-start' }}
                  >
                    Thêm bước xác nhận{fields.length >= MAX_STEPS ? ` (tối đa ${MAX_STEPS})` : ''}
                  </Button>
                )}
              </Flex>
            )}
          </Form.List>
        </Card>

        <Card size="small" title="Ý kiến của người bệnh khi ký">
          <Form.Item name="decision" style={{ marginBottom: decision === 'required' ? undefined : 0 }}>
            <Radio.Group
              options={[
                { value: 'required', label: 'Hỏi Đồng ý / Không đồng ý' },
                { value: 'none', label: 'Chỉ ký xác nhận (không hỏi)' },
              ]}
            />
          </Form.Item>
          {decision === 'required' && (
            <Row gutter={12}>
              <Col span={24}>
                <Form.Item label="Câu hỏi (để trống: “Ý kiến của người bệnh”)" name="decisionLabel" rules={labelRules}>
                  <Input.TextArea autoSize={{ minRows: 1, maxRows: 4 }} />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item label="Nhãn đồng ý (để trống: “Đồng ý”)" name="agreeLabel" rules={labelRules}>
                  <Input placeholder="Ví dụ: Đồng ý (Agree)" />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item label="Nhãn không đồng ý (để trống: “Từ chối”)" name="refuseLabel" rules={labelRules}>
                  <Input placeholder="Ví dụ: Không đồng ý (Disagree)" />
                </Form.Item>
              </Col>
            </Row>
          )}
        </Card>

        <Card size="small" title="Lời xác nhận của người ký">
          <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
            Ô để người bệnh (hoặc người đại diện) ghi lời xác nhận ngay trên khung ký, ví dụ “Tôi đã nghe, hiểu và đồng ý”.
            Các câu gợi ý hiện thành nút bấm chọn nhanh; lời xác nhận in trên PDF ngay trên chữ ký (chữ nâu đậm, nghiêng).
          </Typography.Paragraph>
          <Form.Item name="statementEnabled" valuePropName="checked" style={{ marginBottom: statementEnabled ? 12 : 0 }}>
            <Checkbox>Có ô lời xác nhận</Checkbox>
          </Form.Item>
          {statementEnabled && (
            <>
              <Form.Item label="Tiêu đề ô (để trống: “Lời xác nhận của người ký”)" name="statementLabel" rules={labelRules}>
                <Input placeholder="Ví dụ: Lời xác nhận của người ký (Signer’s statement)" />
              </Form.Item>
              <Form.Item name="statementRequired" valuePropName="checked">
                <Checkbox>Bắt buộc ghi lời xác nhận trước khi ký</Checkbox>
              </Form.Item>
              <Form.List name="statementSuggestions">
                {(fields, { add, remove }) => (
                  <Flex vertical gap={8}>
                    <Typography.Text strong>Câu gợi ý (bấm chọn nhanh)</Typography.Text>
                    {fields.map((field, index) => (
                      <Flex key={field.key} gap={8} align="start">
                        <Form.Item
                          name={field.name}
                          rules={[
                            { required: true, whitespace: true, message: 'Nhập câu gợi ý hoặc xóa dòng này.' },
                            { max: STATEMENT_MAX_LENGTH, message: `Tối đa ${STATEMENT_MAX_LENGTH} ký tự.` },
                          ]}
                          style={{ flex: 1, marginBottom: 0 }}
                        >
                          <Input aria-label={`Câu gợi ý ${index + 1}`} className="signer-statement" />
                        </Form.Item>
                        {editable && (
                          <Button type="text" danger icon={<DeleteOutlined />} aria-label={`Xóa câu gợi ý ${index + 1}`} onClick={() => remove(field.name)} />
                        )}
                      </Flex>
                    ))}
                    {editable && (
                      <Button
                        icon={<PlusOutlined />}
                        onClick={() => add('')}
                        disabled={fields.length >= STATEMENT_MAX_SUGGESTIONS}
                        style={{ alignSelf: 'flex-start' }}
                      >
                        Thêm câu gợi ý{fields.length >= STATEMENT_MAX_SUGGESTIONS ? ` (tối đa ${STATEMENT_MAX_SUGGESTIONS})` : ''}
                      </Button>
                    )}
                  </Flex>
                )}
              </Form.List>
            </>
          )}
        </Card>

        <Card size="small" title="Tên các ô ký trên PDF">
          <Row gutter={12}>
            <Col xs={24} md={12}>
              <Form.Item label="Ô người bệnh (để trống: “Người bệnh”)" name="patient" rules={labelRules}>
                <Input placeholder="Ví dụ: Người bệnh (Patient)" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item label="Ô người đại diện (để trống: “Người đại diện hợp pháp”)" name="representative" rules={labelRules}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item label="Ô xác nhận cuối (để trống: “Nhân viên y tế”)" name="hospital" rules={labelRules}>
                <Input placeholder="Ví dụ: Điều dưỡng khoa (Ward Nurse)" />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item label="Ghi chú ô xác nhận cuối (không bắt buộc)" name="hospitalHint" rules={labelRules}>
                <Input />
              </Form.Item>
            </Col>
          </Row>
        </Card>

        {editable && (
          <Button type="primary" htmlType="submit" loading={save.isPending} style={{ alignSelf: 'flex-start' }}>
            Lưu cấu hình ký
          </Button>
        )}
      </Flex>
    </Form>
  )
}
