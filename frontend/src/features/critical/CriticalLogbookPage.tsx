import { FilePdfOutlined } from '@ant-design/icons'
import { Alert, Button, Card, DatePicker, Flex, Form, Radio, Select, Spin, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { useDepartments } from '../../api/admin'
import { accountDepartments } from '../../api/auth'
import { CRITICAL_FORM_LABELS, criticalApi, criticalKeys, useCriticalLogbook, type CriticalLogbookForm } from '../../api/critical'
import { useAuth } from '../../auth/authContext'
import { Permissions } from '../../auth/permissions'
import { Busy } from '../../components/DashboardCards'
import { PageHeader } from '../../components/PageHeader'
import { errorMessage } from '../../utils/formErrors'
import { PdfModal } from '../nursing/NursingReportFields'
import { CriticalSectionNav } from './CriticalParts'

const MONTH = 'YYYY-MM'
const MAX_MONTHS_BACK = 24
const FORMS: CriticalLogbookForm[] = ['Laboratory', 'Clinical', 'Microbiology']
const FILE_PREFIX: Record<CriticalLogbookForm, string> = { Laboratory: 'so-ST01', Clinical: 'so-ST02', Microbiology: 'so-vi-sinh' }
const TITLES: Record<CriticalLogbookForm, [string, string]> = {
  Laboratory: ['SỔ THEO DÕI THÔNG BÁO KẾT QUẢ NGUY CẤP', 'KHOA CẬN LÂM SÀNG'],
  Clinical: ['SỔ THEO DÕI THÔNG BÁO KẾT QUẢ NGUY CẤP', 'KHOA ĐIỀU TRỊ'],
  Microbiology: ['SỔ THÔNG BÁO KẾT QUẢ NGUY CẤP QUA ĐIỆN THOẠI', 'KHOA VI SINH'],
}

const cell = { border: '1px solid #c9d5d6', padding: '4px 6px', verticalAlign: 'top' as const, fontSize: 13 }

/**
 * Sổ kết quả nguy cấp điện tử (quyết định 60): ST01 của khoa cận lâm sàng, ST02 của khoa điều trị, sổ Khoa Vi sinh; một khoa
 * một tháng, đúng các cột của sổ giấy. Mỗi dòng lấy từ hồ sơ điện tử, không nhập lại; hồ sơ đã hủy gạch ngang kèm lý do.
 */
export function CriticalLogbookPage() {
  const { user, hasPermission } = useAuth()
  const [params, setParams] = useSearchParams()
  const monitor = hasPermission(Permissions.CriticalMonitor)
  const records = hasPermission(Permissions.CriticalRecord)
  const receives = hasPermission(Permissions.CriticalReceive) || hasPermission(Permissions.CriticalTreat)
  const forms = FORMS.filter((f) => monitor || (f === 'Clinical' ? receives : records))
  const own = accountDepartments(user)
  const departments = useDepartments(true)
  const departmentOptions = monitor || hasPermission(Permissions.ScopeAllDepartments)
    ? (departments.data ?? []).map((d) => ({ value: d.id, label: d.isActive ? d.name : `${d.name} (ngưng)` }))
    : own.map((d) => ({ value: d.id, label: d.name }))

  const rawForm = params.get('mau') as CriticalLogbookForm | null
  const form: CriticalLogbookForm = rawForm && forms.includes(rawForm) ? rawForm : (forms[0] ?? 'Clinical')
  const departmentId = params.get('khoa') ?? own[0]?.id ?? departmentOptions[0]?.value
  const rawMonth = params.get('thang')
  const month = rawMonth && /^\d{4}-\d{2}$/.test(rawMonth) ? rawMonth : dayjs().format(MONTH)
  const book = useCriticalLogbook(form, departmentId, month)
  const data = book.data
  const [pdfOpen, setPdfOpen] = useState(false)

  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) next.set(key, value)
    setParams(next, { replace: true })
  }

  const [title, unit] = TITLES[form]
  const first = dayjs(`${month}-01`)

  return (
    <>
      <CriticalSectionNav />
      <PageHeader
        title="Sổ kết quả nguy cấp"
        extra={
          <Button icon={<FilePdfOutlined />} disabled={!data} onClick={() => setPdfOpen(true)}>
            In PDF (A4 ngang)
          </Button>
        }
      />
      <Typography.Paragraph type="secondary">
        Mỗi dòng lấy từ hồ sơ điện tử, không nhập lại. Khoa xem sổ của khoa mình; QLCL xem mọi khoa. In PDF đúng các cột của sổ giấy.
      </Typography.Paragraph>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Form layout="inline" style={{ rowGap: 8 }}>
          <Form.Item label="Mẫu sổ">
            <Radio.Group
              optionType="button"
              value={form}
              onChange={(e) => update({ mau: e.target.value as string })}
              options={forms.map((f) => ({ value: f, label: CRITICAL_FORM_LABELS[f] }))}
            />
          </Form.Item>
          <Form.Item label="Khoa">
            <Select
              showSearch
              optionFilterProp="label"
              style={{ minWidth: 260 }}
              value={departmentId}
              options={departmentOptions}
              onChange={(value: string) => update({ khoa: value })}
              aria-label="Khoa"
            />
          </Form.Item>
          <Form.Item label="Tháng">
            <DatePicker
              picker="month"
              format="MM/YYYY"
              allowClear={false}
              value={first}
              disabledDate={(d) => d.isAfter(dayjs(), 'month') || d.isBefore(dayjs().subtract(MAX_MONTHS_BACK, 'month'), 'month')}
              onChange={(value) => value && update({ thang: value.format(MONTH) })}
              aria-label="Tháng"
            />
          </Form.Item>
        </Form>
      </Card>
      {book.isError && <Alert type="error" showIcon title={errorMessage(book.error)} style={{ marginBottom: 16 }} />}
      {!data ? (
        book.isLoading && <Spin style={{ display: 'block', padding: 48 }} />
      ) : (
        <Busy busy={book.isFetching}>
          <Card size="small">
            <Flex justify="space-between" wrap gap={8}>
              <Typography.Text type="secondary">Tên đơn vị và logo lấy từ Cấu hình hệ thống khi in PDF.</Typography.Text>
              <Typography.Text>
                Mã số: <Typography.Text strong>{data.code}</Typography.Text>
              </Typography.Text>
            </Flex>
            <div style={{ textAlign: 'center', margin: '12px 0' }}>
              <Typography.Title level={4} style={{ margin: 0 }}>
                {title}
              </Typography.Title>
              <Typography.Text strong>{unit}</Typography.Text>
            </div>
            <Flex justify="space-between" wrap gap={8} style={{ marginBottom: 8 }}>
              <span>
                Khoa/ Department: <Typography.Text strong>{data.departmentName}</Typography.Text>
              </span>
              <span>
                Từ {first.format('DD/MM/YYYY')} đến {first.endOf('month').format('DD/MM/YYYY')}
              </span>
            </Flex>
            <div style={{ overflowX: 'auto' }}>
              <table style={{ borderCollapse: 'collapse', width: '100%', minWidth: 960 }} aria-label={`${title} – ${data.departmentName}`}>
                <thead>
                  <tr style={{ background: '#eef2f2' }}>
                    {data.columns.map((column) => (
                      <th key={column} style={{ ...cell, fontWeight: 600, textAlign: 'center' }}>
                        {column}
                      </th>
                    ))}
                  </tr>
                  <tr>
                    {data.columns.map((column, i) => (
                      <th key={column} style={{ ...cell, fontSize: 11, fontWeight: 400, textAlign: 'center' }}>
                        {i + 1}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {data.rows.length === 0 && (
                    <tr>
                      <td colSpan={data.columns.length} style={{ ...cell, textAlign: 'center', color: '#55706d' }}>
                        Không có hồ sơ trong kỳ này.
                      </td>
                    </tr>
                  )}
                  {data.rows.flatMap((row, index) => {
                    const struck = row.cancelled ? { textDecoration: 'line-through', color: '#808080' } : {}
                    const lines = [
                      <tr key={`r${index}`} data-cancelled={row.cancelled || undefined}>
                        {row.cells.map((text, i) => (
                          <td key={i} style={{ ...cell, ...struck, whiteSpace: 'pre-line' }}>
                            {text}
                          </td>
                        ))}
                      </tr>,
                    ]
                    if (row.note) {
                      lines.push(
                        <tr key={`n${index}`}>
                          <td colSpan={data.columns.length} style={{ ...cell, fontSize: 12, color: '#55706d' }}>
                            <strong>Ghi chú:</strong> <em>{row.note}</em>
                          </td>
                        </tr>,
                      )
                    }
                    return lines
                  })}
                </tbody>
              </table>
            </div>
            <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 8, marginBottom: 0 }}>
              {data.rows.length} dòng · Lưu hành nội bộ
            </Typography.Paragraph>
          </Card>
        </Busy>
      )}
      {departmentId && (
        <PdfModal
          open={pdfOpen}
          title={`${title} – ${data?.departmentName ?? ''} – ${first.format('MM/YYYY')}`}
          fileName={`ket-qua-nguy-cap-${FILE_PREFIX[form]}-${month}.pdf`}
          queryKey={[...criticalKeys.logbook(form, departmentId, month), data?.rows.length ?? 0]}
          load={() => criticalApi.logbookPdf(form, departmentId, month)}
          onClose={() => setPdfOpen(false)}
        />
      )}
    </>
  )
}
