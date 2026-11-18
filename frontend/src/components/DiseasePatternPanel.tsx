import { Box, Typography } from '@mui/material';
import type { DiseasePattern } from '../api/types';
import { formatPercent } from '../lib/format';
import { diseaseGroupLabel } from '../lib/labels';
import { Panel } from './Panel';

/** Mô hình bệnh tật: current inpatients and 30-day admissions by hematology disease group (ICD-10). */
export function DiseasePatternPanel({ pattern, limit = 10 }: { pattern: DiseasePattern; limit?: number }) {
  const groups = pattern.groups.slice(0, limit);
  const max = Math.max(1, ...groups.map((g) => Math.max(g.activeInpatients, g.admissionsLast30Days)));
  return (
    <Panel
      title="Mô hình bệnh tật"
      subtitle={`Theo chẩn đoán chính ICD-10 · ${pattern.activeTotal} người bệnh đang điều trị · ${pattern.admissionsTotal} lượt nhập viện 30 ngày`}
    >
      <Box sx={{ display: 'flex', gap: 2, fontSize: 12, color: '#3D4756', mb: 1.5 }}>
        <Box component="span" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          <Box component="span" sx={{ width: 12, height: 12, borderRadius: '3px', bgcolor: '#0E2A47' }} />
          Đang điều trị
        </Box>
        <Box component="span" sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          <Box component="span" sx={{ width: 12, height: 12, borderRadius: '3px', bgcolor: '#9CC0E8' }} />
          Nhập viện 30 ngày
        </Box>
      </Box>
      <Box component="table" sx={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
        <Box component="thead" sx={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
          <tr>
            <th scope="col">Nhóm bệnh</th>
            <th scope="col">Đang điều trị</th>
            <th scope="col">Nhập viện 30 ngày</th>
          </tr>
        </Box>
        <tbody>
          {groups.map((g) => (
            <Box component="tr" key={g.group} sx={{ borderTop: '1px solid #EDF0F4' }}>
              <Box component="th" scope="row" sx={{ textAlign: 'left', fontWeight: 600, py: 1, pr: 1.5, width: '38%' }}>
                {diseaseGroupLabel[g.group]}
              </Box>
              <Box component="td" sx={{ py: 1 }}>
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Box sx={{ height: 10, borderRadius: 999, bgcolor: '#0E2A47', width: `${(g.activeInpatients / max) * 100}%`, minWidth: g.activeInpatients ? 4 : 0 }} />
                    <Typography component="span" sx={{ fontSize: 13, fontWeight: 700, whiteSpace: 'nowrap' }}>
                      {g.activeInpatients}
                      <Box component="span" sx={{ fontWeight: 500, color: 'text.secondary' }}>
                        {' '}
                        ({formatPercent(pattern.activeTotal ? (g.activeInpatients * 100) / pattern.activeTotal : 0)})
                      </Box>
                    </Typography>
                  </Box>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Box sx={{ height: 6, borderRadius: 999, bgcolor: '#9CC0E8', width: `${(g.admissionsLast30Days / max) * 100}%`, minWidth: g.admissionsLast30Days ? 4 : 0 }} />
                    <Typography component="span" sx={{ fontSize: 12, color: 'text.secondary' }}>{g.admissionsLast30Days}</Typography>
                  </Box>
                </Box>
              </Box>
            </Box>
          ))}
        </tbody>
      </Box>
    </Panel>
  );
}
