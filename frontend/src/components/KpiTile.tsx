import { Box, Paper, Typography } from '@mui/material';
import type { ReactNode } from 'react';

interface KpiTileProps {
  label: string;
  value: ReactNode;
  suffix?: string;
  note?: ReactNode;
  color?: string;
  barPercent?: number;
  barColor?: string;
}

export function KpiTile({ label, value, suffix, note, color, barPercent, barColor }: KpiTileProps) {
  return (
    <Paper variant="outlined" sx={{ borderRadius: 4, p: 2.25, display: 'flex', flexDirection: 'column', gap: 1.25, minWidth: 0 }}>
      <Typography sx={{ fontSize: 13, fontWeight: 500, color: 'text.secondary' }}>{label}</Typography>
      <Typography component="p" sx={{ fontSize: 32, fontWeight: 700, lineHeight: 1, letterSpacing: '-0.02em', color }}>
        {value}
        {suffix ? (
          <Box component="span" sx={{ fontSize: 15, fontWeight: 500, color: 'text.secondary' }}>
            {suffix}
          </Box>
        ) : null}
      </Typography>
      {barPercent !== undefined ? (
        <Box role="presentation" sx={{ height: 6, borderRadius: 999, bgcolor: '#EDF0F4', overflow: 'hidden' }}>
          <Box sx={{ height: '100%', width: `${Math.min(100, barPercent)}%`, bgcolor: barColor, borderRadius: 999 }} />
        </Box>
      ) : null}
      {note ? <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>{note}</Typography> : null}
    </Paper>
  );
}
