# Hướng dẫn triển khai — Quản lý Phiếu đồng thuận

> Dành cho phòng CNTT bệnh viện. Áp dụng cho máy chủ **Windows Server** tại bệnh viện (quyết định 5), theo một trong hai hình thức: **A. IIS + SQL Server có sẵn** hoặc **B. Docker** (container Linux) + SQL Server có sẵn. Phần sao lưu, khôi phục, khóa và vận hành dùng chung.
> Các file mẫu nằm trong thư mục [`deploy/`](../deploy). Mọi giá trị trong tài liệu này là ví dụ; **không có bí mật thật nào trong repo**.

---

## 1. Kiến trúc triển khai

```
Trình duyệt (máy tính bàn, máy tính bảng tại giường) ──HTTPS──► Reverse proxy (IIS hoặc nginx)
                                                                   ├── /            → file tĩnh của giao diện web (frontend/dist)
                                                                   └── /api, /health → API ASP.NET Core (cổng nội bộ, không mở ra ngoài)
                                                                                          ├── SQL Server (CSDL ConsentManagement)
                                                                                          └── Thư mục PDF đã ký (Storage:DocumentsPath)
```

- **HTTPS là bắt buộc:** camera quét QR CCCD chỉ hoạt động qua HTTPS; cookie phiên đăng nhập chỉ gửi qua HTTPS ngoài môi trường phát triển. Dùng chứng chỉ nội bộ của bệnh viện (CA nội bộ cài trên máy trạm và máy tính bảng) hoặc chứng chỉ công cộng.
- API chỉ nghe trên `localhost` (hoặc mạng nội bộ Docker) và nhận yêu cầu từ reverse proxy; không mở cổng API ra mạng bệnh viện.
- Dung lượng: khoảng 200–500 KB cho mỗi PDF đã ký; với 500–3.000 phiếu/ngày cần dự trù **200–550 GB/năm** cho thư mục PDF (xem kế hoạch, mục 2.7). CSDL nhỏ hơn nhiều (nội dung phiếu dạng JSON mã hóa, không chứa PDF).

## 2. Yêu cầu

| Thành phần | Yêu cầu |
|---|---|
| Hệ điều hành | Windows Server 2019 trở lên (hoặc Linux nếu dùng Docker) |
| SQL Server | 2017 trở lên (đã kiểm thử với 2022). Express dùng được cho quy mô vừa (giới hạn 10 GB/CSDL, không có SQL Agent: lên lịch sao lưu bằng Task Scheduler). Collation CSDL: `Vietnamese_CI_AS` hoặc `Latin1_General_100_CI_AI` (ứng dụng tự đặt collation không dấu cho cột tìm kiếm) |
| Hình thức A (IIS) | IIS 10 với **ASP.NET Core Hosting Bundle 10.0**, **URL Rewrite**, **Application Request Routing (ARR)**; .NET SDK 10 trên máy build |
| Hình thức B (Docker) | Docker Engine 24+ và Docker Compose v2 (Windows: Docker Desktop hoặc Docker Engine với container Linux) |
| Máy trạm | Chrome/Edge bản mới; máy tính bảng iPad (Safari) hoặc Android (Chrome), có bút cảm ứng càng tốt |
| Phần cứng tối thiểu | 4 vCPU, 8 GB RAM cho máy chủ ứng dụng; SQL Server theo khuyến nghị của Microsoft |

## 3. Bí mật và cấu hình

Tất cả bí mật đặt bằng **biến môi trường** (IIS: biến môi trường của Application Pool; Docker: file `--env-file` nằm ngoài repo, quyền đọc chỉ cho quản trị). Không ghi vào `appsettings.json`, không commit.

| Biến môi trường | Ý nghĩa | Cách tạo |
|---|---|---|
| `ConnectionStrings__Default` | Chuỗi kết nối SQL Server bằng tài khoản `consent_app` (quyền tối thiểu, mục 4) | `Server=SQL01;Database=ConsentManagement;User Id=consent_app;Password=…;Encrypt=True;TrustServerCertificate=False` (`True` nếu SQL Server dùng chứng chỉ tự ký) |
| `Jwt__SigningKey` | Khóa ký token đăng nhập, ≥ 32 ký tự ngẫu nhiên | `openssl rand -base64 48` hoặc PowerShell: `[Convert]::ToBase64String((1..48 \| % { Get-Random -Max 256 }))` |
| `DataProtection__FieldEncryptionKey` | Khóa AES-256 mã hóa CCCD, SĐT, BHYT, nội dung phiếu, ảnh chữ ký và PDF đã ký. **Mất khóa = mất dữ liệu** | `openssl rand -base64 32` |
| `DataProtection__BlindIndexKey` | Khóa HMAC cho chỉ mục mù (tra cứu CCCD). Phải khác khóa trên | `openssl rand -base64 32` |
| `Seed__Enabled`, `Seed__AdminPassword` | Tạo vai trò hệ thống và tài khoản `admin` lần đầu (phải đổi mật khẩu ở lần đăng nhập đầu). Sau khi đăng nhập được, **xóa `Seed__AdminPassword`** | — |
| `Seed__DemoData` | Phải là `false` (API từ chối khởi động nếu bật ở Production) | — |
| `Storage__DocumentsPath` | Thư mục lưu PDF đã ký. Ổ đĩa riêng hoặc thư mục mạng (tài khoản chạy ứng dụng cần quyền ghi) | ví dụ `D:\ConsentDocuments` |
| `AllowedHosts` | Tên máy chủ người dùng truy cập (chống giả mạo Host), ví dụ `phieudongthuan.benhvien.local` | — |
| `ReverseProxy__TrustAllProxies` | `true` khi API chỉ nhận yêu cầu từ reverse proxy trên cùng máy (để ghi đúng IP người dùng vào nhật ký) | — |
| `Jwt__AccessTokenMinutes`, `Jwt__SigningTokenMinutes`, `Auth__*`, `RateLimiting__*` | Tùy chọn; mặc định hợp lý (xem [kiến trúc, mục 9](kien-truc.md#9-cấu-hình-và-bí-mật)) | — |
| `His__Provider` | `None` (chưa kết nối HIS) | — |

**Lưu khóa:** in hoặc ghi 3 khóa (`Jwt__SigningKey`, hai khóa `DataProtection__*`) vào kho mật khẩu của phòng CNTT và một bản niêm phong, **tách khỏi bản sao lưu CSDL**. Không có khóa thì bản sao lưu CSDL và thư mục PDF không đọc được.

Tên đơn vị in trên PDF (cơ quan chủ quản, tên bệnh viện, địa danh) do quản trị nhập tại **Quản trị → Cấu hình hệ thống** sau khi triển khai; không cần đặt trong cấu hình.

## 4. Cơ sở dữ liệu

### 4.1. Tạo CSDL và tài khoản

Chạy [`deploy/sql/01-create-login.sql`](../deploy/sql/01-create-login.sql) bằng tài khoản quản trị SQL Server (SSMS hoặc `sqlcmd`), **sau khi đổi hai mật khẩu mẫu trong script**. Script tạo:

- CSDL `ConsentManagement` (RECOVERY FULL).
- `consent_app`: tài khoản ứng dụng, chỉ đọc/ghi dữ liệu (`db_datareader`, `db_datawriter`, `UPDATE` trên schema để dùng sequence số phiếu). Không tạo/sửa được bảng và không xóa được nhật ký audit (bảng `AuditLogs` còn có trigger chặn UPDATE/DELETE).
- `consent_migrator`: tài khoản chạy migration (`db_owner`), chỉ dùng lúc triển khai/nâng cấp; có thể `ALTER LOGIN … DISABLE` giữa các lần nâng cấp.

### 4.2. Chạy migration (tạo/nâng cấp lược đồ)

Trên máy build (có .NET SDK 10), tạo **gói migration** chạy được mà không cần SDK trên máy chủ:

```bash
cd backend
dotnet tool restore
dotnet tool run dotnet-ef migrations bundle --project src/Consent.Persistence --startup-project src/Consent.Persistence \
  --configuration Release --self-contained -r win-x64 --output ../artifacts/efbundle.exe
```

Trên máy chủ (hoặc máy quản trị có kết nối tới SQL Server), chạy bằng tài khoản `consent_migrator`:

```powershell
$env:ConnectionStrings__Default = "Server=SQL01;Database=ConsentManagement;User Id=consent_migrator;Password=…;Encrypt=True;TrustServerCertificate=True"
.\efbundle.exe --connection $env:ConnectionStrings__Default
```

Gói migration áp dụng lần lượt các migration còn thiếu (idempotent; chạy lại không gây hại). **Sao lưu CSDL trước mỗi lần nâng cấp.** Không bật `Database__ApplyMigrationsOnStartup` ở môi trường chính thức.

## 5. Hình thức A — IIS + SQL Server có sẵn

### 5.1. Build

Trên máy build:

```bash
# API
cd backend
dotnet publish src/Consent.Api/Consent.Api.csproj -c Release -o ../artifacts/api
# Giao diện web
cd ../frontend
npm ci && npm run build          # kết quả trong frontend/dist
```

Sao chép `artifacts/api` → `D:\Apps\ConsentApi`, `frontend/dist` → `D:\Apps\ConsentWeb` trên máy chủ. Thay `D:\Apps\ConsentApi\web.config` bằng [`deploy/iis/web.config`](../deploy/iis/web.config) và chép [`deploy/iis/web.frontend.config`](../deploy/iis/web.frontend.config) thành `D:\Apps\ConsentWeb\web.config`.

### 5.2. Site API (cổng nội bộ)

1. Tạo Application Pool `ConsentApi`: .NET CLR version **No Managed Code**, identity `ApplicationPoolIdentity`, *Start Mode* = AlwaysRunning, *Idle Time-out* = 0.
2. Tạo site `ConsentApi` trỏ `D:\Apps\ConsentApi`, binding **http, địa chỉ `127.0.0.1`, cổng 5080** (không binding địa chỉ ngoài).
3. Đặt biến môi trường cho Application Pool (IIS Manager → Application Pools → ConsentApi → *Advanced Settings* → *Environment Variables*, hoặc PowerShell):

   ```powershell
   Import-Module WebAdministration
   $pool = 'ConsentApi'
   $vars = @{
     'ASPNETCORE_ENVIRONMENT'            = 'Production'
     'ConnectionStrings__Default'        = 'Server=SQL01;Database=ConsentManagement;User Id=consent_app;Password=…;Encrypt=True;TrustServerCertificate=True'
     'Jwt__SigningKey'                   = '…'
     'DataProtection__FieldEncryptionKey'= '…'
     'DataProtection__BlindIndexKey'     = '…'
     'Storage__DocumentsPath'            = 'D:\ConsentDocuments'
     'AllowedHosts'                      = 'phieudongthuan.benhvien.local'
     'ReverseProxy__TrustAllProxies'     = 'true'
     'Seed__Enabled'                     = 'true'
     'Seed__AdminPassword'               = '…'   # xóa sau khi đăng nhập lần đầu
     'Seed__DemoData'                    = 'false'
   }
   foreach ($k in $vars.Keys) {
     Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter "system.applicationHost/applicationPools/add[@name='$pool']/environmentVariables" -name '.' -value @{name=$k; value=$vars[$k]}
   }
   ```

4. Cấp quyền *Modify* cho `IIS AppPool\ConsentApi` trên `D:\ConsentDocuments` (và quyền *Read* trên `D:\Apps\ConsentApi`).
5. Kiểm tra: `curl http://127.0.0.1:5080/health` → `{"status":"Healthy"}`; `curl http://127.0.0.1:5080/health/ready` → kết nối được SQL Server.

### 5.3. Site Web (HTTPS, công khai trong mạng bệnh viện)

1. Cài **URL Rewrite** và **ARR**; trong ARR (*Server Proxy Settings*) bật *Enable proxy*, tắt *Reverse rewrite host in response headers*.
2. Tạo site `ConsentWeb` trỏ `D:\Apps\ConsentWeb`, binding **https cổng 443**, hostname `phieudongthuan.benhvien.local`, chọn chứng chỉ; thêm binding http 80 với rule chuyển hướng sang https (hoặc không mở 80).
3. `web.config` của site (bản mẫu) chuyển tiếp `/api/*` và `/health*` tới `http://localhost:5080`, các đường dẫn khác trả `index.html`, và đặt security header giống `frontend/nginx.conf` (CSP có `frame-src blob:` để xem PDF trong ứng dụng). Nếu API chạy cổng khác, sửa URL trong rule *API proxy*.
4. Mở `https://phieudongthuan.benhvien.local`, đăng nhập `admin`, đổi mật khẩu, vào **Quản trị → Cấu hình hệ thống** nhập tên đơn vị.

### 5.4. Nâng cấp phiên bản

1. Sao lưu CSDL (mục 7). 2. Chạy gói migration mới (mục 4.2). 3. Dừng Application Pool `ConsentApi`, thay thư mục `D:\Apps\ConsentApi` (giữ `web.config`), khởi động lại. 4. Thay `D:\Apps\ConsentWeb` bằng `dist` mới (giữ `web.config`). 5. Kiểm tra `/health/ready` và đăng nhập thử.

## 6. Hình thức B — Docker + SQL Server có sẵn

1. Trên máy chủ Docker, sao chép repo (hoặc chỉ thư mục `backend`, `frontend`, `deploy`).
2. Tạo file `C:\ConsentSecrets\consent.env` (Windows) hoặc `/etc/consent/consent.env` (Linux), quyền đọc chỉ cho quản trị:

   ```env
   CONNECTION_STRING=Server=SQL01;Database=ConsentManagement;User Id=consent_app;Password=…;Encrypt=True;TrustServerCertificate=True
   JWT_SIGNING_KEY=…
   FIELD_ENCRYPTION_KEY=…
   BLIND_INDEX_KEY=…
   SEED_ADMIN_PASSWORD=…
   ALLOWED_HOSTS=phieudongthuan.benhvien.local
   DOCUMENTS_PATH=D:\ConsentDocuments     # hoặc để trống để dùng volume consent-documents
   WEB_PORT=8080
   APP_VERSION=1.0.0
   ```

3. Chạy migration (mục 4.2) rồi khởi động:

   ```bash
   docker compose -f deploy/docker-compose.prod.yml --env-file /etc/consent/consent.env up -d --build
   docker compose -f deploy/docker-compose.prod.yml ps
   curl http://127.0.0.1:8080/health/ready
   ```

   [`deploy/docker-compose.prod.yml`](../deploy/docker-compose.prod.yml) chạy `api` (không mở cổng) và `web` (nginx, chỉ mở `127.0.0.1:8080`), môi trường Production, không tự migrate, không dữ liệu demo.
4. Đặt reverse proxy HTTPS phía trước cổng 8080: IIS với ARR (rule chuyển tiếp toàn bộ `https://phieudongthuan.benhvien.local/*` → `http://127.0.0.1:8080/*`, thêm header `X-Forwarded-Proto: https`) hoặc nginx với chứng chỉ của bệnh viện. Thêm header `Strict-Transport-Security` ở lớp này.
5. Nâng cấp: `git pull` (hoặc chép mã nguồn mới) → sao lưu CSDL → chạy gói migration → `docker compose -f deploy/docker-compose.prod.yml --env-file … up -d --build`.

Bản `docker-compose.yml` ở thư mục gốc **chỉ dành cho phát triển** (SQL Server trong container, tài khoản `sa`, tự migrate, dữ liệu demo).

## 7. Sao lưu và khôi phục

Ba thứ phải sao lưu, **cùng thời điểm**:

| Thứ cần sao lưu | Cách | Tần suất |
|---|---|---|
| CSDL `ConsentManagement` | [`deploy/sql/02-backup.sql`](../deploy/sql/02-backup.sql): sao lưu đầy đủ có `CHECKSUM` + `RESTORE VERIFYONLY`; sao lưu nhật ký giao dịch | Đầy đủ hằng ngày; nhật ký 15–30 phút |
| Thư mục PDF đã ký (`Storage__DocumentsPath`) | Sao chép tăng dần (robocopy `/MIR` sang ổ sao lưu, hoặc phần mềm sao lưu của bệnh viện). File đã mã hóa nên bản sao lưu không lộ dữ liệu, nhưng vẫn cần bảo vệ | Hằng ngày (file chỉ thêm, không sửa) |
| Ba khóa bí mật và chuỗi kết nối | Kho mật khẩu của phòng CNTT + bản niêm phong; **không** để cùng chỗ với bản sao lưu CSDL | Mỗi khi đổi |

Ví dụ lệnh robocopy (Task Scheduler, hằng đêm): `robocopy D:\ConsentDocuments \\BACKUP01\Consent\Documents /MIR /R:3 /W:10 /LOG+:D:\Backup\Consent\robocopy.log`.

**Khôi phục** ([`deploy/sql/03-restore.sql`](../deploy/sql/03-restore.sql)): khôi phục CSDL (đầy đủ + nhật ký), khôi phục thư mục PDF từ cùng thời điểm, đặt lại **đúng các khóa cũ**, khởi động ứng dụng, kiểm tra `/health/ready`, mở một phiếu đã ký và xem PDF (ứng dụng tự đối chiếu SHA-256; file sai sẽ báo "không khớp mã băm"). **Diễn tập khôi phục ít nhất mỗi quý** trên máy chủ thử nghiệm.

Lưu ý khi khôi phục về thời điểm cũ hơn thư mục PDF: phiếu hoàn tất sau thời điểm đó sẽ mất trong CSDL nhưng file vẫn còn (vô hại); ngược lại, CSDL mới hơn thư mục PDF thì các phiếu thiếu file sẽ báo lỗi khi mở PDF — vì vậy sao lưu hai thứ cùng thời điểm.

## 8. Khóa và mật khẩu: xoay vòng

| Khóa | Cách đổi | Ảnh hưởng |
|---|---|---|
| Mật khẩu `consent_app` / `consent_migrator` | `ALTER LOGIN … WITH PASSWORD`, cập nhật chuỗi kết nối, khởi động lại API | Không ảnh hưởng dữ liệu |
| `Jwt__SigningKey` | Đổi biến môi trường, khởi động lại API | Mọi người dùng phải đăng nhập lại; phiên ký đang mở trên máy tính bảng kết thúc |
| `Seed__AdminPassword` | Xóa biến sau lần đăng nhập đầu; đổi mật khẩu `admin` trong ứng dụng | — |
| `DataProtection__BlindIndexKey` | **Chưa có công cụ xoay vòng.** Đổi khóa làm chỉ mục mù cũ không còn khớp: không tra cứu/chặn trùng CCCD được cho hồ sơ cũ | Chỉ đổi khi nghi lộ khóa, kèm kế hoạch tính lại chỉ mục (công cụ sẽ bổ sung khi bệnh viện yêu cầu) |
| `DataProtection__FieldEncryptionKey` | **Chưa có công cụ xoay vòng.** Đổi khóa làm toàn bộ dữ liệu đã mã hóa (CCCD, SĐT, BHYT, nội dung phiếu, ảnh chữ ký, PDF đã ký) không đọc được | Chỉ đổi khi nghi lộ khóa; cần công cụ giải mã bằng khóa cũ và mã hóa lại bằng khóa mới trên CSDL và thư mục PDF trong thời gian dừng hệ thống. Khi cần, phòng CNTT liên hệ nhóm phát triển; đây là **hạn chế đã biết** của phiên bản này |

Khuyến nghị: bảo vệ hai khóa `DataProtection__*` như bảo vệ CSDL (chỉ quản trị đọc được biến môi trường, máy chủ trong phòng máy, nhật ký truy cập máy chủ), thay vì xoay vòng định kỳ.

## 9. Vận hành

- **Giám sát:** `GET /health` (tiến trình) và `GET /health/ready` (SQL Server) cho công cụ giám sát (Zabbix, PRTG, Uptime Kuma…); theo dõi dung lượng ổ `Storage__DocumentsPath` và ổ CSDL.
- **Log ứng dụng:** ghi ra stdout/Event Log (IIS: bật `stdoutLogEnabled` tạm thời khi chẩn đoán; Docker: `docker compose logs api`). Log không chứa dữ liệu cá nhân; mỗi dòng có mã truy vết (`CorrelationId`) trùng với mã hiển thị cho người dùng khi lỗi và với nhật ký audit.
- **Nhật ký audit** nằm trong CSDL (bảng `AuditLogs`, chỉ ghi thêm): xem tại **Nhật ký hệ thống**; lưu theo thời hạn hồ sơ bệnh án, không xóa.
- **Tài khoản:** quản trị tạo tài khoản cho nhân viên tại **Quản trị → Người dùng** (mật khẩu tạm, bắt buộc đổi), gán vai trò và khoa. Khóa tài khoản khi nhân viên nghỉ việc (không xóa, để giữ nhật ký).
- **Máy tính bảng ký phiếu:** cài chứng chỉ CA nội bộ (nếu dùng), thêm trang web vào màn hình chính, bật **Guided Access** (iPad) hoặc **Ghim màn hình** (Android) khi đưa máy cho người bệnh; tắt tự khóa màn hình trong lúc ký.
- **Dữ liệu demo:** không bao giờ bật `Seed__DemoData` ở môi trường chính thức (API từ chối khởi động). Dữ liệu người bệnh thật không được nhập vào môi trường thử nghiệm.

## 10. Danh sách kiểm tra sau triển khai

1. `https://…/health/ready` trả `Healthy`; `http://` chuyển sang `https://` (hoặc không mở).
2. Đăng nhập `admin`, đổi mật khẩu; đã xóa `Seed__AdminPassword`.
3. **Quản trị → Cấu hình hệ thống**: nhập cơ quan chủ quản, tên bệnh viện, địa danh; mở một phiếu nháp → PDF in đúng tên.
4. **Mẫu phiếu**: Phòng KHTH rà soát 4 mẫu phiếu của bệnh viện (được tạo ở trạng thái nháp khi không có dữ liệu demo, xem [mẫu phiếu](mau-phieu.md)) và **phát hành**.
5. Tạo khoa/phòng và tài khoản thật; đăng nhập thử từng vai trò (menu chỉ hiện đúng chức năng).
6. Trên máy tính bảng: quét QR CCCD bằng camera (cần HTTPS), lập một phiếu thử với người bệnh giả lập, ký, hoàn tất, xem PDF, rồi **thu hồi** phiếu thử (ghi lý do "kiểm tra triển khai").
7. Chạy sao lưu đầu tiên (CSDL + thư mục PDF) và **khôi phục thử** sang máy chủ thử nghiệm.
8. Lưu 3 khóa vào kho mật khẩu và bản niêm phong; ghi lại người giữ.
9. Thiết lập giám sát `/health/ready` và cảnh báo dung lượng đĩa.
10. Lưu lại phiên bản đã triển khai (mã commit/`APP_VERSION`) vào biên bản bàn giao.

## 11. Sự cố thường gặp

| Hiện tượng | Nguyên nhân | Xử lý |
|---|---|---|
| API không khởi động, log ghi "Thiếu khóa ký JWT" / "Thiếu khóa bảo vệ dữ liệu" | Thiếu hoặc sai định dạng biến môi trường | Kiểm tra 3 khóa (Base64 32 byte cho `DataProtection__*`), khởi động lại |
| "Dữ liệu minh họa (Seed:DemoData) không được bật ở môi trường Production" | `Seed__DemoData=true` | Đặt `false` |
| `/health/ready` trả 503 | Không kết nối được SQL Server | Kiểm tra chuỗi kết nối, tường lửa cổng 1433, chứng chỉ SQL (`TrustServerCertificate`) |
| Không đọc được CCCD/PDF, lỗi "không khớp mã băm" sau khôi phục | Đặt sai khóa `FieldEncryptionKey` hoặc thư mục PDF khác thời điểm với CSDL | Đặt lại đúng khóa cũ; khôi phục thư mục PDF cùng thời điểm |
| Camera không bật khi quét CCCD | Trang không chạy qua HTTPS hoặc trình duyệt chưa cấp quyền | Dùng HTTPS với chứng chỉ được tin cậy trên thiết bị; cấp quyền camera |
| PDF không hiển thị trong ứng dụng (trang trắng) | Reverse proxy ghi đè CSP, thiếu `frame-src blob:` | Dùng CSP như `deploy/iis/web.frontend.config` / `frontend/nginx.conf` |
| 429 "Bạn thao tác quá nhiều lần" | Vượt giới hạn đăng nhập (10 lần/phút/IP) hoặc xuất dữ liệu (10 lần/phút/người) | Chờ 1 phút; nếu nhiều máy chung IP qua NAT, tăng `RateLimiting__AuthPermitLimit` |
| Nhật ký ghi IP là `127.0.0.1` | Reverse proxy không gửi `X-Forwarded-For` hoặc `ReverseProxy__TrustAllProxies=false` | Cấu hình proxy gửi header và bật biến trên (chỉ khi API không mở cổng ra ngoài) |
