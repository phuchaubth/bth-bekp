import { screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { MaintenanceSettings } from '../../../api/maintenance'
import type { AppVersion } from '../../../api/version'
import { adminSession, baseRoutes } from '../../../test/fixtures'
import { mockApi, problem } from '../../../test/mockApi'
import { renderApp } from '../../../test/renderApp'
import { formatDateTime } from '../../../utils/format'
import { compareVersions, webVersion } from './appVersion'

// Quyết định 67: phiên bản đã cài (máy chủ và giao diện) trên trang Bảo trì hệ thống.
const COMMIT_96 = 'ce52acb0123456789abcdef0123456789abcdef0'
const COMMIT_97 = '1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d'
const BUILT_96 = '2026-11-27T09:10:11Z'
const BUILT_97 = '2026-11-28T07:05:09Z'
const v96: AppVersion = { package: 96, commit: COMMIT_96, builtAtUtc: BUILT_96 }
const v97: AppVersion = { package: 97, commit: COMMIT_97, builtAtUtc: BUILT_97 }
const unknown: AppVersion = { package: null, commit: null, builtAtUtc: null }
const off: MaintenanceSettings = { enabled: false, reason: null, expectedEndAtUtc: null, startedAtUtc: null, startedBy: null }

/** Giao diện được build từ gói (quy trình đóng gói truyền các biến này lúc build). */
function buildWeb(version: AppVersion) {
  vi.stubEnv('VITE_APP_PACKAGE', String(version.package))
  vi.stubEnv('VITE_APP_COMMIT', version.commit ?? '')
  vi.stubEnv('VITE_APP_BUILT_AT', version.builtAtUtc ?? '')
}

function openPage(server: AppVersion | (() => Response)) {
  const { calls } = mockApi({
    ...baseRoutes(adminSession()),
    'GET /api/v1/maintenance/settings': off,
    'GET /api/v1/version': typeof server === 'function' ? server : () => server,
  })
  renderApp('/quan-tri/bao-tri')
  return calls
}

const card = async () => (await screen.findByText('Phiên bản đã cài')).closest('.ant-card') as HTMLElement
const row = (container: HTMLElement, label: string) => within(container).getByText(label).closest('tr') as HTMLElement
/** Nội dung các dòng báo (Alert) trong thẻ. */
const alerts = (container: HTMLElement) => within(container).queryAllByRole('alert').map((a) => a.textContent)

afterEach(() => {
  vi.unstubAllEnvs()
  window.history.replaceState(null, '', '/')
})

describe('Phiên bản giao diện (biến lúc build)', () => {
  it('đọc số gói, mã commit, thời điểm đóng gói do quy trình đóng gói truyền vào', () => {
    buildWeb(v97)

    expect(webVersion()).toEqual(v97)
  })

  it('bản phát triển (không có biến) hoặc giá trị sai định dạng thì không rõ', () => {
    expect(webVersion()).toEqual(unknown)

    vi.stubEnv('VITE_APP_PACKAGE', '0')
    vi.stubEnv('VITE_APP_COMMIT', COMMIT_97.toUpperCase())
    vi.stubEnv('VITE_APP_BUILT_AT', '2026-13-45T07:05:09Z')
    expect(webVersion()).toEqual(unknown)

    vi.stubEnv('VITE_APP_PACKAGE', '97.5')
    vi.stubEnv('VITE_APP_COMMIT', COMMIT_97.slice(0, 7))
    vi.stubEnv('VITE_APP_BUILT_AT', '28/11/2026 14:05')
    expect(webVersion()).toEqual(unknown)

    vi.stubEnv('VITE_APP_PACKAGE', '-97')
    expect(webVersion().package).toBeNull()
  })

  it('so giao diện với máy chủ theo số gói', () => {
    expect(compareVersions(v97, v97)).toBe('same')
    expect(compareVersions(v96, v97)).toBe('webOlder')
    expect(compareVersions(v97, v96)).toBe('serverOlder')
    expect(compareVersions({ ...v97, commit: COMMIT_96 }, v97)).toBe('different')
    // Thiếu mã commit một bên: chỉ so số gói.
    expect(compareVersions({ ...v97, commit: null }, v97)).toBe('same')
    expect(compareVersions(v97, unknown)).toBe('serverUnknown')
    expect(compareVersions(unknown, v97)).toBe('webUnknown')
    expect(compareVersions(unknown, unknown)).toBe('webUnknown')
  })
})

describe('Thẻ Phiên bản đã cài (trang Bảo trì hệ thống)', () => {
  it('cùng gói: hiện số gói, mã commit rút gọn, thời điểm đóng gói của cả hai bên và báo cùng gói', async () => {
    buildWeb(v97)
    const calls = openPage(v97)

    const container = await card()
    expect(await within(container).findByText('Giao diện và máy chủ cùng gói 97.')).toBeInTheDocument()
    expect(alerts(container)).toEqual(['Giao diện và máy chủ cùng gói 97.'])
    for (const label of ['Máy chủ (API)', 'Giao diện trên trình duyệt này']) {
      const line = row(container, label)
      expect(line).toHaveTextContent(`Gói 97 · mã 1a2b3c4 · đóng gói ${formatDateTime(BUILT_97)}`)
      expect(within(line).getByText('1a2b3c4').tagName).toBe('CODE')
    }
    expect(container).toHaveTextContent('xem-phien-ban.ps1')
    expect(calls.filter((c) => c.path === '/api/v1/version')).toHaveLength(1)
  })

  it('giao diện cũ hơn máy chủ: nhắc Ctrl+F5, rồi kiểm tra thư mục ConsentWeb', async () => {
    buildWeb(v96)
    openPage(v97)

    const container = await card()
    expect(await within(container).findByText('Trình duyệt này đang chạy giao diện gói 96, cũ hơn máy chủ (gói 97).')).toBeInTheDocument()
    expect(container).toHaveTextContent('Nhấn Ctrl+F5 để tải lại giao diện.')
    expect(container).toHaveTextContent('thư mục ConsentWeb trên máy chủ chưa được chép gói mới')
    expect(row(container, 'Giao diện trên trình duyệt này')).toHaveTextContent(`Gói 96 · mã ce52acb · đóng gói ${formatDateTime(BUILT_96)}`)
    expect(alerts(container)).toHaveLength(1)
  })

  it('máy chủ cũ hơn giao diện: thư mục ConsentApi chưa chép hoặc API chưa khởi động lại', async () => {
    buildWeb(v97)
    openPage(v96)

    const container = await card()
    expect(await within(container).findByText('Máy chủ (API) đang chạy gói 96, cũ hơn giao diện (gói 97).')).toBeInTheDocument()
    expect(container).toHaveTextContent('Thư mục ConsentApi trên máy chủ chưa được chép gói mới, hoặc API chưa khởi động lại sau khi chép.')
  })

  it('cùng số gói nhưng khác mã commit: chép lại cả hai thư mục từ cùng một gói', async () => {
    buildWeb({ ...v97, commit: COMMIT_96 })
    openPage(v97)

    const container = await card()
    expect(await within(container).findByText('Giao diện và máy chủ cùng gói 97 nhưng khác mã commit.')).toBeInTheDocument()
    expect(container).toHaveTextContent('Chép lại cả hai thư mục ConsentApi và ConsentWeb từ cùng một gói.')
  })

  it('máy chủ cài từ gói cũ (chưa ghi phiên bản): "Chưa rõ" và nhắc chép thư mục ConsentApi của gói đang dùng', async () => {
    buildWeb(v97)
    openPage(unknown)

    const container = await card()
    expect(await within(container).findByText('Máy chủ (API) chưa có thông tin phiên bản.')).toBeInTheDocument()
    expect(row(container, 'Máy chủ (API)')).toHaveTextContent('Chưa rõ')
    expect(container).toHaveTextContent('Chép thư mục ConsentApi của gói 97 rồi khởi động lại API.')
  })

  it('giao diện là bản phát triển: không so được', async () => {
    openPage(v97)

    const container = await card()
    expect(
      await within(container).findByText('Giao diện là bản phát triển (không build từ gói cài đặt), không so được với máy chủ.'),
    ).toBeInTheDocument()
    expect(row(container, 'Giao diện trên trình duyệt này')).toHaveTextContent('Bản phát triển')
    expect(row(container, 'Máy chủ (API)')).toHaveTextContent('Gói 97')
  })

  it('không đọc được phiên bản máy chủ: báo lỗi, không báo lệch', async () => {
    buildWeb(v97)
    openPage(() => problem(500, 'Lỗi hệ thống.'))

    const container = await card()
    expect(await within(container).findByText(/^Không đọc được phiên bản máy chủ: /)).toBeInTheDocument()
    expect(row(container, 'Máy chủ (API)')).toHaveTextContent('—')
    expect(row(container, 'Giao diện trên trình duyệt này')).toHaveTextContent('Gói 97')
    expect(alerts(container)).toEqual([expect.stringMatching(/^Không đọc được phiên bản máy chủ: /)])
  })
})
