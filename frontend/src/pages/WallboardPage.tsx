import { Box, ButtonBase, CssBaseline, ThemeProvider, Typography } from '@mui/material';
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useDepartment, useOverview } from '../api/queries';
import type { Alert, BedTile, DepartmentSummary, FebrileNeutropenia, HospitalOverview } from '../api/types';
import { BloodDropLogo } from '../components/AppHeader';
import { LiveIndicator } from '../components/LiveIndicator';
import { useNow } from '../hooks/useNow';
import { occupancyLevelOf, sumDepartments } from '../lib/aggregate';
import { formatClock, formatLongDate, formatPercent, formatTime, formatTimer } from '../lib/format';
import { aboLabel, describeAlert, describeEvent, productLabel, requestStatusLabel } from '../lib/labels';
import { clampSeconds, nextRotation, type RotationState } from '../lib/rotation';
import { mergeEvents, useRealtime } from '../realtime/context';
import { wallboardTheme } from '../theme';
import { describeRequest, eventToneColor, febrileElapsedSeconds } from '../lib/ui';

const W = 1920;
const H = 1080;

const dark = {
  page: '#070D18',
  panel: '#0F1A2B',
  border: '#1E2B40',
  track: '#1A2740',
  text: '#E6EDF7',
  muted: '#9AA8BC',
  level: {
    normal: { bar: '#5B9BE6', text: '#93C5FD' },
    high: { bar: '#F2B544', text: '#FCD34D' },
    critical: { bar: '#F06A5D', text: '#FCA5A5' },
    overCapacity: { bar: '#F06A5D', text: '#FCA5A5' },
  },
};

/** Scales the fixed 1920×1080 design to the actual screen. */
function useFitScale() {
  const [scale, setScale] = useState(1);
  useLayoutEffect(() => {
    const update = () => setScale(Math.min(window.innerWidth / W, window.innerHeight / H));
    update();
    window.addEventListener('resize', update);
    return () => window.removeEventListener('resize', update);
  }, []);
  return scale;
}

function DarkPanel({ title, right, children, grow = false }: { title: string; right?: ReactNode; children: ReactNode; grow?: boolean }) {
  return (
    <Box component="section" aria-label={title} sx={{ bgcolor: dark.panel, border: `1px solid ${dark.border}`, borderRadius: '18px', p: '18px 22px', display: 'flex', flexDirection: 'column', gap: 1.5, minHeight: 0, flex: grow ? 1 : 'none', overflow: 'hidden' }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <Typography component="h2" sx={{ fontSize: 22, fontWeight: 700 }}>
          {title}
        </Typography>
        {right}
      </Box>
      {children}
    </Box>
  );
}

interface Tile {
  label: string;
  value: ReactNode;
  note: string;
  color: string;
  alert?: boolean;
}

function Tiles({ tiles }: { tiles: Tile[] }) {
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(6, minmax(0, 1fr))', gap: 2, height: 146, flex: 'none' }}>
      {tiles.map((t) => (
        <Box key={t.label} sx={{ bgcolor: dark.panel, border: `1px solid ${t.alert ? '#7F1D1D' : dark.border}`, borderRadius: '18px', p: '16px 22px', display: 'flex', flexDirection: 'column', justifyContent: 'space-between' }}>
          <Typography sx={{ fontSize: 18, fontWeight: 600, color: dark.muted }}>{t.label}</Typography>
          <Typography sx={{ fontSize: 58, fontWeight: 800, lineHeight: 1, letterSpacing: '-0.03em', color: t.color }}>{t.value}</Typography>
          <Typography sx={{ fontSize: 15, color: dark.muted, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{t.note}</Typography>
        </Box>
      ))}
    </Box>
  );
}

function FnTimers({ episodes, now, showDepartment }: { episodes: FebrileNeutropenia[]; now: Date; showDepartment: boolean }) {
  if (episodes.length === 0) {
    return <Typography sx={{ fontSize: 17, color: '#86EFAC', fontWeight: 600 }}>Không có ca đang xử lý</Typography>;
  }

  return (
    <>
      {episodes.slice(0, 3).map((e) => {
        const seconds = febrileElapsedSeconds(e, now);
        const minutes = Math.floor(seconds / 60);
        const given = e.antibioticStartedAtUtc !== null;
        const color = given ? '#93C5FD' : minutes >= 60 ? '#F06A5D' : minutes >= 30 ? '#F2B544' : '#93C5FD';
        return (
          <Box key={e.id} sx={{ p: '12px 14px', borderRadius: '14px', bgcolor: given ? '#0E1F33' : minutes >= 30 ? '#2A1210' : '#121F33', border: `1px solid ${given ? '#1E3A5F' : minutes >= 30 ? '#7F1D1D' : dark.border}`, display: 'flex', flexDirection: 'column', gap: 1 }}>
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1.5 }}>
              <Box sx={{ minWidth: 0 }}>
                <Typography sx={{ fontSize: 17, fontWeight: 700 }}>{e.stayCode}{e.bedCode ? ` · ${e.bedCode}` : ''}</Typography>
                {showDepartment ? <Typography sx={{ fontSize: 14, color: dark.muted, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{e.departmentName}</Typography> : null}
              </Box>
              <Typography sx={{ fontSize: 38, fontWeight: 800, lineHeight: 1, color }}>{given ? `ĐẠT ${minutes}′` : formatTimer(seconds)}</Typography>
            </Box>
            <Box sx={{ height: 8, borderRadius: 999, bgcolor: dark.track, overflow: 'hidden' }}>
              <Box sx={{ height: '100%', width: `${Math.min(100, (seconds / 3600) * 100)}%`, bgcolor: color }} />
            </Box>
          </Box>
        );
      })}
    </>
  );
}

function AlertList({ alerts, focus }: { alerts: Alert[]; focus: number }) {
  if (alerts.length === 0) {
    return <Typography sx={{ fontSize: 17, color: '#86EFAC', fontWeight: 600 }}>Không có cảnh báo</Typography>;
  }

  const shown = alerts.slice(0, 5);
  return (
    <>
      {shown.map((a, i) => {
        const { title, detail } = describeAlert(a);
        const critical = a.severity === 'critical';
        const ring = i === focus % shown.length ? (critical ? '#F06A5D' : '#F2B544') : 'transparent';
        return (
          <Box key={`${a.type}-${a.departmentCode ?? ''}-${a.subject ?? i}`} sx={{ display: 'flex', gap: 1.75, p: '14px 16px', borderRadius: '14px', bgcolor: critical ? '#2A1210' : '#2A200B', border: `2px solid ${ring}` }}>
            <Box sx={{ flex: 'none', mt: 0.5, width: 14, height: 14, borderRadius: '4px', bgcolor: critical ? '#F06A5D' : '#F2B544' }} />
            <Box sx={{ minWidth: 0 }}>
              <Typography sx={{ fontSize: 18, fontWeight: 700, lineHeight: 1.3 }}>{title}</Typography>
              <Typography sx={{ fontSize: 15, color: '#C9D3E1', lineHeight: 1.4 }}>{detail}</Typography>
            </Box>
          </Box>
        );
      })}
    </>
  );
}

function OverviewScreen({ data, now }: { data: HospitalOverview; now: Date }) {
  const rows = [...data.departments].sort((a, b) => b.occupancyPercent - a.occupancyPercent);
  const focus = Math.floor(now.getTime() / 5000);
  const cols = '240px minmax(0, 1fr) repeat(4, 64px)';
  return (
    <Box sx={{ flex: 1, minHeight: 0, display: 'grid', gridTemplateColumns: '840px 500px minmax(0, 1fr)', gap: 2.5 }}>
      <DarkPanel title="Tình trạng theo khoa">
        <Box sx={{ display: 'grid', gridTemplateColumns: cols, gap: 1.75, fontSize: 15, fontWeight: 600, color: dark.muted, borderBottom: `1px solid ${dark.border}`, pb: 1 }}>
          <span />
          <span>Công suất giường</span>
          <Box component="span" sx={{ textAlign: 'right' }}>Trống</Box>
          <Box component="span" sx={{ textAlign: 'right' }}>Giảm BC</Box>
          <Box component="span" sx={{ textAlign: 'right' }}>Truyền</Box>
          <Box component="span" sx={{ textAlign: 'right' }}>Chờ máu</Box>
        </Box>
        {rows.map((d) => {
          const lv = dark.level[d.occupancyLevel];
          return (
            <Box key={d.code} sx={{ flex: 1, display: 'grid', gridTemplateColumns: cols, gap: 1.75, alignItems: 'center', borderBottom: '1px solid #16233A' }}>
              <Box sx={{ minWidth: 0 }}>
                <Typography sx={{ fontSize: 20, fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{d.name}</Typography>
                <Typography sx={{ fontSize: 14, color: dark.muted }}>{d.occupied}/{d.capacity} giường</Typography>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Box sx={{ flex: 1, height: 14, borderRadius: 999, bgcolor: dark.track, overflow: 'hidden' }}>
                  <Box sx={{ height: '100%', width: `${Math.min(100, d.occupancyPercent)}%`, bgcolor: lv.bar }} />
                </Box>
                <Typography sx={{ width: 88, textAlign: 'right', fontSize: 26, fontWeight: 800, color: lv.text }}>{formatPercent(d.occupancyPercent)}</Typography>
              </Box>
              <Typography sx={{ textAlign: 'right', fontSize: 26, fontWeight: 800, color: d.available === 0 ? '#FCA5A5' : '#86EFAC' }}>{d.available}</Typography>
              <Typography sx={{ textAlign: 'right', fontSize: 26, fontWeight: 700, color: '#C4B5FD' }}>{d.neutropenic}</Typography>
              <Typography sx={{ textAlign: 'right', fontSize: 26, fontWeight: 700, color: '#F9A8D4' }}>{d.transfusing}</Typography>
              <Typography sx={{ textAlign: 'right', fontSize: 26, fontWeight: 700 }}>{d.pendingBloodRequests}</Typography>
            </Box>
          );
        })}
      </DarkPanel>
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, minHeight: 0 }}>
        <DarkPanel title="Sốt giảm BC – kháng sinh" right={<Typography sx={{ fontSize: 15, color: dark.muted }}>≤ 60 phút</Typography>}>
          <FnTimers episodes={data.febrileNeutropenia} now={now} showDepartment />
        </DarkPanel>
        <DarkPanel title="Tồn kho chế phẩm máu" grow>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) repeat(4, 66px)', gap: 0.75, fontSize: 15, color: dark.muted, fontWeight: 600 }}>
            <span />
            {(['o', 'a', 'b', 'ab'] as const).map((g) => (
              <Box key={g} component="span" sx={{ textAlign: 'center' }}>{aboLabel[g]}</Box>
            ))}
          </Box>
          {(['redCells', 'platelets', 'plasma', 'cryoprecipitate'] as const).map((p) => (
            <Box key={p} sx={{ flex: 1, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) repeat(4, 66px)', gap: 0.75 }}>
              <Typography sx={{ fontSize: 16, fontWeight: 600, display: 'flex', alignItems: 'center' }}>{productLabel[p]}</Typography>
              {(['o', 'a', 'b', 'ab'] as const).map((g) => {
                const item = data.bloodInventory.find((i) => i.product === p && i.aboGroup === g);
                const level = item?.level ?? 'adequate';
                return (
                  <Box key={g} sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '10px', fontSize: 22, fontWeight: 800, bgcolor: level === 'critical' ? '#D63B2F' : level === 'low' ? '#3A2A0B' : dark.track, color: level === 'critical' ? '#fff' : level === 'low' ? '#FCD34D' : dark.text, border: `1px solid ${level === 'low' ? '#F2B544' : 'transparent'}` }}>
                    {item?.unitsAvailable ?? '—'}
                  </Box>
                );
              })}
            </Box>
          ))}
        </DarkPanel>
      </Box>
      <DarkPanel title="Cảnh báo cần xử lý" right={<Box sx={{ px: 1.75, py: 0.5, borderRadius: 999, bgcolor: '#3B1512', color: '#FCA5A5', fontSize: 16, fontWeight: 800 }}>{data.alerts.length} MỞ</Box>}>
        <AlertList alerts={data.alerts} focus={focus} />
      </DarkPanel>
    </Box>
  );
}

function tileColors(bed: BedTile) {
  const o = bed.occupant;
  if (bed.status === 'available') return { bg: '#0F2A1C', border: '#4ADE80', style: 'solid', flag: 'TRỐNG', flagColor: '#86EFAC' };
  if (bed.status === 'cleaning') return { bg: '#2A200B', border: '#F2B544', style: 'dashed', flag: 'VS', flagColor: '#FCD34D' };
  if (bed.status === 'outOfService') return { bg: '#142238', border: '#2B3D5C', style: 'dashed', flag: 'NGƯNG', flagColor: dark.muted };
  if (o?.isSevere) return { bg: '#3B1512', border: bed.isExtra ? '#F06A5D' : '#F06A5D', style: bed.isExtra ? 'dashed' : 'solid', flag: 'NẶNG', flagColor: '#FCA5A5' };
  if (o?.isNeutropenic) return { bg: '#231B45', border: bed.isExtra ? '#F06A5D' : '#8B74E8', style: bed.isExtra ? 'dashed' : 'solid', flag: '', flagColor: dark.text };
  if (o?.expectedDischargeToday) return { bg: '#10243F', border: '#5B9BE6', style: 'dashed', flag: 'RA VIỆN', flagColor: '#93C5FD' };
  return { bg: '#142238', border: bed.isExtra ? '#F06A5D' : '#2B3D5C', style: bed.isExtra ? 'dashed' : 'solid', flag: '', flagColor: dark.text };
}

function DepartmentScreen({ code, summary, now }: { code: string; summary: DepartmentSummary; now: Date }) {
  const { data } = useDepartment(code);
  const beds = data?.rooms.flatMap((r) => r.beds) ?? [];
  const cols = beds.length <= 16 ? 6 : beds.length <= 36 ? 9 : 11;
  const rows = Math.max(1, Math.ceil(beds.length / cols));
  const tileH = Math.min(150, Math.floor((560 - (rows - 1) * 10) / rows));
  const lv = dark.level[summary.occupancyLevel];
  return (
    <Box sx={{ flex: 1, minHeight: 0, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 560px', gap: 2.5 }}>
      <DarkPanel
        title={`Sơ đồ giường · ${summary.name}`}
        right={
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
            <Box sx={{ width: 220, height: 12, borderRadius: 999, bgcolor: dark.track, overflow: 'hidden' }}>
              <Box sx={{ height: '100%', width: `${Math.min(100, summary.occupancyPercent)}%`, bgcolor: lv.bar }} />
            </Box>
            <Typography sx={{ fontSize: 24, fontWeight: 800, color: lv.text }}>{formatPercent(summary.occupancyPercent)}</Typography>
          </Box>
        }
      >
        <Box sx={{ flex: 1, display: 'grid', gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`, gridAutoRows: `${tileH}px`, gap: '10px', alignContent: 'start' }}>
          {beds.map((b) => {
            const c = tileColors(b);
            const o = b.occupant;
            return (
              <Box key={b.code} sx={{ borderRadius: '12px', p: '8px 10px', display: 'flex', flexDirection: 'column', justifyContent: 'space-between', bgcolor: c.bg, border: `${b.isExtra ? 2 : 1}px ${c.style} ${c.border}` }}>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 0.5 }}>
                  <Typography sx={{ fontSize: 17, fontWeight: 800 }}>{b.code}</Typography>
                  <Typography sx={{ fontSize: 12, fontWeight: 800, color: c.flagColor }}>{c.flag}</Typography>
                </Box>
                <Typography sx={{ fontSize: 12, color: dark.muted, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{o ? `${o.stayCode} · N${o.dayOfStay}` : ''}</Typography>
                <Box sx={{ display: 'flex', gap: 0.5 }}>
                  {o?.isNeutropenic ? <Box component="span" sx={{ px: 0.6, borderRadius: '5px', bgcolor: '#2E2560', color: '#DDD6FE', fontSize: 11, fontWeight: 800 }}>BC</Box> : null}
                  {o?.isTransfusing ? <Box component="span" sx={{ px: 0.6, borderRadius: '5px', bgcolor: '#4A1734', color: '#FBCFE8', fontSize: 11, fontWeight: 800 }}>TM</Box> : null}
                  {o?.hasChemotherapyToday ? <Box component="span" sx={{ px: 0.6, borderRadius: '5px', bgcolor: '#13304F', color: '#BFDBFE', fontSize: 11, fontWeight: 800 }}>HT</Box> : null}
                </Box>
              </Box>
            );
          })}
        </Box>
        <Typography sx={{ fontSize: 14, color: '#C9D3E1' }}>
          <strong style={{ color: '#DDD6FE' }}>BC</strong> giảm BC · <strong style={{ color: '#FBCFE8' }}>TM</strong> đang truyền máu · <strong style={{ color: '#BFDBFE' }}>HT</strong> hóa trị hôm nay · viền đứt đỏ: giường kê thêm
        </Typography>
      </DarkPanel>
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5, minHeight: 0 }}>
        <DarkPanel title="Sốt giảm BC – kháng sinh">
          <FnTimers episodes={data?.febrileNeutropenia ?? []} now={now} showDepartment={false} />
        </DarkPanel>
        <DarkPanel title="Yêu cầu chế phẩm máu" right={<Box sx={{ px: 1.75, py: 0.5, borderRadius: 999, bgcolor: '#4A1734', color: '#FBCFE8', fontSize: 16, fontWeight: 800 }}>{summary.pendingBloodRequests} CHỜ</Box>}>
          {(data?.bloodRequests ?? []).slice(0, 4).map((r) => (
            <Box key={r.id} sx={{ display: 'flex', justifyContent: 'space-between', gap: 1.5, p: '10px 14px', borderRadius: '12px', bgcolor: '#121F33' }}>
              <Typography sx={{ fontSize: 16, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{describeRequest(r)}</Typography>
              <Typography sx={{ flex: 'none', fontSize: 14, fontWeight: 700, color: '#93C5FD' }}>{requestStatusLabel[r.status]}</Typography>
            </Box>
          ))}
        </DarkPanel>
        <DarkPanel title="Cảnh báo của khoa" grow>
          <AlertList alerts={data?.alerts ?? []} focus={Math.floor(now.getTime() / 5000)} />
        </DarkPanel>
      </Box>
    </Box>
  );
}

export function WallboardPage() {
  const scale = useFitScale();
  const now = useNow();
  const [params] = useSearchParams();
  const rotate = params.get('rotate') !== '0';
  const periodMs = clampSeconds(params.get('seconds')) * 1000;
  const { data } = useOverview();
  const { events } = useRealtime();
  const departments = data?.departments ?? [];
  const screenCount = departments.length + 1;
  const [rotation, setRotation] = useState<RotationState>(() => ({ screen: 0, startedAt: Date.now() }));
  const rotationRef = useRef(rotation);
  rotationRef.current = rotation;

  useEffect(() => {
    if (!rotate) return;
    const next = nextRotation(rotationRef.current, now.getTime(), periodMs, screenCount);
    if (next !== rotationRef.current) setRotation(next);
  }, [now, rotate, periodMs, screenCount]);

  const screen = Math.min(rotation.screen, screenCount - 1);
  const current = screen === 0 ? null : departments[screen - 1] ?? null;
  const elapsed = now.getTime() - rotation.startedAt;
  const left = Math.max(0, Math.ceil((periodMs - elapsed) / 1000));
  const nextName = (screen + 1) % screenCount === 0 ? 'Toàn viện' : departments[screen]?.name ?? '';
  const ticker = mergeEvents(data?.recentEvents ?? [], events, 12);

  const totals = sumDepartments(departments);
  const fnWaiting = (data?.febrileNeutropenia ?? []).filter((e) => !e.antibioticStartedAtUtc).length;
  const tiles: Tile[] = current
    ? [
        { label: 'Đang điều trị', value: `${current.occupied}/${current.capacity}`, note: `công suất ${formatPercent(current.occupancyPercent)}`, color: dark.level[current.occupancyLevel].text },
        { label: current.isSterileUnit ? 'Phòng vô trùng trống' : 'Giường trống', value: current.available, note: `+ ${current.cleaning} đang vệ sinh`, color: current.available === 0 ? '#FCA5A5' : '#86EFAC', alert: current.available === 0 },
        { label: 'Giảm BC trung tính nặng', value: current.neutropenic, note: 'ANC < 0,5 G/L · cách ly bảo vệ', color: '#C4B5FD' },
        { label: 'Hóa trị hôm nay', value: current.chemotherapyToday, note: 'có y lệnh hóa trị', color: '#93C5FD' },
        { label: 'Chế phẩm máu hôm nay', value: current.unitsIssuedToday, note: `${current.transfusing} đang truyền · ${current.pendingBloodRequests} chờ`, color: '#F9A8D4' },
        { label: 'Sốt giảm BC', value: (data?.febrileNeutropenia ?? []).filter((e) => e.departmentCode === current.code).length, note: 'mục tiêu kháng sinh ≤ 60 phút', color: '#FCA5A5' },
      ]
    : [
        { label: 'Công suất giường', value: formatPercent(totals.occupancyPercent), note: `${totals.occupied}/${totals.capacity} giường có người bệnh`, color: dark.level[occupancyLevelOf(totals.occupancyPercent)].text },
        { label: 'Giường trống', value: totals.available, note: `+ ${totals.cleaning} vệ sinh · ${totals.sterileAvailable} phòng vô trùng`, color: '#86EFAC' },
        { label: 'Hóa trị trong ngày', value: totals.chemotherapy, note: 'có y lệnh hóa trị hôm nay', color: '#93C5FD' },
        { label: 'Chế phẩm máu đã phát', value: totals.units, note: `${totals.transfusing} đang truyền · ${totals.pendingRequests} chờ`, color: '#F9A8D4' },
        { label: 'Giảm BC trung tính nặng', value: totals.neutropenic, note: 'ANC < 0,5 G/L · cách ly bảo vệ', color: '#C4B5FD' },
        { label: 'Sốt giảm BC đang xử lý', value: data?.febrileNeutropenia.length ?? 0, note: `${fnWaiting} ca chưa có kháng sinh`, color: '#FCA5A5', alert: fnWaiting > 0 },
      ];

  return (
    <ThemeProvider theme={wallboardTheme}>
      <CssBaseline />
      <Box sx={{ position: 'fixed', inset: 0, bgcolor: dark.page, overflow: 'hidden' }}>
        <Box sx={{ width: W, height: H, transform: `scale(${scale})`, transformOrigin: 'top left', p: '24px 32px 28px', boxSizing: 'border-box', color: dark.text, display: 'flex', flexDirection: 'column', gap: 2.25 }}>
          <Box component="header" sx={{ flex: 'none', display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            <Box sx={{ height: 72, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 3 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, minWidth: 0 }}>
                <BloodDropLogo size={52} accent="#F472B6" />
                <Box sx={{ minWidth: 0 }}>
                  <Typography component="h1" sx={{ fontSize: 30, fontWeight: 800, whiteSpace: 'nowrap' }}>{current ? `Khoa ${current.name}` : 'Trung tâm Điều hành Nội trú'}</Typography>
                  <Typography sx={{ fontSize: 17, color: dark.muted, whiteSpace: 'nowrap' }}>
                    {current ? `${current.code} · ${current.description}` : `Bệnh viện chuyên khoa Huyết học – Truyền máu · ${departments.length} khoa lâm sàng`}
                  </Typography>
                </Box>
              </Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 3 }}>
                <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 1 }}>
                  <Box role="group" aria-label="Chọn màn hình" sx={{ display: 'flex', gap: 0.75 }}>
                    {Array.from({ length: screenCount }, (_, i) => (
                      <ButtonBase
                        key={i}
                        aria-label={i === 0 ? 'Màn toàn viện' : `Màn khoa ${departments[i - 1]?.name ?? ''}`}
                        aria-pressed={i === screen}
                        onClick={() => setRotation({ screen: i, startedAt: Date.now() })}
                        sx={{ width: i === screen ? 34 : 12, height: 12, borderRadius: 999, bgcolor: i === screen ? '#5B9BE6' : '#2B3D5C' }}
                      />
                    ))}
                  </Box>
                  <Typography sx={{ fontSize: 15, color: dark.muted }}>
                    Màn <strong style={{ color: dark.text }}>{screen + 1}/{screenCount}</strong> · {rotate ? `tiếp theo: ${nextName} sau ${left} giây` : 'luân phiên đang tắt'}
                  </Typography>
                </Box>
                <LiveIndicator dark />
                <Box sx={{ textAlign: 'right' }}>
                  <Typography sx={{ fontSize: 44, fontWeight: 800, lineHeight: 1 }}>{formatClock(now)}</Typography>
                  <Typography sx={{ fontSize: 16, color: dark.muted }}>{formatLongDate(now)}</Typography>
                </Box>
              </Box>
            </Box>
            <Box sx={{ height: 4, borderRadius: 999, bgcolor: '#131F33', overflow: 'hidden' }}>
              <Box sx={{ height: '100%', width: rotate ? `${Math.min(100, (elapsed / periodMs) * 100)}%` : 0, bgcolor: '#5B9BE6' }} />
            </Box>
          </Box>

          <Tiles tiles={tiles} />

          {data ? current ? <DepartmentScreen key={current.code} code={current.code} summary={current} now={now} /> : <OverviewScreen data={data} now={now} /> : <Box sx={{ flex: 1 }} />}

          <Box component="footer" aria-label="Sự kiện trực tiếp" sx={{ height: 52, flex: 'none', display: 'flex', alignItems: 'center', gap: 2.25, bgcolor: dark.panel, border: `1px solid ${dark.border}`, borderRadius: '14px', px: 2.25, overflow: 'hidden' }}>
            <Typography sx={{ flex: 'none', fontSize: 16, fontWeight: 800, color: '#86EFAC' }}>SỰ KIỆN</Typography>
            <Box sx={{ display: 'flex', gap: 5, overflow: 'hidden', whiteSpace: 'nowrap' }}>
              {ticker.map((e) => {
                const d = describeEvent(e);
                return (
                  <Box key={e.id} component="span" sx={{ display: 'flex', alignItems: 'center', gap: 1.25, fontSize: 17 }}>
                    <Box component="span" sx={{ width: 10, height: 10, borderRadius: '3px', bgcolor: eventToneColor[d.tone] }} />
                    <Box component="span" sx={{ color: dark.muted }}>{formatTime(e.occurredAtUtc)}</Box>
                    <strong>{d.label}</strong>
                    <span>{d.text}</span>
                    {e.departmentName ? <Box component="span" sx={{ color: dark.muted }}>· {e.departmentName}</Box> : null}
                  </Box>
                );
              })}
            </Box>
          </Box>
        </Box>
      </Box>
    </ThemeProvider>
  );
}
