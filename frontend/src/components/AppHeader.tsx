import { Box, Link as MuiLink, Typography } from '@mui/material';
import { NavLink } from 'react-router-dom';
import { useNow } from '../hooks/useNow';
import { formatClock, formatLongDate } from '../lib/format';
import { LiveIndicator } from './LiveIndicator';

const navItems = [
  { to: '/', label: 'Toàn viện', end: true },
  { to: '/departments', label: 'Từng khoa', end: false },
  { to: '/wallboard', label: 'Phòng điều hành', end: false },
];

export function BloodDropLogo({ size = 36, accent = '#FFFFFF' }: { size?: number; accent?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 36 36" aria-hidden="true">
      <rect width="36" height="36" rx="9" fill="#FFFFFF" fillOpacity="0.12" />
      <path d="M18 8c-4 5.2-7 9.1-7 12.6A7 7 0 0 0 25 20.6C25 17.1 22 13.2 18 8z" fill={accent} />
      <path d="M15.5 21.5h5M18 19v5" stroke="#0E2A47" strokeWidth="2" strokeLinecap="round" />
    </svg>
  );
}

export function AppHeader({ subtitle }: { subtitle: string }) {
  const now = useNow();
  return (
    <Box component="header" sx={{ bgcolor: 'primary.main', color: '#fff' }}>
      <Box sx={{ maxWidth: 1376, mx: 'auto', px: 3, py: 1.75, display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '16px 24px' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flex: '1 1 260px', minWidth: 0 }}>
          <BloodDropLogo />
          <Box sx={{ minWidth: 0 }}>
            <Typography sx={{ fontWeight: 700, fontSize: 17 }}>Điều hành Nội trú</Typography>
            <Typography sx={{ fontSize: 12, color: '#C9D6E6' }}>{subtitle}</Typography>
          </Box>
        </Box>
        <Box component="nav" aria-label="Màn hình" sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
          {navItems.map((item) => (
            <MuiLink
              key={item.to}
              component={NavLink}
              to={item.to}
              end={item.end}
              underline="none"
              sx={{
                color: '#C9D6E6',
                px: 1.5,
                py: 1.25,
                borderRadius: 2,
                fontSize: 14,
                fontWeight: 600,
                '&.active': { color: '#fff', bgcolor: 'rgba(255,255,255,0.12)' },
                '&:hover': { color: '#fff' },
              }}
            >
              {item.label}
            </MuiLink>
          ))}
        </Box>
        <LiveIndicator />
        <Typography sx={{ fontSize: 13, color: '#C9D6E6' }}>
          {formatLongDate(now)} ·{' '}
          <Box component="strong" sx={{ color: '#fff' }}>
            {formatClock(now)}
          </Box>
        </Typography>
      </Box>
    </Box>
  );
}
