import { Box, Paper, Skeleton, ToggleButton, ToggleButtonGroup, Tooltip, Typography } from '@mui/material';
import { useState } from 'react';
import { useTimeStandards } from '../api/queries';
import type { ReportPeriod, TimeStandardStat } from '../api/types';
import { formatMinutes, formatPercent } from '../lib/format';
import { timeStandardLabel } from '../lib/labels';
import { Panel } from './Panel';
import { complianceTone } from '../lib/ui';

const periods: { value: ReportPeriod; label: string }[] = [
  { value: 'today', label: 'Hôm nay' },
  { value: 'last7Days', label: '7 ngày' },
  { value: 'last30Days', label: '30 ngày' },
];

function StandardCard({ stat }: { stat: TimeStandardStat }) {
  const tone = complianceTone(stat.compliancePercent);
  return (
    <Paper variant="outlined" sx={{ borderRadius: 3.5, p: 2, display: 'flex', flexDirection: 'column', gap: 1, bgcolor: '#FBFCFD' }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 1 }}>
        <Typography sx={{ fontSize: 13, fontWeight: 600, color: '#3D4756', lineHeight: 1.35 }}>{timeStandardLabel[stat.standard]}</Typography>
        <Box component="span" sx={{ flex: 'none', px: 1, py: 0.25, borderRadius: 999, fontSize: 11, fontWeight: 700, bgcolor: tone.bg, color: tone.fg }}>
          {tone.label}
        </Box>
      </Box>
      <Typography sx={{ fontSize: 26, fontWeight: 700, lineHeight: 1, color: tone.fg }}>
        {stat.compliancePercent === null ? '—' : formatPercent(stat.compliancePercent)}
        <Box component="span" sx={{ fontSize: 13, fontWeight: 500, color: 'text.secondary' }}>
          {' '}
          đạt ({stat.withinTarget}/{stat.completed} lượt)
        </Box>
      </Typography>
      <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '2px 12px', fontSize: 12 }}>
        <Box component="dt" sx={{ color: 'text.secondary' }}>Mục tiêu</Box>
        <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>≤ {formatMinutes(stat.targetMinutes)}</Box>
        <Box component="dt" sx={{ color: 'text.secondary' }}>Trung vị · P90</Box>
        <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>
          {stat.medianMinutes === null ? '—' : formatMinutes(stat.medianMinutes)} · {stat.p90Minutes === null ? '—' : formatMinutes(stat.p90Minutes)}
        </Box>
        <Box component="dt" sx={{ color: 'text.secondary' }}>Đang thực hiện</Box>
        <Box component="dd" sx={{ m: 0, fontWeight: 600, color: stat.inProgressOverdue > 0 ? '#A21B12' : 'inherit' }}>
          {stat.inProgress} ({stat.inProgressOverdue} quá hạn)
        </Box>
      </Box>
      <Tooltip title={stat.reference}>
        <Typography sx={{ fontSize: 11, color: stat.referenceConfirmed ? 'text.secondary' : '#7A4A00', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {stat.referenceConfirmed ? 'Căn cứ: ' : 'Chưa xác nhận · '}
          {stat.reference}
        </Typography>
      </Tooltip>
    </Paper>
  );
}

/** Thống kê thời gian thực hiện theo quy định (BYT / nội bộ). */
export function TimeStandardsPanel({ department }: { department?: string }) {
  const [period, setPeriod] = useState<ReportPeriod>('today');
  const { data, isPending, isError } = useTimeStandards(period, department);
  return (
    <Panel
      title="Thời gian thực hiện theo quy định"
      subtitle="Tỷ lệ đạt mục tiêu thời gian · mục tiêu và văn bản căn cứ do Phòng QLCL cấu hình"
      actions={
        <ToggleButtonGroup size="small" exclusive value={period} onChange={(_, v: ReportPeriod | null) => v && setPeriod(v)} aria-label="Kỳ thống kê">
          {periods.map((p) => (
            <ToggleButton key={p.value} value={p.value}>
              {p.label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
      }
    >
      {isError ? (
        <Typography sx={{ color: '#A21B12', fontSize: 14 }}>Không tải được thống kê.</Typography>
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(250px, 1fr))', gap: 1.75 }}>
          {isPending || !data
            ? Array.from({ length: 6 }, (_, i) => <Skeleton key={i} variant="rounded" height={180} />)
            : data.standards.map((s) => <StandardCard key={s.standard} stat={s} />)}
        </Box>
      )}
    </Panel>
  );
}
