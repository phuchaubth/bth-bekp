import { LogoutOutlined, MenuOutlined, UserOutlined } from '@ant-design/icons'
import { Avatar, Button, Drawer, Dropdown, Grid, Layout, Menu, Space, Typography, type MenuProps } from 'antd'
import { useMemo, useState, type CSSProperties } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { filterNavItems, flattenNavItems, navItems, type NavItem } from '../../app/navigation'
import { BRAND } from '../../app/theme'
import { useAuth } from '../../auth/authContext'

const { Header, Content, Footer } = Layout

const APP_TITLE = 'Phiếu đồng thuận'

/** compact: thanh menu ngang trên máy tính (không biểu tượng, tên ngắn) để đủ chỗ cho mọi mục. */
function toMenuItems(items: NavItem[], compact = false): MenuProps['items'] {
  return items.map((item) => ({
    key: item.path,
    icon: compact ? undefined : item.icon,
    label: compact ? (item.shortLabel ?? item.label) : item.label,
    children: item.children ? toMenuItems(item.children) : undefined,
  }))
}

function openKeysFor(items: NavItem[], pathname: string): string[] {
  return items
    .filter((item) => item.children?.some((child) => pathname.startsWith(child.path)))
    .map((item) => item.path)
}

/** Dấu nhận diện: ô vuông xanh ngọc có chữ thập y tế. */
function BrandMark() {
  return (
    <span
      aria-hidden="true"
      style={{
        width: 34,
        height: 34,
        borderRadius: 10,
        background: BRAND,
        display: 'grid',
        placeItems: 'center',
        flex: 'none',
      }}
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="2.2" strokeLinejoin="round">
        <path d="M9 3h6v6h6v6h-6v6H9v-6H3V9h6z" />
      </svg>
    </span>
  )
}

/** Nội dung căn giữa, rộng tối đa 1280 px (giống nhau giữa thanh trên, nội dung và chân trang). */
const CONTAINER: CSSProperties = { maxWidth: 1280, margin: '0 auto', width: '100%', boxSizing: 'border-box' }

/**
 * Bố cục chung: thanh trên cùng có tên ứng dụng, menu ngang và tài khoản (máy tính); điện thoại/máy tính bảng dùng nút
 * menu mở ngăn kéo. Nội dung căn giữa, rộng tối đa 1280 px.
 */
export function AppLayout() {
  const screens = Grid.useBreakpoint()
  const isDesktop = screens.lg ?? true
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { user, hasPermission, logout } = useAuth()

  const visibleItems = useMemo(() => filterNavItems(navItems, hasPermission), [hasPermission])
  // Mục đang mở: trang hiện tại hoặc trang cha của nó (ví dụ /phieu-dong-thuan/... → Phiếu đồng thuận).
  const selectedKey =
    flattenNavItems(visibleItems)
      .map((item) => item.path)
      .filter((path) => (path === '/' ? pathname === '/' : pathname === path || pathname.startsWith(`${path}/`)))
      .sort((a, b) => b.length - a.length)[0] ?? pathname

  const onNavigate: MenuProps['onClick'] = ({ key }) => {
    navigate(key)
    setDrawerOpen(false)
  }

  const brand = (
    <a
      href="/"
      onClick={(e) => {
        e.preventDefault()
        navigate('/')
      }}
      style={{ display: 'flex', alignItems: 'center', gap: 10, color: 'inherit', textDecoration: 'none', flex: 'none' }}
    >
      <BrandMark />
      <span style={{ display: 'flex', flexDirection: 'column', lineHeight: 1.2 }}>
        <Typography.Text strong style={{ fontSize: 15 }}>
          {APP_TITLE}
        </Typography.Text>
        {isDesktop && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {user?.departmentName ?? 'Toàn viện'}
          </Typography.Text>
        )}
      </span>
    </a>
  )

  const userMenu: MenuProps = {
    items: [
      { key: 'account', icon: <UserOutlined />, label: 'Tài khoản của tôi' },
      { type: 'divider' },
      { key: 'logout', icon: <LogoutOutlined />, label: 'Đăng xuất', danger: true },
    ],
    onClick: ({ key }) => {
      if (key === 'account') {
        navigate('/tai-khoan')
      } else if (key === 'logout') {
        void logout()
      }
    },
  }

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Header
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 20,
          borderBottom: '1px solid #e1eaeb',
          paddingInline: isDesktop ? 24 : 12,
        }}
      >
        <div style={{ ...CONTAINER, height: '100%', display: 'flex', alignItems: 'center', gap: isDesktop ? 24 : 8 }}>
          {!isDesktop && (
            <Button type="text" icon={<MenuOutlined />} aria-label="Mở menu" onClick={() => setDrawerOpen(true)} />
          )}
          {brand}
          {isDesktop ? (
            <nav aria-label="Điều hướng chính" style={{ flex: '1 1 auto', minWidth: 0 }}>
              <Menu
                mode="horizontal"
                // Bấm để mở menu con (máy tính bảng cảm ứng không có thao tác di chuột).
                triggerSubMenuAction="click"
                selectedKeys={[selectedKey]}
                items={toMenuItems(visibleItems, true)}
                onClick={onNavigate}
                style={{ borderBottom: 0, background: 'transparent' }}
              />
            </nav>
          ) : (
            <div style={{ flex: 1 }} />
          )}
          <Dropdown menu={userMenu} trigger={['click']} placement="bottomRight">
            <Button type="text" aria-label="Tài khoản" style={{ flexShrink: 0, height: 'auto', padding: '4px 8px' }}>
              <Space size={8}>
                <Avatar size="small" icon={<UserOutlined />} style={{ background: BRAND }} />
                {isDesktop && <Typography.Text>{user?.fullName}</Typography.Text>}
              </Space>
            </Button>
          </Dropdown>
        </div>
      </Header>
      {!isDesktop && (
        <Drawer
          placement="left"
          open={drawerOpen}
          onClose={() => setDrawerOpen(false)}
          size={280}
          title={brand}
          styles={{ body: { padding: 8 } }}
        >
          <nav aria-label="Điều hướng chính">
            <Menu
              mode="inline"
              selectedKeys={[selectedKey]}
              defaultOpenKeys={openKeysFor(visibleItems, pathname)}
              items={toMenuItems(visibleItems)}
              onClick={onNavigate}
              style={{ borderInlineEnd: 0 }}
            />
          </nav>
        </Drawer>
      )}
      <Content style={{ padding: isDesktop ? '28px 24px' : 16 }}>
        <main style={CONTAINER}>
          <Outlet />
        </main>
      </Content>
      <Footer style={{ textAlign: 'center', color: '#55706d', fontSize: 12 }}>
        Dữ liệu minh họa là dữ liệu giả lập. Không nhập thông tin người bệnh thật trên môi trường thử nghiệm.
      </Footer>
    </Layout>
  )
}
