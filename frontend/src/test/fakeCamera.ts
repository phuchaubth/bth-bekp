import { vi, type Mock } from 'vitest'

type DecodeCallback = (result: { getText: () => string } | undefined, error: unknown, controls: { stop: () => void }) => void

/** Một lần mở camera (một lần gọi thư viện đọc mã); test quyết định lúc camera bật, lỗi, đọc được mã. */
export interface FakeCamera {
  constraints: MediaStreamConstraints
  video: HTMLVideoElement
  /** Gọi khi camera được tắt. */
  stop: Mock<() => void>
  /** Trình duyệt cho phép, camera bật. */
  start: () => void
  /** Không mở được camera, với tên lỗi của trình duyệt (ví dụ `NotAllowedError`). */
  fail: (name: string) => void
  /** Camera thấy một mã QR trong khung hình. */
  read: (text: string) => void
}

/** Các lần mở camera, theo thứ tự. */
export const fakeCameras: FakeCamera[] = []

/** Thay thư viện đọc mã trong test: `vi.mock('@zxing/browser', () => import('../../test/fakeCamera').then((m) => m.fakeZxing))`. */
export const fakeZxing = {
  BrowserQRCodeReader: class {
    decodeFromConstraints(constraints: MediaStreamConstraints, video: HTMLVideoElement, callback: DecodeCallback) {
      return new Promise((resolve, reject) => {
        const stop = vi.fn<() => void>()
        const controls = { stop }
        fakeCameras.push({
          constraints,
          video,
          stop,
          start: () => resolve(controls),
          fail: (name) => reject(new DOMException('Camera giả lập', name)),
          read: (text) => callback({ getText: () => text }, undefined, controls),
        })
      })
    }
  },
}

/**
 * jsdom không có camera: giả lập `navigator.mediaDevices` như trang chạy HTTPS, với các thiết bị cho trước, và quyền camera
 * của trang (`denied`: trình duyệt chặn camera). Trả hàm gỡ bỏ.
 */
export function installFakeMediaDevices(
  kinds: MediaDeviceKind[] = ['videoinput'],
  permission: PermissionState = 'prompt',
): () => void {
  fakeCameras.length = 0
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: {
      getUserMedia: vi.fn(),
      enumerateDevices: vi.fn(async () => kinds.map((kind) => ({ kind, deviceId: '', groupId: '', label: '' }))),
    },
  })
  Object.defineProperty(navigator, 'permissions', {
    configurable: true,
    value: { query: vi.fn(async () => ({ state: permission })) },
  })
  return () => {
    Reflect.deleteProperty(navigator, 'mediaDevices')
    Reflect.deleteProperty(navigator, 'permissions')
    fakeCameras.length = 0
  }
}
