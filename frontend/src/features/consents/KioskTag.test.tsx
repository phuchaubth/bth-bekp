import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { KioskTag } from './KioskTag'

describe('nhãn phiếu lập tại kiosk', () => {
  it('ghi cách người bệnh nhận diện; phiếu nhân viên lập thì không có nhãn', () => {
    const { rerender, container } = render(<KioskTag method="SelfReported" />)
    expect(screen.getByText('Lập tại kiosk · tự khai')).toBeInTheDocument()
    rerender(<KioskTag method="CitizenIdCard" />)
    expect(screen.getByText('Lập tại kiosk · quét CCCD')).toBeInTheDocument()
    rerender(<KioskTag method={null} />)
    expect(container).toBeEmptyDOMElement()
  })
})
