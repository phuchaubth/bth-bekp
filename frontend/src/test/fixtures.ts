import type { DepartmentSummary, HospitalOverview, Room, TimeStandardsReport } from '../api/types';

/** Synthetic fixtures only. */
export function department(overrides: Partial<DepartmentSummary> = {}): DepartmentSummary {
  return {
    code: 'DEP-0001',
    name: 'Bạch cầu cấp người lớn',
    description: 'Khoa mô phỏng',
    group: 'malignantHematology',
    isSterileUnit: false,
    capacity: 48,
    occupied: 47,
    available: 0,
    cleaning: 1,
    extraBedsInUse: 0,
    occupancyPercent: 97.9,
    occupancyLevel: 'critical',
    severe: 7,
    neutropenic: 18,
    transfusing: 6,
    chemotherapyToday: 14,
    expectedDischargesToday: 2,
    pendingBloodRequests: 5,
    unitsIssuedToday: 14,
    admissionsToday: 3,
    dischargesToday: 1,
    beds: [
      { code: '201-1', state: 'severe', isExtra: false },
      { code: '201-2', state: 'available', isExtra: false },
    ],
    ...overrides,
  };
}

export function overview(): HospitalOverview {
  const departments = [
    department(),
    department({ code: 'DEP-0005', name: 'Thalassemia & Bệnh lý hồng cầu', group: 'benignHematology', capacity: 40, occupied: 33, available: 6, occupancyPercent: 82.5, occupancyLevel: 'normal', neutropenic: 0, chemotherapyToday: 0, unitsIssuedToday: 24, pendingBloodRequests: 6 }),
  ];
  return {
    generatedAtUtc: '2026-11-17T02:42:00Z',
    totals: {
      capacity: 88, occupied: 80, occupancyPercent: 90.9, occupancyLevel: 'high', available: 6, cleaning: 2, sterileRoomsAvailable: 0, severe: 8, neutropenic: 18,
      transfusing: 12, chemotherapyToday: 14, expectedDischargesToday: 4, pendingBloodRequests: 11, unitsIssuedToday: 38, admissionsToday: 5, dischargesToday: 2,
      febrileNeutropeniaActive: 1, febrileNeutropeniaAwaitingAntibiotic: 1,
    },
    departments,
    alerts: [
      { type: 'bloodStockCritical', severity: 'critical', departmentCode: null, departmentName: null, subject: 'Platelets:O', value: 9, reference: 20, sinceUtc: '2026-11-17T01:30:00Z' },
      { type: 'departmentFull', severity: 'warning', departmentCode: 'DEP-0001', departmentName: 'Bạch cầu cấp người lớn', subject: null, value: 47, reference: 48, sinceUtc: null },
    ],
    bloodInventory: [
      { product: 'platelets', aboGroup: 'o', unitsAvailable: 9, minimumUnits: 20, level: 'critical', updatedAtUtc: '2026-11-17T02:40:00Z' },
    ],
    febrileNeutropenia: [
      { id: 'fn-1', stayCode: 'ADM-005112', departmentCode: 'DEP-0001', departmentName: 'Bạch cầu cấp người lớn', bedCode: '201-3', feverDetectedAtUtc: '2026-11-17T02:00:00Z', temperatureCelsius: 38.9, antibioticStartedAtUtc: null, elapsedMinutes: 42, status: 'warning' },
    ],
    diseasePattern: { activeTotal: 80, admissionsTotal: 120, groups: [{ group: 'acuteMyeloidLeukemia', activeInpatients: 30, admissionsLast30Days: 40 }, { group: 'thalassemia', activeInpatients: 25, admissionsLast30Days: 60 }] },
    recentEvents: [
      { id: 'e1', type: 'admission', occurredAtUtc: '2026-11-17T02:41:00Z', departmentCode: 'DEP-0001', departmentName: 'Bạch cầu cấp người lớn', stayCode: 'ADM-005140', subject: '201-4', value: null },
      { id: 'e2', type: 'bloodRequestStatusChanged', occurredAtUtc: '2026-11-17T02:38:00Z', departmentCode: 'DEP-0005', departmentName: 'Thalassemia & Bệnh lý hồng cầu', stayCode: 'ADM-005131', subject: '2 RedCells O+', value: 'Issued' },
    ],
  };
}

export function rooms(): Room[] {
  return [
    {
      label: 'Phòng 201',
      beds: [
        { code: '201-1', status: 'occupied', isExtra: false, occupant: { stayCode: 'ADM-004100', dayOfStay: 5, careLevel: 'level1', principalDiagnosisCode: 'C92.0', diseaseGroup: 'acuteMyeloidLeukemia', isSevere: true, isNeutropenic: true, isTransfusing: true, hasChemotherapyToday: false, expectedDischargeToday: false, expectedDischargeDate: null } },
        { code: '201-2', status: 'occupied', isExtra: false, occupant: { stayCode: 'ADM-004107', dayOfStay: 12, careLevel: 'level3', principalDiagnosisCode: 'C91.0', diseaseGroup: 'acuteLymphoblasticLeukemia', isSevere: false, isNeutropenic: false, isTransfusing: false, hasChemotherapyToday: true, expectedDischargeToday: true, expectedDischargeDate: '2026-11-17' } },
        { code: '201-3', status: 'available', isExtra: false, occupant: null },
        { code: '201-4', status: 'cleaning', isExtra: false, occupant: null },
      ],
    },
  ];
}

export function timeStandards(): TimeStandardsReport {
  return {
    generatedAtUtc: '2026-11-17T02:42:00Z',
    fromUtc: '2026-11-16T17:00:00Z',
    period: 'today',
    departmentCode: null,
    standards: [
      { standard: 'bloodIssueEmergency', targetMinutes: 30, reference: 'Mục tiêu tạm – cần xác nhận', referenceConfirmed: false, completed: 10, withinTarget: 9, compliancePercent: 90, medianMinutes: 22, p90Minutes: 31, inProgress: 1, inProgressOverdue: 0 },
      { standard: 'bedTurnaround', targetMinutes: 60, reference: 'Quy định nội bộ', referenceConfirmed: true, completed: 0, withinTarget: 0, compliancePercent: null, medianMinutes: null, p90Minutes: null, inProgress: 2, inProgressOverdue: 1 },
    ],
  };
}
