import type { AppVersion } from '../../../api/version'

const COMMIT = /^[0-9a-f]{40}$/
const BUILT_AT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/

/**
 * Phiên bản giao diện đang chạy trên trình duyệt này (quyết định 67). Quy trình đóng gói truyền số gói, mã commit, thời điểm đóng
 * gói vào lúc build (biến VITE_APP_PACKAGE, VITE_APP_COMMIT, VITE_APP_BUILT_AT), cùng giá trị với version.json của API trong
 * cùng gói. Bản phát triển không có các biến này: các giá trị là `null`.
 */
export function webVersion(): AppVersion {
  const pkg = Number(import.meta.env.VITE_APP_PACKAGE)
  const commit = import.meta.env.VITE_APP_COMMIT
  const builtAt = import.meta.env.VITE_APP_BUILT_AT
  return {
    package: Number.isInteger(pkg) && pkg > 0 ? pkg : null,
    commit: commit && COMMIT.test(commit) ? commit : null,
    builtAtUtc: builtAt && BUILT_AT.test(builtAt) && !Number.isNaN(Date.parse(builtAt)) ? builtAt : null,
  }
}

/**
 * Giao diện trên trình duyệt này so với máy chủ:
 * - `same`: cùng gói;
 * - `webOlder`: giao diện cũ hơn (chưa nhấn Ctrl+F5, hoặc thư mục ConsentWeb chưa được chép gói mới);
 * - `serverOlder`: máy chủ cũ hơn (thư mục ConsentApi chưa được chép gói mới, hoặc API chưa khởi động lại);
 * - `different`: cùng số gói nhưng khác mã commit;
 * - `serverUnknown`: máy chủ chưa ghi phiên bản (bản cài từ gói cũ);
 * - `webUnknown`: giao diện là bản phát triển, không so được.
 */
export type VersionMatch = 'same' | 'webOlder' | 'serverOlder' | 'different' | 'serverUnknown' | 'webUnknown'

export function compareVersions(web: AppVersion, server: AppVersion): VersionMatch {
  if (web.package === null) {
    return 'webUnknown'
  }
  if (server.package === null) {
    return 'serverUnknown'
  }
  if (web.package !== server.package) {
    return web.package < server.package ? 'webOlder' : 'serverOlder'
  }
  return web.commit && server.commit && web.commit !== server.commit ? 'different' : 'same'
}
