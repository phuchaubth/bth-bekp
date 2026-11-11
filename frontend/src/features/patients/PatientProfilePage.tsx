import { DeleteOutlined, EditOutlined, EyeInvisibleOutlined, EyeOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Empty,
  Flex,
  Form,
  Popconfirm,
  Space,
  Spin,
  Table,
  Typography,
  type TableColumnsType,
} from 'antd'
import { useState } from 'react'
import { useParams } from 'react-router'
import { patientKeys, patientsApi, usePatient, type PatientDetail, type Representative } from '../../api/patients'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { applyServerFieldErrors, errorMessage } from '../../utils/formErrors'
import { formatDate, formatDateTime } from '../../utils/format'
import { maskCitizenId, maskInsurance, maskPhone } from './cccd'
import { ageFrom, genderLabels, relationshipLabels } from './labels'
import { PatientForm } from './PatientForm'
import { formValuesToInput, patientToFormValues, type PatientFormValues } from './patientFormValues'
import { HisTag } from './PatientsPage'
import { RepresentativeFormModal } from './RepresentativeFormModal'

/** Giá trị nhạy cảm: mặc định che, bấm để xem đầy đủ (tránh lộ khi người khác nhìn màn hình). */
function SensitiveValue({ value, mask, label }: { value: string | null; mask: (v: string | null) => string; label: string }) {
  const [revealed, setRevealed] = useState(false)
  if (!value) {
    return <>—</>
  }
  return (
    <Space size={4}>
      <span style={{ fontFamily: 'monospace' }}>{revealed ? value : mask(value)}</span>
      <Button
        type="text"
        size="small"
        icon={revealed ? <EyeInvisibleOutlined /> : <EyeOutlined />}
        aria-label={revealed ? `Ẩn ${label}` : `Hiện ${label}`}
        onClick={() => setRevealed((r) => !r)}
      />
    </Space>
  )
}

function PatientDetails({ patient }: { patient: PatientDetail }) {
  return (
    <Descriptions column={{ xs: 1, md: 2 }} size="small" bordered>
      <Descriptions.Item label="Mã người bệnh">{patient.patientCode ?? '—'}</Descriptions.Item>
      <Descriptions.Item label="Đối chiếu HIS">
        <HisTag verified={patient.hisVerified} />
        {patient.hisVerifiedAtUtc && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {formatDateTime(patient.hisVerifiedAtUtc)}
          </Typography.Text>
        )}
      </Descriptions.Item>
      <Descriptions.Item label="Ngày sinh">
        {formatDate(patient.dateOfBirth)} ({ageFrom(patient.dateOfBirth)} tuổi)
      </Descriptions.Item>
      <Descriptions.Item label="Giới tính">{genderLabels[patient.gender]}</Descriptions.Item>
      <Descriptions.Item label="Số CCCD">
        <SensitiveValue value={patient.citizenId} mask={maskCitizenId} label="số CCCD" />
      </Descriptions.Item>
      <Descriptions.Item label="Ngày cấp">{formatDate(patient.citizenIdIssueDate)}</Descriptions.Item>
      <Descriptions.Item label="Số CMND cũ">
        <SensitiveValue value={patient.oldIdNumber} mask={maskCitizenId} label="số CMND cũ" />
      </Descriptions.Item>
      <Descriptions.Item label="Số điện thoại">
        <SensitiveValue value={patient.phoneNumber} mask={maskPhone} label="số điện thoại" />
      </Descriptions.Item>
      <Descriptions.Item label="Nơi thường trú" span={{ xs: 1, md: 2 }}>
        {patient.permanentAddress ?? '—'}
      </Descriptions.Item>
      <Descriptions.Item label="Địa chỉ hiện tại" span={{ xs: 1, md: 2 }}>
        {patient.currentAddress ?? patient.permanentAddress ?? '—'}
      </Descriptions.Item>
      <Descriptions.Item label="Dân tộc">{patient.ethnicity ?? '—'}</Descriptions.Item>
      <Descriptions.Item label="Quốc tịch">{patient.nationality ?? '—'}</Descriptions.Item>
      <Descriptions.Item label="Nghề nghiệp">{patient.occupation ?? '—'}</Descriptions.Item>
      <Descriptions.Item label="Mã thẻ BHYT">
        <SensitiveValue value={patient.healthInsuranceNumber} mask={maskInsurance} label="mã thẻ BHYT" />
      </Descriptions.Item>
    </Descriptions>
  )
}

function EditPatientCard({ patient, onDone }: { patient: PatientDetail; onDone: () => void }) {
  const [form] = Form.useForm<PatientFormValues>()
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const save = useMutation({
    mutationFn: (values: PatientFormValues) => patientsApi.update(patient.id, formValuesToInput(values)),
    onSuccess: async (updated) => {
      queryClient.setQueryData(patientKeys.detail(patient.id), updated)
      await queryClient.invalidateQueries({ queryKey: patientKeys.all })
      message.success('Đã cập nhật hồ sơ người bệnh.')
      onDone()
    },
    onError: (error) => {
      if (!applyServerFieldErrors(form, error)) {
        message.error(errorMessage(error))
      }
    },
  })

  return (
    <Card
      title="Sửa thông tin người bệnh"
      extra={
        <Space>
          <Button onClick={onDone}>Hủy</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
            Lưu thay đổi
          </Button>
        </Space>
      }
    >
      <PatientForm form={form} initialValues={patientToFormValues(patient)} onFinish={(values) => save.mutate(values)} />
    </Card>
  )
}

function RepresentativesCard({ patient, canManage }: { patient: PatientDetail; canManage: boolean }) {
  const [editing, setEditing] = useState<Representative | 'new' | null>(null)
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const remove = useMutation({
    mutationFn: (representativeId: string) => patientsApi.removeRepresentative(patient.id, representativeId),
    onSuccess: (updated) => {
      queryClient.setQueryData(patientKeys.detail(patient.id), updated)
      message.success('Đã xóa người đại diện.')
    },
    onError: (e) => message.error(errorMessage(e)),
  })

  const columns: TableColumnsType<Representative> = [
    { title: 'Họ tên', dataIndex: 'fullName' },
    { title: 'Quan hệ', dataIndex: 'relationship', render: (value: Representative['relationship']) => relationshipLabels[value] },
    { title: 'Số CCCD', dataIndex: 'citizenId', render: (value: string | null) => <span style={{ fontFamily: 'monospace' }}>{maskCitizenId(value)}</span> },
    { title: 'Số điện thoại', dataIndex: 'phoneNumber', render: (value: string | null) => maskPhone(value) },
  ]
  if (canManage) {
    columns.push({
      title: 'Thao tác',
      key: 'actions',
      width: 170,
      render: (_, representative) => (
        <Space size={0}>
          <Button type="link" icon={<EditOutlined />} onClick={() => setEditing(representative)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xóa người đại diện này?"
            okText="Xóa"
            cancelText="Hủy"
            okButtonProps={{ danger: true }}
            onConfirm={() => remove.mutate(representative.id)}
          >
            <Button type="link" danger icon={<DeleteOutlined />} aria-label={`Xóa ${representative.fullName}`} />
          </Popconfirm>
        </Space>
      ),
    })
  }

  return (
    <Card
      title="Người đại diện / người ký thay"
      extra={
        canManage && (
          <Button icon={<PlusOutlined />} onClick={() => setEditing('new')}>
            Thêm người đại diện
          </Button>
        )
      }
    >
      <Table<Representative>
        rowKey="id"
        columns={columns}
        dataSource={patient.representatives}
        pagination={false}
        scroll={{ x: 640 }}
        locale={{ emptyText: 'Chưa có người đại diện.' }}
      />
      {editing !== null && (
        <RepresentativeFormModal
          patientId={patient.id}
          representative={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
    </Card>
  )
}

/** Màn hình 10: hồ sơ người bệnh. */
export function PatientProfilePage() {
  const { id = '' } = useParams()
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.PatientsManage)
  const { data: patient, isPending, isError, error } = usePatient(id)
  const [editing, setEditing] = useState(false)

  if (isPending) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }

  if (isError || !patient) {
    return <Alert type="error" showIcon title={errorMessage(error)} />
  }

  return (
    <>
      <PageHeader
        title={patient.fullName}
        extra={
          canManage &&
          !editing && (
            <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>
              Sửa thông tin
            </Button>
          )
        }
      />
      <Flex vertical gap={16}>
        {editing ? (
          <EditPatientCard patient={patient} onDone={() => setEditing(false)} />
        ) : (
          <Card title="Thông tin nhân thân">
            <PatientDetails patient={patient} />
          </Card>
        )}
        <RepresentativesCard patient={patient} canManage={canManage} />
        <Card title="Phiếu đồng thuận">
          <Empty description="Các phiếu của người bệnh sẽ hiển thị ở đây (Sprint 3)." />
        </Card>
      </Flex>
    </>
  )
}
