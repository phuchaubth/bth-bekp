import { Alert, Button, Card, Descriptions, Empty, Flex, Spin, Typography } from 'antd'
import { useState } from 'react'
import { useMyTestAssignments, type TestAssignment } from '../../api/testing'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateRange, formatDateTime } from '../../utils/format'
import { TestReportModal } from './TestReportModal'
import { TestResultTag, TestStatusTag } from './testingTags'

/**
 * Việc kiểm thử của tôi (quyết định 49): tính năng được phân công kiểm thử, thời gian, cách kiểm thử; gửi báo cáo bắt buộc sau
 * khi kiểm thử; xem hướng xử lý của người quản lý kiểm thử. Mọi tài khoản đều mở được, chỉ thấy việc của mình.
 */
export function MyTestAssignmentsPage() {
  const mine = useMyTestAssignments()
  const [reporting, setReporting] = useState<TestAssignment | null>(null)

  return (
    <>
      <PageHeader title="Việc kiểm thử của tôi" />
      <Typography.Paragraph type="secondary">
        Kiểm thử các tính năng mới được phân công trong thời gian ghi ở mỗi việc (thường trên môi trường kiểm thử UAT), rồi bấm{' '}
        <b>Gửi báo cáo</b>. Báo cáo là bắt buộc: kết quả, lỗi phát hiện, đề xuất nâng cấp. Sửa được báo cáo cho tới khi người quản lý
        kiểm thử ghi hướng xử lý.
      </Typography.Paragraph>
      {mine.isError ? (
        <Alert type="error" showIcon title={errorMessage(mine.error)} />
      ) : !mine.data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : mine.data.length === 0 ? (
        <Empty description="Bạn chưa được phân công kiểm thử." />
      ) : (
        <Flex vertical gap={12}>
          {mine.data.map((a) => (
            <Card
              key={a.id}
              size="small"
              title={
                <Flex vertical gap={2} style={{ whiteSpace: 'normal', paddingBlock: 6 }}>
                  <span>{a.feature}</span>
                  <Typography.Text type="secondary" style={{ fontWeight: 'normal', fontSize: 13 }}>
                    {a.version}
                  </Typography.Text>
                </Flex>
              }
              extra={
                a.canReport && (
                  <Button type={a.result ? 'default' : 'primary'} onClick={() => setReporting(a)}>
                    {a.result ? 'Sửa báo cáo' : 'Gửi báo cáo'}
                  </Button>
                )
              }
            >
              <Descriptions
                size="small"
                column={{ xs: 1, md: 2 }}
                items={[
                  { key: 'period', label: 'Thời gian', children: formatDateRange(a.startDate, a.dueDate) },
                  { key: 'status', label: 'Trạng thái', children: <TestStatusTag assignment={a} /> },
                  ...(a.instructions
                    ? [{ key: 'how', label: 'Cách kiểm thử', span: 2, children: <span style={{ whiteSpace: 'pre-line' }}>{a.instructions}</span> }]
                    : []),
                  ...(a.result
                    ? [
                        { key: 'result', label: 'Kết quả', children: <TestResultTag result={a.result} /> },
                        { key: 'at', label: 'Gửi lúc', children: formatDateTime(a.reportedAtUtc) },
                        ...(a.findings
                          ? [{ key: 'findings', label: 'Lỗi, vấn đề', span: 2, children: <span style={{ whiteSpace: 'pre-line' }}>{a.findings}</span> }]
                          : []),
                        ...(a.suggestions
                          ? [{ key: 'suggestions', label: 'Đề xuất', span: 2, children: <span style={{ whiteSpace: 'pre-line' }}>{a.suggestions}</span> }]
                          : []),
                      ]
                    : []),
                  ...(a.reviewNote
                    ? [{ key: 'review', label: 'Hướng xử lý', span: 2, children: `${a.reviewNote} (${a.reviewedBy ?? ''})` }]
                    : []),
                ]}
              />
              {a.status === 'Upcoming' && (
                <Typography.Text type="secondary">Gửi báo cáo được từ ngày bắt đầu kiểm thử.</Typography.Text>
              )}
            </Card>
          ))}
        </Flex>
      )}
      {reporting && <TestReportModal assignment={reporting} onClose={() => setReporting(null)} />}
    </>
  )
}
