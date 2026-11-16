import { renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useObjectUrl } from './useObjectUrl'

/** Sổ đường dẫn blob giả lập (jsdom không có URL.createObjectURL): biết đường dẫn nào còn dùng được và tạo cho blob nào. */
let live: Set<string>
let blobOf: Map<string, Blob>

beforeEach(() => {
  live = new Set()
  blobOf = new Map()
  let next = 0
  URL.createObjectURL = vi.fn((blob: Blob) => {
    const url = `blob:test/${++next}`
    live.add(url)
    blobOf.set(url, blob)
    return url
  })
  URL.revokeObjectURL = vi.fn((url: string) => {
    live.delete(url)
  })
})

describe('useObjectUrl', () => {
  it('StrictMode gỡ thử rồi gắn lại: đường dẫn đang hiển thị vẫn dùng được; gỡ thật thì thu hồi hết', () => {
    const blob = new Blob(['PNG-gia-lap'], { type: 'image/png' })
    const { result, unmount } = renderHook(() => useObjectUrl(blob), { reactStrictMode: true })

    expect(result.current).not.toBeNull()
    expect(live).toEqual(new Set([result.current]))

    unmount()
    expect(live.size).toBe(0)
  })

  it('dữ liệu đổi (tải lại): giữ đường dẫn cũ tới khi có đường dẫn mới rồi mới thu hồi; mọi đường dẫn trả về đều còn dùng được', () => {
    const first = new Blob(['A'], { type: 'image/png' })
    const second = new Blob(['B'], { type: 'image/png' })
    const rendered: { blob: Blob | undefined; url: string | null; alive: boolean }[] = []
    const initialProps: { blob: Blob | undefined } = { blob: first }
    const { result, rerender, unmount } = renderHook(
      ({ blob }) => {
        const url = useObjectUrl(blob)
        rendered.push({ blob, url, alive: url === null || live.has(url) })
        return url
      },
      { initialProps, reactStrictMode: true },
    )
    const firstUrl = result.current

    rerender({ blob: second })
    // Lần render ngay sau khi dữ liệu đổi vẫn dùng đường dẫn cũ: ảnh đang hiện không bị gỡ để hiện "đang tải".
    expect(rendered).toContainEqual({ blob: second, url: firstUrl, alive: true })
    expect(blobOf.get(result.current!)).toBe(second)
    expect(live).toEqual(new Set([result.current]))

    rerender({ blob: undefined })
    expect(result.current).toBeNull()
    expect(live.size).toBe(0)

    expect(rendered.every((render) => render.alive)).toBe(true)
    unmount()
    expect(live.size).toBe(0)
  })

  it('không có dữ liệu: trả về null, không tạo đường dẫn', () => {
    const { result } = renderHook(() => useObjectUrl(null), { reactStrictMode: true })

    expect(result.current).toBeNull()
    expect(URL.createObjectURL).not.toHaveBeenCalled()
  })
})
