import { PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Flex, Input, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { usePatients, type PatientListItem, type PatientQuery } from '../../api/patients'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDate, formatDateTime } from '../../utils/format'
import { genderLabels } from './labels'

export function HisTag({ verified }: { verified: boolean }) {
  return verified ? <Tag color="green">Đã đối chiếu HIS</Tag> : <Tag color="orange">Chưa đối chiếu HIS</Tag>
}

/** Màn hình 9: danh sách người bệnh (số CCCD đã được che). */
export function PatientsPage() {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const [query, setQuery] = useState<PatientQuery>({ page: 1, pageSize: 20 })
  const { data, isPending, isFetching, isError, error } = usePatients(query)

  const columns: TableColumnsType<PatientListItem> = [
    {
      title: 'Người bệnh',
      key: 'patient',
      width: 240,
      fixed: 'left',
      render: (_, patient) => (
        <Flex vertical>
          <Link to={`/nguoi-benh/${patient.id}`}>
            <Typography.Text strong>{patient.fullName}</Typography.Text>
          </Link>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {patient.patientCode ?? 'Chưa có mã người bệnh'}
          </Typography.Text>
        </Flex>
      ),
    },
    { title: 'Ngày sinh', dataIndex: 'dateOfBirth', width: 120, render: (value: string) => formatDate(value) },
    { title: 'Giới tính', dataIndex: 'gender', width: 100, render: (value: PatientListItem['gender']) => genderLabels[value] },
    {
      title: 'Số CCCD',
      dataIndex: 'citizenIdMasked',
      width: 150,
      render: (value: string | null) => <span style={{ fontFamily: 'monospace' }}>{value ?? '—'}</span>,
    },
    { title: 'HIS', dataIndex: 'hisVerified', width: 170, render: (value: boolean) => <HisTag verified={value} /> },
    { title: 'Cập nhật', dataIndex: 'updatedAtUtc', width: 160, render: (value: string) => formatDateTime(value) },
  ]

  return (
    <>
      <PageHeader
        title="Người bệnh"
        extra={
          hasPermission(Permissions.PatientsManage) && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/nguoi-benh/tiep-nhan')}>
              Tiếp nhận người bệnh
            </Button>
          )
        }
      />
      <Card>
        <Input.Search
          placeholder="Tìm theo họ tên (không dấu cũng được), mã người bệnh hoặc số CCCD đủ 12 số"
          allowClear
          style={{ maxWidth: 520, marginBottom: 16 }}
          onSearch={(keyword) => setQuery((q) => ({ ...q, keyword: keyword.trim() || undefined, page: 1 }))}
        />
        {isError && <Alert type="error" showIcon title={errorMessage(error)} style={{ marginBottom: 16 }} />}
        <Table<PatientListItem>
          rowKey="id"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={isPending || isFetching}
          scroll={{ x: 940 }}
          locale={{ emptyText: 'Không có người bệnh phù hợp.' }}
          pagination={{
            current: query.page,
            pageSize: query.pageSize,
            total: data?.totalCount ?? 0,
            showSizeChanger: true,
            showTotal: (total) => `Tổng ${total} người bệnh`,
            onChange: (page, pageSize) => setQuery((q) => ({ ...q, page, pageSize })),
          }}
        />
      </Card>
    </>
  )
}
