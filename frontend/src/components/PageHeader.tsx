import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'

/** Tiêu đề trang kèm nút thao tác chính (tự xuống dòng trên màn hình hẹp). */
export function PageHeader({ title, extra }: { title: string; extra?: ReactNode }) {
  return (
    <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 16 }}>
      <Typography.Title level={3} style={{ margin: 0 }}>
        {title}
      </Typography.Title>
      {extra}
    </Flex>
  )
}
