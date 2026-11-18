import { Box, Link, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import type { BedCellState, DepartmentSummary } from '../api/types';
import { formatPercent } from '../lib/format';
import { bedCellColors, occupancyColors, tokens } from '../lib/levels';
import { Panel } from './Panel';

const legend: { state: BedCellState; label: string }[] = [
  { state: 'occupied', label: 'Đang điều trị' },
  { state: 'severe', label: 'Nặng' },
  { state: 'neutropenic', label: 'Giảm BC – cách ly' },
  { state: 'expectedDischarge', label: 'Dự kiến ra viện' },
  { state: 'available', label: 'Trống' },
  { state: 'cleaning', label: 'Đang vệ sinh' },
];

export function BedLegend({ items = legend }: { items?: typeof legend }) {
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.75, fontSize: 12, color: '#3D4756' }}>
      {items.map((i) => (
        <Box key={i.state} component="span" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          <Box component="span" sx={{ width: 12, height: 12, borderRadius: '3px', bgcolor: bedCellColors[i.state].bg }} />
          {i.label}
        </Box>
      ))}
      <Box component="span" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
        <Box component="span" sx={{ width: 12, height: 12, borderRadius: '3px', border: '2px dashed #B42318', boxSizing: 'border-box' }} />
        Kê thêm
      </Box>
    </Box>
  );
}

/** Compact bed board of every department: one square per bed. */
export function BedBoard({ departments }: { departments: DepartmentSummary[] }) {
  return (
    <Panel title="Sơ đồ giường các khoa" subtitle="Mỗi ô là một giường · cập nhật trực tiếp">
      <BedLegend />
      <Box sx={{ mt: 2, display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(256px, 1fr))', gap: 1.75 }}>
        {departments.map((d) => {
          const emptyCount = d.beds.filter((b) => b.state === 'available').length;
          return (
            <Link
              key={d.code}
              component={RouterLink}
              to={`/departments/${d.code}`}
              underline="none"
              aria-label={`Sơ đồ giường khoa ${d.name}: ${d.occupied} đang điều trị, ${d.severe} nặng, ${d.neutropenic} giảm bạch cầu, ${emptyCount} trống`}
              sx={{ color: tokens.ink, border: `1px solid ${tokens.border}`, borderRadius: 3.5, p: 2, display: 'flex', flexDirection: 'column', gap: 1.25, bgcolor: '#FBFCFD', '&:hover': { borderColor: '#9CC0E8' } }}
            >
              <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 1 }}>
                <Box sx={{ minWidth: 0 }}>
                  <Typography sx={{ fontSize: 15, fontWeight: 700 }}>{d.name}</Typography>
                  <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                    {d.code} · {d.occupied}/{d.capacity} giường
                  </Typography>
                </Box>
                <Box component="span" sx={{ alignSelf: 'flex-start', px: 1, py: 0.35, borderRadius: 999, fontSize: 12, fontWeight: 700, bgcolor: occupancyColors[d.occupancyLevel].bg, color: occupancyColors[d.occupancyLevel].fg }}>
                  {formatPercent(d.occupancyPercent)}
                </Box>
              </Box>
              <Box aria-hidden="true" sx={{ display: 'flex', flexWrap: 'wrap', gap: '3px' }}>
                {d.beds.map((b) => (
                  <Box
                    key={b.code}
                    title={b.code}
                    sx={{
                      width: 14,
                      height: 14,
                      borderRadius: '3px',
                      boxSizing: 'border-box',
                      bgcolor: b.isExtra && (b.state === 'occupied' || b.state === 'expectedDischarge') ? '#FFFFFF' : bedCellColors[b.state].bg,
                      border: b.isExtra ? '2px dashed #B42318' : 'none',
                    }}
                  />
                ))}
              </Box>
              <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: '4px 12px', fontSize: 12, color: '#3D4756' }}>
                <span>Nặng <strong>{d.severe}</strong></span>
                <span>Giảm BC <strong>{d.neutropenic}</strong></span>
                <span>
                  Trống <Box component="strong" sx={{ color: emptyCount === 0 ? tokens.danger : tokens.ok }}>{emptyCount}</Box>
                </span>
                <span>Vệ sinh <strong>{d.cleaning}</strong></span>
                <Box component="span" sx={{ ml: 'auto', color: tokens.link, fontWeight: 600 }}>Chi tiết →</Box>
              </Box>
            </Link>
          );
        })}
      </Box>
    </Panel>
  );
}
