import type { BedTile, BloodRequest, DepartmentSummary, FebrileNeutropenia } from '../api/types';
import { aboLabel, modifierLabel, productLabel, type EventTone } from './labels';

export type BedFilter = 'all' | 'severe' | 'neutropenic' | 'transfusing' | 'chemotherapy' | 'expectedDischarge' | 'free';

export const bedFilters: { key: BedFilter; label: string }[] = [
  { key: 'all', label: 'Tất cả' },
  { key: 'severe', label: 'Nặng' },
  { key: 'neutropenic', label: 'Giảm BC' },
  { key: 'transfusing', label: 'Truyền máu' },
  { key: 'chemotherapy', label: 'Hóa trị' },
  { key: 'expectedDischarge', label: 'Dự kiến ra viện' },
  { key: 'free', label: 'Trống / vệ sinh' },
];

export function matchesFilter(bed: BedTile, filter: BedFilter): boolean {
  const o = bed.occupant;
  switch (filter) {
    case 'all':
      return true;
    case 'severe':
      return Boolean(o?.isSevere);
    case 'neutropenic':
      return Boolean(o?.isNeutropenic);
    case 'transfusing':
      return Boolean(o?.isTransfusing);
    case 'chemotherapy':
      return Boolean(o?.hasChemotherapyToday);
    case 'expectedDischarge':
      return Boolean(o?.expectedDischargeToday);
    case 'free':
      return bed.status !== 'occupied';
  }
}

export interface TileStyle {
  bg: string;
  border: string;
  borderStyle: 'solid' | 'dashed';
  borderWidth: number;
  status: string;
}

export function tileStyle(bed: BedTile, sterile: boolean): TileStyle {
  const o = bed.occupant;
  if (bed.status === 'available') {
    return { bg: '#EDF8F1', border: '#6CBF8C', borderStyle: 'solid', borderWidth: 1, status: sterile ? 'Phòng vô trùng trống' : 'Giường trống' };
  }

  if (bed.status === 'cleaning') {
    return { bg: '#FFF8EA', border: '#D9A84E', borderStyle: 'dashed', borderWidth: 1, status: 'Đang vệ sinh' };
  }

  if (bed.status === 'outOfService') {
    return { bg: '#F1F3F6', border: '#C8D1DC', borderStyle: 'dashed', borderWidth: 1, status: 'Tạm ngưng sử dụng' };
  }

  const base: TileStyle = o?.isSevere
    ? { bg: '#FFF1EF', border: '#E79A91', borderStyle: 'solid', borderWidth: 1, status: 'Nặng · theo dõi sát' }
    : o?.isNeutropenic
      ? { bg: '#F5F2FD', border: '#A893E0', borderStyle: 'solid', borderWidth: 1, status: 'Giảm BC · cách ly bảo vệ' }
      : o?.expectedDischargeToday
        ? { bg: '#F2F7FD', border: '#6E9FD6', borderStyle: 'dashed', borderWidth: 1, status: 'Dự kiến ra viện' }
        : { bg: '#FFFFFF', border: '#C8D1DC', borderStyle: 'solid', borderWidth: 1, status: 'Đang điều trị' };
  return bed.isExtra ? { ...base, border: '#B42318', borderStyle: 'dashed', borderWidth: 2 } : base;
}

export function describeRequest(r: BloodRequest): string {
  const modifiers = r.modifiers.map((m) => modifierLabel[m]).join(', ');
  return `${r.units} đv ${productLabel[r.product]} ${aboLabel[r.aboGroup]}${r.rhDPositive ? '+' : '−'}${modifiers ? ` · ${modifiers}` : ''}`;
}

export type SortKey = 'occupancy' | 'code';

export function sortDepartments(departments: DepartmentSummary[], sort: SortKey): DepartmentSummary[] {
  return [...departments].sort((a, b) => (sort === 'occupancy' ? b.occupancyPercent - a.occupancyPercent : a.code.localeCompare(b.code)));
}

export const eventToneColor: Record<EventTone, string> = {
  admission: '#2F6FB5',
  discharge: '#F0A35E',
  blood: '#C0265E',
  alert: '#B42318',
  bed: '#22A04B',
  clinical: '#6D4FC2',
};

export const ANTIBIOTIC_TARGET_MINUTES = 60;

/** Seconds since fever, frozen once antibiotics started. */
export function febrileElapsedSeconds(episode: FebrileNeutropenia, now: Date): number {
  const end = episode.antibioticStartedAtUtc ? new Date(episode.antibioticStartedAtUtc) : now;
  return Math.max(0, Math.floor((end.getTime() - new Date(episode.feverDetectedAtUtc).getTime()) / 1000));
}

/** Compliance rate colour: ≥ 90% met (blue), ≥ 75% watch (amber), else below (red). Always shown with the number. */
export function complianceTone(percent: number | null): { fg: string; bg: string; label: string } {
  if (percent === null) {
    return { fg: '#3D4756', bg: '#EEF0F3', label: 'Chưa có dữ liệu' };
  }

  if (percent >= 90) {
    return { fg: '#1D4F8C', bg: '#E8F1FB', label: 'Đạt' };
  }

  return percent >= 75 ? { fg: '#7A4A00', bg: '#FDF3E1', label: 'Cần cải thiện' } : { fg: '#A21B12', bg: '#FDE8E6', label: 'Chưa đạt' };
}
