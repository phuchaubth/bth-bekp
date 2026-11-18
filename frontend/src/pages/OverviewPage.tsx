import { Box, Chip, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { useOverview } from '../api/queries';
import { AlertsPanel } from '../components/AlertsPanel';
import { AppHeader } from '../components/AppHeader';
import { BedBoard } from '../components/BedBoard';
import { BloodInventoryPanel } from '../components/BloodInventoryPanel';
import { DepartmentTable } from '../components/DepartmentTable';
import { DiseasePatternPanel } from '../components/DiseasePatternPanel';
import { EventFeed } from '../components/EventFeed';
import { FebrileNeutropeniaPanel } from '../components/FebrileNeutropeniaPanel';
import { KpiTile } from '../components/KpiTile';
import { Loading, LoadError, SyntheticDataBadge } from '../components/PageState';
import { TimeStandardsPanel } from '../components/TimeStandardsPanel';
import { episodesFor, filterDepartments, groupFilters, occupancyLevelOf, sumDepartments, type GroupFilter } from '../lib/aggregate';
import { formatPercent } from '../lib/format';
import { occupancyColors, tokens } from '../lib/levels';
import { mergeEvents, useRealtime } from '../realtime/context';

export function OverviewPage({ showSyntheticBadge = true }: { showSyntheticBadge?: boolean }) {
  const { data, isPending, isError } = useOverview();
  const { events: liveEvents } = useRealtime();
  const [filter, setFilter] = useState<GroupFilter>('all');

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppHeader subtitle="BEKP · Bệnh viện chuyên khoa Huyết học – Truyền máu" />
      <Box component="main" sx={{ maxWidth: 1376, mx: 'auto', px: 3, py: 3.5, display: 'flex', flexDirection: 'column', gap: 3 }}>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', justifyContent: 'space-between', gap: 2 }}>
          <Box>
            <Typography component="h1" sx={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.02em' }}>
              Tổng quan khoa lâm sàng
            </Typography>
            <Typography sx={{ fontSize: 14, color: 'text.secondary', display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
              {data ? `${filterDepartments(data.departments, filter).length} khoa điều trị nội trú` : 'Đang tải'}
              {showSyntheticBadge ? <SyntheticDataBadge /> : null}
            </Typography>
          </Box>
          <Stack direction="row" role="group" aria-label="Lọc theo nhóm khoa" sx={{ flexWrap: 'wrap', gap: 1 }}>
            {groupFilters.map((f) => (
              <Chip
                key={f.key}
                label={f.label}
                onClick={() => setFilter(f.key)}
                color={filter === f.key ? 'primary' : 'default'}
                variant={filter === f.key ? 'filled' : 'outlined'}
                aria-pressed={filter === f.key}
                sx={{ height: 44, px: 1, fontSize: 14, fontWeight: 600, borderRadius: 999, bgcolor: filter === f.key ? undefined : '#fff' }}
              />
            ))}
          </Stack>
        </Box>

        {isPending ? <Loading /> : null}
        {isError && !data ? <LoadError /> : null}
        {data ? <OverviewContent data={data} filter={filter} liveEvents={liveEvents} /> : null}
      </Box>
    </Box>
  );
}

function OverviewContent({ data, filter, liveEvents }: { data: NonNullable<ReturnType<typeof useOverview>['data']>; filter: GroupFilter; liveEvents: ReturnType<typeof useRealtime>['events'] }) {
  const departments = filterDepartments(data.departments, filter);
  const totals = sumDepartments(departments);
  const episodes = episodesFor(data.febrileNeutropenia, departments);
  const waiting = episodes.filter((e) => e.antibioticStartedAtUtc === null).length;
  const level = occupancyLevelOf(totals.occupancyPercent);
  const codes = new Set(departments.map((d) => d.code));
  const alerts = filter === 'all' ? data.alerts : data.alerts.filter((a) => a.departmentCode !== null && codes.has(a.departmentCode));

  return (
    <>
      <Box component="section" aria-label="Chỉ số chính" sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 2 }}>
        <KpiTile label="Công suất giường" value={formatPercent(totals.occupancyPercent)} barPercent={totals.occupancyPercent} barColor={occupancyColors[level].bar} note={`${totals.occupied}/${totals.capacity} giường có người bệnh`} />
        <KpiTile label="Giường trống" value={totals.available} note={`+ ${totals.cleaning} đang vệ sinh · ${totals.sterileAvailable} phòng vô trùng trống`} />
        <KpiTile label="Hóa trị trong ngày" value={totals.chemotherapy} color="#1D4F8C" note="người bệnh có y lệnh hóa trị hôm nay" />
        <KpiTile label="Chế phẩm máu đã phát" value={totals.units} suffix=" đơn vị" color={tokens.blood} note={`${totals.transfusing} đang truyền · ${totals.pendingRequests} yêu cầu chờ`} />
        <KpiTile label="Giảm BC trung tính nặng" value={totals.neutropenic} color={tokens.neutropenia} note="ANC < 0,5 G/L · cách ly bảo vệ" />
        <KpiTile label="Sốt giảm BC đang xử lý" value={episodes.length} color={tokens.danger} note={`${waiting} ca chưa có kháng sinh · mục tiêu ≤ 60 phút`} />
      </Box>

      <Box sx={{ display: 'grid', gap: 3, alignItems: 'start', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(0, 1fr) 400px' } }}>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }}>
          <DepartmentTable departments={departments} />
          <BedBoard departments={departments} />
          <DiseasePatternPanel pattern={data.diseasePattern} />
        </Box>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }}>
          <AlertsPanel alerts={alerts} />
          <FebrileNeutropeniaPanel episodes={episodes} />
          <BloodInventoryPanel inventory={data.bloodInventory} />
          <EventFeed events={mergeEvents(data.recentEvents, liveEvents)} />
        </Box>
      </Box>

      <TimeStandardsPanel />
    </>
  );
}
