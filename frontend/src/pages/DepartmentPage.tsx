import { Box, FormControl, InputLabel, Link, MenuItem, Select, Typography } from '@mui/material';
import { useState } from 'react';
import { Navigate, Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useDepartment, useDepartments } from '../api/queries';
import type { BedTile, DepartmentDetail } from '../api/types';
import { AlertsPanel } from '../components/AlertsPanel';
import { AppHeader } from '../components/AppHeader';
import { BedMap } from '../components/BedMap';
import type { BedFilter } from '../lib/ui';
import { BloodRequestsPanel } from '../components/BloodRequestsPanel';
import { DiseasePatternPanel } from '../components/DiseasePatternPanel';
import { EventFeed } from '../components/EventFeed';
import { FebrileNeutropeniaPanel } from '../components/FebrileNeutropeniaPanel';
import { KpiTile } from '../components/KpiTile';
import { OccupancyBadge } from '../components/OccupancyBadge';
import { Loading, LoadError, SyntheticDataBadge } from '../components/PageState';
import { Panel } from '../components/Panel';
import { TimeStandardsPanel } from '../components/TimeStandardsPanel';
import { careLevelLabel, diseaseGroupLabel } from '../lib/labels';
import { occupancyColors, tokens } from '../lib/levels';
import { mergeEvents, useRealtime } from '../realtime/context';

export function DepartmentIndexRedirect() {
  const { data } = useDepartments();
  const first = data?.[0];
  return first ? <Navigate to={`/departments/${first.code}`} replace /> : <Loading />;
}

export function DepartmentPage() {
  const { code } = useParams<{ code: string }>();
  const navigate = useNavigate();
  const departments = useDepartments();
  const { data, isPending, isError } = useDepartment(code);
  const [filter, setFilter] = useState<BedFilter>('all');
  const [selected, setSelected] = useState<string | null>(null);

  const changeDepartment = (next: string) => {
    setFilter('all');
    setSelected(null);
    navigate(`/departments/${next}`);
  };

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppHeader subtitle="BEKP · Màn hình khoa lâm sàng" />
      <Box component="main" sx={{ maxWidth: 1376, mx: 'auto', px: 3, py: 2.5, display: 'flex', flexDirection: 'column', gap: 2.75 }}>
        <Box component="nav" aria-label="Đường dẫn" sx={{ fontSize: 13, color: 'text.secondary', display: 'flex', gap: 1 }}>
          <Link component={RouterLink} to="/" sx={{ fontWeight: 600, py: 1.25 }} underline="hover">
            Toàn viện
          </Link>
          <Box component="span" aria-hidden="true" sx={{ py: 1.25 }}>
            /
          </Box>
          <Box component="span" sx={{ py: 1.25 }}>
            {data?.summary.name ?? code}
          </Box>
        </Box>

        <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', justifyContent: 'space-between', gap: 2 }}>
          <Box>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
              <Typography component="h1" sx={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.02em' }}>
                Khoa {data?.summary.name ?? ''}
              </Typography>
              {data ? <OccupancyBadge level={data.summary.occupancyLevel} percent={data.summary.occupancyPercent} /> : null}
            </Box>
            {data ? (
              <Typography sx={{ fontSize: 14, color: 'text.secondary', display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
                {data.summary.code} · {data.summary.capacity} giường kế hoạch · {data.summary.description} <SyntheticDataBadge />
              </Typography>
            ) : null}
          </Box>
          <FormControl sx={{ minWidth: 300 }}>
            <InputLabel id="dept-select-label">Chọn khoa lâm sàng</InputLabel>
            <Select labelId="dept-select-label" label="Chọn khoa lâm sàng" value={departments.data ? (code ?? '') : ''} onChange={(e) => changeDepartment(String(e.target.value))} sx={{ bgcolor: '#fff', fontWeight: 600 }}>
              {(departments.data ?? []).map((d) => (
                <MenuItem key={d.code} value={d.code}>
                  {d.name} · {d.code}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        </Box>

        {isPending ? <Loading /> : null}
        {isError && !data ? <LoadError message="Không tìm thấy khoa hoặc không tải được dữ liệu." /> : null}
        {data ? <DepartmentContent data={data} filter={filter} onFilter={setFilter} selected={selected} onSelect={setSelected} /> : null}
      </Box>
    </Box>
  );
}

interface ContentProps {
  data: DepartmentDetail;
  filter: BedFilter;
  onFilter: (f: BedFilter) => void;
  selected: string | null;
  onSelect: (code: string) => void;
}

function DepartmentContent({ data, filter, onFilter, selected, onSelect }: ContentProps) {
  const { events } = useRealtime();
  const s = data.summary;
  const liveForDepartment = events.filter((e) => e.departmentCode === s.code);
  const selectedBed = data.rooms.flatMap((r) => r.beds).find((b) => b.code === selected) ?? null;
  const waiting = data.febrileNeutropenia.filter((e) => e.antibioticStartedAtUtc === null).length;

  return (
    <>
      <Box component="section" aria-label="Chỉ số khoa" sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 1.75 }}>
        <KpiTile label="Đang điều trị" value={s.occupied} suffix={` / ${s.capacity}`} barPercent={s.occupancyPercent} barColor={occupancyColors[s.occupancyLevel].bar} note={s.extraBedsInUse ? `${s.extraBedsInUse} giường kê thêm` : undefined} />
        <KpiTile label={s.isSterileUnit ? 'Phòng vô trùng trống' : 'Giường trống'} value={s.available} color={s.available === 0 ? tokens.danger : tokens.ok} note={s.cleaning ? `+ ${s.cleaning} đang vệ sinh` : 'không có giường chờ vệ sinh'} />
        <KpiTile label="Giảm BC trung tính nặng" value={s.neutropenic} color={tokens.neutropenia} note="ANC < 0,5 G/L · cách ly bảo vệ" />
        <KpiTile label="Hóa trị hôm nay" value={s.chemotherapyToday} color="#1D4F8C" note={`Nhập ${s.admissionsToday} · Ra ${s.dischargesToday} hôm nay`} />
        <KpiTile label="Chế phẩm máu hôm nay" value={s.unitsIssuedToday} suffix=" đv" color={tokens.blood} note={`${s.transfusing} đang truyền · ${s.pendingBloodRequests} yêu cầu chờ`} />
        <KpiTile label="Sốt giảm BC đang xử lý" value={data.febrileNeutropenia.length} color={tokens.danger} note={`${waiting} ca chưa có kháng sinh`} />
      </Box>

      <Box sx={{ display: 'grid', gap: 3, alignItems: 'start', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(0, 1fr) 380px' } }}>
        <BedMap rooms={data.rooms} sterile={s.isSterileUnit} filter={filter} onFilterChange={onFilter} selected={selected} onSelect={onSelect} />
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }}>
          <SelectedBedPanel bed={selectedBed} />
          <FebrileNeutropeniaPanel episodes={data.febrileNeutropenia} showDepartment={false} />
          <BloodRequestsPanel requests={data.bloodRequests} />
          <AlertsPanel alerts={data.alerts} title="Cảnh báo của khoa" />
        </Box>
      </Box>

      <Box sx={{ display: 'grid', gap: 3, alignItems: 'start', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(0, 1fr) 380px' } }}>
        <DiseasePatternPanel pattern={data.diseasePattern} />
        <EventFeed events={mergeEvents(data.recentEvents, liveForDepartment, 10)} title="Sự kiện của khoa" />
      </Box>

      <TimeStandardsPanel department={s.code} />
    </>
  );
}

function SelectedBedPanel({ bed }: { bed: BedTile | null }) {
  const o = bed?.occupant;
  return (
    <Panel title="Giường đang chọn">
      {!bed ? (
        <Typography sx={{ fontSize: 14, color: 'text.secondary', lineHeight: 1.5 }}>
          Chọn một giường trên sơ đồ để xem trạng thái điều trị. Màn hình không hiển thị họ tên — chỉ dùng mã đợt điều trị.
        </Typography>
      ) : (
        <Box>
          <Typography sx={{ fontSize: 22, fontWeight: 700, mb: 1 }}>Giường {bed.code}</Typography>
          {o ? (
            <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '8px 16px', fontSize: 14 }}>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Mã đợt điều trị</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>{o.stayCode}</Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Chẩn đoán chính</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>
                {o.principalDiagnosisCode} · {diseaseGroupLabel[o.diseaseGroup]}
              </Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Ngày điều trị</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>Ngày thứ {o.dayOfStay}</Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Mức chăm sóc</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>{careLevelLabel[o.careLevel]}</Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Giảm BC nặng</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>{o.isNeutropenic ? 'Có · cách ly bảo vệ' : 'Không'}</Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Truyền máu</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>{o.isTransfusing ? 'Đang truyền' : 'Không'}</Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Hóa trị hôm nay</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>{o.hasChemotherapyToday ? 'Có y lệnh' : 'Không'}</Box>
              <Box component="dt" sx={{ color: 'text.secondary' }}>Dự kiến ra viện</Box>
              <Box component="dd" sx={{ m: 0, fontWeight: 600 }}>{o.expectedDischargeToday ? 'Hôm nay' : (o.expectedDischargeDate ?? '—')}</Box>
            </Box>
          ) : (
            <Typography sx={{ fontSize: 14, color: 'text.secondary' }}>
              {bed.status === 'available' ? 'Giường trống, sẵn sàng nhận người bệnh.' : bed.status === 'cleaning' ? 'Đang vệ sinh, khử khuẩn.' : 'Tạm ngưng sử dụng.'}
            </Typography>
          )}
        </Box>
      )}
    </Panel>
  );
}
