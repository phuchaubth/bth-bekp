import { Box, Typography } from '@mui/material';
import type { BloodRequest, BloodRequestStatus } from '../api/types';
import { formatTime } from '../lib/format';
import { requestStatusLabel, urgencyLabel } from '../lib/labels';
import { describeRequest } from '../lib/ui';
import { Panel } from './Panel';

const statusColor: Record<BloodRequestStatus, string> = {
  requested: '#A21B12',
  crossMatching: '#7A4A00',
  approved: '#1D4F8C',
  issued: '#1E6B3A',
  transfusing: '#9D174D',
  completed: '#3D4756',
  cancelled: '#3D4756',
};

export function BloodRequestsPanel({ requests }: { requests: BloodRequest[] }) {
  return (
    <Panel
      title="Yêu cầu chế phẩm máu"
      actions={
        <Box component="span" sx={{ px: 1.25, py: 0.4, borderRadius: 999, bgcolor: '#FCE7F0', color: '#9D174D', fontSize: 12, fontWeight: 700 }}>
          {requests.length} đang chờ
        </Box>
      }
    >
      {requests.length === 0 ? (
        <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>Không có yêu cầu đang chờ.</Typography>
      ) : (
        <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0, display: 'flex', flexDirection: 'column', gap: 1 }}>
          {requests.map((r) => (
            <Box component="li" key={r.id} sx={{ p: 1.25, borderRadius: 2.5, bgcolor: '#F7F9FB' }}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 1 }}>
                <Typography sx={{ fontSize: 14, fontWeight: 600 }}>{describeRequest(r)}</Typography>
                <Typography component="time" dateTime={r.requestedAtUtc} sx={{ flex: 'none', fontSize: 12, color: 'text.secondary' }}>
                  {formatTime(r.requestedAtUtc)}
                </Typography>
              </Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 1, fontSize: 12 }}>
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                  {r.stayCode ?? '—'}
                  {r.bedCode ? ` · ${r.bedCode}` : ''} · {urgencyLabel[r.urgency]}
                </Typography>
                <Typography sx={{ fontSize: 12, fontWeight: 700, color: statusColor[r.status] }}>{requestStatusLabel[r.status]}</Typography>
              </Box>
            </Box>
          ))}
        </Box>
      )}
    </Panel>
  );
}
