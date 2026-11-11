import { describe, expect, it } from 'vitest'
import { flattenNavItems, navItems } from './navigation'

describe('navigation', () => {
  it('làm phẳng menu và bỏ các nhóm cha', () => {
    const paths = flattenNavItems().map((item) => item.path)

    expect(paths).toContain('/quan-tri/nguoi-dung')
    expect(paths).not.toContain('/quan-tri')
  })

  it('không có đường dẫn trùng nhau', () => {
    const paths = flattenNavItems(navItems).map((item) => item.path)

    expect(new Set(paths).size).toBe(paths.length)
  })
})
