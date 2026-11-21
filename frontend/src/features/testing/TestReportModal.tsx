import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Form, Input, Modal, Radio, Typography } from 'antd'
import { TEST_RESULT_LABELS, testingApi, testingKeys, type TestAssignment, type TestResult } from '../../api/testing'
import { applyServerFieldErrors, errorMessageWithDetails } from '../../utils/formErrors'

interface ReportValues {
  result?: TestResult
  findings?: string
  suggestions?: string
}

const RESULTS: TestResult[] = ['Passed', 'PartiallyPassed', 'Failed']
const TEXT_MAX = 4000

/**
 * Báo cáo kiểm thử của người được phân công (quyết định 49): kết quả, lỗi hoặc vấn đề phát hiện (bắt buộc khi chưa đạt), đề xuất
 * nâng cấp. Sửa được cho tới khi người quản lý kiểm thử ghi hướng xử lý.
 */
export function TestReportModal({ assignment, onClose }: { assignment: TestAssignment; onClose: () => void }) {
  const [form] = Form.useForm<ReportValues>()
  const result = Form.useWatch('result', form)
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const save = useMutation({
    mutationFn: (values: ReportValues) =>
      testingApi.report(assignment.id, {
        result: values.result!,
        findings: values.findings?.trim() || null,
        suggestions: values.suggestions?.trim() || null,
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: testingKeys.all })
      message.success('Đã gửi báo cáo kiểm thử.')
      onClose()
    },
    onError: (e) => {
      if (!applyServerFieldErrors(form, e)) {
        message.error(errorMessageWithDetails(e))
      }
    },
  })

  return (
    <Modal
      open
      title={`Báo cáo kiểm thử: ${assignment.feature}`}
      okText="Gửi báo cáo"
      cancelText="Hủy"
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
      width={640}
    >
      {assignment.instructions && (
        <Typography.Paragraph type="secondary" style={{ whiteSpace: 'pre-line' }}>
          {assignment.instructions}
        </Typography.Paragraph>
      )}
      <Form<ReportValues>
        form={form}
        layout="vertical"
        initialValues={{
          result: assignment.result ?? undefined,
          findings: assignment.findings ?? undefined,
          suggestions: assignment.suggestions ?? undefined,
        }}
        onFinish={(values) => save.mutate(values)}
      >
        <Form.Item name="result" label="Kết quả" rules={[{ required: true, message: 'Chọn kết quả kiểm thử.' }]}>
          <Radio.Group options={RESULTS.map((r) => ({ value: r, label: TEST_RESULT_LABELS[r] }))} />
        </Form.Item>
        <Form.Item
          name="findings"
          label="Lỗi, vấn đề phát hiện"
          extra="Ghi cách làm để gặp lỗi, màn hình, thông báo hiện ra. Không ghi thông tin người bệnh thật."
          rules={[
            {
              required: result !== undefined && result !== 'Passed',
              whitespace: true,
              message: 'Ghi lỗi hoặc vấn đề phát hiện khi kết quả không đạt hoặc đạt một phần.',
            },
          ]}
        >
          <Input.TextArea rows={4} maxLength={TEXT_MAX} showCount />
        </Form.Item>
        <Form.Item name="suggestions" label="Đề xuất nâng cấp (không bắt buộc)">
          <Input.TextArea rows={3} maxLength={TEXT_MAX} showCount />
        </Form.Item>
      </Form>
    </Modal>
  )
}
