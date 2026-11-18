import type {
  AboGroup,
  Alert,
  BloodProductModifier,
  BloodProductType,
  BloodRequestStatus,
  BloodRequestUrgency,
  CareLevel,
  DepartmentGroup,
  DiseaseGroup,
  OccupancyLevel,
  OperationalEvent,
  TimeStandardType,
} from '../api/types';
import { formatMinutes, formatPercent } from './format';

export const occupancyLabel: Record<OccupancyLevel, string> = {
  normal: 'Ổn định',
  high: 'Cao',
  critical: 'Quá tải',
  overCapacity: 'Vượt công suất',
};

export const departmentGroupLabel: Record<DepartmentGroup, string> = {
  malignantHematology: 'Huyết học ác tính',
  benignHematology: 'Huyết học lành tính',
  pediatricHematology: 'Huyết học Nhi',
  stemCellTransplant: 'Ghép tế bào gốc',
  intensiveCare: 'Hồi sức',
};

export const careLevelLabel: Record<CareLevel, string> = {
  level1: 'Cấp 1 – theo dõi sát',
  level2: 'Cấp 2',
  level3: 'Cấp 3',
};

export const productLabel: Record<BloodProductType, string> = {
  redCells: 'Khối hồng cầu',
  platelets: 'Tiểu cầu',
  plasma: 'Huyết tương tươi đông lạnh',
  cryoprecipitate: 'Tủa lạnh',
};

export const aboLabel: Record<AboGroup, string> = { o: 'O', a: 'A', b: 'B', ab: 'AB' };

export const modifierLabel: Record<BloodProductModifier, string> = {
  leukoreduced: 'lọc BC',
  irradiated: 'chiếu xạ',
  washed: 'rửa',
  extendedPhenotypeMatch: 'hòa hợp mở rộng',
};

export const urgencyLabel: Record<BloodRequestUrgency, string> = {
  routine: 'Thường quy',
  urgent: 'Khẩn',
  emergency: 'Cấp cứu',
};

export const requestStatusLabel: Record<BloodRequestStatus, string> = {
  requested: 'Chờ duyệt',
  crossMatching: 'Đang làm phản ứng hòa hợp',
  approved: 'Đã duyệt · chờ phát',
  issued: 'Đã phát · đang vận chuyển',
  transfusing: 'Đang truyền',
  completed: 'Hoàn tất',
  cancelled: 'Đã hủy',
};

export const diseaseGroupLabel: Record<DiseaseGroup, string> = {
  acuteMyeloidLeukemia: 'Lơ-xê-mi cấp dòng tủy',
  acuteLymphoblasticLeukemia: 'Lơ-xê-mi cấp dòng lympho',
  chronicLeukemia: 'Lơ-xê-mi mạn',
  hodgkinLymphoma: 'U lympho Hodgkin',
  nonHodgkinLymphoma: 'U lympho không Hodgkin',
  multipleMyeloma: 'Đa u tủy xương',
  myelodysplasticSyndrome: 'Hội chứng rối loạn sinh tủy',
  myeloproliferativeNeoplasm: 'Tân sản tăng sinh tủy',
  thalassemia: 'Thalassemia',
  aplasticAnemia: 'Suy tủy xương',
  otherAnemia: 'Thiếu máu khác',
  hemophilia: 'Hemophilia',
  otherCoagulationDisorder: 'Rối loạn đông máu khác',
  immuneThrombocytopenia: 'Giảm tiểu cầu miễn dịch',
  other: 'Khác',
};

export const timeStandardLabel: Record<TimeStandardType, string> = {
  bloodIssueEmergency: 'Phát máu – yêu cầu cấp cứu',
  bloodIssueUrgent: 'Phát máu – yêu cầu khẩn',
  bloodIssueRoutine: 'Phát máu – yêu cầu thường quy',
  transfusionStartAfterIssue: 'Nhận máu → bắt đầu truyền',
  febrileNeutropeniaAntibiotic: 'Sốt giảm BC → kháng sinh',
  bedTurnaround: 'Vệ sinh giường → sẵn sàng',
};

export function productWithGroup(subject: string | null): string {
  if (!subject) {
    return '';
  }

  const [product, group] = subject.split(':');
  const productText = productLabel[(product ?? '').charAt(0).toLowerCase() + (product ?? '').slice(1) as BloodProductType] ?? product;
  return `${productText} nhóm ${group ?? ''}`.trim();
}

/** Formats a structured alert from the API as Vietnamese title + detail. */
export function describeAlert(alert: Alert): { title: string; detail: string } {
  const where = alert.departmentName ?? '';
  const value = alert.value ?? 0;
  const reference = alert.reference ?? 0;
  switch (alert.type) {
    case 'departmentOverCapacity':
      return { title: `${where} vượt công suất ${formatPercent(value)}`, detail: `${reference} giường kê thêm đang sử dụng.` };
    case 'departmentFull':
      return { title: `${where} hết giường trống`, detail: `${value}/${reference} giường có người bệnh. Phối hợp ra viện sớm các ca dự kiến.` };
    case 'sterileRoomsFull':
      return { title: `${where}: hết phòng vô trùng`, detail: `${value}/${reference} phòng có người bệnh. Điều phối lịch nhập ghép.` };
    case 'febrileNeutropeniaAntibioticDelay':
      return {
        title: `Sốt giảm BC: ${formatMinutes(value)} chưa có kháng sinh`,
        detail: `${alert.subject ?? ''} · ${where}. Mục tiêu khởi động kháng sinh ≤ ${reference} phút.`,
      };
    case 'bloodStockCritical':
      return { title: `${productWithGroup(alert.subject)} dưới mức tối thiểu`, detail: `Còn ${value}/${reference} đơn vị. Cần điều phối nguồn máu.` };
    case 'bloodStockLow':
      return { title: `${productWithGroup(alert.subject)} ở mức thấp`, detail: `Còn ${value}/${reference} đơn vị.` };
    case 'highNeutropeniaLoad':
      return { title: `${where}: ${value}/${reference} người bệnh giảm BC nặng`, detail: 'Tăng cường kiểm soát nhiễm khuẩn, kiểm tra cách ly bảo vệ.' };
    case 'pendingBloodRequests':
      return { title: `${where}: ${value} yêu cầu chế phẩm máu đang chờ`, detail: 'Liên hệ Khoa Truyền máu nếu cần khẩn.' };
  }
}

export type EventTone = 'admission' | 'discharge' | 'blood' | 'alert' | 'bed' | 'clinical';

const bloodStatusShort: Record<string, string> = {
  CrossMatching: 'đang hòa hợp',
  Approved: 'đã duyệt',
  Issued: 'đã phát',
  Transfusing: 'bắt đầu truyền',
  Completed: 'hoàn tất',
  Cancelled: 'đã hủy',
};

function describeBloodSubject(subject: string | null): string {
  // "2 RedCells O+" → "2 đv Khối hồng cầu O+"
  const match = subject?.match(/^(\d+) (\w+) (\w+[+-])$/);
  if (!match) {
    return subject ?? '';
  }

  const [, units, product, group] = match;
  const key = ((product ?? '').charAt(0).toLowerCase() + (product ?? '').slice(1)) as BloodProductType;
  return `${units} đv ${productLabel[key] ?? product} ${group}`;
}

/** Vietnamese label and tone for a live operational event. */
export function describeEvent(event: OperationalEvent): { label: string; text: string; tone: EventTone } {
  const bed = event.subject ? ` · giường ${event.subject}` : '';
  switch (event.type) {
    case 'admission':
      return { label: 'Nhập viện', text: `${event.stayCode ?? ''}${bed}`, tone: 'admission' };
    case 'discharge':
      return { label: 'Ra viện', text: `${event.stayCode ?? ''}${bed}`, tone: 'discharge' };
    case 'bedStatusChanged':
      return { label: 'Giường', text: `${event.subject ?? ''} ${event.value === 'Available' ? 'sẵn sàng' : event.value === 'Cleaning' ? 'đang vệ sinh' : 'tạm ngưng'}`, tone: 'bed' };
    case 'clinicalStatusChanged':
      return { label: 'Cập nhật', text: `${event.stayCode ?? ''}${bed}`, tone: 'clinical' };
    case 'feverRecorded':
      return { label: 'Sốt giảm BC', text: `${event.stayCode ?? ''} · ${event.value?.replace('.', ',') ?? ''}°C`, tone: 'alert' };
    case 'antibioticStarted':
      return { label: 'Kháng sinh', text: `${event.stayCode ?? ''} · sau ${event.value ?? '?'} phút`, tone: 'clinical' };
    case 'bloodRequested':
      return { label: 'Yêu cầu máu', text: describeBloodSubject(event.subject), tone: 'blood' };
    case 'bloodRequestStatusChanged':
      return { label: 'Chế phẩm máu', text: `${describeBloodSubject(event.subject)} ${bloodStatusShort[event.value ?? ''] ?? ''}`.trim(), tone: 'blood' };
    case 'bloodInventoryUpdated':
      return { label: 'Tồn kho máu', text: `${event.subject ?? ''}: ${event.value ?? ''} đv`, tone: 'blood' };
  }
}
