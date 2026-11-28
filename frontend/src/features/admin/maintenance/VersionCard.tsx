import { Alert, Card, Descriptions, Flex, Typography } from 'antd'
import { useServerVersion, type AppVersion } from '../../../api/version'
import { errorMessage } from '../../../utils/formErrors'
import { formatDateTime } from '../../../utils/format'
import { compareVersions, webVersion } from './appVersion'

/** "Gói 97 · mã 1a2b3c4 · đóng gói 28/11/2026 14:05"; không rõ số gói thì hiện `unknown`. */
function VersionText({ version, unknown }: { version: AppVersion; unknown: string }) {
  if (version.package === null) {
    return <Typography.Text type="secondary">{unknown}</Typography.Text>
  }
  return (
    <span>
      <Typography.Text strong>Gói {version.package}</Typography.Text>
      {version.commit && (
        <>
          {' · mã '}
          <Typography.Text code>{version.commit.slice(0, 7)}</Typography.Text>
        </>
      )}
      {version.builtAtUtc && ` · đóng gói ${formatDateTime(version.builtAtUtc)}`}
    </span>
  )
}

function MatchAlert({ web, server }: { web: AppVersion; server: AppVersion }) {
  switch (compareVersions(web, server)) {
    case 'same':
      return <Alert type="success" showIcon title={`Giao diện và máy chủ cùng gói ${server.package}.`} />
    case 'webOlder':
      return (
        <Alert
          type="warning"
          showIcon
          title={`Trình duyệt này đang chạy giao diện gói ${web.package}, cũ hơn máy chủ (gói ${server.package}).`}
          description="Nhấn Ctrl+F5 để tải lại giao diện. Tải lại rồi vẫn thấy dòng này: thư mục ConsentWeb trên máy chủ chưa được chép gói mới."
        />
      )
    case 'serverOlder':
      return (
        <Alert
          type="warning"
          showIcon
          title={`Máy chủ (API) đang chạy gói ${server.package}, cũ hơn giao diện (gói ${web.package}).`}
          description="Thư mục ConsentApi trên máy chủ chưa được chép gói mới, hoặc API chưa khởi động lại sau khi chép."
        />
      )
    case 'different':
      return (
        <Alert
          type="warning"
          showIcon
          title={`Giao diện và máy chủ cùng gói ${server.package} nhưng khác mã commit.`}
          description="Chép lại cả hai thư mục ConsentApi và ConsentWeb từ cùng một gói."
        />
      )
    case 'serverUnknown':
      return (
        <Alert
          type="warning"
          showIcon
          title="Máy chủ (API) chưa có thông tin phiên bản."
          description={`Thư mục ConsentApi là bản cài từ gói cũ (trước khi gói ghi phiên bản) hoặc thiếu file version.json, trong khi giao diện đã là gói ${web.package}. Chép thư mục ConsentApi của gói ${web.package} rồi khởi động lại API.`}
        />
      )
    case 'webUnknown':
      return <Alert type="info" showIcon title="Giao diện là bản phát triển (không build từ gói cài đặt), không so được với máy chủ." />
  }
}

/**
 * Phiên bản đã cài (quyết định 67): phiên bản API đang chạy và giao diện đang mở trên trình duyệt này, báo khi hai bên lệch nhau.
 * So với gói mới tải về thì chạy xem-phien-ban.ps1 trên máy chủ (hướng dẫn triển khai, mục 5.17).
 */
export function VersionCard() {
  const server = useServerVersion()
  const web = webVersion()
  return (
    <Card title="Phiên bản đã cài" loading={server.isPending}>
      <Flex vertical gap={12}>
        {server.isError && <Alert type="error" showIcon title={`Không đọc được phiên bản máy chủ: ${errorMessage(server.error)}`} />}
        <Descriptions
          size="small"
          column={1}
          bordered
          items={[
            {
              key: 'server',
              label: 'Máy chủ (API)',
              children: server.data ? <VersionText version={server.data} unknown="Chưa rõ" /> : '—',
            },
            { key: 'web', label: 'Giao diện trên trình duyệt này', children: <VersionText version={web} unknown="Bản phát triển" /> },
          ]}
        />
        {server.data && <MatchAlert web={web} server={server.data} />}
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          Số gói là số lần chạy của quy trình đóng gói trên GitHub Actions (hướng dẫn cài ghi "Cài gói lần …"). Muốn biết gói mới tải
          về mới hơn hay cũ hơn bản đang cài: giải nén gói trên máy chủ rồi chạy{' '}
          <Typography.Text code style={{ whiteSpace: 'nowrap' }}>
            xem-phien-ban.ps1
          </Typography.Text>{' '}
          trong thư mục gói (hướng dẫn triển khai, mục 5.17).
        </Typography.Paragraph>
      </Flex>
    </Card>
  )
}
