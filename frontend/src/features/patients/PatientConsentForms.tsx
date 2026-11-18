import { FileAddOutlined } from '@ant-design/icons'
import { Button, Card, Table, Typography } from 'antd'
import { Link, useNavigate } from 'react-router'
import { useConsentForms, type ConsentFormListItem, type ConsentFormStatus } from '../../api/consentForms'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { formatDateTime } from '../../utils/format'
import { FormStatusTag } from '../consents/FormStatusTag'

/** Các phiếu của người bệnh (trong phạm vi khoa của người xem) và nút lập phiếu mới. */
export function PatientConsentForms({ patientId }: { patientId: string }) {
  const { hasPermission } = useAuth()
  const navigate = useNavigate()
  const canRead = hasPermission(Permissions.ConsentsRead)
  const { data, isPending } = useConsentForms({ patientId, page: 1, pageSize: 20 }, canRead)

  return (
    <Card
      title="Phiếu đồng thuận"
      extra={
        hasPermission(Permissions.ConsentsCreate) && (
          <Button icon={<FileAddOutlined />} onClick={() => navigate(`/phieu-dong-thuan/tao-moi?patientId=${patientId}`)}>
            Lập phiếu
          </Button>
        )
      }
    >
      {canRead ? (
        <Table<ConsentFormListItem>
          rowKey="id"
          size="small"
          loading={isPending}
          dataSource={data?.items}
          pagination={false}
          scroll={{ x: 640 }}
          locale={{ emptyText: 'Người bệnh chưa có phiếu nào.' }}
          columns={[
            {
              title: 'Số phiếu',
              dataIndex: 'formNo',
              width: 160,
              render: (formNo: string, form) => <Link to={`/phieu-dong-thuan/${form.id}`}>{formNo}</Link>,
            },
            { title: 'Mẫu phiếu', key: 'template', render: (_, form) => `${form.templateCode} – ${form.templateName}` },
            { title: 'Trạng thái', dataIndex: 'status', width: 120, render: (status: ConsentFormStatus) => <FormStatusTag status={status} /> },
            { title: 'Lập lúc', dataIndex: 'createdAtUtc', width: 150, render: (value: string) => formatDateTime(value) },
          ]}
        />
      ) : (
        <Typography.Text type="secondary">Bạn không có quyền xem phiếu.</Typography.Text>
      )}
    </Card>
  )
}
