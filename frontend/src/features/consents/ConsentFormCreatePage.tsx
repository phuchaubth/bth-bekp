import { FileAddOutlined, UserAddOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Descriptions, Empty, Flex, Input, Spin, Steps, Table, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { consentFormKeys, consentFormsApi } from '../../api/consentForms'
import { useAvailableTemplates } from '../../api/consentTemplates'
import { usePatient, usePatients, type PatientListItem } from '../../api/patients'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDate } from '../../utils/format'
import { genderLabels } from '../patients/labels'

/** Màn hình 4–5 (lập phiếu): chọn người bệnh → chọn mẫu đang phát hành → tạo phiếu nháp rồi chuyển sang soạn nội dung. */
export function ConsentFormCreatePage() {
  const [params] = useSearchParams()
  const [patientId, setPatientId] = useState<string>(params.get('patientId') ?? '')
  const patient = usePatient(patientId)
  const templates = useAvailableTemplates(null)
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const create = useMutation({
    mutationFn: (templateId: string) => consentFormsApi.create({ patientId, templateId }),
    onSuccess: async (form) => {
      await queryClient.invalidateQueries({ queryKey: consentFormKeys.all })
      message.success(`Đã lập phiếu nháp ${form.formNo}.`)
      navigate(`/phieu-dong-thuan/${form.id}`)
    },
    onError: (error) => message.error(errorMessage(error)),
  })

  return (
    <>
      <PageHeader title="Lập phiếu" />
      <Steps
        current={patientId ? 1 : 0}
        items={[{ title: 'Người bệnh' }, { title: 'Mẫu phiếu' }, { title: 'Nội dung và trình ký' }]}
        style={{ marginBottom: 16 }}
      />
      <Flex vertical gap={16}>
        <Card title="1. Người bệnh">
          {patientId ? (
            patient.isPending ? (
              <Spin />
            ) : patient.isError ? (
              <Alert type="error" showIcon title={errorMessage(patient.error)} />
            ) : (
              <Flex justify="space-between" align="start" gap={16} wrap>
                <Descriptions size="small" column={{ xs: 1, md: 3 }} style={{ flex: '1 1 480px' }}>
                  <Descriptions.Item label="Họ và tên">
                    <Typography.Text strong>{patient.data.fullName}</Typography.Text>
                  </Descriptions.Item>
                  <Descriptions.Item label="Ngày sinh">{formatDate(patient.data.dateOfBirth)}</Descriptions.Item>
                  <Descriptions.Item label="Giới tính">{genderLabels[patient.data.gender]}</Descriptions.Item>
                  <Descriptions.Item label="Mã người bệnh">{patient.data.patientCode ?? '—'}</Descriptions.Item>
                </Descriptions>
                <Button onClick={() => setPatientId('')}>Đổi người bệnh</Button>
              </Flex>
            )
          ) : (
            <PatientPicker onPick={setPatientId} />
          )}
        </Card>

        <Card title="2. Mẫu phiếu">
          {!patientId ? (
            <Typography.Text type="secondary">Chọn người bệnh trước.</Typography.Text>
          ) : templates.isPending ? (
            <Spin />
          ) : templates.isError ? (
            <Alert type="error" showIcon title={errorMessage(templates.error)} />
          ) : templates.data.length === 0 ? (
            <Empty description="Chưa có mẫu phiếu nào đang phát hành cho khoa của bạn." />
          ) : (
            <Table
              rowKey="templateId"
              size="small"
              dataSource={templates.data}
              pagination={false}
              showHeader={false}
              scroll={{ x: 520 }}
              columns={[
                {
                  key: 'template',
                  render: (_, template) => (
                    <Flex vertical>
                      <Typography.Text strong>{template.templateName}</Typography.Text>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {template.templateCode} · phiên bản {template.versionNo}
                      </Typography.Text>
                    </Flex>
                  ),
                },
                {
                  key: 'create',
                  width: 140,
                  align: 'right',
                  render: (_, template) => (
                    <Button
                      type="primary"
                      icon={<FileAddOutlined />}
                      loading={create.isPending && create.variables === template.templateId}
                      disabled={create.isPending}
                      onClick={() => create.mutate(template.templateId)}
                      aria-label={`Lập phiếu ${template.templateCode}`}
                    >
                      Lập phiếu
                    </Button>
                  ),
                },
              ]}
            />
          )}
        </Card>
      </Flex>
    </>
  )
}

function PatientPicker({ onPick }: { onPick: (id: string) => void }) {
  const { hasPermission } = useAuth()
  const [keyword, setKeyword] = useState('')
  const results = usePatients({ keyword, page: 1, pageSize: 8 })

  return (
    <Flex vertical gap={12}>
      <Flex gap={12} wrap>
        <Input.Search
          allowClear
          placeholder="Tìm theo họ tên, mã người bệnh hoặc số CCCD"
          onSearch={(value) => setKeyword(value.trim())}
          style={{ width: 380, maxWidth: '100%' }}
        />
        {hasPermission(Permissions.PatientsManage) && (
          <Link to="/nguoi-benh/tiep-nhan">
            <Button icon={<UserAddOutlined />}>Tiếp nhận người bệnh mới</Button>
          </Link>
        )}
      </Flex>
      <Table<PatientListItem>
        rowKey="id"
        size="small"
        loading={results.isFetching}
        dataSource={results.data?.items}
        pagination={false}
        scroll={{ x: 640 }}
        locale={{ emptyText: 'Không có người bệnh phù hợp.' }}
        columns={[
          { title: 'Họ và tên', dataIndex: 'fullName' },
          { title: 'Ngày sinh', dataIndex: 'dateOfBirth', width: 110, render: (value: string) => formatDate(value) },
          { title: 'Số CCCD', dataIndex: 'citizenIdMasked', width: 140, render: (value: string | null) => value ?? '—' },
          {
            title: '',
            key: 'pick',
            width: 90,
            render: (_, patient) => (
              <Button size="small" onClick={() => onPick(patient.id)} aria-label={`Chọn ${patient.fullName}`}>
                Chọn
              </Button>
            ),
          },
        ]}
      />
    </Flex>
  )
}
