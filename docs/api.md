# Tài liệu API — Quản lý Phiếu đồng thuận

> Phiên bản API: `v1`. Tài liệu được cập nhật theo từng sprint (hiện tại: Sprint 1 — xác thực, phân quyền, quản trị, nhật ký).
> Ở môi trường Development, đặc tả OpenAPI tự sinh có tại `GET /openapi/v1.json`.
>
> Mọi ví dụ dưới đây dùng **dữ liệu giả lập**.

## 1. Quy ước chung

| Chủ đề | Quy ước |
|---|---|
| Địa chỉ gốc | Cùng domain với giao diện web (qua reverse proxy): `/api/v1/...` |
| Định dạng | JSON (UTF-8), tên trường camelCase, thời gian ISO 8601 UTC (ví dụ `2026-11-11T08:30:00+00:00`) |
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

**Vai trò mặc định**: `ADMIN` (Quản trị hệ thống), `TEMPLATE_MANAGER`, `DOCTOR`, `NURSE`, `RECEPTIONIST`, `DEPARTMENT_HEAD`, `REPORTER`, `AUDITOR`. Quyền mặc định của từng vai trò: xem `backend/src/Consent.Domain/Identity/SystemRoles.cs`.

---

## 6. Nhật ký audit (`/api/v1/audit-logs`)

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/audit-logs?userId=&action=&entityType=&entityId=&fromUtc=&toUtc=&page=1&pageSize=50` | `audit.read` | Tra cứu, mới nhất trước |

**Không có API sửa hoặc xóa nhật ký.** Bảng `AuditLogs` còn được bảo vệ bằng trigger trong SQL Server (mọi lệnh UPDATE/DELETE đều bị từ chối).

Hành động (`action`) được ghi:

| Hành động | Nguồn |
|---|---|
| `Create`, `Update`, `Delete` | Tự động cho mọi thay đổi trên khoa, người dùng, vai trò, gán vai trò, gán quyền. `details` chứa giá trị (tạo/xóa) hoặc `{ "Trường": { "old": …, "new": … } }` (sửa); mật khẩu đã băm luôn hiển thị `***` |
| `Auth.LoginSucceeded`, `Auth.LoginFailed`, `Auth.LockedOut` | Đăng nhập (`details.reason`: `unknown_user`, `invalid_password`, `locked_out`, `inactive`) |
| `Auth.Logout`, `Auth.PasswordChanged`, `Auth.RefreshTokenReuseDetected` | Phiên đăng nhập |

Mỗi bản ghi có thời điểm (UTC), người thực hiện, địa chỉ IP, mã truy vết (`correlationId`).

---

## 7. Hệ thống

| Phương thức | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| GET | `/health` | Không cần đăng nhập | Tiến trình còn chạy |
| GET | `/health/ready` | Không cần đăng nhập | Kết nối được SQL Server (`503` nếu không) |
| GET | `/openapi/v1.json` | Không cần đăng nhập | Chỉ có ở môi trường Development |
