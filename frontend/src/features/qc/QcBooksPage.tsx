import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Flex, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { qcApi, qcKeys, useQcBooks, type QcBookSummary } from '../../api/qc'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { QcStatusTag } from './qcTags'

const MONTH = 'YYYY-MM'

/**
 * Sổ nội kiểm đường huyết tại giường của một tháng (quyết định 57): khoa thấy máy của khoa mình, Khoa HSH thấy mọi khoa. Máy
 * chưa mở sổ tháng này thì bấm "Mở sổ".
 */
export function QcBooksPage() {
  const [params, setParams] = useSearchParams()
  const month = params.get('thang') ?? dayjs().format(MONTH)
  const departmentId = params.get('khoa') ?? undefined
  const books = useQcBooks(month, departmentId)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const open = useMutation({
    mutationFn: (meterId: string) => qcApi.open(meterId, month),
    onSuccess: (book) => {
      queryClient.setQueryData(qcKeys.book(book.id), book)
      void queryClient.invalidateQueries({ queryKey: qcKeys.books })
      void navigate(`/noi-kiem/so/${book.id}`)
    },
    onError: (error) => void message.error(errorMessage(error)),
  })

  const setParam = (key: string, value: string | undefined) => {
    const next = new URLSearchParams(params)
    if (value) {
      next.set(key, value)
    } else {
      next.delete(key)
    }
    setParams(next, { replace: true })
  }

  const items = books.data?.items ?? []
  const departments = [...new Map(items.map((i) => [i.departmentId, i.departmentName])).entries()].map(([value, label]) => ({ value, label }))

  const columns: TableColumnsType<QcBookSummary> = [
    { title: 'Khoa', dataIndex: 'departmentName' },
    { title: 'Máy (loại máy, số seri)', dataIndex: 'meterLabel' },
    {
      title: 'Trạng thái',
      render: (_, row) => (row.status ? <QcStatusTag status={row.status} /> : <Tag>Chưa mở sổ</Tag>),
    },
    { title: 'Ngày đã ghi', dataIndex: 'recordedDays', align: 'right' },
    {
      title: 'Ngày không đạt',
      align: 'right',
      render: (_, row) => (row.failedDays > 0 ? <Typography.Text type="danger">{row.failedDays}</Typography.Text> : 0),
    },
    { title: 'Ngày Khoa HSH đã xem xét', dataIndex: 'reviewedDays', align: 'right' },
    {
      title: 'Chờ xem xét',
      align: 'right',
      render: (_, row) => (row.daysToReview > 0 ? <Typography.Text type="warning">{row.daysToReview}</Typography.Text> : 0),
    },
    {
      title: '',
      render: (_, row) =>
        row.bookId ? (
          <Link to={`/noi-kiem/so/${row.bookId}`}>Xem sổ</Link>
        ) : row.canOpen ? (
          <Button size="small" loading={open.isPending && open.variables === row.meterId} onClick={() => open.mutate(row.meterId)}>
            Mở sổ
          </Button>
        ) : null,
    },
  ]

  return (
    <>
      <PageHeader title="Sổ nội kiểm đường huyết tại giường" />
      <Card>
        <Flex gap={12} wrap style={{ marginBottom: 16 }}>
          <DatePicker
            picker="month"
            format="MM/YYYY"
            allowClear={false}
            value={dayjs(`${month}-01`)}
            disabledDate={(d) => d.isAfter(dayjs(), 'month')}
            onChange={(value) => value && setParam('thang', value.format(MONTH))}
            aria-label="Tháng"
          />
          {departments.length > 1 || departmentId ? (
            <Select
              allowClear
              placeholder="Mọi khoa"
              style={{ minWidth: 240 }}
              options={departments}
              value={departmentId}
              onChange={(value: string | undefined) => setParam('khoa', value)}
              aria-label="Khoa"
            />
          ) : null}
        </Flex>
        {books.isError ? <Alert type="error" showIcon title={errorMessage(books.error)} style={{ marginBottom: 16 }} /> : null}
        <Table
          rowKey={(row) => row.bookId ?? row.meterId}
          columns={columns}
          dataSource={items}
          loading={books.isFetching}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'Chưa có máy đo nào. Khoa HSH khai báo máy đo của khoa ở mục "Máy đo và lô vật tư".' }}
        />
      </Card>
    </>
  )
}
