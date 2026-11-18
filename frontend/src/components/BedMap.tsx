import { Box, ButtonBase, Chip, Stack, Typography } from '@mui/material';
import type { Room } from '../api/types';
import { tokens } from '../lib/levels';
import { Panel } from './Panel';
import { bedFilters, matchesFilter, tileStyle, type BedFilter } from '../lib/ui';

function Tag({ label, bg, fg }: { label: string; bg: string; fg: string }) {
  return (
    <Box component="span" sx={{ px: 0.75, borderRadius: 1.5, bgcolor: bg, color: fg, fontSize: 11, fontWeight: 700 }}>
      {label}
    </Box>
  );
}

interface BedMapProps {
  rooms: Room[];
  sterile: boolean;
  filter: BedFilter;
  onFilterChange: (filter: BedFilter) => void;
  selected: string | null;
  onSelect: (code: string) => void;
}

export function BedMap({ rooms, sterile, filter, onFilterChange, selected, onSelect }: BedMapProps) {
  const all = rooms.flatMap((r) => r.beds);
  return (
    <Panel
      title="Sơ đồ giường"
      padded={false}
      actions={
        <Stack direction="row" role="group" aria-label="Lọc giường" sx={{ flexWrap: 'wrap', gap: 0.75 }}>
          {bedFilters.map((f) => (
            <Chip
              key={f.key}
              label={`${f.label} ${all.filter((b) => matchesFilter(b, f.key)).length}`}
              onClick={() => onFilterChange(f.key)}
              color={filter === f.key ? 'primary' : 'default'}
              variant={filter === f.key ? 'filled' : 'outlined'}
              aria-pressed={filter === f.key}
              sx={{ fontWeight: 600, height: 36 }}
            />
          ))}
        </Stack>
      }
    >
      <Box sx={{ mt: 2 }}>
        {rooms.map((room) => (
          <Box key={room.label} sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.5, px: 2.5, py: 1.75, borderTop: `1px solid #EDF0F4` }}>
            <Box sx={{ flex: '0 0 110px', pt: 0.5 }}>
              <Typography sx={{ fontSize: 14, fontWeight: 700 }}>{room.label}</Typography>
              <Typography sx={{ fontSize: 12, color: 'text.secondary' }}>
                {room.beds.filter((b) => b.status === 'occupied').length}/{room.beds.length} giường
              </Typography>
            </Box>
            <Box sx={{ flex: '1 1 520px', minWidth: 0, display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(112px, 1fr))', gap: 1.25 }}>
              {room.beds.map((bed) => {
                const style = tileStyle(bed, sterile);
                const o = bed.occupant;
                const isSelected = selected === bed.code;
                const dimmed = !matchesFilter(bed, filter);
                return (
                  <ButtonBase
                    key={bed.code}
                    onClick={() => onSelect(bed.code)}
                    aria-pressed={isSelected}
                    aria-label={`Giường ${bed.code}, ${style.status}${o?.isTransfusing ? ', đang truyền máu' : ''}${o?.hasChemotherapyToday ? ', hóa trị hôm nay' : ''}`}
                    sx={{
                      minHeight: 104,
                      p: 1.25,
                      borderRadius: 3,
                      textAlign: 'left',
                      display: 'flex',
                      flexDirection: 'column',
                      alignItems: 'stretch',
                      justifyContent: 'flex-start',
                      gap: 0.5,
                      bgcolor: style.bg,
                      border: `${style.borderWidth}px ${style.borderStyle} ${style.border}`,
                      opacity: dimmed ? 0.3 : 1,
                      boxShadow: isSelected ? '0 0 0 3px #7FB3E8' : 'none',
                      '&:focus-visible': { outline: '3px solid #7FB3E8', outlineOffset: 2 },
                    }}
                  >
                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 0.5 }}>
                      <Typography sx={{ fontSize: 14, fontWeight: 700 }}>{bed.code}</Typography>
                      {o?.isSevere ? <Tag label="Nặng" bg="#B42318" fg="#fff" /> : o?.expectedDischargeToday ? <Tag label="Ra viện" bg="#1D4F8C" fg="#fff" /> : null}
                    </Box>
                    <Typography sx={{ fontSize: 12, color: '#3D4756' }}>{o ? o.stayCode : style.status}</Typography>
                    <Typography sx={{ fontSize: 12, color: o ? tokens.muted : '#1E6B3A', fontWeight: o ? 400 : 600 }}>
                      {o ? `Ngày thứ ${o.dayOfStay} · ${o.principalDiagnosisCode}` : bed.status === 'cleaning' ? (sterile ? 'Khử khuẩn' : 'Sắp sẵn sàng') : bed.status === 'available' ? 'Sẵn sàng nhận' : ''}
                    </Typography>
                    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 'auto' }}>
                      {o?.isNeutropenic ? <Tag label="Giảm BC" bg="#F1EDFB" fg="#4B2F9E" /> : null}
                      {o?.isTransfusing ? <Tag label="Truyền máu" bg="#FCE7F0" fg="#9D174D" /> : null}
                      {o?.hasChemotherapyToday ? <Tag label="Hóa trị" bg="#E8F1FB" fg="#1D4F8C" /> : null}
                    </Box>
                  </ButtonBase>
                );
              })}
            </Box>
          </Box>
        ))}
      </Box>
    </Panel>
  );
}
