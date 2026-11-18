# Dashboard điều hành nội trú realtime – thiết kế

## Phạm vi (PR này)

- 3 màn hình: Toàn viện, Từng khoa, Phòng điều hành (luân phiên).
- Chuyên khoa Huyết học – Truyền máu: giảm bạch cầu trung tính nặng, sốt giảm BC (đồng hồ kháng sinh ≤ 60 phút), hóa trị trong ngày, yêu cầu & tồn kho chế phẩm máu, phòng vô trùng.
- Mô hình bệnh tật theo nhóm bệnh huyết học (ICD-10 chẩn đoán chính).
- Thống kê thời gian thực hiện theo quy định (cấu hình được mục tiêu và văn bản căn cứ).

## Mô hình dữ liệu (schema `bekp`)

| Bảng | Mô tả |
|---|---|
| `Departments` | Khoa (`DEP-0000`), nhóm khoa, đơn vị vô trùng |
| `Beds` | Giường (mã giường, phòng/khu, giường kê thêm, trạng thái) |
| `InpatientStays` | Đợt điều trị ẩn danh (`ADM-000000`), mức chăm sóc, ICD-10, nhóm bệnh, cờ giảm BC/truyền máu/hóa trị, dự kiến ra viện |
| `FebrileNeutropeniaEpisodes` | Sốt giảm BC: thời điểm sốt, nhiệt độ, thời điểm kháng sinh |
| `BloodRequests` | Yêu cầu chế phẩm: loại, nhóm ABO/Rh, số đơn vị, mức khẩn, yêu cầu đặc biệt, trạng thái |
| `BloodInventory` | Tồn kho theo chế phẩm × nhóm ABO, mức tối thiểu |
| `OperationalEvents` | Dòng sự kiện realtime (chỉ chứa mã) |
| `ProcessTimings` | Khoảng thời gian đã đo cho từng quy trình có quy định |

Khóa chính UUID; mã nghiệp vụ là cột riêng có chỉ mục duy nhất. Mọi thời điểm lưu UTC.

## Quy tắc nghiệp vụ chính

- Công suất giường = người bệnh đang điều trị / giường kế hoạch (không tính giường kê thêm). Ổn định < 85%, Cao 85–94%, Quá tải ≥ 95%, Vượt công suất > 100%.
- Giường chỉ chuyển sang *Đang điều trị* qua nhập viện; ra viện → *Đang vệ sinh* → *Trống* (đo thời gian quay vòng giường).
- Sốt giảm BC chỉ ghi nhận cho người bệnh đang có cờ giảm BC nặng, nhiệt độ 38,0–43,0 °C, mỗi người bệnh tối đa một đợt chưa dùng kháng sinh.
- Quy trình yêu cầu máu: Requested → (CrossMatching) → Approved → Issued → Transfusing → Completed; có thể hủy trước khi phát. Khi phát, tồn kho giảm tương ứng (không âm).
- Tồn kho: *Thấp* khi dưới mức tối thiểu, *Thiếu* khi dưới 50% mức tối thiểu.

## Cảnh báo

| Loại | Mức | Điều kiện |
|---|---|---|
| Vượt công suất | Nghiêm trọng | > 100% |
| Hết giường / hết phòng vô trùng | Cảnh báo | 0 giường trống |
| Sốt giảm BC chưa có kháng sinh | Nghiêm trọng (≥ 30 phút) / Cảnh báo | Đợt chưa có kháng sinh |
| Tồn kho thiếu / thấp | Nghiêm trọng / Cảnh báo | Theo mức tối thiểu |
| Tải giảm BC cao | Cảnh báo | > 30% người bệnh của khoa (khoa ≥ 5 người bệnh) |
| Yêu cầu máu tồn đọng | Cảnh báo | ≥ 4 yêu cầu chờ |

API trả cảnh báo dạng cấu trúc (loại, mức, giá trị, ngưỡng); giao diện tự định dạng tiếng Việt.

## Thời gian thực hiện theo quy định

| Quy trình | Bắt đầu → kết thúc | Mục tiêu tạm |
|---|---|---|
| Phát máu cấp cứu / khẩn / thường quy | Yêu cầu → phát | 30 / 60 / 240 phút |
| Bắt đầu truyền | Phát → bắt đầu truyền | 30 phút |
| Kháng sinh sốt giảm BC | Phát hiện sốt → liều kháng sinh đầu | 60 phút |
| Quay vòng giường | Ra viện → giường sẵn sàng | 60 phút |

Thống kê: số lượt, số đạt, tỷ lệ đạt, trung vị, P90 (nearest-rank), số đang thực hiện và số đang quá hạn. Mục tiêu và văn bản căn cứ cấu hình tại `TimeStandards:Standards`; khi `ReferenceConfirmed=false` giao diện hiện "Chưa xác nhận".

## Ví dụ tích hợp (HIS → BEKP)

```http
POST /api/inpatient/stays
Authorization: Bearer <token có vai trò bekp.integration>
Content-Type: application/json

{
  "departmentCode": "DEP-0001",
  "bedCode": "201-3",
  "stayCode": "ADM-005140",
  "careLevel": "level2",
  "principalDiagnosisCode": "C92.0",
  "expectedDischargeDate": "2026-11-30",
  "admittedAt": "2026-11-18T08:15:00+07:00"
}
```

```http
PUT /api/transfusion/requests/{id}/status
{ "status": "issued" }
```

## Bảo mật

- Không lưu thông tin định danh người bệnh; mã đợt điều trị phải khớp `ADM-\d{6}`, chẩn đoán chỉ là mã ICD-10.
- RBAC qua JWT; chế độ dev bị chặn ngoài môi trường Development; dữ liệu mô phỏng và bộ mô phỏng bị chặn ở Production.
- Kiểm tra đầu vào hai lớp (DataAnnotations ở API, validator ở Application); enum số nguyên bị từ chối.
- Header bảo mật (nosniff, DENY frame, CSP, no-referrer) ở API và nginx; secrets chỉ qua biến môi trường.
