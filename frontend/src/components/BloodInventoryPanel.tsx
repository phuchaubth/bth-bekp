import { Box, Typography } from '@mui/material';
import type { AboGroup, BloodInventory, BloodProductType } from '../api/types';
import { formatTime } from '../lib/format';
import { aboLabel, productLabel } from '../lib/labels';
import { stockColors } from '../lib/levels';
import { Panel } from './Panel';

const products: BloodProductType[] = ['redCells', 'platelets', 'plasma', 'cryoprecipitate'];
const groups: AboGroup[] = ['o', 'a', 'b', 'ab'];
const levelText = { adequate: 'đủ', low: 'thấp', critical: 'thiếu' } as const;

export function BloodInventoryPanel({ inventory }: { inventory: BloodInventory[] }) {
  const latest = inventory.reduce<string | null>((max, i) => (max === null || i.updatedAtUtc > max ? i.updatedAtUtc : max), null);
  return (
    <Panel title="Tồn kho chế phẩm máu" subtitle={latest ? `đơn vị · cập nhật ${formatTime(latest)}` : 'đơn vị'}>
      <Box component="table" sx={{ width: '100%', borderCollapse: 'separate', borderSpacing: '4px', m: '-4px', fontSize: 13 }}>
        <thead>
          <tr>
            <Box component="th" scope="col" sx={{ textAlign: 'left', fontWeight: 600, color: 'text.secondary', fontSize: 12, p: 0.5 }}>
              Chế phẩm
            </Box>
            {groups.map((g) => (
              <Box component="th" key={g} scope="col" sx={{ fontWeight: 600, color: 'text.secondary', fontSize: 12, p: 0.5 }}>
                {aboLabel[g]}
              </Box>
            ))}
          </tr>
        </thead>
        <tbody>
          {products.map((p) => (
            <tr key={p}>
              <Box component="th" scope="row" sx={{ textAlign: 'left', fontWeight: 600, p: 0.75, lineHeight: 1.3 }}>
                {productLabel[p]}
              </Box>
              {groups.map((g) => {
                const item = inventory.find((i) => i.product === p && i.aboGroup === g);
                if (!item) {
                  return <td key={g}>—</td>;
                }

                const colors = stockColors[item.level];
                return (
                  <Box
                    component="td"
                    key={g}
                    aria-label={`${productLabel[p]} nhóm ${aboLabel[g]}: ${item.unitsAvailable} đơn vị, tối thiểu ${item.minimumUnits}, ${levelText[item.level]}`}
                    sx={{ textAlign: 'center', py: 1, borderRadius: 2, fontWeight: 700, bgcolor: colors.bg, color: colors.fg, border: `1px solid ${colors.border}` }}
                  >
                    {item.unitsAvailable}
                  </Box>
                );
              })}
            </tr>
          ))}
        </tbody>
      </Box>
      <Typography sx={{ mt: 1.5, fontSize: 12, color: '#3D4756' }}>Nền vàng: dưới mức tối thiểu · Nền đỏ: dưới 50% mức tối thiểu</Typography>
    </Panel>
  );
}
