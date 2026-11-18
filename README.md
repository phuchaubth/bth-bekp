# BEKP – Dashboard điều hành nội trú realtime

BTH Enterprise Knowledge Platform (BEKP). Phân hệ đầu tiên: **dashboard quản lý realtime cho các khoa lâm sàng nội trú** của bệnh viện chuyên khoa Huyết học – Truyền máu.

> Toàn bộ dữ liệu trong repo là **dữ liệu mô phỏng**. Hệ thống chỉ lưu mã đợt điều trị ẩn danh (`ADM-000000`) và mã ICD-10, **không** lưu họ tên, ngày sinh hay mã hồ sơ người bệnh.

## Màn hình

| Đường dẫn | Màn hình | Nội dung chính |
|---|---|---|
| `/` | Toàn viện | 6 chỉ số realtime, bảng tình trạng 8 khoa, sơ đồ giường thu gọn, cảnh báo, đồng hồ kháng sinh sốt giảm BC, tồn kho chế phẩm máu, sự kiện trực tiếp, mô hình bệnh tật, thời gian thực hiện theo quy định |
| `/departments/:code` | Từng khoa | Sơ đồ giường theo phòng (nặng, giảm BC, truyền máu, hóa trị, dự kiến ra viện, trống, vệ sinh, kê thêm), giường đang chọn, yêu cầu chế phẩm máu, cảnh báo khoa, mô hình bệnh tật và thời gian thực hiện của khoa |
| `/wallboard` | Phòng điều hành | Màn hình 1920×1080 nền tối, tự luân phiên Toàn viện → từng khoa. Tham số: `?seconds=15` (5–120), `?rotate=0` để tắt luân phiên |

## Kiến trúc

```
backend/   ASP.NET Core 10 Web API – Clean Architecture (web.config cho IIS trong BEKP.Api)
  BEKP.Domain          Thực thể & quy tắc nghiệp vụ (không phụ thuộc tầng nào)
  BEKP.Application     Use case, DTO, cổng (interface) – chỉ phụ thuộc Domain + Shared
  BEKP.Persistence     EF Core + SQL Server (schema "bekp"), repository, migration, seeder mô phỏng
  BEKP.Infrastructure  SignalR hub/notifier, đồng hồ hệ thống, audit log, bộ mô phỏng sự kiện
  BEKP.Api             Controller, xác thực/ủy quyền, middleware, DI
  BEKP.Shared          Hằng số vai trò & chính sách RBAC
  tests/               xUnit: Domain, Application, Persistence (SQLite in-memory), Api (WebApplicationFactory)
frontend/  React 19 + TypeScript + Vite + MUI + TanStack Query + SignalR client
database/  Script SQL migration (idempotent) cho DBA
docs/      Tài liệu thiết kế
scripts/   check.sh – chạy toàn bộ kiểm tra; publish-iis.ps1 / publish-iis.sh – đóng gói triển khai IIS
```

**Realtime:** mọi thay đổi (nhập/ra viện, trạng thái giường, sốt giảm BC, yêu cầu máu, tồn kho) đi qua API tích hợp → lưu DB → ghi `OperationalEvent` → phát qua SignalR (`/hubs/dashboard`). Trình duyệt nhận sự kiện, làm mới đúng truy vấn bị ảnh hưởng; nếu mất kết nối, tự cập nhật mỗi 60 giây.

## Cơ sở dữ liệu

SQL Server, **database riêng `BEKP`** trên cùng máy chủ SQL với app Giấy đồng thuận. Bảng nằm trong schema `bekp`. Chuỗi kết nối **chỉ** truyền qua biến môi trường `ConnectionStrings__Bekp` (không commit).

- Phát triển: `Database__ApplyMigrationsOnStartup=true` tự áp migration.
- Production: DBA chạy `database/migrations/*.sql` (idempotent); khuyến nghị đăng nhập SQL bằng Windows Authentication của Application Pool IIS (`IIS APPPOOL\BEKP`), quyền tối thiểu trên database `BEKP`.

## Chạy local (máy phát triển)

Yêu cầu: .NET SDK 10, Node.js ≥ 22 với **npm ≥ 11**, SQL Server (Developer/Express/LocalDB hoặc máy chủ dùng chung – database riêng `BEKP`).

```powershell
# 1) Backend – Development: tài khoản dev cục bộ, tự áp migration, seed dữ liệu mô phỏng, bộ mô phỏng sự kiện
cd backend
$env:ConnectionStrings__Bekp = "Server=(localdb)\MSSQLLocalDB;Database=BEKP;Integrated Security=true;TrustServerCertificate=true"
dotnet run --project BEKP.Api      # http://localhost:5080

# 2) Frontend
cd ../frontend
npm ci
npm run dev                        # http://localhost:5173 (proxy /api, /hubs → :5080)
```

## Triển khai IIS (không dùng Docker)

Một site IIS duy nhất: API ASP.NET Core (in-process) phục vụ luôn giao diện React trong `wwwroot`, SignalR chạy qua WebSocket của IIS – không cần ARR/URL Rewrite.

```powershell
# Site riêng:              ./scripts/publish-iis.ps1 -Output C:\deploy\bekp
# Sub-application /bekp:   ./scripts/publish-iis.ps1 -Output C:\deploy\bekp -BasePath /bekp/
```

Hướng dẫn từng bước (Hosting Bundle, Application Pool, WebSocket, chuỗi kết nối Windows Authentication, HTTPS, migration): **[docs/deploy-iis.md](docs/deploy-iis.md)**.

## Xác thực & phân quyền (RBAC)

| Vai trò (claim `roles`) | Quyền |
|---|---|
| `bekp.dashboard.viewer` | Xem dashboard, kết nối realtime (`Dashboard.Read`) |
| `bekp.integration` | Gửi sự kiện từ HIS / LIS / ngân hàng máu (`Inpatient.Write`) |
| `bekp.admin` | Cả hai |

- Mặc định `Authentication:Mode=Jwt`: bắt buộc cấu hình `Authentication:Authority` và `Authentication:Audience` (OpenID Connect). Thiếu cấu hình → API không khởi động.
- `Authentication:Mode=Development` chỉ được phép khi `ASPNETCORE_ENVIRONMENT=Development` (ứng dụng từ chối khởi động ở môi trường khác).
- Mọi endpoint mặc định yêu cầu đăng nhập; chỉ `/health` cho phép ẩn danh.
- Mọi thao tác ghi được ghi audit (`BEKP.Audit`): người thực hiện, hành động, mã đối tượng, thời điểm UTC.

## API

| Phương thức | Đường dẫn | Quyền |
|---|---|---|
| GET | `/api/departments` | Dashboard.Read |
| GET | `/api/dashboard/overview` | Dashboard.Read |
| GET | `/api/dashboard/departments/{DEP-0000}` | Dashboard.Read |
| GET | `/api/dashboard/time-standards?period=today\|last7Days\|last30Days&department=DEP-0000` | Dashboard.Read |
| POST | `/api/inpatient/stays` | Inpatient.Write |
| POST | `/api/inpatient/stays/{ADM-000000}/discharge` | Inpatient.Write |
| PUT | `/api/inpatient/stays/{ADM-000000}/clinical-status` | Inpatient.Write |
| PUT | `/api/inpatient/departments/{DEP-0000}/beds/{bed}/status` | Inpatient.Write |
| POST | `/api/inpatient/stays/{ADM-000000}/febrile-neutropenia` | Inpatient.Write |
| POST | `/api/inpatient/febrile-neutropenia/{id}/antibiotic` | Inpatient.Write |
| POST | `/api/transfusion/requests` | Inpatient.Write |
| PUT | `/api/transfusion/requests/{id}/status` | Inpatient.Write |
| PUT | `/api/transfusion/inventory/{product}/{aboGroup}` | Inpatient.Write |

Enum dạng chuỗi camelCase (ví dụ `"careLevel": "level2"`), thời gian ISO-8601 có múi giờ. Lỗi trả về RFC 7807: 400 (dữ liệu không hợp lệ), 404, 409 (vi phạm quy tắc nghiệp vụ). Chi tiết: [docs/realtime-inpatient-dashboard.md](docs/realtime-inpatient-dashboard.md).

## Cấu hình chính

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `ConnectionStrings__Bekp` | — | Chuỗi kết nối SQL Server (biến môi trường) |
| `Hospital__TimeZoneId` | `Asia/Ho_Chi_Minh` | Xác định "hôm nay" |
| `Database__ApplyMigrationsOnStartup` | `false` | Tự áp migration |
| `Database__SeedSyntheticData` | `false` | Seed dữ liệu mô phỏng (bị từ chối ở Production) |
| `Simulation__Enabled` | `false` | Sinh sự kiện mô phỏng (bị từ chối ở Production) |
| `TimeStandards__Standards__<Loại>__TargetMinutes` / `__Reference` / `__ReferenceConfirmed` | mục tiêu tạm | Mục tiêu thời gian và văn bản căn cứ (BYT / nội bộ) |
| `Cors__AllowedOrigins__0` | — | Chỉ cần khi frontend chạy khác domain (mặc định cùng site IIS) |

## Kiểm tra chất lượng

```bash
./scripts/check.sh   # build + test backend, lint + test + build frontend
./scripts/publish-iis.sh /tmp/bekp-iis /   # kiểm tra gói triển khai IIS
```

## Hạn chế đã biết

- Chưa có module chỉ số quản lý tháng/quý (KPI) – cần Phòng QLCL xác nhận định nghĩa và nguồn dữ liệu (PR tiếp theo).
- Mục tiêu "thời gian thực hiện theo quy định" đang là **giá trị tạm**, đánh dấu "Chưa xác nhận" trên giao diện cho đến khi cấu hình văn bản BYT/quy định nội bộ.
- Chưa có báo cáo phản ứng truyền máu (huyết cảnh giác) và dữ liệu nhân lực ca trực.
- Frontend chưa tích hợp đăng nhập OIDC (đã có điểm cắm `setAccessTokenProvider`).
- Audit log hiện ghi ra log có cấu trúc; lưu trữ bền vững sẽ bổ sung sau.
- Font Be Vietnam Pro tải từ Google Fonts; nếu máy chủ/máy trạm không ra Internet, giao diện dùng font hệ thống thay thế.
