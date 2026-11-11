import { LogoutOutlined, MenuOutlined, UserOutlined } from '@ant-design/icons'
import { Avatar, Button, Drawer, Dropdown, Grid, Layout, Menu, Space, Typography, type MenuProps } from 'antd'
import { useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { filterNavItems, navItems, type NavItem } from '../../app/navigation'
import { useAuth } from '../../auth/authContext'

const { Header, Sider, Content, Footer } = Layout

const APP_TITLE = 'Quản lý Phiếu đồng thuận'

function toMenuItems(items: NavItem[]): MenuProps['items'] {
  return items.map((item) => ({
    key: item.path,
    icon: item.icon,
    label: item.label,
    children: item.children ? toMenuItems(item.children) : undefined,
  }))
}

function openKeysFor(items: NavItem[], pathname: string): string[] {
  return items
    .filter((item) => item.children?.some((child) => pathname.startsWith(child.path)))
    .map((item) => item.path)
}

/** Bố cục chung: menu trái trên máy tính, menu dạng ngăn kéo trên điện thoại/máy tính bảng. */
export function AppLayout() {
  const screens = Grid.useBreakpoint()
  const isDesktop = screens.lg ?? true
  const [drawerOpen, setDrawerOpen] = useState(false)
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { user, hasPermission, logout } = useAuth()

  const visibleItems = useMemo(() => filterNavItems(navItems, hasPermission), [hasPermission])

  const menu = (
    <Menu
      theme="dark"
      mode="inline"
      selectedKeys={[pathname]}
      defaultOpenKeys={openKeysFor(visibleItems, pathname)}
      items={toMenuItems(visibleItems)}
      onClick={({ key }) => {
        navigate(key)
        setDrawerOpen(false)
      }}
    />
  )

  const brand = (
    <div style={{ color: '#fff', fontWeight: 600, padding: '16px 20px', lineHeight: 1.3 }}>{APP_TITLE}</div>
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
      {isDesktop ? (
        <Sider width={248} aria-label="Điều hướng chính">
          {brand}
          <nav>{menu}</nav>
        </Sider>
      ) : (
        <Drawer
          placement="left"
          open={drawerOpen}
          onClose={() => setDrawerOpen(false)}
          size={264}
          styles={{ body: { padding: 0, background: '#0a2540' }, header: { display: 'none' } }}
        >
          {brand}
          <nav aria-label="Điều hướng chính">{menu}</nav>
        </Drawer>
      )}
      <Layout>
        <Header
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
            padding: '0 16px',
            borderBottom: '1px solid #e5e9f0',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 1, minWidth: 0 }}>
            {!isDesktop && (
              <Button type="text" icon={<MenuOutlined />} aria-label="Mở menu" onClick={() => setDrawerOpen(true)} />
            )}
            {!isDesktop && (
              <Typography.Text strong ellipsis style={{ minWidth: 0 }}>
                {APP_TITLE}
              </Typography.Text>
            )}
          </div>
          <Dropdown menu={userMenu} trigger={['click']} placement="bottomRight">
            <Button type="text" aria-label="Tài khoản" style={{ flexShrink: 0, height: 'auto', padding: '4px 8px' }}>
              <Space size={8}>
                <Avatar size="small" icon={<UserOutlined />} />
                {isDesktop && <Typography.Text>{user?.fullName}</Typography.Text>}
              </Space>
            </Button>
          </Dropdown>
        </Header>
        <Content style={{ padding: isDesktop ? 24 : 16 }}>
          <main>
            <Outlet />
          </main>
        </Content>
        <Footer style={{ textAlign: 'center', color: '#8c96a3', fontSize: 12 }}>
          Dữ liệu minh họa là dữ liệu giả lập. Không nhập thông tin người bệnh thật trên môi trường thử nghiệm.
        </Footer>
      </Layout>
    </Layout>
  )
}
