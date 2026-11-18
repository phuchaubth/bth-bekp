import { Box, Typography } from '@mui/material';
import type { Alert } from '../api/types';
import { formatTime } from '../lib/format';
import { describeAlert } from '../lib/labels';
import { Panel } from './Panel';

export function AlertsPanel({ alerts, title = 'Cảnh báo cần xử lý', limit = 6 }: { alerts: Alert[]; title?: string; limit?: number }) {
  const shown = alerts.slice(0, limit);
  return (
    <Panel
      title={title}
      actions={
        <Box component="span" sx={{ px: 1.25, py: 0.4, borderRadius: 999, bgcolor: alerts.length ? '#FDE8E6' : '#E7F4EC', color: alerts.length ? '#A21B12' : '#1E6B3A', fontSize: 12, fontWeight: 700 }}>
          {alerts.length} mở
        </Box>
      }
    >
      {shown.length === 0 ? (
        <Typography sx={{ fontSize: 14, fontWeight: 600, color: '#1E6B3A' }}>Không có cảnh báo.</Typography>
      ) : (
        <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0, display: 'flex', flexDirection: 'column', gap: 1.25 }}>
          {shown.map((alert, index) => {
            const { title: heading, detail } = describeAlert(alert);
            const critical = alert.severity === 'critical';
            return (
              <Box component="li" key={`${alert.type}-${alert.departmentCode ?? ''}-${alert.subject ?? index}`} sx={{ display: 'flex', gap: 1.5, p: 1.5, borderRadius: 3, bgcolor: critical ? '#FDF1EF' : '#FEF7EC' }}>
                <Box aria-hidden="true" sx={{ flex: 'none', mt: 0.25, width: 28, height: 28, borderRadius: 2, bgcolor: critical ? '#B42318' : '#B86E00', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M12 9v4" />
                    <path d="M12 17h.01" />
                    <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />
                  </svg>
                </Box>
                <Box sx={{ minWidth: 0 }}>
                  <Typography sx={{ fontSize: 14, fontWeight: 600 }}>
                    <Box component="span" sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
                      {critical ? 'Nghiêm trọng: ' : 'Cảnh báo: '}
                    </Box>
                    {heading}
                  </Typography>
                  <Typography sx={{ fontSize: 13, color: '#4A5565', lineHeight: 1.45 }}>{detail}</Typography>
                  {alert.sinceUtc ? <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>Từ {formatTime(alert.sinceUtc)}</Typography> : null}
                </Box>
              </Box>
            );
          })}
        </Box>
      )}
    </Panel>
  );
}
