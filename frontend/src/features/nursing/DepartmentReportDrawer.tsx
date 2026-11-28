import { FilePdfOutlined, RollbackOutlined } from '@ant-design/icons'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Drawer, Flex, Form, Input, Modal, Spin, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { nursingKeys, nursingReportsApi, useNursingReport, useNursingReportForm } from '../../api/nursingReports'
import { errorMessage } from '../../utils/formErrors'
import { formatDateTime } from '../../utils/format'
import { NursingReportFields, PdfModal, ReportStatus } from './NursingReportFields'
import type { NursingReportFormValues } from './nursingReportUtils'

/**
 * Khoa đang xem báo cáo trên Dashboard (bấm mã khoa ở mục A của bảng tổng hợp hoặc ở bảng theo khoa), kèm ngày của bảng đã bấm:
 * bảng đang hiện số cũ trong lúc tải ngày mới vẫn mở đúng báo cáo của ngày đang hiện.
 */
export interface OpenedDepartment {
  departmentId: string
  departmentName: string
  reportDate: string
}

/**
 * Mã khoa (tên viết tắt) bấm được để mở báo cáo của khoa; rê chuột thấy tên khoa đầy đủ, trình đọc màn hình đọc cả mã và tên.
 * Dùng ở mục A của bảng tổng hợp và bảng theo khoa của Dashboard (quyết định 55, 58).
 */
export function DepartmentCodeLink({ code, name, onClick }: { code: string; name: string; onClick: () => void }) {
  return (
    <Tooltip title={name}>
      <Button
        type="link"
        aria-label={`${code} – ${name}`}
        style={{ padding: 0, height: 'auto', alignSelf: 'flex-start', textAlign: 'start', whiteSpace: 'normal' }}
        onClick={onClick}
      >
        {code}
      </Button>
    </Tooltip>
  )
}

/**
 * Báo cáo của một khoa trong một ngày (chỉ đọc): in PDF, Phòng Điều dưỡng trả lại khoa để sửa khi cần. Mở từ bảng tổng hợp và
 * bảng theo khoa của Dashboard (quyết định 55, 58); nút "Trả lại khoa" chỉ có khi máy chủ cho phép (báo cáo đã gửi, người dùng có
 * quyền quản lý).
 */
export function DepartmentReportDrawer({
  departmentId,
  departmentName,
  reportDate,
  onClose,
}: {
  departmentId: string
  departmentName: string
  reportDate: string
  onClose: () => void
}) {
  const definition = useNursingReportForm()
  const report = useNursingReport(reportDate, departmentId)
  const [form] = Form.useForm<NursingReportFormValues>()
  const [pdfOpen, setPdfOpen] = useState(false)
  const [reopenOpen, setReopenOpen] = useState(false)
  const data = report.data

  return (
    <Drawer
      open
      onClose={onClose}
      size={Math.min(960, window.innerWidth)}
      title={`${departmentName} – ngày ${dayjs(reportDate).format('DD/MM/YYYY')}`}
      extra={
        data?.id && (
          <Flex gap={8}>
            {data.canReopen && (
              <Button icon={<RollbackOutlined />} onClick={() => setReopenOpen(true)}>
                Trả lại khoa
              </Button>
            )}
            <Button icon={<FilePdfOutlined />} onClick={() => setPdfOpen(true)}>
              In PDF
            </Button>
          </Flex>
        )
      }
    >
      {report.isError || definition.isError ? (
        <Alert type="error" showIcon title={errorMessage(report.error ?? definition.error)} />
      ) : !data || !definition.data ? (
        <Spin style={{ display: 'block', padding: 48 }} />
      ) : data.status === 'New' ? (
        <Alert type="info" showIcon title="Khoa chưa lập báo cáo ngày này." />
      ) : (
        <>
          <ReportStatus report={data} />
          {data.reopenReason && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title={`Đã trả lại khoa lúc ${data.reopenedAtUtc ? formatDateTime(data.reopenedAtUtc) : ''}${data.reopenedBy ? ` bởi ${data.reopenedBy}` : ''}`}
              description={`Lý do: ${data.reopenReason}`}
            />
          )}
          <NursingReportFields definition={definition.data} report={data} form={form} editable={false} />
        </>
      )}
      {data?.id && (
        <PdfModal
          open={pdfOpen}
          title={`Báo cáo điều dưỡng ${departmentName}`}
          fileName={`bao-cao-dieu-duong-${reportDate}.pdf`}
          queryKey={[...nursingKeys.daily(reportDate, departmentId), data.updatedAtUtc]}
          load={() => nursingReportsApi.pdf(data.id!)}
          onClose={() => setPdfOpen(false)}
        />
      )}
      {reopenOpen && data?.id && (
        <ReopenModal reportId={data.id} departmentId={departmentId} reportDate={reportDate} onClose={() => setReopenOpen(false)} />
      )}
    </Drawer>
  )
}

/**
 * Trả lại báo cáo cho khoa sửa (bắt buộc ghi lý do). Báo cáo về dạng nháp nên không còn được tính ở bảng tổng hợp và Dashboard:
 * làm mới cả hai (mọi khoảng ngày của Dashboard, vì ngày báo cáo có thể nằm trong nhiều khoảng).
 */
function ReopenModal({ reportId, departmentId, reportDate, onClose }: { reportId: string; departmentId: string; reportDate: string; onClose: () => void }) {
  const [reason, setReason] = useState('')
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const reopen = useMutation({
    mutationFn: () => nursingReportsApi.reopen(reportId, reason.trim()),
    onSuccess: (report) => {
      queryClient.setQueryData(nursingKeys.daily(reportDate, departmentId), report)
      void queryClient.invalidateQueries({ queryKey: nursingKeys.summary(reportDate) })
      void queryClient.invalidateQueries({ queryKey: nursingKeys.dashboards })
      message.success('Đã trả lại báo cáo cho khoa. Khoa sửa và gửi lại.')
      onClose()
    },
    onError: (e) => message.error(errorMessage(e)),
  })
  return (
    <Modal
      open
      title="Trả lại báo cáo cho khoa sửa"
      okText="Trả lại khoa"
      cancelText="Đóng"
      okButtonProps={{ disabled: reason.trim().length === 0, loading: reopen.isPending }}
      onOk={() => reopen.mutate()}
      onCancel={onClose}
    >
      <Typography.Paragraph>Báo cáo trở lại dạng nháp; khoa sửa rồi gửi lại. Lý do được ghi vào nhật ký.</Typography.Paragraph>
      <Input.TextArea
        aria-label="Lý do trả lại"
        placeholder="Lý do trả lại (bắt buộc)"
        value={reason}
        maxLength={500}
        autoSize={{ minRows: 2, maxRows: 6 }}
        onChange={(e) => setReason(e.target.value)}
      />
    </Modal>
  )
}
