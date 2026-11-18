import { Box, IconButton, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import type { DepartmentSummary } from '../api/types';
import { formatPercent } from '../lib/format';
import { tokens } from '../lib/levels';
import { OccupancyBadge, OccupancyBar } from './OccupancyBadge';
import { Panel } from './Panel';
import { sortDepartments, type SortKey } from '../lib/ui';

const columns = 'minmax(230px, 2.3fr) minmax(190px, 1.9fr) repeat(5, minmax(66px, 0.6fr)) 52px';

export function DepartmentTable({ departments }: { departments: DepartmentSummary[] }) {
  const [sort, setSort] = useState<SortKey>('occupancy');
  const rows = sortDepartments(departments, sort);
  return (
    <Panel
      title="Tình trạng theo khoa"
      padded={false}
      subtitle="Ổn định < 85% · Cao 85–94% · Quá tải ≥ 95%"
      actions={
        <ToggleButtonGroup size="small" exclusive value={sort} onChange={(_, v: SortKey | null) => v && setSort(v)} aria-label="Sắp xếp">
          <ToggleButton value="occupancy">Công suất</ToggleButton>
          <ToggleButton value="code">Mã khoa</ToggleButton>
        </ToggleButtonGroup>
      }
    >
      <Box sx={{ overflowX: 'auto', mt: 2 }}>
        <Box role="table" aria-label="Tình trạng theo khoa" sx={{ minWidth: 940 }}>
          <Box role="row" sx={{ display: 'grid', gridTemplateColumns: columns, gap: 1.5, px: 2.5, py: 1.25, bgcolor: '#F7F9FB', borderTop: `1px solid ${tokens.border}`, fontSize: 12, fontWeight: 600, color: 'text.secondary' }}>
            <span role="columnheader">Khoa</span>
            <span role="columnheader">Công suất giường</span>
            <Box component="span" role="columnheader" sx={{ textAlign: 'right' }}>Trống</Box>
            <Box component="span" role="columnheader" sx={{ textAlign: 'right' }}>Hóa trị</Box>
            <Box component="span" role="columnheader" sx={{ textAlign: 'right' }}>Truyền máu</Box>
            <Box component="span" role="columnheader" sx={{ textAlign: 'right' }}>Giảm BC</Box>
            <Box component="span" role="columnheader" sx={{ textAlign: 'right' }}>Chờ máu</Box>
            <span role="columnheader" />
          </Box>
          {rows.map((d) => (
            <Box key={d.code} role="row" sx={{ display: 'grid', gridTemplateColumns: columns, gap: 1.5, alignItems: 'center', px: 2.5, py: 1.75, borderTop: `1px solid #EDF0F4`, fontSize: 15 }}>
              <Box role="cell" sx={{ minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600 }}>{d.name}</Typography>
                <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                  {d.code} · {d.capacity} giường
                </Typography>
              </Box>
              <Box role="cell" sx={{ display: 'flex', flexDirection: 'column', gap: 0.75 }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
                  <Typography sx={{ fontWeight: 700 }}>{formatPercent(d.occupancyPercent)}</Typography>
                  <OccupancyBadge level={d.occupancyLevel} />
                </Box>
                <OccupancyBar level={d.occupancyLevel} percent={d.occupancyPercent} />
              </Box>
              <Box role="cell" sx={{ textAlign: 'right', fontWeight: 700, color: d.available === 0 ? tokens.danger : tokens.ink }}>{d.available}</Box>
              <Box role="cell" sx={{ textAlign: 'right' }}>{d.chemotherapyToday}</Box>
              <Box role="cell" sx={{ textAlign: 'right' }}>{d.transfusing}</Box>
              <Box role="cell" sx={{ textAlign: 'right' }}>{d.neutropenic}</Box>
              <Box role="cell" sx={{ textAlign: 'right', fontWeight: d.pendingBloodRequests >= 4 ? 700 : 400, color: d.pendingBloodRequests >= 4 ? tokens.blood : tokens.ink }}>{d.pendingBloodRequests}</Box>
              <Box role="cell" sx={{ justifySelf: 'end' }}>
                <IconButton component={RouterLink} to={`/departments/${d.code}`} aria-label={`Xem chi tiết khoa ${d.name}`} sx={{ width: 44, height: 44, bgcolor: '#F1F5FA', color: tokens.link }}>
                  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="m9 18 6-6-6-6" />
                  </svg>
                </IconButton>
              </Box>
            </Box>
          ))}
        </Box>
      </Box>
    </Panel>
  );
}
