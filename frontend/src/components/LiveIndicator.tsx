import { Box, Typography } from '@mui/material';
import { keyframes } from '@mui/system';
import { useNow } from '../hooks/useNow';
import { useRealtime, type ConnectionStatus } from '../realtime/context';

const pulse = keyframes`
  0% { box-shadow: 0 0 0 0 rgba(74,222,128,.7); }
  70% { box-shadow: 0 0 0 8px rgba(74,222,128,0); }
  100% { box-shadow: 0 0 0 0 rgba(74,222,128,0); }
`;

const statusText: Record<ConnectionStatus, string> = {
  connecting: 'Đang kết nối',
  connected: 'Trực tuyến',
  reconnecting: 'Đang kết nối lại',
  disconnected: 'Mất kết nối · tự cập nhật mỗi phút',
};

export function LiveIndicator({ dark = false }: { dark?: boolean }) {
  const { status, lastEventAt } = useRealtime();
  const now = useNow();
  const online = status === 'connected';
  const seconds = lastEventAt ? Math.max(0, Math.round((now.getTime() - lastEventAt.getTime()) / 1000)) : null;
  return (
    <Box
      role="status"
      aria-live="polite"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1.25,
        px: 1.75,
        py: 1,
        borderRadius: 999,
        bgcolor: dark ? (online ? '#0F2A1C' : '#2A200B') : 'rgba(255,255,255,0.1)',
        border: dark ? `1px solid ${online ? '#1E5236' : '#5C4512'}` : 'none',
      }}
    >
      <Box component="span" sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: online ? '#4ADE80' : '#F2B544', animation: online ? `${pulse} 2s infinite` : 'none' }} />
      <Typography component="span" sx={{ fontSize: dark ? 18 : 13, fontWeight: 700, color: dark ? (online ? '#86EFAC' : '#FCD34D') : '#fff' }}>
        {statusText[status]}
      </Typography>
      {online && seconds !== null ? (
        <Typography component="span" sx={{ fontSize: dark ? 15 : 13, color: dark ? '#9AA8BC' : '#C9D6E6' }}>
          sự kiện {seconds} giây trước
        </Typography>
      ) : null}
    </Box>
  );
}
