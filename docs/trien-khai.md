# Hướng dẫn triển khai — Quản lý Phiếu đồng thuận

> Dành cho phòng CNTT bệnh viện. Áp dụng cho máy chủ **Windows Server** tại bệnh viện (quyết định 5).
> **Bệnh viện triển khai theo hình thức A. IIS + SQL Server 2025 Standard** trên Windows Server (quyết định 12, điều chỉnh 13/11/2026: hiện chưa bố trí hạ tầng Docker) — làm theo **mục 5**. Hình thức B (Docker) giữ lại để chuyển sang sau này. Người bệnh **ký trên điện thoại thông minh** (quyết định 13) — chuẩn bị điện thoại theo **mục 9a**. Phần sao lưu, khôi phục, khóa và vận hành dùng chung.
> Các file mẫu nằm trong thư mục [`deploy/`](../deploy). Mọi giá trị trong tài liệu này là ví dụ; **không có bí mật thật nào trong repo**.

---

## 1. Kiến trúc triển khai

```
Trình duyệt (máy tính bàn, điện thoại ký phiếu) ──HTTPS──► IIS: site ConsentWeb (giao diện + ARR chuyển tiếp)
                                                                   ├── /            → file tĩnh của giao diện web (frontend/dist)
                                                                   └── /api, /health → API ASP.NET Core (cổng nội bộ, không mở ra ngoài)
                                                                                          ├── SQL Server (CSDL ConsentManagement)
                                                                                          └── Thư mục PDF đã ký (Storage:DocumentsPath)
```

- **HTTPS là bắt buộc:** camera quét QR CCCD chỉ hoạt động qua HTTPS; cookie phiên đăng nhập chỉ gửi qua HTTPS ngoài môi trường phát triển. **Bệnh viện dùng chứng chỉ công cộng** (quyết định 15), xem mục 2a.
- API chỉ nghe trên `localhost` (hoặc mạng nội bộ Docker) và nhận yêu cầu từ reverse proxy; không mở cổng API ra mạng bệnh viện.
- Dung lượng: khoảng 200–500 KB cho mỗi PDF đã ký; với 500–3.000 phiếu/ngày cần dự trù **200–550 GB/năm** cho thư mục PDF (xem kế hoạch, mục 2.7). CSDL nhỏ hơn nhiều (nội dung phiếu dạng JSON mã hóa, không chứa PDF).

## 2. Yêu cầu

| Thành phần | Yêu cầu |
|---|---|
| Hệ điều hành | Windows Server 2019 trở lên (hoặc Linux nếu dùng Docker) |
| SQL Server | **SQL Server 2025 Standard** (quyết định 16, mục 2b). Đã kiểm thử toàn bộ với 2025 (mặc định của kiểm thử tích hợp) và 2022. Collation CSDL: `Vietnamese_CI_AS` hoặc `Latin1_General_100_CI_AI` (ứng dụng tự đặt collation không dấu cho cột tìm kiếm) |
| Hình thức A (IIS) | IIS 10 với **ASP.NET Core Hosting Bundle 10.0**, **URL Rewrite**, **Application Request Routing (ARR)**; .NET SDK 10 trên máy build |
| Hình thức B (Docker) | Docker Engine 24+ và Docker Compose v2 (Windows: Docker Desktop hoặc Docker Engine với container Linux) |
| Máy trạm | Chrome/Edge bản mới |
| Điện thoại ký phiếu | iPhone iOS 16+ (Safari) hoặc Android 10+ (Chrome bản mới), màn hình từ 6 inch; kết nối Wi-Fi nội bộ tới máy chủ |
| Phần cứng tối thiểu | 4 vCPU, 8 GB RAM cho máy chủ ứng dụng; SQL Server theo khuyến nghị của Microsoft |

## 2a. Chứng chỉ HTTPS công cộng (quyết định 15)

1. **Tên miền: `phieudongthuan.bth.org.vn`** (quyết định 15). Trên **DNS nội bộ**, trỏ tên này về IP nội bộ của máy chủ; điện thoại và máy trạm vào bằng Wi-Fi/mạng nội bộ. Không cần mở hệ thống ra Internet. Nếu có DNS công cộng cho `bth.org.vn`, không cần bản ghi A công cộng cho tên này (chỉ cần bản ghi TXT khi xác thực DNS-01).
2. **Cấp chứng chỉ:** từ một CA công cộng (thương mại hoặc Let's Encrypt). Vì máy chủ không mở ra Internet, xác thực tên miền bằng **bản ghi DNS (DNS-01)** hoặc theo quy trình của nhà cung cấp chứng chỉ thương mại.
3. **Gia hạn:** thời hạn tối đa của chứng chỉ công cộng đang rút ngắn theo quy định chung của các CA (200 ngày từ 15/3/2026, 100 ngày từ 15/3/2027, 47 ngày từ 15/3/2029). Nên **tự động gia hạn** (ví dụ win-acme trên Windows, hoặc certbot với plugin DNS) và đặt cảnh báo trước khi hết hạn 30 ngày.
4. **Gắn chứng chỉ** vào binding HTTPS của site `ConsentWeb` trên IIS (mục 5.3), kèm chuỗi chứng chỉ trung gian đầy đủ. Trên Windows có thể dùng **win-acme** (Let's Encrypt, xác thực DNS) để cấp, gắn binding IIS và tự gia hạn bằng Task Scheduler; hoặc nhập chứng chỉ thương mại vào kho *Local Computer → Personal* rồi chọn trong binding. Điện thoại và máy trạm **không cần cài thêm chứng chỉ**.
5. Không dùng chứng chỉ có ký tự đại diện (`*.`) chung với các hệ thống khác nếu khóa riêng phải sao chép sang nhiều máy; ưu tiên chứng chỉ riêng cho tên miền này.

## 2b. Chọn phiên bản SQL Server

Đo trên CSDL thử nghiệm: mỗi phiếu hoàn tất chiếm khoảng **30 KB** trong CSDL (nội dung phiếu mã hóa, **ảnh chữ ký tay mã hóa khoảng 15 KB**, nhật ký audit, chỉ mục). File PDF đã ký nằm ở thư mục riêng, không tính vào CSDL.

| Số phiếu/ngày | CSDL tăng mỗi năm (ước tính) | Sau 10 năm |
|---|---|---|
| 500 | khoảng 5–6 GB | khoảng 55 GB |
| 1.500 | khoảng 16 GB | khoảng 160 GB |
| 3.000 | khoảng 33 GB | khoảng 330 GB |

- **Bệnh viện chọn SQL Server 2025 Standard** (quyết định 16). Kiểm thử tích hợp và môi trường phát triển (`docker-compose.yml`) dùng SQL Server 2025 **bản Standard Developer** (`MSSQL_PID=StandardDeveloper`), cùng tính năng với bản Standard; ứng dụng cũng chạy được với 2022. Cài bản cập nhật tích lũy (CU) mới nhất; mức tương thích CSDL để mặc định của 2025.
- **Không dùng Express cho môi trường chính thức:** Express giới hạn dung lượng mỗi CSDL (10 GB ở bản 2022), trong khi phiếu phải lưu theo thời hạn hồ sơ bệnh án và không xóa. Express chỉ hợp cho môi trường thử nghiệm hoặc thí điểm ngắn ở một vài khoa.
- **Enterprise không cần thiết:** giới hạn lõi và bộ nhớ của Standard cao hơn nhiều so với nhu cầu của ứng dụng.
- **Giấy phép:** Standard theo lõi (tối thiểu 4 lõi) hoặc theo máy chủ + CAL. Phòng CNTT đối chiếu với hợp đồng bản quyền hiện có của bệnh viện. Ứng dụng hỗ trợ từ 2017, nhưng chỉ kiểm thử thường xuyên với 2025 và 2022.
- **Cấu hình gợi ý:** 4 lõi, 16 GB RAM cho SQL Server; ổ dữ liệu và ổ nhật ký giao dịch tách riêng; chế độ khôi phục **Full** với sao lưu nhật ký định kỳ (mục 7); SQL Agent lên lịch sao lưu (Standard có sẵn).
- SQL Server có thể đặt trên máy chủ CSDL sẵn có của bệnh viện hoặc cùng máy chủ IIS; ứng dụng chỉ cần kết nối tới cổng 1433.

## 3. Bí mật và cấu hình

Tất cả bí mật đặt bằng **biến môi trường** (IIS: biến môi trường của Application Pool; Docker: file `--env-file` nằm ngoài repo, quyền đọc chỉ cho quản trị). Không ghi vào `appsettings.json`, không commit.

| Biến môi trường | Ý nghĩa | Cách tạo |
|---|---|---|
| `ConnectionStrings__Default` | Chuỗi kết nối SQL Server bằng tài khoản `consent_app` (quyền tối thiểu, mục 4) | `Server=tcp:SQL01,1433;Database=ConsentManagement;User Id=consent_app;Password=…;Encrypt=True;TrustServerCertificate=False` (`True` nếu SQL Server dùng chứng chỉ tự ký) |
| `Jwt__SigningKey` | Khóa ký token đăng nhập, ≥ 32 ký tự ngẫu nhiên | `openssl rand -base64 48` hoặc PowerShell (bộ sinh số ngẫu nhiên an toàn, **không dùng `Get-Random`**): `$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)` |
| `DataProtection__FieldEncryptionKey` | Khóa AES-256 mã hóa CCCD, SĐT, BHYT, nội dung phiếu, ảnh chữ ký và PDF đã ký. **Mất khóa = mất dữ liệu** | `openssl rand -base64 32`, hoặc lệnh PowerShell ở dòng trên với `byte[] 32` |
| `DataProtection__BlindIndexKey` | Khóa HMAC cho chỉ mục mù (tra cứu CCCD). Phải khác khóa trên | `openssl rand -base64 32`, hoặc lệnh PowerShell với `byte[] 32` |
| `Seed__Enabled`, `Seed__AdminPassword` | Tạo vai trò hệ thống và tài khoản `admin` lần đầu (phải đổi mật khẩu ở lần đăng nhập đầu). Sau khi đăng nhập được, **xóa `Seed__AdminPassword`** | — |
| `Seed__DemoData` | Phải là `false` (API từ chối khởi động nếu bật ở Production) | — |
| `Storage__DocumentsPath` | Thư mục lưu PDF đã ký. Ổ đĩa riêng hoặc thư mục mạng (tài khoản chạy ứng dụng cần quyền ghi) | ví dụ `D:\ConsentDocuments` |
| `AllowedHosts` | Tên máy chủ được chấp nhận (chống giả mạo Host). IIS: `phieudongthuan.bth.org.vn;localhost;127.0.0.1` (thêm `localhost` để kiểm tra sức khỏe trực tiếp trên máy chủ; API chỉ nghe trên `127.0.0.1` nên không mở rộng truy cập) | — |
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
$env:ConnectionStrings__Default = "Server=tcp:SQL01,1433;Database=ConsentManagement;User Id=consent_migrator;Password=…;Encrypt=True;TrustServerCertificate=True"
.\efbundle.exe --connection $env:ConnectionStrings__Default
```

Gói migration áp dụng lần lượt các migration còn thiếu (idempotent; chạy lại không gây hại). **Sao lưu CSDL trước mỗi lần nâng cấp.** Không bật `Database__ApplyMigrationsOnStartup` ở môi trường chính thức.

## 5. Hình thức A — IIS + SQL Server có sẵn

### 5.1. Lấy gói triển khai

**Cách 1 (khuyến nghị): tải gói đã đóng gói sẵn.** Mỗi lần có mã nguồn mới, GitHub Actions chạy quy trình *Gói triển khai IIS* (`.github/workflows/package-iis.yml`) và tạo file `goi-trien-khai-iis-<mã commit>.zip` (giữ 30 ngày). Vào repo trên GitHub → **Actions** → *Gói triển khai IIS* → lần chạy mới nhất có dấu ✓ → mục **Artifacts** → tải về (cần tài khoản GitHub có quyền đọc repo). Giải nén trên máy chủ:

| Thư mục/file trong gói | Chép tới | Ghi chú |
|---|---|---|
| `ConsentApi\` | `D:\Apps\ConsentApi` | Đã gồm `web.config` mẫu (`deploy/iis/web.config`) |
| `ConsentWeb\` | `D:\Apps\ConsentWeb` | Đã gồm `web.config` mẫu (`deploy/iis/web.frontend.config`) |
| `efbundle.exe` | Thư mục tạm trên máy chủ | Cập nhật lược đồ CSDL (mục 4.2) |
| `sql\` | Máy có SSMS | `01-create-login.sql`, `02-backup.sql`, `03-restore.sql` |
| `PHIEN-BAN.txt` | Lưu vào biên bản bàn giao | Mã commit và ngày đóng gói |

**Cách 2: tự build** trên một máy có .NET SDK 10 và Node.js 24.15 trở lên (kết quả giống cách 1):

```bash
# API
cd backend
dotnet publish src/Consent.Api/Consent.Api.csproj -c Release -r win-x64 --self-contained false -o ../artifacts/api
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
     'ConnectionStrings__Default'        = 'Server=tcp:SQL01,1433;Database=ConsentManagement;User Id=consent_app;Password=…;Encrypt=True;TrustServerCertificate=True'
     'Jwt__SigningKey'                   = '…'
     'DataProtection__FieldEncryptionKey'= '…'
     'DataProtection__BlindIndexKey'     = '…'
     'Storage__DocumentsPath'            = 'D:\ConsentDocuments'
     'AllowedHosts'                      = 'phieudongthuan.bth.org.vn;localhost;127.0.0.1'
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

1. Cài **URL Rewrite** và **ARR**, rồi cấu hình ARR ở cấp máy chủ (PowerShell quản trị):

   ```powershell
   Import-Module WebAdministration
   # Bật proxy; giữ nguyên Host gốc (phieudongthuan.bth.org.vn) khi chuyển tới API; không sửa Host trong header phản hồi.
   Set-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter 'system.webServer/proxy' -name 'enabled' -value 'True'
   Set-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter 'system.webServer/proxy' -name 'preserveHostHeader' -value 'True'
   Set-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter 'system.webServer/proxy' -name 'reverseRewriteHostInResponseHeaders' -value 'False'
   # Cho phép rule "API proxy" đặt header X-Forwarded-Proto (thiếu bước này IIS trả lỗi 500).
   Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter 'system.webServer/rewrite/allowedServerVariables' -name '.' -value @{name='HTTP_X_FORWARDED_PROTO'}
   ```

   ARR tự thêm header `X-Forwarded-For` (IP thật của người dùng) để API ghi đúng vào nhật ký.
2. Tạo site `ConsentWeb` trỏ `D:\Apps\ConsentWeb`, binding **https cổng 443**, hostname `phieudongthuan.bth.org.vn`, chọn chứng chỉ; thêm binding http 80 với rule chuyển hướng sang https (hoặc không mở 80).
3. `web.config` của site (bản mẫu) chuyển tiếp `/api/*` và `/health*` tới `http://localhost:5080`, các đường dẫn khác trả `index.html`, và đặt security header giống `frontend/nginx.conf` (CSP có `frame-src blob:` để xem PDF trong ứng dụng). Nếu API chạy cổng khác, sửa URL trong rule *API proxy*.
4. Kiểm tra qua IIS: `https://phieudongthuan.bth.org.vn/health/ready` trả `Healthy`. Lỗi 400 thường do `AllowedHosts` hoặc `preserveHostHeader`; lỗi 500 thường do thiếu `allowedServerVariables` (bước 1).
5. Mở `https://phieudongthuan.bth.org.vn`, đăng nhập `admin`, đổi mật khẩu, vào **Quản trị → Cấu hình hệ thống** nhập tên đơn vị.

### 5.4. Nâng cấp phiên bản

1. Sao lưu CSDL (mục 7). 2. Chạy gói migration mới (mục 4.2). 3. Dừng Application Pool `ConsentApi`, thay thư mục `D:\Apps\ConsentApi` (giữ `web.config`), khởi động lại. 4. Thay `D:\Apps\ConsentWeb` bằng `dist` mới (giữ `web.config`). 5. Kiểm tra `/health/ready` và đăng nhập thử.

## 6. Hình thức B — Docker + SQL Server có sẵn

1. Trên máy chủ Docker, sao chép repo (hoặc chỉ thư mục `backend`, `frontend`, `deploy`).
2. Tạo file `C:\ConsentSecrets\consent.env` (Windows) hoặc `/etc/consent/consent.env` (Linux), quyền đọc chỉ cho quản trị:

   ```env
   CONNECTION_STRING=Server=tcp:SQL01,1433;Database=ConsentManagement;User Id=consent_app;Password=…;Encrypt=True;TrustServerCertificate=True
   JWT_SIGNING_KEY=…
   FIELD_ENCRYPTION_KEY=…
   BLIND_INDEX_KEY=…
   SEED_ADMIN_PASSWORD=…
   ALLOWED_HOSTS=phieudongthuan.bth.org.vn
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
4. Đặt reverse proxy HTTPS phía trước cổng 8080: IIS với ARR (rule chuyển tiếp toàn bộ `https://phieudongthuan.bth.org.vn/*` → `http://127.0.0.1:8080/*`, thêm header `X-Forwarded-Proto: https`) hoặc nginx với chứng chỉ của bệnh viện. Thêm header `Strict-Transport-Security` ở lớp này.
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
- **Điện thoại ký phiếu:** xem mục 9a.
- **Dữ liệu demo:** không bao giờ bật `Seed__DemoData` ở môi trường chính thức (API từ chối khởi động). Dữ liệu người bệnh thật không được nhập vào môi trường thử nghiệm.

## 9a. Điện thoại ký phiếu (quyết định 13)

Nhân viên lập phiếu và mở chế độ ký ngay trên điện thoại, rồi đưa máy cho người bệnh (hoặc người đại diện) đọc và ký bằng ngón tay. Ứng dụng là trang web, không cần cài ứng dụng riêng.

1. **Mạng:** điện thoại vào Wi-Fi nội bộ truy cập được địa chỉ HTTPS của hệ thống. Không mở hệ thống ra Internet.
2. **Chứng chỉ:** bệnh viện dùng chứng chỉ công cộng (mục 2a), nên điện thoại **không cần cài thêm chứng chỉ**. Chỉ cần ngày giờ trên điện thoại đúng (đặt tự động); sai ngày giờ thì trình duyệt báo lỗi chứng chỉ.
3. **Lối tắt:** mở trang trong Safari/Chrome → *Thêm vào màn hình chính*.
4. **Khóa ứng dụng khi đưa máy cho người bệnh:**
   - iPhone: bật *Cài đặt → Trợ năng → Truy cập được hướng dẫn* (Guided Access), khi ký thì nhấn nút sườn 3 lần.
   - Android: bật *Ghim ứng dụng* trong Bảo mật.
   Thiết lập này giúp người bệnh không thoát khỏi màn hình ký. Màn hình ký cũng không có menu, và chỉ nhận chữ ký cho đúng phiếu đó; muốn thoát phải đăng nhập lại.
5. **Màn hình:** đặt thời gian tự khóa ít nhất 2 phút; nên tắt tự xoay nếu người bệnh hay xoay nhầm. Cầm dọc vẫn ký được (khung ký cao hơn); xoay ngang thì khung ký rộng hơn.
6. **Tài khoản:** mỗi nhân viên đăng nhập tài khoản của mình. Điện thoại dùng chung thì đăng xuất khi đổi ca; phiên đăng nhập tự hết hạn.
7. **Vệ sinh và an toàn:** lau màn hình giữa các người bệnh. Đặt mã khóa máy. Bật tính năng tìm/xóa máy từ xa (Find My, Find My Device) để xử lý khi mất máy. Ứng dụng không lưu dữ liệu người bệnh trên điện thoại ngoài bộ nhớ tạm của trang đang mở.

Đã kiểm thử tự động trên iPhone 13 và Pixel 7 giả lập (cầm dọc và ngang): lập phiếu, ký, xác nhận, không tràn ngang. Cần thử trên vài điện thoại thật của bệnh viện trước khi dùng chính thức.

## 10. Danh sách kiểm tra sau triển khai

1. `https://…/health/ready` trả `Healthy`; `http://` chuyển sang `https://` (hoặc không mở).
2. Đăng nhập `admin`, đổi mật khẩu; đã xóa `Seed__AdminPassword`.
3. **Quản trị → Cấu hình hệ thống**: nhập cơ quan chủ quản, tên bệnh viện, địa danh; mở một phiếu nháp → PDF in đúng tên.
4. **Mẫu phiếu**: Phòng KHTH rà soát 4 mẫu phiếu của bệnh viện (được tạo ở trạng thái nháp khi không có dữ liệu demo, xem [mẫu phiếu](mau-phieu.md)) và **phát hành**.
5. Tạo khoa/phòng và tài khoản thật; đăng nhập thử từng vai trò (menu chỉ hiện đúng chức năng).
6. Trên điện thoại (mục 9a): quét QR CCCD bằng camera (cần HTTPS), lập một phiếu thử với người bệnh giả lập, ký, hoàn tất, xem PDF, rồi **thu hồi** phiếu thử (ghi lý do "kiểm tra triển khai").
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
| `Named Pipes Provider, error: 40` / `The network path was not found` (khi chạy `efbundle.exe` hoặc API) | Không tới được SQL Server: sai tên máy, bản cài theo tên riêng, TCP/IP tắt, tường lửa chặn 1433, dịch vụ SQL chưa chạy | Dùng `Server=tcp:<máy>,1433` (hoặc `<máy>\<TÊN>` + SQL Server Browser); `Get-Service *SQL*`; bật TCP/IP, cổng 1433 trong SQL Server Configuration Manager; `Test-NetConnection <máy-SQL> -Port 1433` |
| `Process should have elevated status to access IIS configuration data`, rồi `Cannot find drive 'IIS'` và `No mapping between account names and security IDs` | PowerShell không chạy bằng quyền quản trị nên không tạo được Application Pool; `icacls` lỗi theo vì tài khoản `IIS AppPool\…` chưa tồn tại | Mở PowerShell bằng *Run as administrator*, chạy lại nguyên khối lệnh |
| Không đọc được CCCD/PDF, lỗi "không khớp mã băm" sau khôi phục | Đặt sai khóa `FieldEncryptionKey` hoặc thư mục PDF khác thời điểm với CSDL | Đặt lại đúng khóa cũ; khôi phục thư mục PDF cùng thời điểm |
| Camera không bật khi quét CCCD | Trang không chạy qua HTTPS hoặc trình duyệt chưa cấp quyền | Dùng HTTPS với chứng chỉ được tin cậy trên thiết bị; cấp quyền camera |
| PDF không hiển thị trong ứng dụng (trang trắng) | Reverse proxy ghi đè CSP, thiếu `frame-src blob:` | Dùng CSP như `deploy/iis/web.frontend.config` / `frontend/nginx.conf` |
| 429 "Bạn thao tác quá nhiều lần" | Vượt giới hạn đăng nhập (10 lần/phút/IP) hoặc xuất dữ liệu (10 lần/phút/người) | Chờ 1 phút; nếu nhiều máy chung IP qua NAT, tăng `RateLimiting__AuthPermitLimit` |
| Nhật ký ghi IP là `127.0.0.1` | Reverse proxy không gửi `X-Forwarded-For` hoặc `ReverseProxy__TrustAllProxies=false` | Cấu hình proxy gửi header và bật biến trên (chỉ khi API không mở cổng ra ngoài) |
