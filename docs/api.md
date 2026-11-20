# Tài liệu API — Quản lý Phiếu đồng thuận

> Phiên bản API: `v1`. Tài liệu được cập nhật theo từng sprint (hiện tại: Sprint 6 — thêm cấu hình hệ thống; Sprint 5: xuất Excel/CSV, báo cáo, tổng quan; Sprint 4: ký tay trên máy tính bảng, PDF đã ký, thu hồi).
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
| GET | `/api/v1/account/signature-specimen` | Đã đăng nhập | Ảnh PNG chữ ký mẫu **của chính mình**, mực xanh như khi in lên phiếu (`Cache-Control: no-store`); chưa có → `404` |
| PUT | `/api/v1/account/signature-specimen` | Đã đăng nhập | `{ image }` PNG Base64 vẽ trong khung ký; kiểm tra và chuẩn hóa như chữ ký trên phiếu (thiếu/sai → `400` trường `image`, khung trống → `422 consent_forms.signature_blank`); lưu mã hóa (cột `Users.SignatureSpecimenEncrypted`) → `{ updatedAtUtc }`. `auth/me` có `hasSignatureSpecimen`. Khi xác nhận phiếu (`confirm-step`, `complete`) gửi `{ "useSpecimen": true }` thay cho `image` để ký bằng chữ ký mẫu; chưa có chữ ký mẫu → `400` trường `image`. Nhật ký ghi nguồn chữ ký (`signature`: `specimen`/`drawn`) |
| DELETE | `/api/v1/account/signature-specimen` | Đã đăng nhập | Xóa chữ ký mẫu → `204` |

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
    "departments": [
      { "id": "01a1ca94-9e60-7c35-8f0b-0b8f2f0f4a11", "name": "Khoa Nội tổng hợp" },
      { "id": "…", "name": "Khoa Khám bệnh" }
    ],
    "roles": [{ "id": "…", "code": "DOCTOR", "name": "Bác sĩ" }],
    "permissions": ["consents.create", "consents.read", "consents.sign", "patients.manage", "patients.read", "templates.read"],
    "mustChangePassword": false
  }
}
```

`departmentId`, `departmentName` là **khoa chính** (khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng); `departments` là **mọi khoa** của tài khoản, khoa chính đứng đầu (quyết định 41): người dùng thấy dữ liệu của các khoa này. Access token mang khoa chính ở claim `dept` và mỗi khoa khác một claim `dept_other`.

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
| POST | `/api/v1/departments/sync-his` | `departments.manage` | **Lấy danh mục khoa/phòng từ HIS** (quyết định 42) qua API thông tin người bệnh → `200 { total, added, updated, unchanged, deactivated: [tên khoa], skipped: [mã] }`. `422`: `departments.his_disabled` (chưa cấu hình `PatientSource:Provider = Api`), `departments.his_not_configured` (API chưa đặt `PatientInfo:DepartmentQuery`), `departments.his_unavailable` (không gọi được API hoặc CSDL bệnh viện lỗi), `departments.his_empty` (không có dòng hợp lệ); khi đó danh mục giữ nguyên |

Dữ liệu gửi: `{ "code": "KKB", "name": "Khoa Khám bệnh", "isActive": true }`. Mã khoa được chuẩn hóa chữ hoa, duy nhất (`409 departments.code_exists`).

**Danh mục lấy từ HIS** (quyết định 42): khi đã cấu hình API thông tin người bệnh (`/api/v1/features` trả `departmentsFromHis: true`), thêm, sửa, xóa trả `422 departments.managed_by_his`; danh mục chỉ đổi qua `sync-his`. Cách ghép: mã khoa HIS (chuẩn hóa chữ hoa) trùng mã khoa → cập nhật tên, dùng lại nếu đang ngừng; khoa có mã không có trên HIS mà trùng tên (cùng dạng Unicode, không phân biệt hoa thường, bỏ khoảng trắng thừa) → nhận mã HIS, giữ `id` (người dùng, phiếu, báo cáo đã gắn giữ nguyên); còn lại thêm mới. Khoa không còn trên HIS → ngừng sử dụng (`deactivated`), không xóa. Dòng có mã rỗng hoặc dài quá 20 ký tự bị bỏ (`skipped`); mã lặp lấy dòng đầu; tên thiếu lấy mã, dài quá 200 ký tự thì cắt. Mỗi thay đổi ghi nhật ký hệ thống (`Department`).

---

## 4. Người dùng (`/api/v1/users`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/users?keyword=&departmentId=&isActive=&page=1&pageSize=20` | `users.read` | Tìm kiếm có phân trang. `keyword` tìm theo họ tên/tên đăng nhập/chức danh, **không phân biệt dấu** ("dieu duong" khớp "Điều dưỡng"). `departmentId` khớp khoa chính hoặc khoa khác; mỗi dòng có `departmentName` (khoa chính) và `additionalDepartmentNames` |
| GET | `/api/v1/users/{id}` | `users.read` | Chi tiết, có `additionalDepartments: [{ id, name }]` |
| POST | `/api/v1/users` | `users.manage` | Tạo người dùng với mật khẩu tạm; người dùng phải đổi mật khẩu ở lần đăng nhập đầu |
| PUT | `/api/v1/users/{id}` | `users.manage` | Sửa hồ sơ, khoa chính và khoa khác, vai trò, mã SmartCA (gửi đủ các trường; bỏ `additionalDepartmentIds` là không còn khoa khác) |
| POST | `/api/v1/users/{id}/lock` | `users.manage` | Khóa tài khoản và thu hồi mọi phiên → `204` |
| POST | `/api/v1/users/{id}/unlock` | `users.manage` | Mở khóa (kể cả khóa tạm do đăng nhập sai và khóa tra cứu của tài khoản kiosk) → `204`. Danh sách người dùng có `isLockedOut` (khóa tạm do đăng nhập sai) và `isKioskLookupLocked` (tài khoản kiosk đang tạm khóa tra cứu) |
| POST | `/api/v1/users/{id}/reset-password` | `users.manage` | Đặt mật khẩu tạm `{ "newPassword": "…" }`, thu hồi mọi phiên, bắt buộc đổi ở lần đăng nhập sau → `204` |

Dữ liệu tạo người dùng:

```json
{
  "userName": "dieuduong.moi",
  "fullName": "Điều dưỡng Mới",
  "title": "Điều dưỡng",
  "departmentId": "…",
  "additionalDepartmentIds": ["…"],
  "roleIds": ["…"],
  "email": "dd@example.test",
  "phoneNumber": "0900000000",
  "smartCaUserId": null,
  "password": "********"
}
```

`departmentId` là khoa chính; `additionalDepartmentIds` là các khoa/phòng khác (quyết định 41): tối đa 20, khoa trùng khoa chính hay lặp lại thì bỏ qua; có khoa khác mà không có khoa chính, hoặc khoa không tồn tại/ngừng dùng → `400` ở `additionalDepartmentIds`; tài khoản kiosk không có khoa khác.

Quy tắc nghiệp vụ: không tự khóa chính mình (`422 users.cannot_lock_self`); hệ thống luôn phải còn ít nhất một quản trị viên đang hoạt động (`422 users.last_administrator`); tên đăng nhập trùng → `409 users.username_exists`. Đổi vai trò, khoa của tài khoản có hiệu lực ở lần làm mới token tiếp theo (≤ 15 phút) hoặc khi đăng nhập lại.

---

## 5. Vai trò và quyền

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/permissions` | Đã đăng nhập | Danh mục quyền: `[{ code, name, group }]` |
| GET | `/api/v1/roles` | Đã đăng nhập | Danh sách vai trò kèm mã quyền |
| POST | `/api/v1/roles` | `roles.manage` | Thêm vai trò (quyết định 40) `{ "name", "description"?, "permissions": [...] }` → `201`. Mã vai trò do máy chủ sinh từ tên: tiền tố `VT_`, tên bỏ dấu viết hoa nối bằng `_`, trùng thì thêm `_2`, `_3`… Lỗi `400`: tên trống, quá 100 ký tự hoặc trùng tên vai trò khác (không phân biệt hoa thường, khoảng trắng thừa) ở `errors.name`; mô tả quá 500 ký tự ở `errors.description`; quyền không tồn tại hoặc `kiosk.use` (chỉ dành cho vai trò Kiosk) ở `errors.permissions` |
| PUT | `/api/v1/roles/{id}` | `roles.manage` | Đổi tên, mô tả `{ "name", "description"? }` (cả vai trò có sẵn); mã giữ nguyên. Lỗi như khi thêm; `404 roles.not_found` |
| DELETE | `/api/v1/roles/{id}` | `roles.manage` | Xóa vai trò tự tạo → `204`. `422 roles.system_role` (vai trò có sẵn), `422 roles.in_use` (còn gán cho tài khoản, kể cả tài khoản đã khóa), `422 roles.used_by_templates` (đang dùng ở bước xác nhận cuối hoặc bước xác nhận trung gian của mẫu phiếu, mọi phiên bản; thông báo kèm tên mẫu) |
| PUT | `/api/v1/roles/{id}/permissions` | `roles.manage` | Thay toàn bộ quyền `{ "permissions": ["consents.read", …] }`. Vai trò Quản trị hệ thống luôn có toàn quyền và không sửa được (`422 roles.admin_immutable`); vai trò Kiosk chỉ có `kiosk.use` và không sửa được (`422 roles.kiosk_immutable`). Danh sách vai trò có `isLocked` cho hai vai trò này. Quyền `kiosk.use` không gán được cho vai trò khác (`400`, `errors.permissions`) |

Quyền mới có hiệu lực khi access token được làm mới (tối đa 15 phút).

**Danh mục quyền (Sprint 1)**

| Nhóm | Mã quyền |
|---|---|
| Hệ thống | `users.read`, `users.manage`, `roles.manage`, `departments.manage`, `audit.read`, `settings.manage` |
| Người bệnh | `patients.read`, `patients.manage` |
| Mẫu phiếu | `templates.read`, `templates.manage` |
| Phiếu đồng thuận | `consents.read`, `consents.create`, `consents.sign`, `consents.revoke`, `kiosk.use` (dùng màn hình kiosk, quyết định 24) |
| Báo cáo và xuất dữ liệu | `reports.read`, `exports.run`, `exports.sensitive` |
| Phạm vi dữ liệu | `scope.all_departments` (xem dữ liệu mọi khoa) |

**Vai trò mặc định**: `ADMIN` (Quản trị hệ thống), `TEMPLATE_MANAGER`, `DOCTOR`, `NURSE`, `RECEPTIONIST`, `DEPARTMENT_HEAD`, `REPORTER`, `AUDITOR`, `KIOSK` (máy kiosk người bệnh: chỉ `kiosk.use`; tài khoản mang vai trò này không được có vai trò khác và phải thuộc một khoa — `400` ở `roleIds`/`departmentId`). Vai trò hệ thống còn thiếu được tự tạo khi khởi động (nâng cấp phiên bản không cần thao tác). Quyền mặc định của từng vai trò: xem `backend/src/Consent.Domain/Identity/SystemRoles.cs`. Quyền thu hồi phiếu (`consents.revoke`) mặc định có ở Quản trị hệ thống và Trưởng khoa.

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
| GET | `/api/v1/patients?keyword=&page=1&pageSize=20` | `patients.read` | Tìm kiếm. `keyword` đủ 12 chữ số → tìm chính xác theo CCCD; còn lại → mã người bệnh (chính xác) hoặc họ tên (**không phân biệt dấu**). Người không có `scope.all_departments` (quyết định 41): danh sách (không có `keyword`) và tìm theo họ tên chỉ gồm người bệnh có phiếu ở các khoa của mình, có phiếu mình lập, hoặc hồ sơ mình tạo; tìm đúng số CCCD hoặc mã người bệnh thì ra mọi người bệnh |
| GET | `/api/v1/patients/{id}` | `patients.read` | Hồ sơ đầy đủ, kèm người đại diện; ghi nhật ký `Patients.Viewed` |
| POST | `/api/v1/patients/lookup` | `patients.read` | Tra cứu khi quét CCCD `{ "citizenId": "…" }` → hồ sơ đã có (nếu có) + kết quả HIS (`Disabled` khi chưa kết nối HIS); ghi nhật ký `Patients.LookedUp` (không ghi số CCCD) |
| POST | `/api/v1/patients/lookup-source` | `patients.manage` | Lấy thông tin hành chính từ **API thông tin người bệnh** của bệnh viện (quyết định 22): `{ "patientCode": "…" }` **hoặc** `{ "citizenId": "…" }` (đúng một) → `{ status, patient, existingPatient }`. `status`: `Found` / `NotFound` / `Unavailable` (API lỗi, quá thời gian `His:TimeoutSeconds`) / `Disabled` (chưa cấu hình). `existingPatient`: hồ sơ đã có cùng số CCCD (để chọn luôn, không tạo trùng). Ghi nhật ký `Patients.SourceLookedUp` (chỉ ghi tra theo gì và kết quả, không ghi mã/CCCD) |
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
| `healthInsuranceNumber` | Mã thẻ mới 17 ký tự (2 chữ + 15 số: 5 ký tự đầu như mã cũ + 12 số định danh). Mã thẻ cũ 15 ký tự (2 chữ + 13 số) vẫn được chấp nhận; giao diện cảnh báo nhẹ để cập nhật |
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
{ "his": false, "smartCa": false, "patientSource": false, "departmentsFromHis": false }
```

- `his`: đã kết nối HIS (theo `His:Provider`). `false` → giao diện ẩn khung kết quả HIS, cột/nhãn "Đối chiếu HIS"; mã người bệnh nhập tay.
- `patientSource`: đã cấu hình API thông tin người bệnh (`PatientSource:Provider = Api`). `true` → màn hình tiếp nhận và bước 1 của lập phiếu có ô "Lấy thông tin" theo mã người bệnh hoặc CCCD; quét CCCD cũng tự lấy thêm thông tin còn thiếu.
- `departmentsFromHis`: danh mục khoa/phòng lấy từ HIS (quyết định 42; cùng điều kiện với `patientSource`). `true` → trang *Khoa/phòng* không có thêm, sửa, xóa, có nút **Lấy danh mục từ HIS**.
- `smartCa`: đã tích hợp ký số VNPT SmartCA. Hiện luôn `false` (tạm hoãn) → ẩn ô "Mã tài khoản SmartCA" ở màn hình người dùng (giá trị đã lưu được giữ nguyên khi sửa).

## 6a1. API thông tin người bệnh (dịch vụ riêng `PatientInfo.Api`)

Dịch vụ riêng, chỉ đọc, là nơi **duy nhất** kết nối CSDL của bệnh viện (phần mềm phiếu đồng thuận không giữ chuỗi kết nối CSDL đó). Các phần mềm khác cũng gọi được với khóa API riêng.

| Phương thức | Đường dẫn | Xác thực | Mô tả |
|---|---|---|---|
| GET | `/health` | Không | `{"status":"Healthy"}` |
| POST | `/api/v1/patients/lookup` | Header `X-Api-Key` | `{ "patientCode": "…" }` **hoặc** `{ "citizenId": "…" }` (đúng một; mã ≤ 50 ký tự, CCCD 12 chữ số) → `200` thông tin hành chính; `404` không có; `400` sai dữ liệu; `401` thiếu/sai khóa; `503` lỗi CSDL |
| POST | `/api/v1/daily-statistics` | Header `X-Api-Key` | **Số liệu trong ngày cho bảng tổng hợp điều dưỡng** (quyết định 31): `{ "date": "yyyy-MM-dd" }` → `200 { items: [{ indicator, departmentCode, value }] }` (tối đa 1.000 dòng; dòng thiếu mã chỉ số hoặc giá trị không phải số nguyên ≥ 0 bị bỏ); `400` thiếu ngày; `401` thiếu/sai khóa; `501` chưa đặt `PatientInfo:DailyStatisticsQuery`; `503` lỗi CSDL. Chỉ là số đếm, không có thông tin người bệnh |
| GET | `/api/v1/departments` | Header `X-Api-Key` | **Danh mục khoa/phòng** (quyết định 42) → `200 { items: [{ code, name }] }` (dòng không có mã bị bỏ; thiếu tên thì lấy mã); `401` thiếu/sai khóa; `422` hơn 2.000 dòng (báo lỗi, không cắt bớt, vì phần mềm phiếu coi khoa không có trong danh mục là ngừng sử dụng); `501` chưa đặt `PatientInfo:DepartmentQuery`; `503` lỗi CSDL. Không có thông tin người bệnh |
| POST | `/api/v1/encounters/search` | Header `X-Api-Key` | **Lượt tiếp nhận** (quyết định 25): `{ "patientCode" \| "citizenId", "from": "yyyy-MM-dd", "to": "yyyy-MM-dd" }` (đúng một trong mã/CCCD; `from` ≤ `to`, tối đa 366 ngày, tính cả ngày `to`) → `200 { items: [{ encounterNo, receivedAt, place, receivedBy }] }`, mới nhất trước, tối đa 200 lượt; `400` sai dữ liệu; `401` thiếu/sai khóa; `501` chưa đặt `PatientInfo:EncounterQuery`; `503` lỗi CSDL |

```json
{ "patientCode": "BN-GL-001", "fullName": "Lê Văn Thử (giả lập)", "dateOfBirth": "1970-02-03", "gender": "Male",
  "citizenId": "079070000123", "phoneNumber": null, "permanentAddress": null, "currentAddress": null,
  "ethnicity": null, "occupation": null, "nationality": null, "healthInsuranceNumber": null,
  "citizenIdIssueDate": "2021-06-01", "oldIdNumber": null }
```

- `gender`: `Male` / `Female` / `null` (giá trị cột giới tính được đổi theo `PatientInfo:MaleValues`, `PatientInfo:FemaleValues`; đặt thì thay hẳn mặc định, một giá trị không được vừa là Nam vừa là Nữ).
- Khóa API so sánh thời gian hằng; nhiều khóa (`PatientInfo__ApiKeys__0`, `__1`…) để xoay vòng không gián đoạn.
- Nhật ký chỉ ghi tra theo gì và kết quả (`Found`/`NotFound`), mã lỗi SQL; không ghi mã người bệnh, CCCD hay thông tin trả về.
- Lượt tiếp nhận: `receivedAt` là **giờ bệnh viện**, không kèm múi giờ (ví dụ `"2026-11-11T07:30:00"`). Câu truy vấn nhận `@From` = đầu ngày `from`, `@To` = đầu ngày **sau** `to` (lọc `ThoiGian >= @From AND ThoiGian < @To`); dòng thiếu số tiếp nhận hoặc thời gian đọc không được thì bỏ qua.

```json
{ "items": [ { "encounterNo": "TN-GL-0002", "receivedAt": "2026-11-11T07:30:00", "place": "Phòng khám 1",
               "receivedBy": "Người tiếp đón B (giả lập)" } ] }
```

---

## 6b. Mẫu phiếu (`/api/v1/consent-templates`)

Mỗi mẫu có nhiều **phiên bản**: `Draft` (nháp, sửa được) → `Published` (đang phát hành, dùng để lập phiếu mới) → `Retired` (ngừng dùng). Mỗi mẫu có **tối đa một** phiên bản nháp và **tối đa một** phiên bản đang phát hành; phát hành phiên bản mới thì phiên bản cũ tự chuyển sang `Retired`. Phiếu đã lập **giữ nguyên phiên bản mẫu** tại thời điểm lập.

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/consent-templates?includeInactive=false` | `templates.read` | Danh sách mẫu (sắp theo mã), kèm phiên bản đang phát hành và phiên bản nháp |
| GET | `/api/v1/consent-templates/available?departmentId=` | `consents.create` | Mẫu **đang phát hành** dùng được để lập phiếu cho khoa (mặc định: khoa chính của người dùng): mẫu toàn viện và mẫu riêng của khoa. Trả định nghĩa của phiên bản đang phát hành |
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
| `choice` | Chọn một | `options` (`value`, `label`, `score` nếu là câu hỏi tính điểm), `layout: "inline"`. Tự chọn theo giá trị số của trường đứng trước: `valueFrom` (trường văn bản hoặc `computed`) và mỗi lựa chọn có `min` (≥) và/hoặc `max` (<); khi trường nguồn có giá trị, máy chủ chọn theo khoảng và bỏ qua lựa chọn gửi lên, chưa có giá trị thì chọn tay |
| `multiChoice` | Chọn nhiều | `options`, `minItems`, `maxItems`, `requireAll` (phải chọn đủ mọi mục) |
| `confirm` | Ô xác nhận (phải đánh dấu nếu `required`) | |
| `repeater` | Bảng nhiều dòng | `columns` (`key`, `label`, `required`, `maxLength`, `pattern`), `minItems`, `maxItems` |
| `score` | Tổng điểm tự tính (không nhập) | `sumOf` (câu hỏi tính điểm đứng trước), `levels` (`code`, `label`, `min` tăng dần) |
| `computed` | Giá trị tự tính theo công thức (không nhập; máy chủ tính lại khi lưu, bỏ qua giá trị gửi lên) | `formula` (hiện có `bmi` = cân nặng kg / [chiều cao m]², làm tròn 1 chữ số, dấu phẩy thập phân), `inputs` (các trường văn bản đứng trước, theo thứ tự công thức; `bmi`: `["weight", "height"]`) |

- Mọi khối nhập liệu có `key` (bắt đầu bằng chữ cái; chữ không dấu, số, `_`; không trùng).
- `visibleWhen`: `{ "field", "in": [...] }` (giá trị của một trường đứng trước thuộc danh sách; chọn nhiều thì chỉ cần một), hoặc `{ "field", "minLevel" }` (trường điểm đạt tối thiểu mức). Trường bị ẩn thì không bắt buộc, và giá trị của nó không được in hay giữ lại khi trình ký.
- `prefill`: tự điền khi lập phiếu từ `patient.healthInsuranceNumber`, `patient.phoneNumber`, `patient.patientCode`, `patient.permanentAddress`, `patient.currentAddress` (chưa có thì dùng địa chỉ thường trú), `patient.ethnicity`, `patient.occupation`.
- **Tùy chọn in PDF** (quyết định 28, 29, 33, 43, không bắt buộc; màn hình nhập liệu không đổi, trừ chỗ ghi rõ):
  - `hideDepartment: true` (gốc định nghĩa): không in dòng "Khoa/phòng: …" dưới tên bệnh viện, cho mẫu mà bản giấy không có dòng này (`TTNB-NGOAITRU`, `TUVAN-CONGTT`).
  - `bilingual: true` (gốc định nghĩa): phần hệ thống in kèm tiếng Anh: mục "I. Thông tin người bệnh" ("Họ và tên (Full name)", "Mã người bệnh (Patient ID)"…, xếp lại để số không bị xuống dòng), dòng ngày tháng ("ngày (day) … tháng (month) … năm (year) …") và ghi chú dưới tên ô ký ("(Signature and full name)").
  - `sameLine: true` (trên khối): in trường này tiếp trên dòng của trường đứng trước, ví dụ "Trình độ văn hóa (Education Level): … Nghề nghiệp (Occupation): …". Chỉ dùng cho trường dạng "nhãn: giá trị" (`text`, `date`, `datetime`, `computed`; kiểu khác → `400`). Trường đứng trước bị ẩn thì in tiếp dòng của trường in trước đó; sau đoạn văn, danh sách thì xuống dòng như thường. Sau câu hỏi chọn `layout: "inline"` thì in tiếp dòng của câu hỏi, ví dụ "2. Dị ứng (Allergies): ☐ Không (None) ☒ Có, ghi rõ (Yes, specify) Ghi rõ (Specify): …" của BM10 (quyết định 43); sau câu hỏi chọn khác thì xuống dòng.
  - `documentControl: { "code", "issueNo", "effectiveDate", "titleInHead"?, "codeLabel"?, "issueNoLabel"?, "effectiveDateLabel"? }` (gốc định nghĩa, mỗi mục tối đa 50 ký tự): đầu phiếu in như bản giấy có mã tài liệu: logo bên trái, quốc hiệu ở giữa, ô "Mã số (Code No) / Lần ban hành (Issue No) / Ngày hiệu lực (Effective Date)" bên phải. Không in tên đơn vị (trừ khi chưa tải logo), dòng khoa và dòng "MS … Số phiếu" (số phiếu vẫn ở chân trang). `titleInHead: true` (bản giấy TTNB, không có quốc hiệu): tên phiếu in ở giữa đầu phiếu thay cho quốc hiệu, chữ hoa đậm cỡ 12, dòng tiếng Anh màu xanh chữ đứng; mã tài liệu cỡ 9; logo, tên phiếu, mã tài liệu canh giữa theo chiều dọc; không in tên phiếu lần nữa bên dưới. `codeLabel`, `issueNoLabel`, `effectiveDateLabel` thay ba nhãn của ô mã tài liệu, ví dụ "Mã số (Code)". Tên phiếu dài tự thu nhỏ cỡ chữ (từ 12 xuống tối đa 8) để mỗi dòng nằm gọn một dòng của cột giữa. `unitInHead: true` (chỉ dùng kèm `titleInHead`, khác → `400`; bản giấy BM10): ô trái in tên cơ quan chủ quản và tên bệnh viện phía trên logo, canh giữa; ô mã tài liệu chữ cỡ 7,5.
  - `patientLines` (gốc định nghĩa, 1–20 dòng, mỗi dòng 1–2 mục): các dòng thông tin người bệnh như bản giấy, thay cho mục "I. Thông tin người bệnh". Mỗi mục `{ "patient" | "field", "label"?, "options"?, "other"?, "indent"? }`: `patient` là thông tin hồ sơ (`fullName`, `dateOfBirth`, `gender`, `patientCode`, `citizenId`, `phoneNumber`, `address`), `field` là một trường của mẫu: trường "nhãn: giá trị" hoặc câu hỏi chọn (in ở đây, không in lại ở thân phiếu; đang ẩn thì để chấm trống). `label` bỏ trống thì dùng nhãn mặc định.
    - Giới tính có `options` (nhãn Nam, nhãn Nữ): in hai ô đánh dấu.
    - Câu hỏi chọn: in nhãn và các ô lựa chọn trên dòng; `other` là một trường văn bản in tiếp sau các ô, ví dụ "☐ Khác (Other): …" (trường đó không in lại ở thân phiếu).
    - Trường văn bản có `options` (2–6 nhãn, nhãn cuối là "Khác"), ví dụ dân tộc `["Kinh", "Khác, ghi rõ (Other, specify)"]`: in các ô như bản giấy. Giá trị trùng phần tiếng Việt của một nhãn (không phân biệt hoa thường) thì đánh dấu ô đó; giá trị khác thì đánh dấu ô cuối và in giá trị sau ô. Màn hình nhập liệu và tự điền từ hồ sơ không đổi.
    - `indent: true` (ở mục đầu của dòng): dòng phụ của dòng trên, thụt vào, không đánh số.
    - Người đại diện (nếu có) vẫn in một dòng ngay dưới.
  - `numberedPatientLines: true` (gốc định nghĩa): đánh số 1, 2, 3… các dòng `patientLines` như bản giấy TTNB, chữ thẳng cột sau số thứ tự; dòng `indent` không có số. Khoảng cách dòng gần như bản giấy để phiếu vừa một trang.
  - `printOptionsOnly: true` (khối `choice`, `multiChoice`): PDF chỉ in các ô lựa chọn, chữ đậm, một dòng riêng, không in câu hỏi (đoạn văn đứng trước đã là câu hỏi).
  - `layout: "row"` (khối `choice` có 2–4 lựa chọn, không kèm `printOptionsOnly`; khác → `400`; quyết định 43): PDF in thành một hàng của bảng kẻ ô, câu hỏi ở cột đầu, mỗi lựa chọn một ô có ô đánh dấu, như bảng Có/Không của bản giấy BM10. Các câu `row` liền nhau, cùng số lựa chọn, in chung một bảng; câu đang ẩn không in. Lựa chọn ngắn (tối đa 22 ký tự) thì mỗi ô rộng 26 mm, dài hơn thì cột câu hỏi 70 mm và các ô chia đều phần còn lại. Màn hình nhập liệu hiện các lựa chọn trên một dòng.
  - `box: true` (khối `list`; khác → `400`): danh sách in trong khung nền xám, viền xanh; `label` là tiêu đề khung (canh giữa, đậm nghiêng), các mục chữ nghiêng, `layout: "ordered"` thì đánh số; như khung "Xin Quý người bệnh và thân nhân lưu ý" của bản giấy QĐ-VHTT-BM02. Màn hình nhập liệu hiện danh sách trong khung có tiêu đề (danh sách không có `box` thì không in, không hiện `label`).
  - `heading: true` (khối `paragraph`; khác → `400`): tiêu đề mục, PDF in đậm, canh giữa, chữ hoa, đi liền nội dung bên dưới; màn hình in đậm. Ví dụ "A - Nguy cơ té ngã (Fall risk)" của BM10.
  - Trên phần (`sections[]`): `banner: true` in tên phần trong dải nền màu, canh giữa, dòng đầu chữ hoa đậm (trừ phần trong ngoặc), các dòng sau theo kiểu song ngữ. `newPage: true`: phần bắt đầu ở trang mới. `signer`: ô ký của một người in ngay cuối phần, thay vì ở khối chữ ký cuối phiếu, như bản giấy BM10 mỗi phần một người ký: `"patient"` (người bệnh/người đại diện), `"hospital"` (xác nhận cuối) hoặc `key` của một bước trong `signatures.steps`; mỗi người chỉ ở một phần (khác → `400`). Ô ký cuối phần nằm ở nửa phải trang: dòng "Ngày (Day) … tháng (month) … năm (year) …" là ngày người đó ký hoặc xác nhận (chưa ký thì để chấm), tên ô ký, chữ ký, họ tên. Người đã in ở cuối phần không in lại cuối phiếu; mọi người đều đã in ở cuối phần và mẫu không hỏi ý kiến (`decision: "none"`) thì không còn khối chữ ký cuối phiếu.
  - `footnote` (gốc định nghĩa, tối đa 2.000 ký tự): ghi chú in cuối phiếu sau khối chữ ký, chữ cỡ 9 nghiêng, có đường kẻ phía trên, như "Hướng dẫn thực hiện phiếu" của bản giấy QĐ-VHTT-BM02; `\n` là xuống dòng. Màn hình nhập liệu không hiện.
  - `signatures.hospitalFirst: true`: các ô ký phía bệnh viện in trước (bên trái), ô người bệnh/người đại diện sau cùng. `signatures.plainTitles: true`: tên ô ký in đúng như đã ghi (không viết hoa toàn bộ, không đậm). `signatures.boldTitles: true`: tên ô ký in đậm, giữ chữ thường như đã ghi (bản giấy QĐ-VHTT-BM02, BM10).
  - `signatures.patientHint`: ghi chú dưới tên ô ký của người bệnh/người đại diện (mặc định "(Ký, ghi rõ họ tên)"). Ở mọi ghi chú dưới tên ô ký (`patientHint`, `hospitalHint`, `hint` của bước), dòng bắt đầu bằng dấu `!` in màu đỏ (dấu `!` không in), như dòng "(đã đối chiếu thông tin trên giấy tờ tùy thân)" của bản giấy TTNB.
  - `signatures.centerDate: true`: dòng "địa danh, ngày … tháng … năm …" canh giữa, chữ đứng (mặc định canh phải, chữ nghiêng).
  - `signatures.paperHints: true`: ghi chú dưới tên ô ký in cỡ 11 (mặc định cỡ 10) và giữ nguyên sau khi nhân viên xác nhận; dòng "Đã ký, xác nhận bằng tài khoản" in dưới họ tên. Mặc định ghi chú của ô ký bệnh viện được thay bằng trạng thái xác nhận.
  - Thẻ *Cấu hình ký* giữ nguyên `hospitalFirst`, `plainTitles`, `patientHint`, `centerDate`, `paperHints`, `boldTitles` và `signer` của các phần khi lưu (chỉ sửa được trong JSON); xóa một bước đang in ở cuối một phần thì phần đó bỏ `signer` (bước đó không còn).
  - Tiêu đề: dòng nằm trọn trong ngoặc, ví dụ "(Về chế độ thanh toán …)", là dòng phụ: không viết hoa, in nghiêng; tiếng Việt đậm cỡ 12, tiếng Anh xanh không đậm cỡ 11.
- **Song ngữ:** tiếng Anh ghi trong ngoặc ("Có (Yes)") hoặc sau dấu "/" đến hết dòng như bản giấy ("Ngày sinh/ Date of birth", "Nam/Male"); phần tiếng Anh in xanh, nghiêng trên PDF và màn hình. Dấu "/" giữa hai chữ tiếng Việt ("người bệnh/người nhà"), trong đường dẫn hay mã ("QT-KNGT/BM02") không tách. Chữ viết tắt tiếng Việt CCCD, CMND, BHYT, BHXH tính là tiếng Việt, nên "(theo CCCD)" giữ màu đen.
- Định nghĩa trả về và lưu chỉ gồm các thuộc tính trên. Trước bản này JSON lưu còn chép thừa danh sách `inputBlocks`; nay bỏ, và nếu gửi lên thì bị bỏ qua.
- `signatures` (không bắt buộc): `patient`, `representative`, `hospital`, `hospitalHint` thay nhãn khối chữ ký (tối đa 500 ký tự). `decision`: `"required"` (mặc định, người bệnh phải chọn đồng ý/từ chối khi ký) hoặc `"none"` (chỉ ký xác nhận, ví dụ phiếu khai thông tin, phiếu nhận định; không có dòng ý kiến trên PDF và `decision` của phiếu để trống). `decisionLabel`, `agreeLabel`, `refuseLabel` thay câu hỏi và nhãn mặc định ("Ý kiến của người bệnh", "Đồng ý", "Từ chối") trên máy tính bảng và PDF; giá trị lưu vẫn là `Agreed`/`Refused`. `steps` (tối đa 5): các **bước xác nhận trung gian theo vai trò** `[{ "key", "roleCode", "label", "hint"? }]`, làm lần lượt sau khi người bệnh ký và trước bước xác nhận cuối (vai trò của mẫu, nhãn `hospital`); vai trò phải tồn tại khi lưu/phát hành phiên bản. Ví dụ BM10: nhân viên hướng dẫn (`RECEPTIONIST`) → điều dưỡng (`NURSE`) → bác sĩ xác nhận cuối.
- 4 mẫu thật của bệnh viện (`QT-KNGT-BM02`, `TTNB-NGOAITRU`, `QT-KNGT-BM10`, `TUVAN-CONGTT`; xem [mẫu phiếu](mau-phieu.md)) nằm trong mã nguồn (`Consent.Application/ConsentTemplates/Defaults/*.json`). Khi khởi động với `Seed:Enabled`, mẫu nào chưa có (theo mã) thì được tạo: ở trạng thái **nháp** để quản trị viên duyệt rồi phát hành, hoặc **phát hành sẵn** khi bật `Seed:DemoData`. Mẫu đã có không bị ghi đè. Ba mẫu đề xuất cũ (CK-01, TT-01, TN-01) nay chỉ dùng trong kiểm thử (`backend/tests/SampleTemplates`).

## 6c. Phiếu đồng thuận (`/api/v1/consent-forms`)

**Phạm vi khoa:** người dùng chỉ thấy phiếu **của khoa mình** hoặc **do mình lập**, trừ khi có quyền `scope.all_departments`. Phiếu ngoài phạm vi trả `404` (không tiết lộ phiếu có tồn tại).

**Vòng đời:** `Draft` (nháp) → `PendingSignature` (chờ ký: người bệnh ký tay, rồi phía bệnh viện xác nhận) → `Completed` (hoàn tất, phiếu bị khóa) → `Revoked` (thu hồi). `Draft`/`PendingSignature` → `Cancelled` (hủy, bắt buộc lý do). Chuyển trạng thái sai quy tắc → `409 consent_forms.invalid_status` kèm thông báo tiếng Việt (ví dụ "Chỉ trình ký được phiếu nháp.").

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/consent-forms?status=&templateId=&patientId=&departmentId=&from=&to=&keyword=&page=1&pageSize=20` | `consents.read` | Tìm kiếm, phân trang ở máy chủ, mới nhất trước. `from`/`to` là ngày lập (`yyyy-MM-dd`, giờ Việt Nam, gồm cả ngày `to`). `keyword`: số phiếu, họ tên người bệnh (không phân biệt dấu) hoặc mã người bệnh |
| GET | `/api/v1/consent-forms/{id}` | `consents.read` | Chi tiết phiếu, kèm định nghĩa của **đúng phiên bản mẫu** đã dùng, dữ liệu, điểm, các mục còn thiếu (`issues`) và thao tác được phép (`actions`); ghi nhật ký `ConsentForms.Viewed` |
| GET | `/api/v1/consent-forms/{id}/pdf` | `consents.read` | File PDF khổ A4 (`application/pdf`, tên `PDT-2026-000001.pdf`). Phiếu chưa hoàn tất: bản xem trước dựng lại mỗi lần, có dòng **"BẢN XEM TRƯỚC – CHƯA CÓ CHỮ KÝ"**. Phiếu hoàn tất/thu hồi: trả **đúng file đã lưu lúc hoàn tất**, sau khi kiểm tra SHA-256 (file mất, hỏng hoặc bị sửa → `409 consent_forms.signed_pdf_unavailable`). Ghi nhật ký `ConsentForms.PdfViewed` |
| POST | `/api/v1/consent-forms` | `consents.create` | Lập phiếu nháp `{ patientId, templateId, departmentId?, replacesFormId? }` → `201`. Dùng **phiên bản đang phát hành** của mẫu (mẫu chưa phát hành, ngừng dùng hoặc không dùng cho khoa → `400` ở trường `templateId`); điền sẵn các trường có `prefill`. **Kế thừa phiếu lần trước** (quyết định 27, quản trị đặt số ngày): nếu người bệnh có phiếu **đã hoàn tất** cùng mẫu, **cùng khoa**, trong số ngày đó, phiếu mới điền sẵn câu trả lời của phiếu gần nhất (chuẩn hóa theo phiên bản mẫu hiện hành; trường tự điền vẫn lấy theo hồ sơ; không kế thừa ý kiến, lời xác nhận, chữ ký), `notices` của phản hồi có câu "Đã điền sẵn theo phiếu … (hoàn tất dd/MM/yyyy): kiểm tra lại trước khi trình ký" và nhật ký ghi `ConsentForms.Inherited`. `departmentId` mặc định là khoa chính của người dùng; người lập chọn được **mọi khoa đang hoạt động** (quyết định 19: khoa điều trị người bệnh, nơi nhân viên sẽ xác nhận), khoa không tồn tại hoặc ngừng dùng → `400` ở trường `departmentId`. Người lập vẫn thấy phiếu mình lập; nhân viên khoa được chọn thấy phiếu để xác nhận |
| PUT | `/api/v1/consent-forms/{id}` | `consents.create` | Lưu nháp `{ data, signerKind, representativeId }`. Chưa bắt buộc đầy đủ; các mục còn thiếu trả về trong `issues`. Chỉ phiếu nháp |
| POST | `/api/v1/consent-forms/{id}/submit` | `consents.create` | Trình ký: kiểm tra đầy đủ theo định nghĩa mẫu (thiếu → `400 consent_forms.incomplete` kèm `errors` theo từng trường), bỏ giá trị của trường đang ẩn, **đóng băng** thông tin người bệnh và người ký (sửa hồ sơ sau đó không làm đổi phiếu) |
| POST | `/api/v1/consent-forms/{id}/return-to-draft` | `consents.create` | Trả phiếu đang chờ ký về nháp để sửa (bỏ thông tin đã đóng băng) |
| POST | `/api/v1/consent-forms/{id}/cancel` | `consents.create` | Hủy phiếu nháp hoặc chờ ký `{ "reason": "…" }` (5–500 ký tự) |
| POST | `/api/v1/consent-forms/{id}/signing-session` | `consents.create` | Mở phiên ký trên máy tính bảng → `{ token, expiresAtUtc }` (mặc định 30 phút). Chỉ cho phiếu chờ ký mà người bệnh chưa ký; mở phiên mới thì phiên cũ hết hiệu lực. Ghi nhật ký `ConsentForms.SigningStarted` |
| POST | `/api/v1/consent-forms/{id}/patient-signature` | **Token phiên ký** | Người bệnh/người đại diện ký: `{ "decision": "Agreed" \| "Refused", "image": "<PNG Base64>" }` → `{ decision, signedAtUtc }`. Mẫu có `signatures.decision = "none"` thì bỏ `decision` (gửi kèm cũng bị bỏ qua, kết quả `decision = null`); mẫu khác thiếu `decision` → `400` ở trường `decision`. `statement` (tùy chọn, tối đa 300 ký tự): lời xác nhận của người ký khi mẫu có `signatures.statement` (`{ label?, required?, suggestions?[] }`, tối đa 8 câu gợi ý); mẫu bắt buộc mà thiếu → `400` ở trường `statement`; mẫu không có ô này thì bỏ qua. Lời xác nhận lưu cùng chữ ký (`signatures[].statement` ở chi tiết phiếu) và in trên PDF ngay trên chữ ký. Xem mục "Ký tay" bên dưới. Ghi nhật ký `ConsentForms.PatientSigned` |
| POST | `/api/v1/consent-forms/{id}/confirm-step` | `consents.sign` + vai trò của bước | Xác nhận bước trung gian đang chờ `{ "stepKey": "nurse", "image": "<PNG Base64>" }` → chi tiết phiếu. `image` là chữ ký tay của nhân viên (quyết định 17), kiểm tra như chữ ký người bệnh: thiếu hoặc sai định dạng → `400` ở trường `image`, khung ký trống → `422 consent_forms.signature_blank`. Người bệnh chưa ký, bước đã xác nhận hoặc chưa đến lượt → `409 consent_forms.invalid_status`; sai vai trò → `403 consent_forms.signer_role_required`; mã bước không có trong mẫu → `400` ở trường `stepKey`; tài khoản đã xác nhận một bước khác trên phiếu → `422 consent_forms.same_confirmer`. Ghi nhật ký `ConsentForms.StepConfirmed` |
| POST | `/api/v1/consent-forms/{id}/complete` | `consents.sign` + vai trò theo mẫu | Phía bệnh viện xác nhận bằng tài khoản kèm chữ ký tay `{ "image": "<PNG Base64>" }` → phiếu `Completed`; ảnh chữ ký in vào ô của nhân viên trên PDF đã ký. Lỗi ảnh như ở `confirm-step`. Người bệnh chưa ký hoặc còn bước trung gian chưa xác nhận → `409`; người đã xác nhận một bước trên phiếu → `422 consent_forms.same_confirmer`; không có vai trò xác nhận của mẫu → `403 consent_forms.signer_role_required`. **Khi quản trị đang bật gắn lượt tiếp nhận** (Cấu hình hệ thống; quyết định 25) gửi kèm `"encounterNo"` và `"encounterDate": "yyyy-MM-dd"` (ngày tiếp nhận của lượt đã chọn): thiếu, hoặc máy chủ tra lại HIS ngày đó không thấy lượt của đúng người bệnh → `400` (trường `encounterNo`); HIS lỗi → `422 consent_forms.encounters_unavailable`. Ghi nhật ký `ConsentForms.Completed` (kèm SHA-256 và số tiếp nhận) |
| GET | `/api/v1/consent-forms/{id}/encounters?from=&to=` | `consents.sign` | **Lượt tiếp nhận** của người bệnh của phiếu để chọn khi ký hoàn tất (quyết định 25): mặc định hôm nay (giờ bệnh viện), hoặc `from`–`to` (`yyyy-MM-dd`, tối đa 366 ngày) → `{ from, to, items: [{ encounterNo, receivedAt, place, receivedBy, forms: [{ id, formNo, templateName, status }] }] }`, mới nhất trước; `forms` là các phiếu của người bệnh đã gắn vào lượt đó. Tra HIS theo mã y tế (ưu tiên) hoặc số CCCD trong hồ sơ. Quản trị đang tắt gắn lượt tiếp nhận (Cấu hình hệ thống) → `422 consent_forms.encounters_disabled` (giao diện hiểu là không cần chọn lượt); HIS lỗi/quá thời gian → `422 consent_forms.encounters_unavailable`; hồ sơ không có cả mã y tế lẫn CCCD → `422 consent_forms.encounters_no_patient_key`; khoảng ngày sai → `400` (trường `from`) |
| GET | `/api/v1/consent-forms/bundles/{bundleId}/encounters?from=&to=` | `consents.sign` | Như trên, cho người bệnh của bộ phiếu (dùng khi "Xác nhận cả bộ") |
| POST | `/api/v1/consent-forms/{id}/revoke` | `consents.revoke` | Thu hồi phiếu đã hoàn tất `{ "reason": "…" }` (5–500 ký tự). Ghi nhật ký `ConsentForms.Revoked` |
| POST | `/api/v1/consent-forms/bundle` | `consents.create` | **Lập bộ phiếu** (quyết định 23): `{ "patientId", "templateIds": [ … ], "departmentId"? }` (2–10 mẫu khác nhau) → `201` bộ phiếu (xem dưới). Tạo mọi phiếu nháp cùng `bundleId`, điền sẵn từ hồ sơ và kế thừa phiếu lần trước như lập lẻ (phiếu nào được kế thừa thì `notices` có một dòng bắt đầu bằng số phiếu đó); mẫu nào không hợp lệ thì không tạo phiếu nào (`400` ở trường `templateIds`) |
| GET | `/api/v1/consent-forms/bundles/{bundleId}` | `consents.read` | Bộ phiếu: `{ bundleId, patientId, patientName, forms: [{ id, formNo, status, templateId, templateCode, templateName, patientSigned, requiresDecision, nextStepLabel, actions }], canStartSigning, canConfirm, notices }`. Chỉ gồm phiếu trong phạm vi người dùng; không có phiếu nào → `404 consent_forms.bundle_not_found` |
| POST | `/api/v1/consent-forms/bundles/{bundleId}/signing-session` | `consents.create` | Một phiên ký cho mọi phiếu của bộ đang chờ người bệnh ký → `{ token, expiresAtUtc, formIds }`. Không có phiếu chờ ký → `409`; các phiếu khác người ký (người bệnh và người đại diện, hoặc hai người đại diện) → `422 consent_forms.bundle_mixed_signers` |
| POST | `/api/v1/consent-forms/{id}/remote-signing` | `consents.create` | **Mã QR ký trên điện thoại riêng của người bệnh** (quyết định 32): mở phiên ký mới cho phiếu (phiên cũ, mã cũ hết hiệu lực) → `{ id, code, expiresAtUtc, formIds }`. `code` (22 ký tự, 128 bit ngẫu nhiên) chỉ trả **một lần** để dựng mã QR `https://<địa chỉ phần mềm>/ky-tu-xa#<code>`; máy chủ chỉ lưu SHA-256. Mã phải được mở trong 15 phút. Phiếu không chờ người bệnh ký → `409`. Ghi nhật ký `ConsentForms.RemoteSigningCreated` |
| POST | `/api/v1/consent-forms/bundles/{bundleId}/remote-signing` | `consents.create` | Như trên, một mã cho mọi phiếu của bộ đang chờ người bệnh ký (ký một lần cho cả bộ); khác người ký → `422 consent_forms.bundle_mixed_signers` |
| GET | `/api/v1/remote-signing/{id}` | `consents.read` | Trạng thái mã để màn hình nhân viên theo dõi: `{ id, state, expiresAtUtc, openedAtUtc, signedAtUtc }`, `state`: `Waiting` (chờ quét), `Opened` (đã mở trên điện thoại), `Signed`, `Expired`, `Cancelled`, `Locked` (sai ngày sinh 5 lần), `Superseded` (đã mở phiên ký khác). Chỉ khi người dùng xem được các phiếu của mã (ngoài phạm vi → `404`) |
| POST | `/api/v1/remote-signing/{id}/cancel` | `consents.create` | Hủy mã và đóng phiên ký của mã: điện thoại đã mở mã cũng không ký được nữa → trạng thái mới. Ghi nhật ký `ConsentForms.RemoteSigningCancelled` |
| POST | `/api/v1/remote-signing/claim` | **Không cần đăng nhập**, giới hạn tần suất theo IP | Điện thoại người bệnh mở mã. Lần đầu `{ code, dateOfBirth: "yyyy-MM-dd" }` (ngày sinh của người bệnh, kể cả khi người đại diện ký): đúng → `{ token, expiresAtUtc, deviceKey, bundleId, forms }`; `deviceKey` gắn mã với điện thoại này (máy chủ lưu SHA-256), máy khác mở → `409 remote_signing.opened_elsewhere`. Tải lại trang: `{ code, deviceKey }` (không cần ngày sinh, trong 60 phút kể từ lúc mở). Sai ngày sinh → `422 remote_signing.wrong_birth_date` kèm `remainingAttempts`; sai 5 lần → `422 remote_signing.locked`, mã bị hủy. Mã sai, hết hạn, đã hủy, đã có phiên khác → cùng một câu trả lời `404 remote_signing.invalid`; đã ký xong → `409 remote_signing.already_signed`. `token` là token phiên ký như ở `signing-session` (chỉ dùng cho `patient-signature` hoặc `signing-bundle/patient-signature`); `forms` là chi tiết phiếu để đọc, đã che số CCCD, số điện thoại và bỏ thông tin nội bộ (người lập, các bước xác nhận, lưu ý). Ghi nhật ký `ConsentForms.RemoteSigningOpened` (kèm IP, trình duyệt của điện thoại) hoặc `ConsentForms.RemoteSigningFailed` |
| POST | `/api/v1/consent-forms/signing-bundle/patient-signature` | **Token phiên ký của bộ** | Người bệnh ký **một lần cho cả bộ** (phương án b): `{ "image": "<PNG Base64>", "statement"?: "…", "decisions": { "<mã phiếu>": "Agreed" \| "Refused" } }`. Phiếu nào hỏi Đồng ý/Không đồng ý thì phải có quyết định (thiếu → `400` ở trường `decisions`); lời xác nhận bắt buộc nếu có phiếu bắt buộc. Kiểm tra hết rồi mới ghi: mọi phiếu cùng ảnh chữ ký, cùng lời xác nhận, cùng thời điểm ký → `{ signedAtUtc, forms: [{ id, formNo, decision }] }`. Nhật ký `ConsentForms.PatientSigned` của từng phiếu ghi `bundle` và `signedTogether` (các số phiếu ký cùng) |
| POST | `/api/v1/consent-forms/bundles/{bundleId}/confirm` | `consents.sign` | Nhân viên **xác nhận một lần** các phiếu trong bộ đang đến lượt mình (bước trung gian đúng vai trò, hoặc xác nhận cuối): `{ "useSpecimen": true }` hoặc `{ "image": "<PNG Base64>" }` → `{ confirmed: [{ id, formNo, action: "step" \| "completed", label }], failed: [{ id, formNo, message }] }`. Mỗi phiếu xử lý và lưu riêng. Không phiếu nào đến lượt → `422 consent_forms.bundle_nothing_to_confirm`. Khi quản trị đang bật gắn lượt tiếp nhận và có phiếu được hoàn tất: gửi kèm `encounterNo`, `encounterDate` như ở `complete`; lượt được kiểm tra **một lần trước khi ghi**, sai thì không phiếu nào bị đổi; lượt chỉ gắn vào các phiếu hoàn tất trong lần này (bước trung gian không cần) |

- `signerKind`: `Patient` (người bệnh tự ký) hoặc `Representative` (người đại diện ký thay; `representativeId` phải thuộc hồ sơ người bệnh).
- `data`: đối tượng `{ "tênTrường": giá trị }`. Giá trị là chuỗi (`text`, `date`, `datetime`, `choice`), mảng chuỗi (`multiChoice`), `true` (`confirm`) hoặc mảng dòng `[{ "cột": "…" }]` (`repeater`). Trường không có trong định nghĩa bị bỏ qua; nội dung tối đa 100.000 ký tự.
- `scores`: `{ "tênTrườngĐiểm": { "total": 45, "levelCode": "high", "levelLabel": "Nguy cơ cao" } }` (`total = null` khi chưa trả lời đủ câu).
- Số phiếu `PDT-<năm>-<6 chữ số>` lấy từ sequence của SQL Server, không trùng kể cả khi nhiều người lập cùng lúc.
- Nội dung phiếu (`data`) và thông tin đóng băng có dữ liệu cá nhân nên được **mã hóa AES-256-GCM** trong CSDL; nhật ký audit che các giá trị này (`***`).
- Tên đơn vị in trên PDF lấy từ cấu hình `Hospital:ParentAgency`, `Hospital:Name`, `Hospital:Place` (**đang để giá trị mẫu, chờ bệnh viện cung cấp**).
- Chi tiết phiếu có `encounter` (quyết định 25; rỗng khi chưa gắn): `{ encounterNo, receivedAt, place, receivedBy }`, `receivedAt` kèm múi giờ `+07:00`. Chân mỗi trang PDF đã ký in "Số tiếp nhận: … · HH:mm dd/MM/yyyy · nơi tiếp nhận · Người tiếp nhận: …" để đối soát.
- Chi tiết phiếu có `bundleId` (rỗng với phiếu lập lẻ). Phiếu ký chung một lần trong bộ in dưới chữ ký người bệnh: "Ký chung một lần cho N phiếu: PDT-…, …".
- Phiếu nháp lập có kế thừa phiếu lần trước luôn có lưu ý "Đã điền sẵn theo phiếu …" trong `notices` (cả khi mở lại, tải lại trang) đến khi trình ký; bộ phiếu (`GET bundles/{bundleId}`) có thêm một dòng bắt đầu bằng số phiếu cho mỗi phiếu nháp như vậy.
- Chi tiết phiếu có thêm `decision` (`Agreed`/`Refused`; `null` khi mẫu chỉ ký xác nhận), `steps` (`key`, `label`, `roleCode`, `roleName`, `isNext`, `confirmation`), `signatures` (`role`: `Patient`/`Representative`/`Staff` (bước trung gian, kèm `stepKey`)/`Hospital`; `method`: `Handwritten`/`AccountConfirmation`; `signerName`, `signerTitle`, `signedAtUtc`; **không kèm ảnh chữ ký**), `signedPdfSha256`, `replacesForm`/`replacedBy` (`{ id, formNo }`) và `actions`: `canEdit`, `canSubmit`, `canReturnToDraft`, `canCancel`, `canStartSigning`, `canConfirmStep`, `canConfirm`, `canRevoke`, `canReplace`; `notices`: danh sách thông báo cho người lập phiếu khi phiếu còn là nháp (ví dụ: hồ sơ người bệnh còn số thẻ BHYT mã cũ 15 ký tự), rỗng khi không có hoặc phiếu đã trình ký.

**Ký tay trên máy tính bảng (quyết định 10, 11)**

1. **Mở phiên ký:** nhân viên gọi `signing-session`, nhận **token phiên ký**. Token này có đối tượng nhận (`aud`) riêng:
   - Chỉ dùng được ở `patient-signature`, cho đúng phiếu và đúng phiên ghi trong token.
   - Token của bộ phiếu (`bundles/{bundleId}/signing-session`) liệt kê nhiều phiếu (claim `sign_form` dạng mảng) và chỉ dùng được ở `signing-bundle/patient-signature`; gửi vào `patient-signature` của một phiếu → `403`.
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

## 6d. Xuất dữ liệu (`/api/v1/exports`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/exports/consent-forms/columns` | `exports.run` | Danh mục cột có thể chọn: `key`, `header` (tiếng Việt), `group`, `default`, `sensitive` |
| POST | `/api/v1/exports/consent-forms` | `exports.run` (+ `exports.sensitive` để xuất đầy đủ) | Xuất phiếu ra file, trả file ngay (`Content-Disposition: attachment`). Giới hạn 10 lần / phút / người dùng (`429`) |
| GET | `/api/v1/exports?page=1&pageSize=20` | `exports.run` | Lịch sử xuất của người dùng; người có `audit.read` thấy mọi lần xuất |

Body của `POST /exports/consent-forms`:

```json
{
  "format": "Xlsx",
  "filter": { "status": "Completed", "templateId": "…", "departmentId": "…", "from": "2026-11-01", "to": "2026-11-30" },
  "columns": ["formNo", "status", "patientName", "citizenId", "decision"],
  "includeContent": false,
  "includeSensitiveData": false
}
```

- `format`: `Xlsx` (Excel, Office Open XML) hoặc `Csv` (UTF-8 có BOM, phân cách dấu phẩy, CRLF). Chuỗi bắt đầu bằng `= + - @` được thêm dấu nháy đơn (chống chèn công thức).
- `filter`: theo **ngày lập** (giờ Việt Nam); người không có `scope.all_departments` chỉ xuất được phiếu các khoa của mình (khoa chính và khoa khác) và phiếu mình lập (`departmentId` không phải khoa của mình → `403 exports.department_forbidden`).
- `columns`: thứ tự trong file theo danh mục, không theo thứ tự gửi lên. Cột nhạy cảm (`citizenId`, `phoneNumber`, `representativeCitizenId`) **mặc định bị che** (`0790******12`); `includeSensitiveData: true` cần quyền `exports.sensitive` (không có → `403 exports.sensitive_forbidden`).
- `includeContent: true`: thêm mỗi trường của mẫu một cột (nhãn tiếng Việt từ định nghĩa mẫu; lựa chọn in nhãn; trường điểm có cột tổng và cột mức); bắt buộc lọc theo đúng một `templateId`. Trường tự điền số thẻ BHYT/số điện thoại bị che trừ khi xuất đầy đủ; bảng nhiều dòng chỉ ghi số dòng khi che.
- Lỗi: không có phiếu nào khớp → `422 exports.no_rows`; quá **100.000** phiếu → `422 exports.too_many_rows` (thu hẹp bộ lọc).
- Phản hồi có header `X-Export-Job-Id`, `X-Export-Row-Count`, `Cache-Control: no-store`. File Excel có trang "Thông tin xuất" (mã lần xuất, người xuất, bộ lọc, phạm vi, mức che dữ liệu).
- Mỗi lần xuất ghi `ExportJobs` (bộ lọc, cột, số dòng, kích thước, SHA-256 của file, thời gian) và audit `Exports.ConsentForms`. **File không được lưu trên máy chủ.**

Mục `GET /exports` trả `ExportJob`: `id`, `requestedAtUtc`, `requestedByName`, `format`, `filter` (kèm `templateCode`, `departmentName` tại thời điểm xuất), `columns`, `includesContent`, `includesSensitiveData`, `rowCount`, `fileName`, `fileSizeBytes`, `fileSha256`, `durationMs`.

---

## 6e. Báo cáo thống kê và tổng quan

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/reports/summary?from=&to=&departmentId=&templateId=` | `reports.read` | Thống kê các phiếu **lập** trong khoảng ngày (mặc định: từ đầu tháng đến hôm nay, giờ Việt Nam; tối đa 366 ngày), trong phạm vi khoa |
| GET | `/api/v1/dashboard` | `consents.read` | Số liệu trang tổng quan của người dùng hiện tại |

`reports/summary` trả: `totals` (theo trạng thái hiện tại), `decisions` (đồng ý/từ chối và `refusalRate` 0–1 trên phiếu hoàn tất), `signers` (người bệnh tự ký / người đại diện), `completionTime` (số phiếu, trung bình, trung vị, phân vị 90 tính bằng phút; số phiếu đến 15 phút, đến 1 giờ, đến 1 ngày, trên 1 ngày), `byDepartment`, `byTemplate` (mỗi nhóm có `counts` và `decisions`), `daily` (mỗi ngày trong khoảng). Phiếu đã thu hồi không tính vào ý kiến và thời gian hoàn tất. `from > to` hoặc khoảng quá dài → `400`; `departmentId` không phải khoa của mình (khoa chính hoặc khoa khác; không có quyền toàn viện) → `403 reports.department_forbidden`.

`dashboard` trả: `drafts`, `pendingSignature`, `completedToday`, `revokedLast30Days`, `awaitingMyConfirmation` (phiếu người bệnh đã ký mà **bước xác nhận đang chờ** thuộc vai trò của người dùng: bước trung gian đầu tiên chưa xác nhận theo định nghĩa của đúng phiên bản mẫu, hết bước trung gian thì là xác nhận cuối; bỏ phiếu người dùng đã xác nhận một bước; `total` + tối đa 5 `items`, cũ nhất trước), `myOpenForms` (phiếu nháp/chờ ký do mình lập), `last14Days` (`date`, `created`, `completed`).

---

## 6f. Cấu hình hệ thống (`/api/v1/settings`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/settings` | `settings.manage` | `hospital` (cơ quan chủ quản, tên bệnh viện, địa danh, `updatedAtUtc`), `hisEnabled` (tra cứu HIS trực tiếp khi quét CCCD, đang tạm hoãn), `patientInfoEnabled` (đã cấu hình API thông tin người bệnh: Lấy thông tin, lượt tiếp nhận, số liệu HIS), `smartCaEnabled`, `exportMaxRows`, `retentionPolicy`. Không có bí mật nào |
| PUT | `/api/v1/settings/hospital` | `settings.manage` | `{ parentAgency, name, place }` (bắt buộc, ≤ 500 ký tự). Lưu trong bảng `SystemSettings`, ghi audit; phiếu in sau đó dùng tên mới, PDF đã ký giữ nguyên. Chưa đặt thì dùng cấu hình `Hospital:*` |
| GET | `/api/v1/settings/logo` | `settings.manage` | Ảnh logo đang dùng (`image/png` hoặc `image/jpeg`, `Cache-Control: no-store`); chưa có → `404` |
| PUT | `/api/v1/settings/logo` | `settings.manage` | `{ image }`: PNG/JPG dạng Base64 (có thể kèm `data:image/...;base64,`), tối đa 1 MB, mỗi chiều 32–4000 điểm ảnh; nhận dạng theo nội dung file, không nhận SVG/GIF (`400` ở trường `image`). File lưu trong kho tài liệu (mã hóa), khóa lưu trong `SystemSettings` (`Logo.*`, ghi audit). In ở góc trên bên trái phiếu (khoảng 18 × 18 mm) cho PDF tạo sau khi lưu; PDF đã ký giữ nguyên. File hỏng không đọc được thì phiếu in không có logo |
| DELETE | `/api/v1/settings/logo` | `settings.manage` | Bỏ logo; trả cấu hình mới (`logo: null`) |
| PUT | `/api/v1/settings/signature-frame` | `settings.manage` | `{ image }`: ảnh **khung chữ ký** (quyết định 26): **chỉ PNG** 8 bit, không xen kẽ (máy chủ đọc điểm ảnh để in lên PDF), tối đa 1 MB, mỗi chiều 32–4000 điểm ảnh; lỗi ở trường `image` ("Ảnh khung chữ ký cần là ảnh PNG.", hoặc PNG kiểu chưa đọc được). Lưu riêng với logo (khóa `SignatureFrame.*`, ghi audit). Trả cấu hình mới, có `signatureFrame: { contentType, width, height, updatedAtUtc }`. Làm nền khung ký trên màn hình; trên PDF tạo sau khi lưu, mỗi chỗ ký in khung này (đã ký thì chữ ký nằm giữa khung, chưa ký thì khung trống); ảnh chữ ký lưu lại không có khung |
| DELETE | `/api/v1/settings/signature-frame` | `settings.manage` | Bỏ khung chữ ký (khung ký trở lại nền trắng, viền nét đứt); trả cấu hình mới (`signatureFrame: null`) |
| GET | `/api/v1/settings/kiosk` | `settings.manage` | Mẫu dùng trên kiosk theo thứ tự làm: `{ templates: [{ id, code, name, problems[] }], updatedAtUtc }` (`problems`: lý do mẫu đã chọn nay không dùng được, ví dụ đã ngừng) |
| GET | `/api/v1/settings/encounters` | `settings.manage` | Gắn phiếu với lượt tiếp nhận HIS (quyết định 25): `{ enabled, available, updatedAtUtc }`. `enabled`: đang bắt buộc chọn lượt khi ký hoàn tất; `available`: đã cấu hình API thông tin người bệnh (`PatientSource:Provider = Api`) nên bật được |
| GET | `/api/v1/settings/form-inheritance` | `settings.manage` | Kế thừa phiếu lần trước (quyết định 27): `{ days, updatedAtUtc }`, `days` = 0 là tắt |
| PUT | `/api/v1/settings/form-inheritance` | `settings.manage` | `{ "days": 0–365 }`, có hiệu lực ngay cho phiếu lập sau đó; ngoài khoảng → `400` ở trường `days`. Lưu khóa `Forms.InheritanceDays`, ghi audit |
| PUT | `/api/v1/settings/encounters` | `settings.manage` | `{ "enabled": true \| false }`: bật/tắt, **có hiệu lực ngay** (không cần khởi động lại). HIS lỗi thì tắt để nhân viên hoàn tất tiếp. Bật khi chưa cấu hình API → `422 settings.encounters_unavailable`. Lưu khóa `Encounters.Enabled`, ghi audit |
| PUT | `/api/v1/settings/kiosk` | `settings.manage` | `{ templateIds: [...] }` theo thứ tự làm, tối đa 10, không trùng; chỉ nhận mẫu đang dùng, đã phát hành, không có trường bảng nhiều dòng/ngày giờ (`400` ở `templateIds`). Danh sách rỗng: bỏ cấu hình. Lưu khóa `Kiosk.TemplateCodes` (mã mẫu), ghi audit |

Mọi phản hồi của API có `Cache-Control: no-store` (trừ ảnh khung chữ ký bên dưới, không có dữ liệu cá nhân); thân yêu cầu lớn hơn 2 MB bị từ chối.

**Ảnh nhận diện (`/api/v1/branding`)**, không cần đăng nhập: điện thoại đang ở chế độ ký (token phiên ký) và kiosk cũng phải tải được. Chỉ trả ảnh do quản trị tải lên.

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/branding/signature-frame` | Không cần đăng nhập | Khung chữ ký đang dùng `{ contentType, width, height, updatedAtUtc }` (`Cache-Control: no-cache`); chưa có → `404`, khung ký hiện như mặc định |
| GET | `/api/v1/branding/signature-frame/image` | Không cần đăng nhập | Ảnh khung (`image/png`; khung JPG tải lên từ bản trước trả `image/jpeg`). Giao diện gọi kèm `?v=<updatedAtUtc>` nên ảnh được lưu đệm một ngày (`Cache-Control: public, max-age=86400`); chưa có → `404` |

---

## 6g. Kiosk người bệnh tự làm phiếu (`/api/v1/kiosk`, quyết định 24)

Chỉ tài khoản có quyền `kiosk.use` (vai trò `KIOSK`; quản trị có toàn quyền nên cũng gọi được). Máy chủ **không giữ trạng thái** giữa các bước: lệnh nộp gửi lại thông tin nhận diện và được kiểm tra lại như lần đầu.

| Phương thức | Đường dẫn | Mô tả |
|---|---|---|
| GET | `/api/v1/kiosk/config` | `{ departmentName, ready, templates: [{ id, code, name, definition }], statement }`: khoa của tài khoản kiosk, các mẫu kiosk dùng được ở khoa đó (theo thứ tự cấu hình), một ô lời xác nhận gộp từ các mẫu (bắt buộc nếu có mẫu bắt buộc, gộp câu gợi ý). `ready: false` khi chưa có mẫu. Tài khoản chưa có khoa → `422 kiosk.no_department` |
| POST | `/api/v1/kiosk/identify` | Nhận diện (xem bảng dưới). Trả `{ method, foundRecord, fullName, dateOfBirth, gender, citizenIdMasked, patientCode, permanentAddress, hasAddressOnFile, hasPhoneNumber, missingFields, prefilledSources, inherited }`. `inherited`: mẫu kiosk có phiếu lần trước kế thừa được (quyết định 27) `[{ templateId, keys }]`, **chỉ tên các câu đã trả lời, không có câu trả lời**; kiosk hỏi một câu "giữ như lần trước". Chưa ghi gì vào hồ sơ |
| POST | `/api/v1/kiosk/submit` | `{ identity, phoneNumber, forms: [{ templateId, data, decision }], statement, image, keepPrevious? }`: nhận diện lại; `keepPrevious: true` (người bệnh chọn không thay đổi so với lần trước) thì máy chủ tự tìm lại phiếu lần trước của hồ sơ có sẵn và lấy các câu kiosk không hỏi lại theo phiếu đó (câu vừa trả lời được ưu tiên; ý kiến Đồng ý/Không đồng ý vẫn phải chọn); tạo hoặc bổ sung hồ sơ (chỉ điền ô trống); lập phiếu cho **đúng các mẫu kiosk** (mẫu thiếu/thừa → `400 forms`), trường tự điền luôn lấy theo hồ sơ; trình ký và ghi **một chữ ký người bệnh cho cả bộ** (từ 2 phiếu thì chung `bundleId`), phiếu ở trạng thái `PendingSignature` chờ nhân viên xác nhận. Trả `{ patientName, patientCode, bundleId, forms: [{ id, formNo, templateName, decision }], signedAtUtc }`. Câu chưa trả lời → `400 kiosk.incomplete`, lỗi theo trường dạng `"<mã mẫu>.<trường>"` (ví dụ `TTNB-NGOAITRU.religion`, `TUVAN-CONGTT.decision`). Hồ sơ cần nhân viên xử lý (trùng mã/CCCD với hồ sơ khác, dữ liệu hệ thống bệnh viện sai định dạng) → `422 kiosk.needs_staff` |

**Cách nhận diện** (`identity.method`, luôn có `dateOfBirth` dạng `yyyy-MM-dd`):

| `method` | Thông tin gửi | Kiểm tra |
|---|---|---|
| `CitizenIdCard` | `citizenId` và `card: { fullName, gender, permanentAddress, issueDate, oldIdNumber }` tách từ mã QR trên CCCD | Số CCCD khớp năm sinh/giới tính. Có hồ sơ (phần mềm này hoặc hệ thống bệnh viện) thì ngày sinh phải khớp; chưa có thì lập hồ sơ mới từ thẻ |
| `LookupCode` | Một trong hai: `patientCode` (mã y tế) hoặc `citizenId` | **Ngày sinh phải khớp hồ sơ.** Không có hồ sơ và sai ngày sinh trả **cùng** `422 kiosk.lookup_failed` (kèm `remainingAttempts`); hệ thống bệnh viện không trả lời được → `422 kiosk.source_unavailable` (không tính lần sai). Không trả địa chỉ, số điện thoại của hồ sơ |
| `SelfReported` | `selfReport: { fullName, gender, phoneNumber, currentAddress }`, `citizenId` nếu có | Có số CCCD trùng hồ sơ thì ngày sinh phải khớp như nhập mã. Phiếu đánh dấu tự khai để nhân viên kiểm tra kỹ giấy tờ |

Sai 5 lần liên tiếp (tính cả khi nộp) thì **tạm khóa tra cứu 5 phút** trên tài khoản kiosk: `422 kiosk.lookup_locked` kèm `lockedUntilUtc`; tự khai không kèm số CCCD vẫn làm được (không tra ai). Khóa tra cứu không làm máy bị đăng xuất; quản trị mở khóa sớm bằng nút Mở khóa tài khoản. Nhật ký ghi cách nhận diện và kết quả (`Kiosk.Identified`, `Kiosk.LookupFailed`, `Kiosk.LookupLocked`, `Kiosk.Submitted`), không ghi mã y tế hay số CCCD.

Phiếu lập trên kiosk có `kioskIdentity` (`CitizenIdCard`/`LookupCode`/`SelfReported`) ở chi tiết phiếu và bộ phiếu (`GET /api/v1/consent-forms/{id}`, `GET /api/v1/consent-forms/bundles/{bundleId}`); khi chờ xác nhận, `notices` có lời nhắc đối chiếu CCCD (tự khai: kiểm tra kỹ giấy tờ và thông tin hành chính).

## 6h. Báo cáo điều dưỡng hằng ngày (`/api/v1/nursing-reports`, quyết định 31, 35)

Khoa lập báo cáo theo bản giấy "Báo cáo công tác điều dưỡng"; Phòng Điều dưỡng tổng hợp theo bản giấy "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày". Ngày là ngày của bệnh viện (`yyyy-MM-dd`). Khi HIS lỗi, mỗi khoa nhập số liệu HIS thuộc nhiệm vụ của khoa (`PUT /his-entry`); Phòng Điều dưỡng không nhập thay, chỉ tổng hợp (quyết định 35).

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/form` | Đăng nhập | Mẫu báo cáo khoa: `{ title, category, sections: [{ title, valueKey, items: [{ key, label, carryOver, since }] }], checks: [{ key, label, inSummary }], issueLabel, okLabel, notesLabel, maxValue, notesMaxLength }`. `valueKey`: dòng tiêu đề mục là ô nhập (mục IV. ADR). `since`: phiên bản mẫu thêm dòng này (2: `foreignPatients`, `transfersOutYesterday` của mục VI, quyết định 44; 1: có từ đầu) |
| GET | `/daily?date=&departmentId=` | `nursingreports.submit` (khoa mình; thêm `scope.all_departments` thì mọi khoa) hoặc `nursingreports.read`/`manage` (mọi khoa) | Báo cáo của khoa trong ngày; bỏ trống `departmentId` là khoa chính của người dùng (tài khoản nhiều khoa chọn được từng khoa của mình). Chưa có thì `status: "New"`, `id: null`, điền sẵn các dòng `carryOver` (nhân sự, chỉ tiêu) theo báo cáo đã gửi gần nhất (`carriedOverKeys`). Trả `{ id, departmentId, departmentName, reportDate, status, values, checks, notes, canEdit, canReopen, updatedBy, updatedAtUtc, submittedBy, submittedAtUtc, reopenedBy, reopenedAtUtc, reopenReason, carriedOverKeys, isReportingDepartment, hisEntry }`. `hisEntry` (số liệu HIS khoa nhập khi HIS lỗi; `null` khi khoa không có dòng nào phải nhập): `{ lines: [{ key, label, value, hisValue }], canEdit, hisHasValues, hisUpdatedAtUtc, updatedBy, updatedAtUtc }`; `hisValue` là số lấy từ HIS của ngày (nếu có) |
| PUT | `/his-entry` | `nursingreports.submit` (khoa mình; thêm `scope.all_departments` thì mọi khoa) | `{ departmentId?, reportDate, values: { key: số \| null } }`: khoa nhập số liệu HIS của ngày khi HIS lỗi. Khoa phải báo cáo nhập `waitingAdmission` của khoa mình; khoa được giao (`PUT /departments`, `hisEntry`) nhập các chỉ số được giao. Lưu riêng, không khóa theo báo cáo đã gửi; ô `null` là xóa số đã nhập; ghi nhật ký. Trả báo cáo như `GET /daily`. Dòng không được giao hoặc số ngoài 0–99.999 → `400 values.{key}`; khoa không có dòng nào phải nhập → `422 nursing_reports.no_his_entry`; khoa khác khoa mình → `403`; ngày sau hôm nay hoặc quá 366 ngày → `400 reportDate` |
| PUT | `/daily` | `nursingreports.submit` | `{ departmentId?, reportDate, values: { key: số \| null }, checks: { key: "ok" \| "issue" \| null }, notes, submit }`: tạo hoặc sửa báo cáo của khoa trong ngày; `submit: true` thì gửi luôn và khóa. Số từ 0 đến 99.999; gửi thì phải chọn đủ phần tick (`400 checks`). Khoa khác khoa mình → `403`; khoa chưa có trong danh sách báo cáo → `422 nursing_reports.department_not_reporting`; đã gửi → `422 nursing_reports.submitted`; ngày sau hôm nay hoặc quá 366 ngày → `400 reportDate` |
| POST | `/{id}/reopen` | `nursingreports.manage` | `{ reason }` (bắt buộc, ≤ 500 ký tự): mở lại báo cáo đã gửi về nháp để khoa sửa và gửi lại (nút **Trả lại khoa** trên giao diện, quyết định 36). Chưa gửi → `422 nursing_reports.not_submitted` |
| GET | `/{id}/pdf` | như `GET /daily` | PDF báo cáo khoa theo bố cục bản giấy (chân trang ghi đã gửi lúc, bởi ai hoặc "Bản nháp") |
| GET | `/summary?date=` | `nursingreports.read` | Bảng tổng hợp: `{ reportDate, columns, rows: [{ departmentId, departmentCode, departmentName, isReporting, reportId, status, submittedBy, submittedAtUtc, cells }], totals, outpatient, procedures, imaging, situation: [{ key, label, text, issueDepartments }], incidents: [{ departmentId, departmentCode, departmentName, text }], statistics: { hisAvailable, hasValues, source, updatedAtUtc, updatedBy, entries, usesDepartmentEntries }, submittedCount, reportingCount, reporters: { outpatient, procedures, imaging, situation, incidents } }`. Chỉ báo cáo đã gửi được tính. `rows` xếp theo `departmentCode` (mã khoa, in ở cột khoa phòng; quyết định 44); `submittedBy` là cột "Người báo cáo". Ô của dòng mẫu thêm sau khi khoa gửi (`since` lớn hơn phiên bản mẫu của báo cáo) là `null`. Dòng mục B, C, D: `{ key, label, value, enteredBy }`; `enteredBy` là mã khoa đã nhập tay số đó khi HIS lỗi. `reporters`: người báo cáo mục B–F, dạng "Họ tên (mã khoa)" cách nhau bởi "; " (B, D: người khoa nhập tay hoặc "Họ tên (lấy từ HIS)"; C, E, F: người gửi báo cáo của khoa có số PTTT, báo "Không ổn", có phần nhập thêm, không khoa nào có thì "Các khoa đã gửi báo cáo (cột Người báo cáo, mục A)"); `null` khi mục chưa có số liệu. `entries`: các khoa phải nhập tay khi HIS lỗi `[{ departmentId, departmentName, keys, enteredAtUtc, enteredBy }]` (`enteredAtUtc: null` là chưa nhập), để Phòng Điều dưỡng nhắc khoa. Số lấy từ HIS dùng trước; ô HIS không có số mới dùng số khoa nhập |
| GET | `/summary/pdf?date=`, `/summary/xlsx?date=` | `nursingreports.read` | Bảng tổng hợp dạng PDF (bố cục bản giấy) và Excel (`.xlsx`, số ghi dạng số) |
| POST | `/statistics/refresh` | `nursingreports.manage` | `{ reportDate }`: lấy số liệu HIS của ngày (thay số HIS cũ của ngày) và lưu lại. Số khoa nhập tay vẫn giữ, chỉ dùng cho ô HIS không có số. Trả `{ summary, unmatchedDepartmentCodes }`. Chưa cấu hình API thông tin người bệnh → `422 nursing_reports.his_disabled`; HIS lỗi hoặc chưa có câu truy vấn → `422 nursing_reports.his_unavailable` |
| GET | `/departments` | `nursingreports.manage` | Các khoa: `[{ departmentId, code, name, isReporting, bedCount, hisIndicators }]`; `hisIndicators`: các chỉ số mục B, D khoa được giao nhập khi HIS lỗi |
| PUT | `/departments` | `nursingreports.manage` | `{ items: [{ departmentId, bedCount }], hisEntry?: { chỉ số: departmentId \| null } }`: danh sách khoa phải báo cáo và số giường thực kê (khoa không có trong danh sách thì bỏ ra). `hisEntry`: khoa nhập tay từng chỉ số mục B, D khi HIS lỗi, gửi đủ cả bảng (chỉ số không có hoặc `null` là chưa giao; bỏ cả `hisEntry` thì giữ nguyên). Chỉ số lạ hoặc khoa ngừng dùng → `400 hisEntry.{chỉ số}` |

Từ quyết định 35, Phòng Điều dưỡng không còn nhập tay số liệu HIS: `PUT /statistics` đã bỏ (`404`/`405`).

**Chỉ số HIS** (cột `Indicator` của câu truy vấn `PatientInfo__DailyStatisticsQuery`): `outpatientVisits` (B. TSNB đến khám), `ultrasound`, `echocardiography`, `ecg`, `xray`, `ct`, `mri` (D. CĐHA) tính toàn viện (trả theo khoa thì cộng lại); `waitingAdmission` (NB PK chờ nhập viện) theo khoa, cột `DepartmentCode` trùng mã khoa trong phần mềm phiếu (không phân biệt hoa thường; mã không khớp được trả lại ở `unmatchedDepartmentCodes`).

**Bảng tổng hợp:** "Số ca đi ngoại viện" là tổng 4 dòng mục V. Mục E: "Ổn" khi mọi khoa đã gửi báo ổn, "Không ổn: <mã khoa>" khi có khoa báo sự cố, trống khi chưa khoa nào gửi. Mục F: phần nhập thêm của khoa có ghi. PDF và Excel có cột "Người báo cáo" cuối bảng A, dòng "Người báo cáo: …" dưới mỗi mục B–F và chỗ ký "PHÒNG ĐIỀU DƯỠNG" cuối bảng (quyết định 44). Số liệu khoa lưu ở `FiguresJson` dạng `{ "values": {…}, "checks": {…}, "form": 2 }`; `form` là phiên bản mẫu lúc lưu (báo cáo lưu trước quyết định 44 không có, tính là 1). Phần nhập thêm có thể có thông tin người bệnh: lưu mã hóa (cột `NotesEncrypted`), nhật ký chỉ ghi `***`.

---

## 7. Nhật ký audit (`/api/v1/audit-logs`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/audit-logs?userId=&action=&entityType=&entityId=&fromUtc=&toUtc=&page=1&pageSize=50` | `audit.read` | Tra cứu, mới nhất trước |

**Không có API sửa hoặc xóa nhật ký.** Bảng `AuditLogs` còn được bảo vệ bằng trigger trong SQL Server (mọi lệnh UPDATE/DELETE đều bị từ chối).

Hành động (`action`) được ghi:

| Hành động | Nguồn |
|---|---|
| `Create`, `Update`, `Delete` | Tự động cho mọi thay đổi trên khoa, người dùng, vai trò, gán vai trò, gán quyền, người bệnh, người đại diện, mẫu phiếu, phiên bản mẫu, phiếu đồng thuận, chữ ký, cấu hình hệ thống. `details` chứa giá trị (tạo/xóa) hoặc `{ "Trường": { "old": …, "new": … } }` (sửa); dữ liệu nhạy cảm (mật khẩu đã băm, số CCCD, số điện thoại, mã BHYT, nội dung phiếu) luôn hiển thị `***` |
| `Auth.LoginSucceeded`, `Auth.LoginFailed`, `Auth.LockedOut` | Đăng nhập (`details.reason`: `unknown_user`, `invalid_password`, `locked_out`, `inactive`) |
| `Auth.Logout`, `Auth.PasswordChanged`, `Auth.RefreshTokenReuseDetected` | Phiên đăng nhập |
| `Patients.Viewed`, `Patients.LookedUp` | Xem hồ sơ người bệnh; tra cứu theo CCCD (không ghi số CCCD) |
| `ConsentForms.Viewed`, `ConsentForms.PdfViewed` | Xem chi tiết phiếu; tải/xem PDF phiếu |
| `ConsentForms.SigningStarted`, `ConsentForms.PatientSigned`, `ConsentForms.Completed`, `ConsentForms.Revoked` | Mở phiên ký; người bệnh ký (kèm quyết định, người ký; thiết bị lưu ở chữ ký); phía bệnh viện xác nhận (kèm SHA-256 của PDF); thu hồi |
| `Exports.ConsentForms` | Xuất dữ liệu phiếu (`details`: định dạng, số dòng, cột, kèm nội dung, dữ liệu nhạy cảm, bộ lọc, SHA-256 của file; `entityId` = mã lần xuất) |

Mỗi bản ghi có thời điểm (UTC), người thực hiện, địa chỉ IP, mã truy vết (`correlationId`).

---

## 8. Hệ thống

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/health` | Không cần đăng nhập | Tiến trình còn chạy |
| GET | `/health/ready` | Không cần đăng nhập | Kết nối được SQL Server (`503` nếu không) |
| GET | `/openapi/v1.json` | Không cần đăng nhập | Chỉ có ở môi trường Development |
