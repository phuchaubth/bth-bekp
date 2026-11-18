import type { DepartmentGroup, DepartmentSummary, FebrileNeutropenia } from '../api/types';

export type GroupFilter = 'all' | 'malignant' | 'benign' | 'pediatric' | 'transplantIcu';

export const groupFilters: { key: GroupFilter; label: string; groups: DepartmentGroup[] }[] = [
  { key: 'all', label: 'Tất cả', groups: [] },
  { key: 'malignant', label: 'Huyết học ác tính', groups: ['malignantHematology'] },
  { key: 'benign', label: 'Huyết học lành tính', groups: ['benignHematology'] },
  { key: 'pediatric', label: 'Huyết học Nhi', groups: ['pediatricHematology'] },
  { key: 'transplantIcu', label: 'Ghép & Hồi sức', groups: ['stemCellTransplant', 'intensiveCare'] },
];

export function filterDepartments(departments: DepartmentSummary[], filter: GroupFilter): DepartmentSummary[] {
  const definition = groupFilters.find((f) => f.key === filter);
  if (!definition || definition.groups.length === 0) {
    return departments;
  }

  return departments.filter((d) => definition.groups.includes(d.group));
}

export interface Totals {
  capacity: number;
  occupied: number;
  occupancyPercent: number;
  available: number;
  cleaning: number;
  sterileAvailable: number;
  chemotherapy: number;
  units: number;
  transfusing: number;
  pendingRequests: number;
  neutropenic: number;
}

/** Client-side totals for the selected department group (same rounding as the API). */
export function sumDepartments(departments: DepartmentSummary[]): Totals {
  const sum = (pick: (d: DepartmentSummary) => number) => departments.reduce((acc, d) => acc + pick(d), 0);
  const capacity = sum((d) => d.capacity);
  const occupied = sum((d) => d.occupied);
  return {
    capacity,
    occupied,
    occupancyPercent: capacity === 0 ? 0 : Math.round((occupied * 1000) / capacity) / 10,
    available: sum((d) => d.available),
    cleaning: sum((d) => d.cleaning),
    sterileAvailable: sum((d) => (d.isSterileUnit ? d.available : 0)),
    chemotherapy: sum((d) => d.chemotherapyToday),
    units: sum((d) => d.unitsIssuedToday),
    transfusing: sum((d) => d.transfusing),
    pendingRequests: sum((d) => d.pendingBloodRequests),
    neutropenic: sum((d) => d.neutropenic),
  };
}

export function episodesFor(episodes: FebrileNeutropenia[], departments: DepartmentSummary[]): FebrileNeutropenia[] {
  const codes = new Set(departments.map((d) => d.code));
  return episodes.filter((e) => codes.has(e.departmentCode));
}

export function occupancyLevelOf(percent: number) {
  if (percent > 100) return 'overCapacity' as const;
  if (percent >= 95) return 'critical' as const;
  if (percent >= 85) return 'high' as const;
  return 'normal' as const;
}
