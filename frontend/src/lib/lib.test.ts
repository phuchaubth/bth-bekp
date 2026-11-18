import { describe, expect, it } from 'vitest';
import { department } from '../test/fixtures';
import { filterDepartments, occupancyLevelOf, sumDepartments } from './aggregate';
import { formatMinutes, formatPercent, formatTimer } from './format';
import { describeAlert, describeEvent, productWithGroup } from './labels';
import { febrileStatus } from './levels';
import { clampSeconds, nextRotation } from './rotation';
import { complianceTone, febrileElapsedSeconds, matchesFilter, sortDepartments } from './ui';

describe('format', () => {
  it('uses Vietnamese decimal comma', () => {
    expect(formatPercent(97.9)).toBe('97,9%');
    expect(formatPercent(100)).toBe('100,0%');
  });

  it('formats durations and timers', () => {
    expect(formatMinutes(42)).toBe('42 phút');
    expect(formatMinutes(135)).toBe('2 giờ 15 phút');
    expect(formatMinutes(120)).toBe('2 giờ');
    expect(formatTimer(42 * 60 + 7)).toBe('42:07');
    expect(formatTimer(3725)).toBe('1:02:05');
    expect(formatTimer(-5)).toBe('00:00');
  });
});

describe('occupancy and aggregation', () => {
  it('classifies like the API', () => {
    expect(occupancyLevelOf(84.9)).toBe('normal');
    expect(occupancyLevelOf(85)).toBe('high');
    expect(occupancyLevelOf(95)).toBe('critical');
    expect(occupancyLevelOf(100.1)).toBe('overCapacity');
  });

  it('filters by department group and sums totals', () => {
    const list = [department(), department({ code: 'DEP-0005', group: 'benignHematology', capacity: 40, occupied: 33, available: 6, unitsIssuedToday: 24 })];
    expect(filterDepartments(list, 'benign').map((d) => d.code)).toEqual(['DEP-0005']);
    expect(filterDepartments(list, 'all')).toHaveLength(2);
    const totals = sumDepartments(list);
    expect(totals.capacity).toBe(88);
    expect(totals.occupied).toBe(80);
    expect(totals.occupancyPercent).toBe(90.9);
    expect(totals.units).toBe(38);
  });

  it('sorts by occupancy descending or by code', () => {
    const list = [department({ code: 'DEP-0002', occupancyPercent: 50 }), department({ code: 'DEP-0001', occupancyPercent: 99 })];
    expect(sortDepartments(list, 'occupancy').map((d) => d.code)).toEqual(['DEP-0001', 'DEP-0002']);
    expect(sortDepartments(list, 'code').map((d) => d.code)).toEqual(['DEP-0001', 'DEP-0002']);
  });
});

describe('febrile neutropenia timer', () => {
  it('derives status from elapsed minutes', () => {
    expect(febrileStatus(10, false)).toBe('onTrack');
    expect(febrileStatus(30, false)).toBe('warning');
    expect(febrileStatus(60, false)).toBe('overdue');
    expect(febrileStatus(35, true)).toBe('metTarget');
    expect(febrileStatus(61, true)).toBe('missedTarget');
  });

  it('freezes elapsed time once antibiotics started', () => {
    const base = { id: 'x', stayCode: 'ADM-000001', departmentCode: 'DEP-0001', departmentName: '', bedCode: null, temperatureCelsius: 38.5, elapsedMinutes: 0, status: 'onTrack' as const };
    const now = new Date('2026-11-17T03:00:00Z');
    expect(febrileElapsedSeconds({ ...base, feverDetectedAtUtc: '2026-11-17T02:18:00Z', antibioticStartedAtUtc: null }, now)).toBe(42 * 60);
    expect(febrileElapsedSeconds({ ...base, feverDetectedAtUtc: '2026-11-17T02:00:00Z', antibioticStartedAtUtc: '2026-11-17T02:35:00Z' }, now)).toBe(35 * 60);
  });
});

describe('wallboard rotation', () => {
  it('advances screens and wraps to the overview', () => {
    const start = { screen: 0, startedAt: 0 };
    expect(nextRotation(start, 14_999, 15_000, 9)).toBe(start);
    expect(nextRotation(start, 15_000, 15_000, 9)).toEqual({ screen: 1, startedAt: 15_000 });
    expect(nextRotation({ screen: 8, startedAt: 0 }, 15_000, 15_000, 9)).toEqual({ screen: 0, startedAt: 15_000 });
    expect(nextRotation(start, 46_000, 15_000, 9)).toEqual({ screen: 3, startedAt: 45_000 });
  });

  it('resets when the screen list shrinks', () => {
    expect(nextRotation({ screen: 7, startedAt: 0 }, 1_000, 15_000, 3)).toEqual({ screen: 0, startedAt: 1_000 });
  });

  it('clamps the period', () => {
    expect(clampSeconds(null)).toBe(15);
    expect(clampSeconds('2')).toBe(5);
    expect(clampSeconds('999')).toBe(120);
    expect(clampSeconds('abc')).toBe(15);
  });
});

describe('labels', () => {
  it('formats alerts in Vietnamese from structured values', () => {
    expect(describeAlert({ type: 'bloodStockCritical', severity: 'critical', departmentCode: null, departmentName: null, subject: 'Platelets:O', value: 9, reference: 20, sinceUtc: null }).title).toBe('Tiểu cầu nhóm O dưới mức tối thiểu');
    expect(describeAlert({ type: 'febrileNeutropeniaAntibioticDelay', severity: 'critical', departmentCode: 'DEP-0001', departmentName: 'Bạch cầu cấp', subject: 'ADM-005112', value: 42, reference: 60, sinceUtc: null }).title).toBe('Sốt giảm BC: 42 phút chưa có kháng sinh');
    expect(productWithGroup('RedCells:AB')).toBe('Khối hồng cầu nhóm AB');
  });

  it('formats blood events', () => {
    const text = describeEvent({ id: '1', type: 'bloodRequestStatusChanged', occurredAtUtc: '', departmentCode: null, departmentName: null, stayCode: null, subject: '2 RedCells O+', value: 'Issued' }).text;
    expect(text).toBe('2 đv Khối hồng cầu O+ đã phát');
  });
});

describe('ui helpers', () => {
  it('maps compliance to labelled tones', () => {
    expect(complianceTone(null).label).toBe('Chưa có dữ liệu');
    expect(complianceTone(92).label).toBe('Đạt');
    expect(complianceTone(80).label).toBe('Cần cải thiện');
    expect(complianceTone(60).label).toBe('Chưa đạt');
  });

  it('filters beds', () => {
    const free = { code: '1', status: 'available' as const, isExtra: false, occupant: null };
    expect(matchesFilter(free, 'free')).toBe(true);
    expect(matchesFilter(free, 'neutropenic')).toBe(false);
  });
});

describe('base path (IIS sub-application)', () => {
  it('prefixes API and hub URLs', async () => {
    const { appUrl, routerBasename } = await import('./basePath');
    expect(appUrl('/api/dashboard/overview', '/')).toBe('/api/dashboard/overview');
    expect(appUrl('/api/dashboard/overview', '/bekp/')).toBe('/bekp/api/dashboard/overview');
    expect(appUrl('/hubs/dashboard', '/bekp')).toBe('/bekp/hubs/dashboard');
    expect(routerBasename('/')).toBe('');
    expect(routerBasename('/bekp/')).toBe('/bekp');
  });
});
