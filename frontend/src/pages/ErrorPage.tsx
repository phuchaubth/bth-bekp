import { Button, Result } from 'antd'

/** Hiển thị khi có lỗi không lường trước trong lúc hiển thị trang. */
export function ErrorPage() {
  return (
    <Result
      status="500"
      title="Đã xảy ra lỗi"
      subTitle="Trang không thể hiển thị. Vui lòng tải lại trang hoặc liên hệ bộ phận CNTT nếu lỗi lặp lại."
      extra={
        <Button type="primary" onClick={() => window.location.reload()}>
          Tải lại trang
        </Button>
      }
    />
  )
}
