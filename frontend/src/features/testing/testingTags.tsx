import {
  CheckCircleOutlined,
  ClockCircleOutlined,
  CloseCircleOutlined,
  ExclamationCircleOutlined,
  StopOutlined,
  SyncOutlined,
  WarningOutlined,
} from '@ant-design/icons'
import { Space, Tag } from 'antd'
import type { ReactNode } from 'react'
import {
  TEST_RESULT_LABELS,
  TEST_STATUS_LABELS,
  type TestAssignment,
  type TestAssignmentStatus,
  type TestResult,
} from '../../api/testing'

const STATUS_STYLE: Record<TestAssignmentStatus, { color: string; icon: ReactNode }> = {
  Upcoming: { color: 'default', icon: <ClockCircleOutlined /> },
  InProgress: { color: 'processing', icon: <SyncOutlined /> },
  Overdue: { color: 'error', icon: <ExclamationCircleOutlined /> },
  Reported: { color: 'success', icon: <CheckCircleOutlined /> },
  Cancelled: { color: 'default', icon: <StopOutlined /> },
}

const RESULT_STYLE: Record<TestResult, { color: string; icon: ReactNode }> = {
  Passed: { color: 'success', icon: <CheckCircleOutlined /> },
  PartiallyPassed: { color: 'warning', icon: <WarningOutlined /> },
  Failed: { color: 'error', icon: <CloseCircleOutlined /> },
}

/** Trạng thái phân công (màu luôn kèm biểu tượng và chữ); báo cáo gửi sau hạn có thêm nhãn "Trễ hạn". */
export function TestStatusTag({ assignment }: { assignment: Pick<TestAssignment, 'status' | 'late'> }) {
  const style = STATUS_STYLE[assignment.status]
  return (
    <Space size={4} wrap>
      <Tag color={style.color} icon={style.icon} style={{ marginInlineEnd: 0 }}>
        {TEST_STATUS_LABELS[assignment.status]}
      </Tag>
      {assignment.late && (
        <Tag color="warning" style={{ marginInlineEnd: 0 }}>
          Trễ hạn
        </Tag>
      )}
    </Space>
  )
}

export function TestResultTag({ result }: { result: TestResult | null }) {
  if (!result) {
    return null
  }
  const style = RESULT_STYLE[result]
  return (
    <Tag color={style.color} icon={style.icon} style={{ marginInlineEnd: 0 }}>
      {TEST_RESULT_LABELS[result]}
    </Tag>
  )
}
