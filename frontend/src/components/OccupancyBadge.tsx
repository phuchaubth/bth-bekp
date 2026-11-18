import { Box } from '@mui/material';
import type { OccupancyLevel } from '../api/types';
import { formatPercent } from '../lib/format';
import { occupancyLabel } from '../lib/labels';
import { occupancyColors } from '../lib/levels';

export function OccupancyBadge({ level, percent }: { level: OccupancyLevel; percent?: number }) {
  const colors = occupancyColors[level];
  return (
    <Box component="span" sx={{ px: 1.1, py: 0.35, borderRadius: 999, fontSize: 12, fontWeight: 700, bgcolor: colors.bg, color: colors.fg, whiteSpace: 'nowrap' }}>
      {occupancyLabel[level]}
      {percent !== undefined ? ` · ${formatPercent(percent)}` : ''}
    </Box>
  );
}

export function OccupancyBar({ level, percent, height = 8 }: { level: OccupancyLevel; percent: number; height?: number }) {
  return (
    <Box role="presentation" sx={{ height, borderRadius: 999, bgcolor: '#EDF0F4', overflow: 'hidden' }}>
      <Box sx={{ height: '100%', width: `${Math.min(100, percent)}%`, bgcolor: occupancyColors[level].bar, borderRadius: 999 }} />
    </Box>
  );
}
