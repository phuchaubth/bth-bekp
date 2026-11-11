import { Card, Result, Typography } from 'antd'

interface PagePlaceholderProps {
  title: string
  sprint?: number
}

/** Trang tạm cho các chức năng chưa phát triển. */
export function PagePlaceholder({ title, sprint }: PagePlaceholderProps) {
  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        {title}
      </Typography.Title>
      <Card>
        <Result
          status="info"
          title="Chức năng đang được phát triển"
          subTitle={sprint ? `Dự kiến hoàn thành trong Sprint ${sprint}.` : undefined}
        />
      </Card>
    </>
  )
}
