# Tài liệu API — Quản lý Phiếu đồng thuận

> Phiên bản API: `v1`. Tài liệu được cập nhật theo từng sprint (hiện tại: Sprint 4 — thêm ký tay trên máy tính bảng, xác nhận phía bệnh viện, PDF đã ký, thu hồi).
> Ở môi trường Development, đặc tả OpenAPI tự sinh có tại `GET /openapi/v1.json`.
>
> Mọi ví dụ dưới đây dùng **dữ liệu giả lập**.

## 1. Quy ước chung

| Chủ đề | Quy ước |
|---|---|
| Địa chỉ gốc | Cùng domain với giao diện web (qua reverse proxy): `/api/v1/...` |
| Định dạng | JSON (UTF-8), tên trường camelCase, thời gian ISO 8601 UTC (ví dụ `2026-11-11T08:30:00+00:00`), ngày dạng `yyyy-MM-dd`, giá trị liệt kê (enum) dạng chuỗi (ví dụ `"Female"`) |
| Xác thực | Header `Authorization: Bearer <accessToken>`. **Mọi endpoint đều yêu cầu đăng nhập**, trừ các endpoint ghi rõ "Không cần đăng nhập" |
| Phân quyền | Mỗi endpoint ghi rõ quyền cần có (cột "Quyền"). Thiếu quyền → `403` |
| Lỗi | ProblemDetails (RFC 9457), `Content-Type: application/problem+json`, có `title` tiếng Việt, `code` (mã ổn định để client xử lý), `traceId` (mã truy vết) |
| Lỗi theo trường | HTTP 400 kèm `errors`: `{ "tênTrường": ["thông báo", …] }` |
| Phân trang | Tham số `page` (≥ 1), `pageSize` (1–100) → `{ items, page, pageSize, totalCount, totalPages }` |
| Mã truy vết | Gửi header `X-Correlation-Id` (chữ, số, `-`, `_`, ≤ 64 ký tự) để gắn với log; phản hồi luôn trả lại header này |

### 1.1. Mã trạng thái HTTP

| Mã | Ý nghĩa |
|---|---|
| 200 / 201 / 204 | Thành công (201 khi tạo mới, kèm header `Location`; 204 khi không có nội dung) |
| 400 | Dữ liệu không hợp lệ (`code: "validation"`) |
| 401 | Chưa đăng nhập, token hết hạn/không hợp lệ, hoặc đăng nhập thất bại |
| 403 | Không có quyền; hoặc thiếu header `X-Requested-With` ở endpoint dùng cookie |
| 404 | Không tìm thấy |
| 409 | Trùng dữ liệu (ví dụ trùng mã khoa, trùng tên đăng nhập) hoặc xung đột cập nhật đồng thời |
| 422 | Vi phạm quy tắc nghiệp vụ (ví dụ xóa khoa đang có người dùng) |
| 429 | Gọi quá nhiều lần trong thời gian ngắn (`code: "rate_limited"`, có header `Retry-After`) |
| 500 | Lỗi hệ thống (không trả chi tiết nội bộ; dùng `traceId` để tra log) |

Ví dụ lỗi kiểm tra dữ liệu:

```json
{
  "title": "Dữ liệu không hợp lệ.",
  "status": 400,
  "errors": { "code": ["Mã khoa chỉ gồm chữ không dấu, số, dấu gạch ngang hoặc gạch dưới."] },
  "code": "validation",
  "traceId": "1da2816a5ab5f42f77f3c6fbfa2e6466"
}
```

---

## 2. Xác thực (`/api/v1/auth`)

**Cơ chế phiên đăng nhập**

- **Access token**: JWT, hạn 15 phút, trả trong body. Client **chỉ giữ trong bộ nhớ** (không lưu localStorage).
- **Refresh token**: chuỗi ngẫu nhiên, chỉ nằm trong cookie `consent_rt`: `HttpOnly`, `Secure` (bắt buộc HTTPS ngoài Development), `SameSite=Strict`, `Path=/api/v1/auth`, hạn 12 giờ. Máy chủ chỉ lưu giá trị băm SHA-256.
- Mỗi lần làm mới, refresh token cũ bị thu hồi và cookie nhận token mới (**xoay vòng**). Dùng lại token đã bị thay thế (ngoài 30 giây ân hạn cho trường hợp hai tab cùng làm mới) bị coi là token bị lộ: **toàn bộ phiên của thiết bị đó bị thu hồi** và ghi nhật ký `Auth.RefreshTokenReuseDetected`.
- Endpoint dùng cookie (`refresh`, `logout`) bắt buộc header `X-Requested-With` (chống CSRF).
- Đăng nhập sai **5 lần liên tiếp** → tài khoản tạm khóa **15 phút**.
- Tài khoản mới hoặc vừa được đặt lại mật khẩu (`mustChangePassword: true`) nhận access token **không có quyền nào** cho tới khi đổi mật khẩu.

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| POST | `/api/v1/auth/login` | Không cần đăng nhập · giới hạn 10 lần/phút/IP | Đăng nhập; đặt cookie refresh token |
| POST | `/api/v1/auth/refresh` | Không cần đăng nhập (dùng cookie) · cần `X-Requested-With` · 30 lần/phút/IP | Làm mới access token, xoay vòng refresh token |
| POST | `/api/v1/auth/logout` | Không cần đăng nhập (dùng cookie) · cần `X-Requested-With` | Thu hồi refresh token hiện tại, xóa cookie → `204` |
| GET | `/api/v1/auth/me` | Đã đăng nhập | Thông tin người dùng hiện tại, vai trò, quyền |
| POST | `/api/v1/auth/change-password` | Đã đăng nhập · 10 lần/phút/IP | Đổi mật khẩu; thu hồi các phiên khác; trả phiên mới |

`POST /api/v1/auth/login`

```json
// Yêu cầu
{ "userName": "bacsi.demo", "password": "********" }

// 200 OK (kèm Set-Cookie: consent_rt=...; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth)
{
  "accessToken": "eyJhbGciOi...",
  "accessTokenExpiresAtUtc": "2026-11-11T08:45:00+00:00",
  "user": {
    "id": "01a1ca94-9e64-7f54-9476-5c522f3a926a",
    "userName": "bacsi.demo",
    "fullName": "Bác sĩ Demo",
    "title": "Bác sĩ",
    "departmentId": "01a1ca94-9e60-7c35-8f0b-0b8f2f0f4a11",
    "departmentName": "Khoa Nội tổng hợp",
    "roles": [{ "id": "…", "code": "DOCTOR", "name": "Bác sĩ" }],
    "permissions": ["consents.create", "consents.read", "consents.sign", "patients.manage", "patients.read", "templates.read"],
    "mustChangePassword": false
  }
}
```

`POST /api/v1/auth/change-password`: `{ "currentPassword": "…", "newPassword": "…" }` → `200` với cùng cấu trúc như đăng nhập.

**Mã lỗi xác thực** (`code` trong ProblemDetails, HTTP 401 trừ khi ghi khác):

| `code` | Khi nào |
|---|---|
| `auth.invalid_credentials` | Sai tên đăng nhập hoặc mật khẩu (cùng một thông báo cho cả hai trường hợp) |
| `auth.locked_out` | Tài khoản đang tạm khóa do đăng nhập sai nhiều lần (thông báo kèm số phút còn lại) |
| `auth.inactive` | Tài khoản bị quản trị viên khóa (chỉ báo khi mật khẩu đúng) |
| `auth.refresh_invalid` | Refresh token không có, hết hạn, đã thu hồi hoặc bị phát hiện dùng lại |
| `auth.refresh_superseded` | Refresh token vừa được thay ở yêu cầu khác (ví dụ tab khác); client có thể thử lại sau giây lát |
| `auth.session_invalid` | Tài khoản không còn hợp lệ cho phiên hiện tại |
| `validation` (400) | Ví dụ mật khẩu hiện tại không đúng (`errors.currentPassword`), mật khẩu mới chưa đủ mạnh (`errors.newPassword`) |
| `request.missing_requested_with` (403) | Thiếu header `X-Requested-With` khi gọi `refresh`/`logout` |

**Chính sách mật khẩu**: 10–128 ký tự, có chữ hoa, chữ thường, chữ số, ký tự đặc biệt, không chứa tên đăng nhập.

---

## 3. Khoa/phòng (`/api/v1/departments`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/departments?includeInactive=false` | Đã đăng nhập | Danh sách khoa (mặc định chỉ khoa đang sử dụng) |
| GET | `/api/v1/departments/{id}` | Đã đăng nhập | Chi tiết |
| POST | `/api/v1/departments` | `departments.manage` | Thêm; `201` + `Location` |
| PUT | `/api/v1/departments/{id}` | `departments.manage` | Sửa (gồm ngừng/tiếp tục sử dụng) |
| DELETE | `/api/v1/departments/{id}` | `departments.manage` | Xóa khoa chưa có người dùng → `204`; khoa đang dùng → `422 departments.in_use` |

Dữ liệu gửi: `{ "code": "KKB", "name": "Khoa Khám bệnh", "isActive": true }`. Mã khoa được chuẩn hóa chữ hoa, duy nhất (`409 departments.code_exists`).

---

## 4. Người dùng (`/api/v1/users`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/users?keyword=&departmentId=&isActive=&page=1&pageSize=20` | `users.read` | Tìm kiếm có phân trang. `keyword` tìm theo họ tên/tên đăng nhập/chức danh, **không phân biệt dấu** ("dieu duong" khớp "Điều dưỡng") |
| GET | `/api/v1/users/{id}` | `users.read` | Chi tiết |
| POST | `/api/v1/users` | `users.manage` | Tạo người dùng với mật khẩu tạm; người dùng phải đổi mật khẩu ở lần đăng nhập đầu |
| PUT | `/api/v1/users/{id}` | `users.manage` | Sửa hồ sơ, khoa, vai trò, mã SmartCA |
| POST | `/api/v1/users/{id}/lock` | `users.manage` | Khóa tài khoản và thu hồi mọi phiên → `204` |
| POST | `/api/v1/users/{id}/unlock` | `users.manage` | Mở khóa (kể cả khóa tạm do đăng nhập sai) → `204` |
| POST | `/api/v1/users/{id}/reset-password` | `users.manage` | Đặt mật khẩu tạm `{ "newPassword": "…" }`, thu hồi mọi phiên, bắt buộc đổi ở lần đăng nhập sau → `204` |

Dữ liệu tạo người dùng:

```json
{
  "userName": "dieuduong.moi",
  "fullName": "Điều dưỡng Mới",
  "title": "Điều dưỡng",
  "departmentId": "…",
  "roleIds": ["…"],
  "email": "dd@example.test",
  "phoneNumber": "0900000000",
  "smartCaUserId": null,
  "password": "********"
}
```

Quy tắc nghiệp vụ: không tự khóa chính mình (`422 users.cannot_lock_self`); hệ thống luôn phải còn ít nhất một quản trị viên đang hoạt động (`422 users.last_administrator`); tên đăng nhập trùng → `409 users.username_exists`.

---

## 5. Vai trò và quyền

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/permissions` | Đã đăng nhập | Danh mục quyền: `[{ code, name, group }]` |
| GET | `/api/v1/roles` | Đã đăng nhập | Danh sách vai trò kèm mã quyền |
| PUT | `/api/v1/roles/{id}/permissions` | `roles.manage` | Thay toàn bộ quyền `{ "permissions": ["consents.read", …] }`. Vai trò Quản trị hệ thống luôn có toàn quyền và không sửa được (`422 roles.admin_immutable`) |

Quyền mới có hiệu lực khi access token được làm mới (tối đa 15 phút).

**Danh mục quyền (Sprint 1)**

| Nhóm | Mã quyền |
|---|---|
| Hệ thống | `users.read`, `users.manage`, `roles.manage`, `departments.manage`, `audit.read`, `settings.manage` |
| Người bệnh | `patients.read`, `patients.manage` |
| Mẫu phiếu | `templates.read`, `templates.manage` |
| Phiếu đồng thuận | `consents.read`, `consents.create`, `consents.sign`, `consents.revoke` |
| Báo cáo và xuất dữ liệu | `reports.read`, `exports.run`, `exports.sensitive` |
| Phạm vi dữ liệu | `scope.all_departments` (xem dữ liệu mọi khoa) |

**Vai trò mặc định**: `ADMIN` (Quản trị hệ thống), `TEMPLATE_MANAGER`, `DOCTOR`, `NURSE`, `RECEPTIONIST`, `DEPARTMENT_HEAD`, `REPORTER`, `AUDITOR`. Quyền mặc định của từng vai trò: xem `backend/src/Consent.Domain/Identity/SystemRoles.cs`. Quyền thu hồi phiếu (`consents.revoke`) mặc định có ở Quản trị hệ thống và Trưởng khoa.

---

## 6. Người bệnh (`/api/v1/patients`)

**Bảo vệ dữ liệu cá nhân**

- Số CCCD, số CMND cũ, số điện thoại, mã thẻ BHYT (của người bệnh và người đại diện) được **mã hóa AES-256-GCM** trong CSDL.
- Tra cứu và phát hiện trùng số CCCD dùng **chỉ mục mù** (HMAC-SHA256 có khóa bí mật), không cần giải mã hay lưu số gốc dạng đọc được.
- **Số CCCD không bao giờ nằm trong URL** (tránh lộ qua log máy chủ, proxy, lịch sử trình duyệt): tra cứu theo CCCD dùng `POST /lookup` với số CCCD trong body.
- Danh sách chỉ trả số CCCD **đã che** (`0361******25`). Hồ sơ chi tiết trả số đầy đủ, và **mỗi lần xem đều được ghi nhật ký** (`Patients.Viewed`).
- Nhật ký audit luôn che các giá trị nhạy cảm (`***`).

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/patients?keyword=&page=1&pageSize=20` | `patients.read` | Tìm kiếm. `keyword` đủ 12 chữ số → tìm chính xác theo CCCD; còn lại → mã người bệnh (chính xác) hoặc họ tên (**không phân biệt dấu**) |
| GET | `/api/v1/patients/{id}` | `patients.read` | Hồ sơ đầy đủ, kèm người đại diện; ghi nhật ký `Patients.Viewed` |
| POST | `/api/v1/patients/lookup` | `patients.read` | Tra cứu khi quét CCCD `{ "citizenId": "…" }` → hồ sơ đã có (nếu có) + kết quả HIS (`Disabled` khi chưa kết nối HIS); ghi nhật ký `Patients.LookedUp` (không ghi số CCCD) |
| POST | `/api/v1/patients` | `patients.manage` | Tạo hồ sơ → `201`. Trùng CCCD → `409 patients.duplicate_citizen_id` kèm `existingPatientId`; trùng mã người bệnh → `409 patients.duplicate_patient_code` |
| PUT | `/api/v1/patients/{id}` | `patients.manage` | Cập nhật hồ sơ |
| POST | `/api/v1/patients/{id}/representatives` | `patients.manage` | Thêm người đại diện/người ký thay → trả hồ sơ cập nhật |
| PUT | `/api/v1/patients/{id}/representatives/{representativeId}` | `patients.manage` | Sửa người đại diện |
| DELETE | `/api/v1/patients/{id}/representatives/{representativeId}` | `patients.manage` | Xóa người đại diện |

Dữ liệu tạo/cập nhật người bệnh (chỉ `fullName`, `dateOfBirth`, `gender` là bắt buộc):

```json
{
  "fullName": "Phạm Thị Dung (giả lập)",
  "dateOfBirth": "1988-04-05",
  "gender": "Female",
  "citizenId": "036188000125",
  "oldIdNumber": null,
  "citizenIdIssueDate": "2021-06-01",
  "permanentAddress": "Số 9 Đường Thử Nghiệm, Phường Mẫu",
  "currentAddress": null,
  "phoneNumber": "0900000125",
  "ethnicity": "Kinh",
  "occupation": null,
  "nationality": "Việt Nam",
  "healthInsuranceNumber": null,
  "patientCode": null
}
```

**Quy tắc kiểm tra** (lỗi trả về theo từng trường, tiếng Việt):

| Trường | Quy tắc |
|---|---|
| `citizenId` | 12 chữ số, 3 số đầu là mã tỉnh 001–096; **số thứ 4 (thế kỷ + giới tính) và số 5–6 (năm sinh) phải khớp** `dateOfBirth` và `gender` |
| `dateOfBirth` | Từ 01/01/1900 đến hôm nay |
| `citizenIdIssueDate` | Sau ngày sinh, không sau hôm nay |
| `oldIdNumber` | 9 hoặc 12 chữ số |
| `phoneNumber` | Bắt đầu bằng `0` hoặc `+84`, 9–10 chữ số tiếp theo |
| `healthInsuranceNumber` | 15 ký tự (2 chữ + 13 số) hoặc mã số BHXH 10 chữ số |
| `patientCode` | Chữ không dấu, số, `-`, `_`; tối đa 50 ký tự |

Người đại diện: `{ "fullName", "relationship", "citizenId", "dateOfBirth", "gender", "phoneNumber", "address" }`. `relationship` ∈ `Father`, `Mother`, `Spouse`, `Child`, `Sibling`, `Guardian`, `Relative`, `Other`.

**Đối chiếu HIS (FPT eHospital) — hiện chưa kết nối**

- **Mặc định (`His:Provider = None`)**: `POST /lookup` trả `hisStatus: "Disabled"`, `hisPatient: null`; khi lưu không đối chiếu (`hisVerified: false`), `patientCode` là giá trị nhân viên nhập tay. Giao diện ẩn mọi thông tin HIS (xem mục 6a).
- Khi đã kết nối HIS, `POST /lookup` trả `hisStatus`: `Found` (kèm `hisPatient`: mã người bệnh, đối tượng BHYT/Dịch vụ, mã thẻ BHYT, đợt khám hiện tại), `NotFound`, hoặc `Unavailable` (HIS lỗi/quá thời gian `His:TimeoutSeconds`, mặc định 5 giây).
- Khi **lưu** hồ sơ (đã kết nối HIS), máy chủ tự đối chiếu lại với HIS (không tin cờ từ client): tìm thấy và mã người bệnh khớp (hoặc để trống) → `hisVerified: true`, `patientCode` lấy từ HIS. HIS không khả dụng → vẫn lưu, `hisVerified: false` ("Chưa đối chiếu HIS").
- Đổi mã người bệnh thủ công sẽ xóa trạng thái đã đối chiếu.

**HIS giả lập** (chỉ dùng khi phát triển/kiểm thử, `His:Provider = Fake`; API **từ chối khởi động** nếu bật ở môi trường Production). Kết quả xác định theo số CCCD:

| Số CCCD | Kết quả |
|---|---|
| 3 số cuối `999` | HIS không khả dụng (`Unavailable`) |
| Số cuối `0` | Không tìm thấy (`NotFound`) |
| Số cuối chẵn khác 0 | Tìm thấy, đối tượng BHYT |
| Số cuối lẻ | Tìm thấy, đối tượng Dịch vụ |

## 6a. Tính năng tích hợp (`/api/v1/features`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/features` | Đăng nhập | Tích hợp đang bật, để giao diện ẩn/hiện phần tương ứng |

```json
{ "his": false, "smartCa": false }
```

- `his`: đã kết nối HIS (theo `His:Provider`). `false` → giao diện ẩn khung kết quả HIS, cột/nhãn "Đối chiếu HIS"; mã người bệnh nhập tay.
- `smartCa`: đã tích hợp ký số VNPT SmartCA. Hiện luôn `false` (tạm hoãn) → ẩn ô "Mã tài khoản SmartCA" ở màn hình người dùng (giá trị đã lưu được giữ nguyên khi sửa).

---

## 6b. Mẫu phiếu (`/api/v1/consent-templates`)

Mỗi mẫu có nhiều **phiên bản**: `Draft` (nháp, sửa được) → `Published` (đang phát hành, dùng để lập phiếu mới) → `Retired` (ngừng dùng). Mỗi mẫu có **tối đa một** phiên bản nháp và **tối đa một** phiên bản đang phát hành; phát hành phiên bản mới thì phiên bản cũ tự chuyển sang `Retired`. Phiếu đã lập **giữ nguyên phiên bản mẫu** tại thời điểm lập.

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/consent-templates?includeInactive=false` | `templates.read` | Danh sách mẫu (sắp theo mã), kèm phiên bản đang phát hành và phiên bản nháp |
| GET | `/api/v1/consent-templates/available?departmentId=` | `consents.create` | Mẫu **đang phát hành** dùng được để lập phiếu cho khoa (mặc định: khoa của người dùng): mẫu toàn viện và mẫu riêng của khoa. Trả định nghĩa của phiên bản đang phát hành |
| GET | `/api/v1/consent-templates/{id}` | `templates.read` | Chi tiết mẫu và các phiên bản (mới nhất trước) |
| GET | `/api/v1/consent-templates/{id}/versions/{versionNo}` | `templates.read` | Một phiên bản, kèm định nghĩa (`definition`) |
| POST | `/api/v1/consent-templates` | `templates.manage` | Tạo mẫu `{ code, name, hospitalSignerRoleCode, departmentId }` → `201`, có sẵn phiên bản nháp 1. `code`: 2–20 ký tự (chữ không dấu, số, `-`), lưu chữ hoa; trùng → `409 consent_templates.duplicate_code` |
| PUT | `/api/v1/consent-templates/{id}` | `templates.manage` | Sửa tên, vai trò xác nhận phía bệnh viện, khoa áp dụng, `isActive` (mã mẫu không đổi được) |
| POST | `/api/v1/consent-templates/{id}/versions` | `templates.manage` | Tạo phiên bản nháp mới, chép từ phiên bản mới nhất → `201`. Đã có nháp → `409 consent_templates.draft_exists` |
| PUT | `/api/v1/consent-templates/{id}/versions/{versionNo}` | `templates.manage` | Lưu định nghĩa của phiên bản nháp `{ "definition": { … } }`. Không phải nháp → `409 consent_templates.not_draft`; định nghĩa sai → `400 consent_templates.invalid_definition` kèm `errors.definition` (danh sách lỗi, chỉ rõ phần/khối) |
| POST | `/api/v1/consent-templates/{id}/versions/{versionNo}/publish` | `templates.manage` | Phát hành phiên bản nháp (kiểm tra lại định nghĩa trước khi phát hành) |
| POST | `/api/v1/consent-templates/{id}/versions/{versionNo}/retire` | `templates.manage` | Ngừng dùng phiên bản đang phát hành (mẫu không dùng được để lập phiếu mới cho tới khi phát hành phiên bản khác). Không phải phiên bản đang phát hành → `409 consent_templates.not_published` |

`hospitalSignerRoleCode` là mã vai trò người xác nhận phía bệnh viện (`DOCTOR`, `NURSE`, `RECEPTIONIST`, `DEPARTMENT_HEAD`…; phải là vai trò đang có). `departmentId = null` → mẫu dùng toàn viện.

**Định nghĩa mẫu** (`definition`) chỉ mô tả phần riêng của từng mẫu. Quốc hiệu, tên đơn vị, số phiếu, khối thông tin người bệnh và khối chữ ký do hệ thống tự thêm.

```json
{
  "title": "Giấy cam kết\nsử dụng dịch vụ khám bệnh, chữa bệnh",
  "subtitle": null,
  "sections": [
    {
      "title": "II. Hình thức khám",
      "blocks": [
        { "type": "choice", "key": "examType", "label": "Hình thức khám", "required": true, "layout": "inline",
          "options": [ { "value": "bhyt", "label": "Khám BHYT" }, { "value": "service", "label": "Khám dịch vụ" } ] },
        { "type": "text", "key": "insuranceNumber", "label": "Số thẻ BHYT", "required": true,
          "prefill": "patient.healthInsuranceNumber", "visibleWhen": { "field": "examType", "in": ["bhyt"] } },
        { "type": "paragraph", "text": "Tôi cam kết thanh toán chi phí ngoài phạm vi BHYT…" },
        { "type": "confirm", "key": "acknowledged", "label": "Tôi đã đọc và hiểu nội dung cam kết.", "required": true }
      ]
    }
  ],
  "signatures": { "patient": "Người bệnh", "representative": "Người đại diện", "hospital": "Nhân viên tiếp đón" }
}
```

| Kiểu khối (`type`) | Ý nghĩa | Thuộc tính riêng |
|---|---|---|
| `paragraph`, `list` | Đoạn văn, danh sách cố định (không nhập liệu) | `text`; `items`, `layout: "ordered"` |
| `text` | Ô chữ | `maxLength` (1–4.000), `pattern` + `patternMessage`, `prefill` |
| `date`, `datetime` | Ngày (`yyyy-MM-dd`), ngày giờ (`yyyy-MM-ddTHH:mm`, giờ Việt Nam) | |
| `choice` | Chọn một | `options` (`value`, `label`, `score` nếu là câu hỏi tính điểm), `layout: "inline"` |
| `multiChoice` | Chọn nhiều | `options`, `minItems`, `maxItems`, `requireAll` (phải chọn đủ mọi mục) |
| `confirm` | Ô xác nhận (phải đánh dấu nếu `required`) | |
| `repeater` | Bảng nhiều dòng | `columns` (`key`, `label`, `required`, `maxLength`, `pattern`), `minItems`, `maxItems` |
| `score` | Tổng điểm tự tính (không nhập) | `sumOf` (câu hỏi tính điểm đứng trước), `levels` (`code`, `label`, `min` tăng dần) |

- Mọi khối nhập liệu có `key` (bắt đầu bằng chữ cái; chữ không dấu, số, `_`; không trùng).
- `visibleWhen`: `{ "field", "in": [...] }` (giá trị của một trường đứng trước thuộc danh sách; chọn nhiều thì chỉ cần một), hoặc `{ "field", "minLevel" }` (trường điểm đạt tối thiểu mức). Trường bị ẩn thì không bắt buộc, và giá trị của nó không được in hay giữ lại khi trình ký.
- `prefill`: tự điền khi lập phiếu từ `patient.healthInsuranceNumber`, `patient.phoneNumber`, `patient.patientCode`.
- 3 mẫu giai đoạn 1 (CK-01, TT-01, TN-01) nằm trong mã nguồn (`Consent.Application/ConsentTemplates/Defaults/*.json`), nội dung theo [mẫu phiếu đề xuất](mau-phieu-de-xuat.md). Khi khởi động với `Seed:Enabled`, mẫu nào chưa có (theo mã) thì được tạo: ở trạng thái **nháp** để quản trị viên duyệt rồi phát hành, hoặc **phát hành sẵn** khi bật `Seed:DemoData`. Mẫu đã có không bị ghi đè.

## 6c. Phiếu đồng thuận (`/api/v1/consent-forms`)

**Phạm vi khoa:** người dùng chỉ thấy phiếu **của khoa mình** hoặc **do mình lập**, trừ khi có quyền `scope.all_departments`. Phiếu ngoài phạm vi trả `404` (không tiết lộ phiếu có tồn tại).

**Vòng đời:** `Draft` (nháp) → `PendingSignature` (chờ ký: người bệnh ký tay, rồi phía bệnh viện xác nhận) → `Completed` (hoàn tất, phiếu bị khóa) → `Revoked` (thu hồi). `Draft`/`PendingSignature` → `Cancelled` (hủy, bắt buộc lý do). Chuyển trạng thái sai quy tắc → `409 consent_forms.invalid_status` kèm thông báo tiếng Việt (ví dụ "Chỉ trình ký được phiếu nháp.").

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/consent-forms?status=&templateId=&patientId=&departmentId=&from=&to=&keyword=&page=1&pageSize=20` | `consents.read` | Tìm kiếm, phân trang ở máy chủ, mới nhất trước. `from`/`to` là ngày lập (`yyyy-MM-dd`, giờ Việt Nam, gồm cả ngày `to`). `keyword`: số phiếu, họ tên người bệnh (không phân biệt dấu) hoặc mã người bệnh |
| GET | `/api/v1/consent-forms/{id}` | `consents.read` | Chi tiết phiếu, kèm định nghĩa của **đúng phiên bản mẫu** đã dùng, dữ liệu, điểm, các mục còn thiếu (`issues`) và thao tác được phép (`actions`); ghi nhật ký `ConsentForms.Viewed` |
| GET | `/api/v1/consent-forms/{id}/pdf` | `consents.read` | File PDF khổ A4 (`application/pdf`, tên `PDT-2026-000001.pdf`). Phiếu chưa hoàn tất: bản xem trước dựng lại mỗi lần, có dòng **"BẢN XEM TRƯỚC – CHƯA CÓ CHỮ KÝ"**. Phiếu hoàn tất/thu hồi: trả **đúng file đã lưu lúc hoàn tất**, sau khi kiểm tra SHA-256 (file mất, hỏng hoặc bị sửa → `409 consent_forms.signed_pdf_unavailable`). Ghi nhật ký `ConsentForms.PdfViewed` |
| POST | `/api/v1/consent-forms` | `consents.create` | Lập phiếu nháp `{ patientId, templateId, departmentId?, replacesFormId? }` → `201`. Dùng **phiên bản đang phát hành** của mẫu (mẫu chưa phát hành, ngừng dùng hoặc không dùng cho khoa → `400` ở trường `templateId`); điền sẵn các trường có `prefill`. `departmentId` mặc định là khoa của người dùng; lập cho khoa khác cần quyền toàn viện (`403 consent_forms.department_forbidden`) |
| PUT | `/api/v1/consent-forms/{id}` | `consents.create` | Lưu nháp `{ data, signerKind, representativeId }`. Chưa bắt buộc đầy đủ; các mục còn thiếu trả về trong `issues`. Chỉ phiếu nháp |
| POST | `/api/v1/consent-forms/{id}/submit` | `consents.create` | Trình ký: kiểm tra đầy đủ theo định nghĩa mẫu (thiếu → `400 consent_forms.incomplete` kèm `errors` theo từng trường), bỏ giá trị của trường đang ẩn, **đóng băng** thông tin người bệnh và người ký (sửa hồ sơ sau đó không làm đổi phiếu) |
| POST | `/api/v1/consent-forms/{id}/return-to-draft` | `consents.create` | Trả phiếu đang chờ ký về nháp để sửa (bỏ thông tin đã đóng băng) |
| POST | `/api/v1/consent-forms/{id}/cancel` | `consents.create` | Hủy phiếu nháp hoặc chờ ký `{ "reason": "…" }` (5–500 ký tự) |
| POST | `/api/v1/consent-forms/{id}/signing-session` | `consents.create` | Mở phiên ký trên máy tính bảng → `{ token, expiresAtUtc }` (mặc định 30 phút). Chỉ cho phiếu chờ ký mà người bệnh chưa ký; mở phiên mới thì phiên cũ hết hiệu lực. Ghi nhật ký `ConsentForms.SigningStarted` |
| POST | `/api/v1/consent-forms/{id}/patient-signature` | **Token phiên ký** | Người bệnh/người đại diện ký: `{ "decision": "Agreed" \| "Refused", "image": "<PNG Base64>" }` → `{ decision, signedAtUtc }`. Xem mục "Ký tay" bên dưới. Ghi nhật ký `ConsentForms.PatientSigned` |
| POST | `/api/v1/consent-forms/{id}/complete` | `consents.sign` + vai trò theo mẫu | Phía bệnh viện xác nhận bằng tài khoản → phiếu `Completed`. Người bệnh chưa ký → `409`; không có vai trò xác nhận của mẫu → `403 consent_forms.signer_role_required`. Ghi nhật ký `ConsentForms.Completed` (kèm SHA-256) |
| POST | `/api/v1/consent-forms/{id}/revoke` | `consents.revoke` | Thu hồi phiếu đã hoàn tất `{ "reason": "…" }` (5–500 ký tự). Ghi nhật ký `ConsentForms.Revoked` |

- `signerKind`: `Patient` (người bệnh tự ký) hoặc `Representative` (người đại diện ký thay; `representativeId` phải thuộc hồ sơ người bệnh).
- `data`: đối tượng `{ "tênTrường": giá trị }`. Giá trị là chuỗi (`text`, `date`, `datetime`, `choice`), mảng chuỗi (`multiChoice`), `true` (`confirm`) hoặc mảng dòng `[{ "cột": "…" }]` (`repeater`). Trường không có trong định nghĩa bị bỏ qua; nội dung tối đa 100.000 ký tự.
- `scores`: `{ "tênTrườngĐiểm": { "total": 45, "levelCode": "high", "levelLabel": "Nguy cơ cao" } }` (`total = null` khi chưa trả lời đủ câu).
- Số phiếu `PDT-<năm>-<6 chữ số>` lấy từ sequence của SQL Server, không trùng kể cả khi nhiều người lập cùng lúc.
- Nội dung phiếu (`data`) và thông tin đóng băng có dữ liệu cá nhân nên được **mã hóa AES-256-GCM** trong CSDL; nhật ký audit che các giá trị này (`***`).
- Tên đơn vị in trên PDF lấy từ cấu hình `Hospital:ParentAgency`, `Hospital:Name`, `Hospital:Place` (**đang để giá trị mẫu, chờ bệnh viện cung cấp**).
- Chi tiết phiếu có thêm `decision` (`Agreed`/`Refused`), `signatures` (`role`: `Patient`/`Representative`/`Hospital`; `method`: `Handwritten`/`AccountConfirmation`; `signerName`, `signerTitle`, `signedAtUtc`; **không kèm ảnh chữ ký**), `signedPdfSha256`, `replacesForm`/`replacedBy` (`{ id, formNo }`) và `actions`: `canEdit`, `canSubmit`, `canReturnToDraft`, `canCancel`, `canStartSigning`, `canConfirm`, `canRevoke`, `canReplace`.

**Ký tay trên máy tính bảng (quyết định 10, 11)**

1. **Mở phiên ký:** nhân viên gọi `signing-session`, nhận **token phiên ký**. Token này có đối tượng nhận (`aud`) riêng:
   - Chỉ dùng được ở `patient-signature`, cho đúng phiếu và đúng phiên ghi trong token.
   - Mọi API khác từ chối token này (`401`); ngược lại, `patient-signature` không nhận token đăng nhập thường (`401`).
   - Giao diện đóng phiên đăng nhập của nhân viên trên máy ngay sau khi lấy token. Người bệnh cầm máy không làm được gì khác; muốn thoát chế độ ký phải đăng nhập lại.
2. **Người bệnh ký** (`patient-signature`): mỗi phiên chỉ ký được một lần (ký lại → `409`). Người ký là người đã chọn khi soạn phiếu (người bệnh, hoặc người đại diện) và đã đóng băng khi trình ký.
3. **Ảnh chữ ký:**
   - Yêu cầu: PNG 8 bit không xen kẽ (đúng loại trình duyệt xuất ra), tối đa 512 KB và 3.000×1.500 điểm ảnh.
   - Máy chủ tự giải mã ảnh. Sai định dạng hoặc quá lớn → `400` (trường `image`); khung trống hoặc nét quá ngắn → `422 consent_forms.signature_blank`.
   - Ảnh được cắt sát nét ký và mã hóa lại thành PNG xám; mọi dữ liệu kèm theo trong file gốc bị bỏ.
   - Ảnh được lưu **mã hóa** và không có API trả ảnh riêng: chỉ xem được trong PDF, tức là cần quyền xem phiếu.
4. **Hoàn tất** (`complete`):
   - Người xác nhận phải có **vai trò ghi trong mẫu** (`hospitalSignerRoleCode`).
   - Máy chủ dựng PDF bản cuối: ảnh chữ ký, Đồng ý/Từ chối, người đại diện (nếu có), họ tên, chức danh và thời điểm xác nhận.
   - File PDF được lưu (mã hóa) trong thư mục `Storage:DocumentsPath` kèm SHA-256. Phiếu bị khóa.
5. **Thu hồi rồi lập phiếu thay thế:** `POST /consent-forms` với `replacesFormId` của phiếu đã thu hồi (cùng người bệnh, cùng mẫu; mỗi phiếu chỉ có một phiếu thay thế, lập lần hai → `409 consent_forms.already_replaced`). Nội dung phiếu cũ được chép sang (chuẩn hóa theo phiên bản mẫu đang phát hành).

---

## 7. Nhật ký audit (`/api/v1/audit-logs`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/audit-logs?userId=&action=&entityType=&entityId=&fromUtc=&toUtc=&page=1&pageSize=50` | `audit.read` | Tra cứu, mới nhất trước |

**Không có API sửa hoặc xóa nhật ký.** Bảng `AuditLogs` còn được bảo vệ bằng trigger trong SQL Server (mọi lệnh UPDATE/DELETE đều bị từ chối).

Hành động (`action`) được ghi:

| Hành động | Nguồn |
|---|---|
| `Create`, `Update`, `Delete` | Tự động cho mọi thay đổi trên khoa, người dùng, vai trò, gán vai trò, gán quyền, người bệnh, người đại diện, mẫu phiếu, phiên bản mẫu, phiếu đồng thuận. `details` chứa giá trị (tạo/xóa) hoặc `{ "Trường": { "old": …, "new": … } }` (sửa); dữ liệu nhạy cảm (mật khẩu đã băm, số CCCD, số điện thoại, mã BHYT, nội dung phiếu) luôn hiển thị `***` |
| `Auth.LoginSucceeded`, `Auth.LoginFailed`, `Auth.LockedOut` | Đăng nhập (`details.reason`: `unknown_user`, `invalid_password`, `locked_out`, `inactive`) |
| `Auth.Logout`, `Auth.PasswordChanged`, `Auth.RefreshTokenReuseDetected` | Phiên đăng nhập |
| `Patients.Viewed`, `Patients.LookedUp` | Xem hồ sơ người bệnh; tra cứu theo CCCD (không ghi số CCCD) |
| `ConsentForms.Viewed`, `ConsentForms.PdfViewed` | Xem chi tiết phiếu; tải/xem PDF phiếu |
| `ConsentForms.SigningStarted`, `ConsentForms.PatientSigned`, `ConsentForms.Completed`, `ConsentForms.Revoked` | Mở phiên ký; người bệnh ký (kèm quyết định, người ký; thiết bị lưu ở chữ ký); phía bệnh viện xác nhận (kèm SHA-256 của PDF); thu hồi |

Mỗi bản ghi có thời điểm (UTC), người thực hiện, địa chỉ IP, mã truy vết (`correlationId`).

---

## 8. Hệ thống

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/health` | Không cần đăng nhập | Tiến trình còn chạy |
| GET | `/health/ready` | Không cần đăng nhập | Kết nối được SQL Server (`503` nếu không) |
| GET | `/openapi/v1.json` | Không cần đăng nhập | Chỉ có ở môi trường Development |
