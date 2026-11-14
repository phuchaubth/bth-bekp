import { CheckCircleOutlined, CopyOutlined, StopOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Form, Input, Popconfirm, Space, Spin, Table, Tabs, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { useParams } from 'react-router'
import type { FormData } from '../../api/consentForms'
import {
  consentTemplatesApi,
  templateKeys,
  useConsentTemplate,
  useTemplateVersion,
  versionStatusLabels,
  type ConsentTemplateDetail,
  type ConsentTemplateVersion,
  type ConsentTemplateVersionSummary,
  type TemplateVersionStatus,
} from '../../api/consentTemplates'
import { ApiError } from '../../api/http'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { DynamicForm } from '../consents/DynamicForm'
import { signerRoleLabel } from './signerRoles'
import { Bilingual } from '../consents/Bilingual'

const statusColors: Record<TemplateVersionStatus, string> = { Draft: 'default', Published: 'green', Retired: 'volcano' }

/** Màn hình 12: phiên bản của mẫu, xem trước, soạn định nghĩa (phiên bản nháp), phát hành. */
export function ConsentTemplateDetailPage() {
  const { id = '' } = useParams()
  const { hasPermission } = useAuth()
  const canManage = hasPermission(Permissions.TemplatesManage)
  const { data: template, isPending, isError, error } = useConsentTemplate(id)
  const [selected, setSelected] = useState(0)
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  // Mặc định xem phiên bản nháp (nếu có), không thì phiên bản mới nhất.
  const versionNo = selected || (template?.versions.find((v) => v.status === 'Draft') ?? template?.versions[0])?.versionNo || 0

  const refresh = async (updated?: ConsentTemplateDetail) => {
    if (updated) {
      queryClient.setQueryData(templateKeys.detail(id), updated)
    }
    await queryClient.invalidateQueries({ queryKey: templateKeys.all })
  }
  const onError = (e: unknown) => message.error(errorMessage(e))

  const createDraft = useMutation({
    mutationFn: () => consentTemplatesApi.createDraft(id),
    onSuccess: async (draft) => {
      await refresh()
      setSelected(draft.versionNo)
      message.success(`Đã tạo phiên bản nháp ${draft.versionNo} từ phiên bản mới nhất.`)
    },
    onError,
  })
  const publish = useMutation({
    mutationFn: (version: number) => consentTemplatesApi.publish(id, version),
    onSuccess: async (updated, version) => {
      await refresh(updated)
      message.success(`Đã phát hành phiên bản ${version}. Phiếu mới sẽ dùng phiên bản này.`)
    },
    onError,
  })
  const retire = useMutation({
    mutationFn: (version: number) => consentTemplatesApi.retire(id, version),
    onSuccess: async (updated) => {
      await refresh(updated)
      message.success('Đã ngừng dùng phiên bản; mẫu không còn dùng được để lập phiếu mới.')
    },
    onError,
  })

  if (isPending) {
    return <Spin style={{ display: 'block', padding: 48 }} />
  }
  if (isError) {
    return <Alert type="error" showIcon title={errorMessage(error)} />
  }

  const hasDraft = template.versions.some((v) => v.status === 'Draft')
  const columns: TableColumnsType<ConsentTemplateVersionSummary> = [
    { title: 'Phiên bản', dataIndex: 'versionNo', width: 100 },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      width: 150,
      render: (status: TemplateVersionStatus) => <Tag color={statusColors[status]}>{versionStatusLabels[status]}</Tag>,
    },
    { title: 'Cập nhật', dataIndex: 'updatedAtUtc', width: 160, render: (value: string) => formatDateTime(value) },
    { title: 'Phát hành', dataIndex: 'publishedAtUtc', width: 160, render: (value: string | null) => formatDateTime(value) },
    {
      title: 'Thao tác',
      key: 'actions',
      render: (_, version) => (
        <Space size={4} wrap>
          <Button size="small" type={version.versionNo === versionNo ? 'primary' : 'default'} onClick={() => setSelected(version.versionNo)}>
            Xem
          </Button>
          {canManage && version.status === 'Draft' && (
            <Popconfirm
              title={`Phát hành phiên bản ${version.versionNo}?`}
              description="Phiên bản đang phát hành (nếu có) sẽ ngừng dùng cho phiếu mới. Phiếu đã lập giữ nguyên."
              okText="Phát hành"
              cancelText="Hủy"
              onConfirm={() => publish.mutate(version.versionNo)}
            >
              <Button size="small" icon={<CheckCircleOutlined />} loading={publish.isPending}>
                Phát hành
              </Button>
            </Popconfirm>
          )}
          {canManage && version.status === 'Published' && (
            <Popconfirm
              title="Ngừng dùng phiên bản này?"
              description="Mẫu sẽ không dùng được để lập phiếu mới cho tới khi phát hành phiên bản khác."
              okText="Ngừng dùng"
              cancelText="Hủy"
              okButtonProps={{ danger: true }}
              onConfirm={() => retire.mutate(version.versionNo)}
            >
              <Button size="small" danger icon={<StopOutlined />} loading={retire.isPending}>
                Ngừng dùng
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title={`${template.code} – ${template.name}`}
        extra={
          canManage && (
            <Button icon={<CopyOutlined />} disabled={hasDraft} loading={createDraft.isPending} onClick={() => createDraft.mutate()}>
              Tạo phiên bản mới
            </Button>
          )
        }
      />
      <Flex vertical gap={16}>
        <Card size="small">
          <Space wrap size={24}>
            <span>
              Xác nhận phía bệnh viện: <Typography.Text strong>{signerRoleLabel(template.hospitalSignerRoleCode)}</Typography.Text>
            </span>
            <span>
              Phạm vi: <Typography.Text strong>{template.departmentId ? 'Một khoa' : 'Toàn viện'}</Typography.Text>
            </span>
            {!template.isActive && <Tag>Ngừng sử dụng</Tag>}
          </Space>
        </Card>
        <Card title="Các phiên bản">
          <Table<ConsentTemplateVersionSummary> rowKey="versionNo" columns={columns} dataSource={template.versions} pagination={false} scroll={{ x: 700 }} />
        </Card>
        {versionNo > 0 && <VersionPanel templateId={id} versionNo={versionNo} canManage={canManage} />}
      </Flex>
    </>
  )
}

function VersionPanel({ templateId, versionNo, canManage }: { templateId: string; versionNo: number; canManage: boolean }) {
  const { data: version, isPending, isError, error } = useTemplateVersion(templateId, versionNo)
  const [previewForm] = Form.useForm<FormData>()

  if (isPending) {
    return <Spin style={{ display: 'block', padding: 24 }} />
  }
  if (isError) {
    return <Alert type="error" showIcon title={errorMessage(error)} />
  }

  const editable = canManage && version.status === 'Draft'
  return (
    <Card title={`Phiên bản ${version.versionNo} · ${versionStatusLabels[version.status]}`}>
      <Tabs
        items={[
          {
            key: 'preview',
            label: 'Xem trước',
            children: (
              <>
                <Typography.Paragraph type="secondary">
                  Thử điền để kiểm tra điều kiện hiển thị và cách tính điểm. Dữ liệu thử không được lưu.
                </Typography.Paragraph>
                <Typography.Title level={4} style={{ textAlign: 'center', whiteSpace: 'pre-line', textTransform: 'uppercase' }}>
                  <Bilingual text={version.definition.title} wholeLines />
                </Typography.Title>
                {version.definition.subtitle && (
                  <Typography.Paragraph italic style={{ textAlign: 'center' }}>
                    <Bilingual text={version.definition.subtitle} wholeLines />
                  </Typography.Paragraph>
                )}
                <DynamicForm key={version.updatedAtUtc} definition={version.definition} form={previewForm} initialValues={{}} />
              </>
            ),
          },
          {
            key: 'definition',
            label: editable ? 'Soạn định nghĩa (JSON)' : 'Định nghĩa (JSON)',
            // key: soạn lại từ đầu khi đổi phiên bản hoặc sau khi lưu (định nghĩa đã được máy chủ chuẩn hóa).
            children: (
              <DefinitionEditor key={`${version.versionNo}-${version.updatedAtUtc}`} version={version} editable={editable} canManage={canManage} />
            ),
          },
        ]}
      />
    </Card>
  )
}

function DefinitionEditor({ version, editable, canManage }: { version: ConsentTemplateVersion; editable: boolean; canManage: boolean }) {
  const [text, setText] = useState(() => JSON.stringify(version.definition, null, 2))
  const [problems, setProblems] = useState<string[]>([])
  const queryClient = useQueryClient()
  const { message } = App.useApp()

  const save = useMutation({
    mutationFn: (definition: unknown) => consentTemplatesApi.updateDraft(version.templateId, version.versionNo, definition),
    onSuccess: async (updated) => {
      queryClient.setQueryData(templateKeys.version(version.templateId, version.versionNo), updated)
      await queryClient.invalidateQueries({ queryKey: templateKeys.detail(version.templateId) })
      setProblems([])
      message.success('Đã lưu phiên bản nháp.')
    },
    onError: (e) => {
      if (e instanceof ApiError && e.fieldErrors?.definition) {
        setProblems(e.fieldErrors.definition)
      }
      message.error(errorMessage(e))
    },
  })

  const onSave = () => {
    try {
      save.mutate(JSON.parse(text) as unknown)
    } catch {
      setProblems(['Nội dung không phải JSON hợp lệ.'])
    }
  }

  return (
    <Flex vertical gap={12}>
      {canManage && !editable && (
        <Alert
          type="info"
          showIcon
          title="Chỉ phiên bản nháp sửa được."
          description="Muốn thay đổi câu chữ hoặc trường dữ liệu, hãy bấm “Tạo phiên bản mới”, sửa rồi phát hành. Phiếu đã lập vẫn giữ phiên bản cũ."
        />
      )}
      {problems.length > 0 && (
        <Alert
          type="error"
          showIcon
          title="Định nghĩa chưa hợp lệ"
          description={
            <ul style={{ margin: 0, paddingLeft: 20 }}>
              {problems.map((p) => (
                <li key={p}>{p}</li>
              ))}
            </ul>
          }
        />
      )}
      <Input.TextArea
        aria-label="Định nghĩa mẫu (JSON)"
        value={text}
        onChange={(e) => setText(e.target.value)}
        readOnly={!editable}
        autoSize={{ minRows: 12, maxRows: 32 }}
        spellCheck={false}
        style={{ fontFamily: 'monospace', fontSize: 12 }}
      />
      {editable && (
        <Button type="primary" loading={save.isPending} onClick={onSave} style={{ alignSelf: 'flex-start' }}>
          Lưu phiên bản nháp
        </Button>
      )}
    </Flex>
  )
}
