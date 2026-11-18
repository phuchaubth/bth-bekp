import { Box, Typography } from '@mui/material';
import type { FebrileNeutropenia } from '../api/types';
import { useNow } from '../hooks/useNow';
import { formatDecimal, formatTimer } from '../lib/format';
import { febrileColors, febrileStatus } from '../lib/levels';
import { Panel } from './Panel';
import { ANTIBIOTIC_TARGET_MINUTES, febrileElapsedSeconds } from '../lib/ui';

export function FebrileNeutropeniaPanel({ episodes, showDepartment = true }: { episodes: FebrileNeutropenia[]; showDepartment?: boolean }) {
  const now = useNow();
  return (
    <Panel title="Sốt giảm BC – đồng hồ kháng sinh" subtitle={`Mục tiêu khởi động kháng sinh ≤ ${ANTIBIOTIC_TARGET_MINUTES} phút`}>
      {episodes.length === 0 ? (
        <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>Không có ca sốt giảm BC đang xử lý.</Typography>
      ) : (
        <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0, display: 'flex', flexDirection: 'column', gap: 1.25 }}>
          {episodes.map((e) => {
            const seconds = febrileElapsedSeconds(e, now);
            const minutes = Math.floor(seconds / 60);
            const given = e.antibioticStartedAtUtc !== null;
            const status = febrileStatus(minutes, given);
            const color = febrileColors[status];
            return (
              <Box component="li" key={e.id} sx={{ p: 1.5, borderRadius: 3, bgcolor: given ? '#F1F5FA' : minutes >= 30 ? '#FDF1EF' : '#F7F9FB', display: 'flex', flexDirection: 'column', gap: 1 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', gap: 1 }}>
                  <Box sx={{ minWidth: 0 }}>
                    <Typography sx={{ fontSize: 14, fontWeight: 700 }}>
                      {e.stayCode}
                      {e.bedCode ? ` · giường ${e.bedCode}` : ''}
                    </Typography>
                    {showDepartment ? <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>{e.departmentName}</Typography> : null}
                  </Box>
                  <Typography component="span" role="timer" aria-label={`${minutes} phút từ khi sốt`} sx={{ fontSize: 22, fontWeight: 700, color }}>
                    {given ? `${minutes} phút` : formatTimer(seconds)}
                  </Typography>
                </Box>
                <Box role="presentation" sx={{ height: 6, borderRadius: 999, bgcolor: 'rgba(14,26,43,0.08)', overflow: 'hidden' }}>
                  <Box sx={{ height: '100%', width: `${Math.min(100, (seconds / (ANTIBIOTIC_TARGET_MINUTES * 60)) * 100)}%`, bgcolor: color, borderRadius: 999 }} />
                </Box>
                <Typography sx={{ fontSize: 12, color: '#3D4756' }}>
                  Sốt {formatDecimal(e.temperatureCelsius)}°C ·{' '}
                  {given
                    ? `đã dùng kháng sinh sau ${minutes} phút — ${minutes <= ANTIBIOTIC_TARGET_MINUTES ? 'đạt' : 'không đạt'} mục tiêu`
                    : `chưa dùng kháng sinh (còn ${Math.max(0, ANTIBIOTIC_TARGET_MINUTES - minutes)} phút)`}
                </Typography>
              </Box>
            );
          })}
        </Box>
      )}
    </Panel>
  );
}
