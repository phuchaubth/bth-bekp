import { useEffect } from 'react'
import { logoImageUrl, useLogo } from '../../api/branding'

const DEFAULT_ICON = '/favicon.svg'

/** Biểu tượng thẻ trình duyệt theo logo bệnh viện (quyết định 61); chưa có logo thì biểu tượng mặc định của phần mềm. */
export function LogoFavicon() {
  const logo = useLogo().data
  useEffect(() => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) return
    if (logo) {
      link.type = logo.contentType
      link.href = logoImageUrl(logo)
    } else {
      link.type = 'image/svg+xml'
      link.href = DEFAULT_ICON
    }
  }, [logo])
  return null
}
