# Quản lý Phiếu đồng thuận

Ứng dụng web giúp bệnh viện lập, ký số (VNPT SmartCA) và lưu trữ các phiếu đồng thuận/cam kết của người bệnh trước khi khám bệnh, chữa bệnh. Thông tin nhân thân lấy từ QR CCCD và HIS FPT eHospital.

> **Trạng thái:** Sprint 1 hoàn thành (đăng nhập, phân quyền theo vai trò, nhật ký audit, quản trị người dùng/vai trò/khoa). Xem [lộ trình](#lộ-trình).
>
> **Không dùng dữ liệu người bệnh thật** trên môi trường phát triển/kiểm thử. Mọi dữ liệu mẫu đều là dữ liệu giả lập.

## Công nghệ

| Thành phần | Công nghệ |
|---|---|
| Backend | ASP.NET Core .NET 10 Web API, Clean Architecture, EF Core 10 |
| Cơ sở dữ liệu | Microsoft SQL Server |
| Frontend | React 19 + TypeScript + Vite, Ant Design 6 (tiếng Việt), TanStack Query, React Router |
| Kiểm thử | xUnit (backend), Vitest + Testing Library (frontend) |
| Hạ tầng | Docker Compose, GitHub Actions |

Chi tiết: [docs/kien-truc.md](docs/kien-truc.md).

## Cấu trúc repo

```
backend/     Solution .NET (src/Consent.*, tests/), Dockerfile
frontend/    Ứng dụng React (src/), nginx.conf, Dockerfile
docs/        Kế hoạch, yêu cầu nghiệp vụ, kiến trúc, tích hợp SmartCA
scripts/     run-checks.sh: chạy mọi kiểm tra giống CI
docker-compose.yml, .env.example
```

## Chạy nhanh bằng Docker

Yêu cầu: Docker (có Docker Compose v2).

```bash
cp .env.example .env
# Mở .env và điền: MSSQL_SA_PASSWORD, JWT_SIGNING_KEY (openssl rand -base64 48),
# SEED_ADMIN_PASSWORD, SEED_DEMO_PASSWORD (≥ 10 ký tự, có chữ hoa, chữ thường, chữ số, ký tự đặc biệt)
docker compose up -d --build
```

| Địa chỉ | Mô tả |
|---|---|
| http://localhost:8080 | Giao diện web |
| http://localhost:8080/health | API còn hoạt động (liveness) |
| http://localhost:8080/health/ready | API kết nối được SQL Server (readiness) |

Ở môi trường Development, API tự áp dụng migration và khởi tạo dữ liệu khi khởi động:

| Tài khoản | Vai trò | Mật khẩu |
|---|---|---|
| `admin` | Quản trị hệ thống | `SEED_ADMIN_PASSWORD`; **phải đổi ở lần đăng nhập đầu** |
| `bacsi.demo`, `dieuduong.demo`, `tiepdon.demo`, `truongkhoa.demo`, `mauphieu.demo`, `baocao.demo`, `kiemtra.demo` | Bác sĩ, Điều dưỡng, Tiếp đón, Trưởng khoa, Quản lý mẫu phiếu, Báo cáo, Kiểm tra | `SEED_DEMO_PASSWORD` |

Tài khoản và khoa demo chỉ là dữ liệu giả lập; tắt bằng `SEED_DEMO_DATA=false`. Dừng: `docker compose down` (thêm `-v` để xóa luôn dữ liệu SQL Server).

## Phát triển cục bộ

Yêu cầu: .NET SDK 10, Node.js 22, Docker (để chạy SQL Server).

**1. SQL Server** (chỉ mở cổng trên `127.0.0.1:14333`):

```bash
docker compose up -d sqlserver
```

**2. Backend** (http://localhost:5080):

```bash
cd backend
# Lưu bí mật vào user-secrets (nằm ngoài repo, không bị commit)
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,14333;Database=ConsentManagement;User Id=sa;Password=<mật khẩu trong .env>;Encrypt=True;TrustServerCertificate=True" \
  --project src/Consent.Api
dotnet user-secrets set "Jwt:SigningKey" "<chuỗi ngẫu nhiên ≥ 32 ký tự>" --project src/Consent.Api
dotnet user-secrets set "Seed:AdminPassword" "<mật khẩu quản trị ban đầu>" --project src/Consent.Api
dotnet user-secrets set "Seed:DemoPassword" "<mật khẩu tài khoản demo>" --project src/Consent.Api
dotnet run --project src/Consent.Api --launch-profile http
```

OpenAPI (chỉ ở Development): http://localhost:5080/openapi/v1.json

**3. Frontend** (http://localhost:5173, tự chuyển tiếp `/api` và `/health` tới API):

```bash
cd frontend
npm ci
npm run dev
```

**Tạo migration mới** (sau khi thay đổi mô hình dữ liệu):

```bash
cd backend
dotnet tool restore
dotnet tool run dotnet-ef migrations add <TenMigration> \
  --project src/Consent.Persistence --startup-project src/Consent.Persistence --output-dir Migrations
```

## Kiểm thử và kiểm tra chất lượng

Chạy toàn bộ kiểm tra giống CI:

```bash
./scripts/run-checks.sh
```

Hoặc từng phần:

| Lệnh | Nội dung |
|---|---|
| `cd backend && dotnet build` | Build (cảnh báo bị coi là lỗi) |
| `cd backend && dotnet test` | Unit test, integration test API (khởi động SQL Server bằng Testcontainers, **cần Docker**), test quy tắc phụ thuộc Clean Architecture |
| `cd frontend && npm run lint` | oxlint |
| `cd frontend && npm run typecheck` | Kiểm tra kiểu TypeScript |
| `cd frontend && npm test` | Unit test giao diện |
| `cd frontend && npm run build` | Build production |
| `docker compose config --quiet` | Kiểm tra cấu hình Docker Compose |

CI (GitHub Actions, `.github/workflows/ci.yml`) chạy các bước trên cho mọi pull request, kèm build Docker image.

## Bảo mật

- Không commit bí mật: mật khẩu, chuỗi kết nối, khóa ký JWT, thông tin SmartCA/HIS. Dùng biến môi trường, `dotnet user-secrets` hoặc file `.env` (đã nằm trong `.gitignore`). Thiếu khóa ký JWT thì API không khởi động.
- Đăng nhập: mật khẩu băm PBKDF2 (ASP.NET Core Identity), khóa tạm 15 phút sau 5 lần sai, giới hạn tần suất theo IP. Access token 15 phút chỉ giữ trong bộ nhớ trình duyệt; refresh token trong cookie `HttpOnly` + `SameSite=Strict`, xoay vòng mỗi lần dùng và thu hồi cả phiên khi phát hiện dùng lại.
- Mọi API mặc định yêu cầu đăng nhập; phân quyền theo quyền (permission) gán cho vai trò.
- Nhật ký audit ghi mọi thao tác thêm/sửa/xóa và sự kiện đăng nhập; không thể sửa/xóa (không có API, có trigger chặn trong SQL Server).
- API trả lỗi dạng ProblemDetails tiếng Việt, không lộ chi tiết nội bộ; có security headers; OpenAPI chỉ bật ở Development.
- Cấu hình Docker Compose dùng tài khoản `sa` **chỉ cho môi trường phát triển**. Môi trường chính thức dùng tài khoản SQL riêng với quyền tối thiểu (hướng dẫn trong `docs/trien-khai.md`, Sprint 6).

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [docs/KE_HOACH_TRIEN_KHAI.md](docs/KE_HOACH_TRIEN_KHAI.md) | Kế hoạch triển khai, danh sách màn hình, sprint và tiêu chí nghiệm thu |
| [docs/yeu-cau-nghiep-vu.md](docs/yeu-cau-nghiep-vu.md) | Yêu cầu nghiệp vụ (SRS) |
| [docs/kien-truc.md](docs/kien-truc.md) | Kiến trúc, mô hình dữ liệu, xác thực/phân quyền/audit, ADR |
| [docs/api.md](docs/api.md) | Tài liệu API (endpoint, quyền, mã lỗi, ví dụ) |
| [docs/tich-hop-smartca.md](docs/tich-hop-smartca.md) | Thiết kế tích hợp VNPT SmartCA |

## Lộ trình

| Sprint | Nội dung | Trạng thái |
|---|---|---|
| 0 | Nền móng: khung backend/frontend, Docker, CI, tài liệu yêu cầu và kiến trúc | ✅ Hoàn thành |
| 1 | Xác thực, phân quyền (RBAC), audit log, quản trị người dùng/vai trò/khoa | ✅ Hoàn thành |
| 2 | Người bệnh, quét QR CCCD, tích hợp HIS (giả lập trước) | Chưa bắt đầu |
| 3 | Mẫu phiếu và lập phiếu | Chưa bắt đầu |
| 4 | Ký số VNPT SmartCA (PA1 → PA3 → PA2) | Chưa bắt đầu |
| 5 | Xuất dữ liệu, báo cáo, nhật ký | Chưa bắt đầu |
| 6 | Hoàn thiện, rà soát bảo mật, hướng dẫn triển khai | Chưa bắt đầu |
