// Mirrors BEKP.Application.Dashboard DTOs (camelCase JSON, string enums).

export type OccupancyLevel = 'normal' | 'high' | 'critical' | 'overCapacity';
export type DepartmentGroup =
  | 'malignantHematology'
  | 'benignHematology'
  | 'pediatricHematology'
  | 'stemCellTransplant'
  | 'intensiveCare';
export type BedStatus = 'available' | 'occupied' | 'cleaning' | 'outOfService';
export type BedCellState =
  | 'available'
  | 'cleaning'
  | 'outOfService'
  | 'occupied'
  | 'severe'
  | 'neutropenic'
  | 'expectedDischarge';
export type CareLevel = 'level1' | 'level2' | 'level3';
export type FebrileNeutropeniaStatus = 'onTrack' | 'warning' | 'overdue' | 'metTarget' | 'missedTarget';
export type BloodProductType = 'redCells' | 'platelets' | 'plasma' | 'cryoprecipitate';
export type AboGroup = 'o' | 'a' | 'b' | 'ab';
export type BloodProductModifier = 'leukoreduced' | 'irradiated' | 'washed' | 'extendedPhenotypeMatch';
export type BloodRequestUrgency = 'routine' | 'urgent' | 'emergency';
export type BloodRequestStatus = 'requested' | 'crossMatching' | 'approved' | 'issued' | 'transfusing' | 'completed' | 'cancelled';
export type StockLevel = 'adequate' | 'low' | 'critical';
export type AlertType =
  | 'departmentOverCapacity'
  | 'departmentFull'
  | 'sterileRoomsFull'
  | 'febrileNeutropeniaAntibioticDelay'
  | 'bloodStockCritical'
  | 'bloodStockLow'
  | 'highNeutropeniaLoad'
  | 'pendingBloodRequests';
export type AlertSeverity = 'critical' | 'warning';
export type OperationalEventType =
  | 'admission'
  | 'discharge'
  | 'bedStatusChanged'
  | 'clinicalStatusChanged'
  | 'feverRecorded'
  | 'antibioticStarted'
  | 'bloodRequested'
  | 'bloodRequestStatusChanged'
  | 'bloodInventoryUpdated';
export type DiseaseGroup =
  | 'acuteMyeloidLeukemia'
  | 'acuteLymphoblasticLeukemia'
  | 'chronicLeukemia'
  | 'hodgkinLymphoma'
  | 'nonHodgkinLymphoma'
  | 'multipleMyeloma'
  | 'myelodysplasticSyndrome'
  | 'myeloproliferativeNeoplasm'
  | 'thalassemia'
  | 'aplasticAnemia'
  | 'otherAnemia'
  | 'hemophilia'
  | 'otherCoagulationDisorder'
  | 'immuneThrombocytopenia'
  | 'other';
export type TimeStandardType =
  | 'bloodIssueEmergency'
  | 'bloodIssueUrgent'
  | 'bloodIssueRoutine'
  | 'transfusionStartAfterIssue'
  | 'febrileNeutropeniaAntibiotic'
  | 'bedTurnaround';
export type ReportPeriod = 'today' | 'last7Days' | 'last30Days';

export interface OperationalEvent {
  id: string;
  type: OperationalEventType;
  occurredAtUtc: string;
  departmentCode: string | null;
  departmentName: string | null;
  stayCode: string | null;
  subject: string | null;
  value: string | null;
}

export interface HospitalTotals {
  capacity: number;
  occupied: number;
  occupancyPercent: number;
  occupancyLevel: OccupancyLevel;
  available: number;
  cleaning: number;
  sterileRoomsAvailable: number;
  severe: number;
  neutropenic: number;
  transfusing: number;
  chemotherapyToday: number;
  expectedDischargesToday: number;
  pendingBloodRequests: number;
  unitsIssuedToday: number;
  admissionsToday: number;
  dischargesToday: number;
  febrileNeutropeniaActive: number;
  febrileNeutropeniaAwaitingAntibiotic: number;
}

export interface BedCell {
  code: string;
  state: BedCellState;
  isExtra: boolean;
}

export interface DepartmentSummary {
  code: string;
  name: string;
  description: string;
  group: DepartmentGroup;
  isSterileUnit: boolean;
  capacity: number;
  occupied: number;
  available: number;
  cleaning: number;
  extraBedsInUse: number;
  occupancyPercent: number;
  occupancyLevel: OccupancyLevel;
  severe: number;
  neutropenic: number;
  transfusing: number;
  chemotherapyToday: number;
  expectedDischargesToday: number;
  pendingBloodRequests: number;
  unitsIssuedToday: number;
  admissionsToday: number;
  dischargesToday: number;
  beds: BedCell[];
}

export interface FebrileNeutropenia {
  id: string;
  stayCode: string;
  departmentCode: string;
  departmentName: string;
  bedCode: string | null;
  feverDetectedAtUtc: string;
  temperatureCelsius: number;
  antibioticStartedAtUtc: string | null;
  elapsedMinutes: number;
  status: FebrileNeutropeniaStatus;
}

export interface BloodRequest {
  id: string;
  stayCode: string | null;
  departmentCode: string;
  bedCode: string | null;
  product: BloodProductType;
  aboGroup: AboGroup;
  rhDPositive: boolean;
  units: number;
  urgency: BloodRequestUrgency;
  modifiers: BloodProductModifier[];
  status: BloodRequestStatus;
  requestedAtUtc: string;
  statusChangedAtUtc: string;
}

export interface BloodInventory {
  product: BloodProductType;
  aboGroup: AboGroup;
  unitsAvailable: number;
  minimumUnits: number;
  level: StockLevel;
  updatedAtUtc: string;
}

export interface Alert {
  type: AlertType;
  severity: AlertSeverity;
  departmentCode: string | null;
  departmentName: string | null;
  subject: string | null;
  value: number | null;
  reference: number | null;
  sinceUtc: string | null;
}

export interface DiseasePatternEntry {
  group: DiseaseGroup;
  activeInpatients: number;
  admissionsLast30Days: number;
}

export interface DiseasePattern {
  activeTotal: number;
  admissionsTotal: number;
  groups: DiseasePatternEntry[];
}

export interface HospitalOverview {
  generatedAtUtc: string;
  totals: HospitalTotals;
  departments: DepartmentSummary[];
  alerts: Alert[];
  bloodInventory: BloodInventory[];
  febrileNeutropenia: FebrileNeutropenia[];
  diseasePattern: DiseasePattern;
  recentEvents: OperationalEvent[];
}

export interface BedOccupant {
  stayCode: string;
  dayOfStay: number;
  careLevel: CareLevel;
  principalDiagnosisCode: string;
  diseaseGroup: DiseaseGroup;
  isSevere: boolean;
  isNeutropenic: boolean;
  isTransfusing: boolean;
  hasChemotherapyToday: boolean;
  expectedDischargeToday: boolean;
  expectedDischargeDate: string | null;
}

export interface BedTile {
  code: string;
  status: BedStatus;
  isExtra: boolean;
  occupant: BedOccupant | null;
}

export interface Room {
  label: string;
  beds: BedTile[];
}

export interface DepartmentDetail {
  generatedAtUtc: string;
  summary: DepartmentSummary;
  rooms: Room[];
  febrileNeutropenia: FebrileNeutropenia[];
  bloodRequests: BloodRequest[];
  alerts: Alert[];
  diseasePattern: DiseasePattern;
  recentEvents: OperationalEvent[];
}

export interface DepartmentListItem {
  code: string;
  name: string;
  group: DepartmentGroup;
}

export interface TimeStandardStat {
  standard: TimeStandardType;
  targetMinutes: number;
  reference: string;
  referenceConfirmed: boolean;
  completed: number;
  withinTarget: number;
  compliancePercent: number | null;
  medianMinutes: number | null;
  p90Minutes: number | null;
  inProgress: number;
  inProgressOverdue: number;
}

export interface TimeStandardsReport {
  generatedAtUtc: string;
  fromUtc: string;
  period: ReportPeriod;
  departmentCode: string | null;
  standards: TimeStandardStat[];
}
