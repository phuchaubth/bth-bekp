import type { BedCellState, FebrileNeutropeniaStatus, OccupancyLevel, StockLevel } from '../api/types';

/** Color tokens (light UI). Every color is paired with a text label; pairs differ in lightness. */
export const tokens = {
  navy: '#0E2A47',
  ink: '#0E1A2B',
  muted: '#5B6778',
  border: '#E3E8EF',
  page: '#F3F5F8',
  card: '#FFFFFF',
  link: '#0F5FA8',
  neutropenia: '#4B2F9E',
  blood: '#9D174D',
  ok: '#1E6B3A',
  danger: '#A21B12',
  warn: '#A8470A',
};

export const occupancyColors: Record<OccupancyLevel, { bar: string; bg: string; fg: string }> = {
  normal: { bar: '#3D7CC9', bg: '#E8F1FB', fg: '#1D4F8C' },
  high: { bar: '#E0A030', bg: '#FDF3E1', fg: '#7A4A00' },
  critical: { bar: '#B42318', bg: '#FDE8E6', fg: '#A21B12' },
  overCapacity: { bar: '#B42318', bg: '#B42318', fg: '#FFFFFF' },
};

export const bedCellColors: Record<BedCellState, { bg: string; border?: string }> = {
  occupied: { bg: '#9AABC0' },
  severe: { bg: '#B42318' },
  neutropenic: { bg: '#6D4FC2' },
  expectedDischarge: { bg: '#3D7CC9' },
  available: { bg: '#4FAE73' },
  cleaning: { bg: '#E0A030' },
  outOfService: { bg: '#D5DCE5' },
};

export const stockColors: Record<StockLevel, { bg: string; fg: string; border: string }> = {
  adequate: { bg: '#F1F3F6', fg: '#0E1A2B', border: 'transparent' },
  low: { bg: '#FDF3E1', fg: '#7A4A00', border: '#E0A030' },
  critical: { bg: '#B42318', fg: '#FFFFFF', border: '#B42318' },
};

/** Status of a febrile neutropenia timer, recomputed client-side every second. */
export function febrileStatus(elapsedMinutes: number, antibioticGiven: boolean): FebrileNeutropeniaStatus {
  if (antibioticGiven) {
    return elapsedMinutes <= 60 ? 'metTarget' : 'missedTarget';
  }

  if (elapsedMinutes >= 60) {
    return 'overdue';
  }

  return elapsedMinutes >= 30 ? 'warning' : 'onTrack';
}

export const febrileColors: Record<FebrileNeutropeniaStatus, string> = {
  onTrack: '#1D4F8C',
  warning: '#B86E00',
  overdue: '#B42318',
  metTarget: '#1D4F8C',
  missedTarget: '#A8470A',
};
