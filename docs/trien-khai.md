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
| `PatientSource__Provider`, `PatientSource__BaseUrl`, `PatientSource__ApiKey` | Tùy chọn: lấy thông tin người bệnh qua API thông tin người bệnh (mục 5.5). Mặc định `None` (tắt) | Khóa API giống khóa đặt ở `PatientInfo__ApiKeys__0` |

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
| `PatientInfoApi\` | `C:\Apps\PatientInfoApi` | Tùy chọn: API thông tin người bệnh (mục 5.5); đã gồm `web.config` mẫu (`deploy/iis/web.patientinfo.config`) |
| `efbundle.exe` | Thư mục tạm trên máy chủ | Cập nhật lược đồ CSDL (mục 4.2) |
| `sql\` | Máy có SSMS | `01-create-login.sql`, `02-backup.sql`, `03-restore.sql`, `04-patientinfo-reader.sql` |
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
3. `web.config` của site (bản mẫu) chuyển tiếp `/api/*` và `/health*` tới `http://127.0.0.1:5080` (dùng IP: `localhost` có thể thành IPv6 `::1` mà site API chỉ nhận `127.0.0.1`, IIS trả 400 *Invalid Hostname*), các đường dẫn khác trả `index.html`, và đặt security header giống `frontend/nginx.conf` (CSP có `frame-src blob:` để xem PDF trong ứng dụng). Nếu API chạy cổng khác, sửa URL trong rule *API proxy*.
4. Kiểm tra qua IIS: `https://phieudongthuan.bth.org.vn/health/ready` trả `Healthy`. Lỗi 400 thường do `AllowedHosts` hoặc `preserveHostHeader`; lỗi 500 thường do thiếu `allowedServerVariables` (bước 1).
5. Mở `https://phieudongthuan.bth.org.vn`, đăng nhập `admin`, đổi mật khẩu, vào **Quản trị → Cấu hình hệ thống** nhập tên đơn vị.

### 5.4. Nâng cấp phiên bản

1. Sao lưu CSDL (mục 7). 2. Chạy gói migration mới (mục 4.2). 3. Dừng Application Pool `ConsentApi`, thay thư mục `D:\Apps\ConsentApi` (giữ `web.config`), khởi động lại. 4. Thay `D:\Apps\ConsentWeb` bằng `dist` mới (giữ `web.config`). 5. Nếu có cài API thông tin người bệnh (mục 5.5): dừng Application Pool `PatientInfoApi`, thay thư mục `C:\Apps\PatientInfoApi` bằng thư mục `PatientInfoApi\` của gói mới (giữ `web.config`), khởi động lại; biến môi trường nằm ở Application Pool nên được giữ nguyên. 6. Kiểm tra `/health/ready` và đăng nhập thử.

**Khi bản mới đổi định nghĩa một mẫu khởi tạo sẵn** (ví dụ TTNB in theo bản giấy QT-KNGT/BM03, quyết định 33): máy chủ đang chạy vẫn giữ phiên bản mẫu cũ, nên sau khi cài gói, người quản lý mẫu vào **Mẫu phiếu → mẫu đó → Tạo phiên bản mới**, mở thẻ **Soạn định nghĩa (JSON)**, dán đè bằng nội dung file `backend/src/Consent.Application/ConsentTemplates/Defaults/<mã mẫu>.json` của bản mới, bấm **Lưu phiên bản nháp** rồi **Phát hành**. Phải cài gói trước: bản cũ không biết các tùy chọn mới và sẽ bỏ mất chúng khi lưu. Phiếu đã lập giữ phiên bản cũ; PDF đã ký giữ nguyên.

### 5.5. API thông tin người bệnh (tùy chọn, quyết định 22)

Dịch vụ riêng `PatientInfoApi` đọc thông tin hành chính trong CSDL của bệnh viện (HIS) theo mã người bệnh hoặc CCCD. Phần mềm phiếu đồng thuận **chỉ gọi API này**, không giữ chuỗi kết nối CSDL bệnh viện. Không cài thì phần mềm vẫn chạy bình thường (nhập tay/quét CCCD).

1. **Tài khoản SQL chỉ đọc** trên CSDL bệnh viện: sửa rồi chạy `sql\04-patientinfo-reader.sql` (tên CSDL, bảng/view, mật khẩu). Chỉ cấp `SELECT` trên đúng bảng/view cần dùng.
2. **Câu truy vấn**: một câu `SELECT` trả **một dòng**, dùng tham số `@PatientCode` và/hoặc `@CitizenId` (tham số không dùng là NULL). Đặt tên cột bằng `AS`; cột nào không có thì trường đó để trống.

   | Tên sau `AS` | Điền vào ô |
   |---|---|
   | `PatientCode` | Mã người bệnh |
   | `FullName` | Họ và tên |
   | `DateOfBirth` | Ngày sinh |
   | `Gender` | Giới tính |
   | `CitizenId` | Số CCCD |
   | `CitizenIdIssueDate` | Ngày cấp (CCCD) |
   | `OldIdNumber` | Số CMND cũ |
   | `PermanentAddress` | Nơi thường trú |
   | `CurrentAddress` | Địa chỉ hiện tại (để trống nghĩa là giống nơi thường trú) |
   | `PhoneNumber` | Số điện thoại |
   | `Ethnicity` | Dân tộc |
   | `Occupation` | Nghề nghiệp |
   | `Nationality` | Quốc tịch |
   | `HealthInsuranceNumber` | Mã thẻ BHYT |

   Ngày nhận kiểu `date`/`datetime` hoặc chữ `dd/MM/yyyy`, `yyyy-MM-dd`, `yyyyMMdd`. Ví dụ (tên bảng/cột thay theo CSDL thật):

   ```sql
   SELECT TOP 1 MaBN AS PatientCode, HoTen AS FullName, NgaySinh AS DateOfBirth, GioiTinh AS Gender, SoCCCD AS CitizenId,
          NgayCapCCCD AS CitizenIdIssueDate, DienThoai AS PhoneNumber, DiaChi AS PermanentAddress, DanToc AS Ethnicity,
          SoTheBHYT AS HealthInsuranceNumber
   FROM dbo.BenhNhan
   WHERE MaBN = @PatientCode OR SoCCCD = @CitizenId
   ```

   Nếu CSDL tách địa chỉ thành nhiều cột (số nhà, phường/xã, quận/huyện, tỉnh/thành) hoặc lưu mã tra bảng danh mục, thì ghép ngay trong câu truy vấn:
   - Dùng `CONCAT_WS` (có từ SQL Server 2017); bọc từng phần bằng `NULLIF(…, N'')` để bỏ phần trống.
   - Cấp `SELECT` cho các bảng danh mục (xem cuối `04-patientinfo-reader.sql`).

   ```sql
   SELECT TOP 1 bn.MaBN AS PatientCode, …,
          CONCAT_WS(N', ', NULLIF(bn.SoNha, N''), NULLIF(x.TenXa, N''), NULLIF(h.TenHuyen, N''), NULLIF(t.TenTinh, N'')) AS PermanentAddress
   FROM dbo.BenhNhan bn
   LEFT JOIN dbo.DM_Xa x ON x.MaXa = bn.MaXa
   LEFT JOIN dbo.DM_Huyen h ON h.MaHuyen = bn.MaHuyen
   LEFT JOIN dbo.DM_Tinh t ON t.MaTinh = bn.MaTinh
   WHERE bn.MaBN = @PatientCode OR bn.SoCCCD = @CitizenId
   ```

   Chạy thử trong SSMS (khai báo `DECLARE @PatientCode nvarchar(50) = N'…', @CitizenId nvarchar(20) = NULL;`) trước khi cấu hình. Câu có dấu `;` giữa câu, hoặc không phải `SELECT`/`WITH`, bị từ chối khi khởi động.
3. **Site IIS**: Application Pool `PatientInfoApi` (No Managed Code), site `PatientInfoApi` trỏ `C:\Apps\PatientInfoApi`, binding **http `127.0.0.1` cổng 5081** nếu cùng máy với phần mềm phiếu. Nếu đặt ở máy khác (gần CSDL), dùng binding **https** với chứng chỉ nội bộ và chỉ mở tường lửa cho máy chạy phần mềm phiếu.
4. **Biến môi trường** của Application Pool `PatientInfoApi` (cách đặt như mục 5.2):

   | Biến | Giá trị |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `PatientInfo__ConnectionString` | `Server=tcp:SQLHIS,1433;Database=…;User Id=patientinfo_reader;Password=…;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly` |
   | `PatientInfo__Query` | Câu truy vấn ở bước 2 (một dòng) |
   | `PatientInfo__ApiKeys__0` | Khóa API ngẫu nhiên ≥ 32 ký tự, tạo như `Jwt__SigningKey` ở mục 3 (bộ sinh số ngẫu nhiên an toàn, không dùng `Get-Random`) |
   | `PatientInfo__MaleValues__0`…, `PatientInfo__FemaleValues__0`… | (Tùy chọn) khi cột giới tính không theo mặc định (`Nam/M/Male/1` là Nam, `Nữ/F/Female/0/2` là Nữ). Đặt **cả hai** danh sách, danh sách đặt thay hẳn mặc định. Ví dụ CSDL lưu 0 = Nam, 1 = Nữ: `PatientInfo__MaleValues__0 = 0`, `PatientInfo__FemaleValues__0 = 1`. Cũng có thể đổi ngay trong câu truy vấn: `CASE GioiTinh WHEN 0 THEN N'Nam' WHEN 1 THEN N'Nữ' END AS Gender` |

   Đặt bằng PowerShell (Run as administrator). Hàm `Set-PoolEnv` xóa biến cũ cùng tên rồi thêm lại, nên chạy lại nhiều lần không sao:

   ```powershell
   Import-Module WebAdministration
   function Set-PoolEnv([string]$Pool, [string]$Name, [string]$Value) {
     $f = "system.applicationHost/applicationPools/add[@name='$Pool']/environmentVariables"
     try { Remove-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $f -Name '.' -AtElement @{name=$Name} -ErrorAction Stop } catch { }
     Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $f -Name '.' -Value @{name=$Name; value=$Value}
   }
   $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $key = [Convert]::ToBase64String($b)
   $pw = [System.Net.NetworkCredential]::new('', (Read-Host 'Mat khau patientinfo_reader' -AsSecureString)).Password
   # Câu ở bước 2, bỏ dòng DECLARE, dán nguyên văn (giữ dấu '); không dùng chú thích --. Dòng '@ phải ở đầu dòng.
   $query = @'
   SELECT TOP 1 MaBN AS PatientCode, HoTen AS FullName, NgaySinh AS DateOfBirth, GioiTinh AS Gender, SoCCCD AS CitizenId
   FROM dbo.BenhNhan
   WHERE MaBN = @PatientCode OR SoCCCD = @CitizenId
   '@
   $query = ($query -replace '\s+', ' ').Trim()

   Set-PoolEnv PatientInfoApi 'PatientInfo__ConnectionString' "Server=tcp:SQLHIS,1433;Database=…;User Id=patientinfo_reader;Password=$pw;Encrypt=True;TrustServerCertificate=True;ApplicationIntent=ReadOnly"
   Set-PoolEnv PatientInfoApi 'PatientInfo__Query' $query
   Set-PoolEnv PatientInfoApi 'PatientInfo__ApiKeys__0' $key
   Restart-WebAppPool PatientInfoApi
   ```

   Khi dán vào PowerShell, dòng `'@` phải sát lề trái. Nên đặt câu truy vấn bằng here-string `@' … '@` như trên. Nếu viết trong cặp `'…'` thông thường thì dấu `'` trong câu (ví dụ `N'Nam'`) làm hỏng lệnh: biến `PatientInfo__Query` không được đặt, và API dừng khi khởi động (HTTP 500.30, xem mục 11).

5. Kiểm tra (trong Windows PowerShell, `curl` là tên tắt của `Invoke-WebRequest`, không nhận `-X`/`-H`/`-d`):

   ```powershell
   (Invoke-WebRequest http://127.0.0.1:5081/health -UseBasicParsing).Content     # {"status":"Healthy"}
   Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5081/api/v1/patients/lookup -Headers @{ 'X-Api-Key' = $key } `
     -ContentType 'application/json; charset=utf-8' -Body (@{ patientCode = '<mã người bệnh có thật>' } | ConvertTo-Json)
   ```

   Kết quả là dữ liệu thật của người bệnh: chỉ xem đúng/sai, không chép ra ngoài.
   - `(404)`: không tìm thấy người bệnh.
   - `(400)`: dữ liệu gửi lên không hợp lệ. Ví dụ còn để chữ mẫu, hoặc thiếu `charset=utf-8` nên Windows PowerShell gửi sai chữ có dấu.
   - `(503)`: không truy vấn được CSDL. Mã lỗi SQL ghi trong Event Log, xem mục 11.
6. **Bật trong phần mềm phiếu**: thêm biến môi trường cho Application Pool `ConsentApi` rồi khởi động lại:
   `PatientSource__Provider = Api`, `PatientSource__BaseUrl = http://127.0.0.1:5081/` (dùng IP như mục 5.3: `localhost` có thể thành IPv6 `::1` mà site chỉ nhận `127.0.0.1`, IIS trả 400 *Invalid Hostname*; nếu API ở máy khác thì dùng `https://…/`, ngoài máy này bắt buộc https), `PatientSource__ApiKey = <khóa ở bước 4>`. Đăng nhập lại: màn hình lập phiếu có ô **Lấy thông tin**. Nếu báo "Chưa kết nối được hệ thống thông tin người bệnh", xem nhật ký của `ConsentApi`: dòng *API thông tin người bệnh trả mã 401* là sai khóa, *Không gọi được…* là sai địa chỉ hoặc site `PatientInfoApi` chưa chạy.

### 5.6. Gắn phiếu với lượt tiếp nhận (tùy chọn, quyết định 25)

Khi quản trị bật (bước 5), người ký hoàn tất phiếu (tiếp nhận, bác sĩ…) **bắt buộc chọn lượt tiếp nhận** của người bệnh trên HIS; số tiếp nhận, thời gian, nơi tiếp nhận và người tiếp nhận được in ở **chân mỗi trang PDF** để đối soát. Cần API thông tin người bệnh (mục 5.5) đang chạy. Bản phát hành có tính năng này kèm migration `AddConsentFormEncounter` (thêm 4 cột, 1 chỉ mục vào bảng phiếu): chạy `efbundle.exe` (mục 4.2) trước khi thay thư mục `ConsentApi`.

1. **Quyền đọc**: cấp thêm `SELECT` cho `patientinfo_reader` trên bảng/view tiếp nhận và các bảng câu truy vấn nối thêm (phòng khám, nhân viên): xem phần *lượt tiếp nhận* cuối `deploy\sql\04-patientinfo-reader.sql`.
2. **Câu truy vấn lượt tiếp nhận**: một câu `SELECT` trả **các lượt** của một người bệnh trong khoảng thời gian. Tham số: `@PatientCode` và/hoặc `@CitizenId` (như mục 5.5), `@From` (đầu ngày bắt đầu) và `@To` (đầu ngày **sau** ngày kết thúc). Lọc thời gian bằng `>= @From AND < @To`.

   | Tên sau `AS` | Ý nghĩa |
   |---|---|
   | `EncounterNo` | Số tiếp nhận (bắt buộc, tối đa 50 ký tự; dòng thiếu thì bỏ qua) |
   | `ReceivedAt` | Thời gian tiếp nhận, giờ bệnh viện (bắt buộc). Nhận `datetime` hoặc chữ `dd/MM/yyyy HH:mm[:ss]`, `yyyy-MM-dd HH:mm[:ss]`, `yyyyMMddHHmmss` |
   | `Place` | Nơi tiếp nhận (phòng khám, quầy, khoa) |
   | `ReceivedBy` | Người tiếp nhận: nhân viên đã tiếp nhận lượt đó trên HIS |

   Ví dụ (tên bảng/cột thay theo CSDL thật):

   ```sql
   SELECT TOP 200 tn.SoTiepNhan AS EncounterNo, tn.ThoiGianTiepNhan AS ReceivedAt, pk.TenPhong AS Place, nv.HoTen AS ReceivedBy
   FROM dbo.TiepNhan tn
   JOIN dbo.BenhNhan bn ON bn.MaBN = tn.MaBN
   LEFT JOIN dbo.DM_PhongKham pk ON pk.MaPhong = tn.MaPhong
   LEFT JOIN dbo.NhanVien nv ON nv.MaNV = tn.MaNVTiepNhan
   WHERE (bn.MaBN = @PatientCode OR bn.SoCCCD = @CitizenId)
     AND tn.ThoiGianTiepNhan >= @From AND tn.ThoiGianTiepNhan < @To
   ORDER BY tn.ThoiGianTiepNhan DESC
   ```

   Chạy thử trong SSMS với `DECLARE @PatientCode nvarchar(50) = N'…', @CitizenId nvarchar(20) = NULL, @From datetime2 = '2026-11-15', @To datetime2 = '2026-11-16';`. Câu thiếu `@From`, `@To` hoặc cả hai tham số người bệnh, có `;` giữa câu, hoặc không phải `SELECT`/`WITH` thì API dừng khi khởi động (HTTP 500.30). API trả tối đa 200 lượt, mới nhất trước.
3. **Đặt biến** cho Application Pool `PatientInfoApi` (PowerShell Run as administrator; định nghĩa lại hàm `Set-PoolEnv` ở mục 5.5 bước 4 nếu mở cửa sổ mới):

   ```powershell
   $encounterQuery = @'
   SELECT TOP 200 tn.SoTiepNhan AS EncounterNo, tn.ThoiGianTiepNhan AS ReceivedAt, pk.TenPhong AS Place, nv.HoTen AS ReceivedBy
   FROM dbo.TiepNhan tn
   JOIN dbo.BenhNhan bn ON bn.MaBN = tn.MaBN
   LEFT JOIN dbo.DM_PhongKham pk ON pk.MaPhong = tn.MaPhong
   LEFT JOIN dbo.NhanVien nv ON nv.MaNV = tn.MaNVTiepNhan
   WHERE (bn.MaBN = @PatientCode OR bn.SoCCCD = @CitizenId) AND tn.ThoiGianTiepNhan >= @From AND tn.ThoiGianTiepNhan < @To
   ORDER BY tn.ThoiGianTiepNhan DESC
   '@
   $encounterQuery = ($encounterQuery -replace '\s+', ' ').Trim()
   Set-PoolEnv PatientInfoApi 'PatientInfo__EncounterQuery' $encounterQuery
   Restart-WebAppPool PatientInfoApi
   ```

4. **Kiểm tra** (chỉ in số lượt và tên cột có giá trị, không in dữ liệu):

   ```powershell
   $key = (Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='PatientInfoApi']/environmentVariables/add[@name='PatientInfo__ApiKeys__0']").value
   $today = (Get-Date).ToString('yyyy-MM-dd')
   $r = Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5081/api/v1/encounters/search -Headers @{ 'X-Api-Key' = $key } `
     -ContentType 'application/json; charset=utf-8' -Body (@{ patientCode = '<mã người bệnh có lượt hôm nay>'; from = $today; to = $today } | ConvertTo-Json)
   $r.items.Count
   $r.items | Select-Object -First 1 | ForEach-Object { $_.PSObject.Properties | ForEach-Object { '{0}: {1}' -f $_.Name, ($null -ne $_.Value) } }
   ```

   `(404)`: `PatientInfoApi` còn bản cũ, chưa có chức năng tra lượt tiếp nhận (thay thư mục như mục 5.4 bước 5). `(501)`: chưa đặt `PatientInfo__EncounterQuery`. `(400)`, `(503)`: như mục 5.5 bước 5. Số lượt bằng 0 dù người bệnh có lượt hôm nay: kiểm tra điều kiện ngày và cột thời gian trong câu truy vấn.
5. **Bật**: quản trị vào *Quản trị → Cấu hình hệ thống*, thẻ **Gắn phiếu với lượt tiếp nhận (HIS)**, bật công tắc *Bắt buộc chọn lượt tiếp nhận khi ký hoàn tất phiếu*. Có hiệu lực ngay, không cần khởi động lại; mỗi lần bật/tắt được ghi nhật ký hệ thống. Công tắc chỉ bật được khi `ConsentApi` đã cấu hình API thông tin người bệnh (mục 5.5 bước 6). Từ đó hộp **Ký xác nhận và hoàn tất** và **Xác nhận cả bộ** có bảng lượt tiếp nhận.
   - Hồ sơ người bệnh cần có **mã y tế** (dùng trước) hoặc **số CCCD** để tra lượt tiếp nhận.
   - **HIS hoặc API thông tin người bệnh lỗi:** nhân viên chưa hoàn tất được phiếu (phiếu vẫn chờ xác nhận, giữ chữ ký người bệnh). Quản trị **tắt công tắc** để nhân viên hoàn tất tiếp: trong hộp ký bấm *Thử lại* là ký được, không cần tải lại trang. Phiếu hoàn tất lúc tắt không gắn lượt tiếp nhận. HIS ổn định thì bật lại.

### 5.7. Báo cáo điều dưỡng hằng ngày (quyết định 31)

Khoa lập "Báo cáo công tác điều dưỡng" mỗi ngày, Phòng Điều dưỡng tổng hợp (menu **Báo cáo điều dưỡng**). Bản phát hành có tính năng này kèm migration `AddNursingReports` (3 bảng mới; thêm quyền `nursingreports.submit` cho vai trò **Điều dưỡng** có sẵn): chạy `efbundle.exe` (mục 4.2) trước khi thay thư mục `ConsentApi`. Khi khởi động, phần mềm tự tạo vai trò mới **Phòng Điều dưỡng**. Bản có quyết định 35 (khoa tự nhập số liệu HIS khi HIS lỗi) kèm thêm migration `AddNursingDepartmentStatistics` (1 bảng mới), chạy cùng cách.

1. **Tài khoản Phòng Điều dưỡng:** quản trị vào *Quản trị → Người dùng*, gán vai trò **Phòng Điều dưỡng** cho nhân viên Phòng Điều dưỡng (nên đặt khoa là Phòng Điều dưỡng). Vai trò này xem mọi khoa và bảng tổng hợp, trả lại báo cáo cho khoa sửa (nút **Trả lại khoa**), cài khoa báo cáo, số giường, khoa nhập số liệu HIS và bấm lấy số liệu HIS. Phòng Điều dưỡng không nhập số liệu thay khoa. Ban giám đốc chỉ cần xem: phần mềm chưa tạo được vai trò mới, nên thêm quyền *Xem báo cáo điều dưỡng mọi khoa và bảng tổng hợp* vào vai trò ban giám đốc đang dùng (*Quản trị → Vai trò và quyền*; mọi người có vai trò đó cùng xem được).
2. **Điều dưỡng các khoa:** vai trò **Điều dưỡng** đã có quyền *Lập và gửi báo cáo điều dưỡng của khoa*. Người lập báo cáo phải được đặt **đúng khoa** ở hồ sơ người dùng (báo cáo lập cho khoa của người dùng). Vai trò khác cần lập (ví dụ trưởng khoa) thì thêm quyền này ở *Vai trò và quyền*.
3. **Khoa báo cáo và số giường:** Phòng Điều dưỡng vào *Báo cáo điều dưỡng → Tổng hợp hằng ngày*, bấm **Khoa báo cáo và số giường**, chọn các khoa phải gửi báo cáo và nhập số giường thực kê (không tính băng ca). Khoa chưa được chọn thì chưa lập được báo cáo.
4. **Khoa nhập số liệu HIS khi HIS lỗi:** trong hộp **Khoa báo cáo và số giường**, phần *Khoa nhập số liệu HIS khi HIS lỗi*, Phòng Điều dưỡng chọn khoa nhập từng chỉ số mục B, D (ví dụ TSNB đến khám: Khoa Khám bệnh; siêu âm, X-quang, CT, MRI: Khoa Chẩn đoán hình ảnh; siêu âm tim, điện tâm đồ: Khoa Thăm dò chức năng). Mỗi khoa phải báo cáo tự nhập "NB PK chờ nhập viện" của khoa mình. Mỗi khoa được giao cần **ít nhất một tài khoản đúng khoa có quyền** *Lập và gửi báo cáo điều dưỡng của khoa* (ví dụ vai trò **Điều dưỡng**); khoa được giao không cần nằm trong danh sách khoa báo cáo. Khoa nhập ở trang **Báo cáo của khoa** (hướng dẫn sử dụng mục 9.5).
5. **Số liệu HIS (tùy chọn):** mục B (TSNB đến khám), mục D (số liệu CĐHA) và cột "NB PK chờ nhập viện" lấy từ HIS qua API thông tin người bệnh (mục 5.5). Chưa làm bước này, hoặc HIS lỗi, thì các khoa nhập tay như bước 4; bảng tổng hợp dùng số HIS trước, ô HIS không có số mới dùng số khoa nhập.
   - **Quyền đọc:** cấp `SELECT` cho `patientinfo_reader` trên các bảng câu truy vấn dùng (xem phần *báo cáo điều dưỡng* cuối `deploy\sql\04-patientinfo-reader.sql`).
   - **Câu truy vấn:** một câu `SELECT` (thường nhiều câu nối bằng `UNION ALL`), mỗi dòng một số liệu, tham số `@From` (đầu ngày) và `@To` (đầu ngày hôm sau), lọc thời gian bằng `>= @From AND < @To`.

     | Tên sau `AS` | Ý nghĩa |
     |---|---|
     | `Indicator` | Mã chỉ số: `outpatientVisits` (tổng số người bệnh đến khám), `ultrasound` (siêu âm tổng quát), `echocardiography` (siêu âm tim), `ecg` (điện tâm đồ), `xray` (X-quang), `ct` (CT Scan), `mri` (MRI), `waitingAdmission` (người bệnh phòng khám chờ nhập viện, theo khoa) |
     | `DepartmentCode` | Mã khoa **trong phần mềm phiếu** (*Quản trị → Khoa/phòng*); chỉ cần với `waitingAdmission`. Mã khác thì dùng `CASE` để đổi |
     | `Value` | Số nguyên ≥ 0 |

     Ví dụ (tên bảng/cột thay theo CSDL thật):

     ```sql
     SELECT 'outpatientVisits' AS Indicator, NULL AS DepartmentCode, COUNT(*) AS Value
     FROM dbo.TiepNhan WHERE ThoiGianTiepNhan >= @From AND ThoiGianTiepNhan < @To
     UNION ALL
     SELECT 'waitingAdmission', MaKhoa, COUNT(*)
     FROM dbo.ChoNhapVien WHERE ThoiGianChiDinh >= @From AND ThoiGianChiDinh < @To GROUP BY MaKhoa
     UNION ALL
     SELECT CASE LoaiCLS WHEN N'SA' THEN 'ultrasound' WHEN N'SAT' THEN 'echocardiography' WHEN N'ECG' THEN 'ecg'
                         WHEN N'XQ' THEN 'xray' WHEN N'CT' THEN 'ct' WHEN N'MRI' THEN 'mri' END, NULL, COUNT(*)
     FROM dbo.ChiDinhCLS WHERE ThoiGianThucHien >= @From AND ThoiGianThucHien < @To
       AND LoaiCLS IN (N'SA', N'SAT', N'ECG', N'XQ', N'CT', N'MRI')
     GROUP BY LoaiCLS
     ```

     Chạy thử trong SSMS với `DECLARE @From datetime2 = '2026-11-16', @To datetime2 = '2026-11-17';`. Câu thiếu `@From` hoặc `@To`, có `;` giữa câu, hoặc không phải `SELECT`/`WITH` thì API dừng khi khởi động (HTTP 500.30).
   - **Đặt biến** cho Application Pool `PatientInfoApi` (như mục 5.6 bước 3):

     ```powershell
     $dailyQuery = @'
     <dán câu truy vấn đã chạy thử>
     '@
     $dailyQuery = ($dailyQuery -replace '\s+', ' ').Trim()
     Set-PoolEnv PatientInfoApi 'PatientInfo__DailyStatisticsQuery' $dailyQuery
     Restart-WebAppPool PatientInfoApi
     ```

   - **Kiểm tra** (chỉ in số đếm):

     ```powershell
     $key = (Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='PatientInfoApi']/environmentVariables/add[@name='PatientInfo__ApiKeys__0']").value
     $r = Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5081/api/v1/daily-statistics -Headers @{ 'X-Api-Key' = $key } `
       -ContentType 'application/json; charset=utf-8' -Body (@{ date = (Get-Date).ToString('yyyy-MM-dd') } | ConvertTo-Json)
     $r.items | Format-Table indicator, departmentCode, value
     ```

     `(404)`: `PatientInfoApi` còn bản cũ (thay thư mục như mục 5.4 bước 5). `(501)`: chưa đặt `PatientInfo__DailyStatisticsQuery`. `(503)`: câu truy vấn lỗi hoặc thiếu quyền đọc.
   - Trên bảng tổng hợp, bấm **Lấy số liệu HIS**: số liệu được lưu lại theo ngày (số "chờ nhập viện" là số tại lúc lấy). Mã khoa HIS trả về mà không khớp khoa nào được báo lên màn hình để sửa câu truy vấn.

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

### 9a1. Người bệnh ký trên điện thoại của mình qua mã QR (quyết định 32)

Nhân viên bấm **Mã QR cho người bệnh** (phiếu lẻ) hoặc **Mã QR ký cả bộ** (bộ phiếu); người bệnh quét mã bằng camera điện thoại của mình, nhập **ngày sinh của người bệnh**, đọc và ký. Không cần cài gì thêm trên máy chủ ngoài gói mới (có migration `AddRemoteSigningLinks`).

1. **Địa chỉ trong mã QR** là địa chỉ nhân viên đang dùng để mở phần mềm (ví dụ `https://phieudongthuan.bth.org.vn/ky-tu-xa#…`). Nhân viên phải mở phần mềm bằng **tên miền chính thức**, không bằng `localhost` hay địa chỉ IP, thì điện thoại người bệnh mới mở được.
2. **Mạng cho điện thoại người bệnh** (phòng CNTT quyết định):
   - **Khuyến nghị:** Wi-Fi cho người bệnh (khách) được phép vào **đúng địa chỉ HTTPS của phần mềm** (DNS nội bộ trỏ tên miền về máy chủ IIS), không vào các máy khác trong mạng bệnh viện. Điện thoại dùng 4G thì không mở được: nhờ người bệnh bắt Wi-Fi, hoặc ký trên máy của bệnh viện.
   - Không mở phần mềm ra Internet nếu chưa được phòng CNTT và lãnh đạo đồng ý.
   - **Bắt buộc HTTPS** khi điện thoại người bệnh dùng: phiếu có thông tin sức khỏe đi qua Wi-Fi khách.
3. **Bảo vệ mã:** mã 128 bit ngẫu nhiên, máy chủ chỉ lưu mã băm; mở trong 15 phút; mở được trên một điện thoại; phải nhập đúng ngày sinh, sai 5 lần thì mã bị hủy; mã nằm sau dấu `#` nên không có trong nhật ký truy cập của IIS. Nhân viên hủy mã được bất cứ lúc nào.
4. **Giới hạn tần suất:** trang mở mã cho phép 30 lần/phút mỗi địa chỉ IP (`RateLimiting__RemoteSigningPermitLimit`). Nhiều điện thoại qua cùng một địa chỉ IP (NAT của Wi-Fi khách) mà hay gặp lỗi 429 thì tăng số này, ví dụ 120. Giới hạn và nhật ký chỉ có đúng địa chỉ IP của điện thoại khi API tin reverse proxy trên cùng máy (`ReverseProxy__TrustAllProxies = true`, mục 5.2).
5. **Nhật ký:** mỗi lần tạo mã, mở mã (kèm địa chỉ IP, trình duyệt của điện thoại), nhập sai ngày sinh, hủy mã đều ghi nhật ký hệ thống (`ConsentForms.RemoteSigning*`). Chữ ký ghi trình duyệt của điện thoại đã ký.

## 9b. Máy kiosk cho người bệnh tự làm phiếu (quyết định 24)

Người bệnh tự nhận diện, trả lời và ký một lần cho cả bộ phiếu trên kiosk; nhân viên tiếp đón kiểm tra rồi **Xác nhận cả bộ** như bình thường. Kiosk là một trang của chính phần mềm (`/kiosk`), không cài thêm gì trên máy chủ.

**Phần cứng**

1. **Màn hình cảm ứng 32 inch đặt dọc** (Full HD 1080×1920 hoặc 4K đặt tỉ lệ hiển thị 200%), máy tính Windows 10/11 chạy Chrome hoặc Edge bản mới. Trong Windows: *Settings → System → Display → Display orientation: Portrait*.
2. **Máy quét mã 2D (đọc được QR) cổng USB**, gắn bên dưới màn hình, đặt ở **chế độ bàn phím (USB HID Keyboard)**, **thêm phím Enter sau mỗi lần quét**, và **xuất ký tự Unicode/UTF-8** để đọc đúng họ tên có dấu trong mã QR (xem tài liệu máy quét; nhiều máy có mã vạch cấu hình in sẵn). Thử với vài thẻ thật: CCCD gắn chip, thẻ căn cước mẫu mới và mã QR căn cước trên ứng dụng VNeID; màn hình **Đây có phải thông tin của ông/bà?** phải ghi đúng dấu ở **Nơi thường trú**. Máy quét chưa cài tiếng Việt gõ chữ có dấu thành ký tự lạ (kiểu "Ä‘Æ°á»ng"): phần mềm tự sửa khi máy gõ đủ từng byte (bảng mã Windows-1252 hoặc 1258); máy bỏ mất ký tự, hoặc bỏ hẳn chữ có dấu ("ng" thay cho "Đường"), thì phần mềm không sửa được: bỏ địa chỉ (kiosk ghi **Chưa có** hoặc **Đã có trong hồ sơ** thay cho địa chỉ), và họ tên hỏng thì kiosk chỉ dùng tiếp khi bệnh viện đã có hồ sơ, chưa có thì mời người bệnh chọn **Nhập thông tin**. Kiểm tra máy quét: bật **Num Lock** (nhiều máy gõ chữ có dấu bằng phím Alt + số, chỉ chạy khi Num Lock bật; đặt Num Lock bật khi khởi động máy), mở Notepad trên kiosk và quét mã QR thử vào Notepad: phải hiện đúng dấu. Vì vậy vẫn phải cài máy quét xuất Unicode; camera (mục 3) đọc đúng chữ có dấu.
3. **Camera (không bắt buộc, quyết định 34):** kiosk có webcam (gắn trên đỉnh màn hình, hướng về người bệnh) thì màn hình **Quét CCCD** tự hiện khung hình camera và đọc mã QR từ camera; máy quét 2D vẫn dùng được cùng lúc. Kiosk không có camera thì chỉ dùng máy quét như cũ. Điều kiện:
   - Trang kiosk chạy **HTTPS** (trình duyệt không mở camera ở địa chỉ `http://`).
   - Trình duyệt của kiosk **đã được cho phép dùng camera** cho địa chỉ phần mềm, để người bệnh không gặp hộp hỏi quyền. Cách 1: trên máy kiosk mở `/kiosk`, chạm **Quét CCCD**, chọn **Cho phép** (trình duyệt nhớ lựa chọn). Cách 2 (khuyến nghị): đặt chính sách trình duyệt `VideoCaptureAllowedUrls` bằng PowerShell *Run as administrator*, rồi mở `chrome://policy` (hoặc `edge://policy`) bấm *Reload policies*:

     ```powershell
     # Chrome; Edge thay 'Google\Chrome' bằng 'Microsoft\Edge'
     $k = 'HKLM:\SOFTWARE\Policies\Google\Chrome\VideoCaptureAllowedUrls'
     New-Item -Path $k -Force | Out-Null
     Set-ItemProperty -Path $k -Name '1' -Value 'https://<địa chỉ phần mềm>'
     ```

   - Camera không mở được (người dùng vừa chọn Chặn ở hộp hỏi quyền, không thấy camera, ứng dụng khác đang dùng camera) thì màn hình quét ghi *"Camera của kiosk chưa dùng được (lý do)"* và người bệnh dùng máy quét.
   - **Chọn thiết bị quét** (không có cài đặt trong phần mềm, kiosk tự nhận): chỉ cắm **máy quét 2D** → màn hình máy quét như cũ; chỉ có **camera** → màn hình camera; có **cả hai** → màn hình camera, máy quét vẫn nhận cùng lúc. Máy có sẵn webcam mà bệnh viện **chỉ muốn dùng máy quét**: chặn camera trong trình duyệt của kiosk (biểu tượng bên trái thanh địa chỉ → *Camera* → *Chặn*, hoặc chính sách `VideoCaptureAllowed` = 0); kiosk hiện màn hình máy quét, không báo lỗi.
   - Hình từ camera chỉ dùng để đọc mã ngay trên máy kiosk, không lưu, không gửi lên máy chủ. Thẻ còn nằm trong khung hình thì máy không gửi lại, để không tự tính nhiều lần sai.

**Tài khoản và mẫu phiếu** (làm một lần)

1. *Quản trị → Người dùng → Thêm*: tên đăng nhập ví dụ `kiosk.kkb`, chọn **khoa đặt máy** (phiếu lập trên kiosk thuộc khoa này) và **chỉ vai trò "Kiosk người bệnh"**. Máy chủ từ chối nếu thêm vai trò khác hoặc không chọn khoa. Vai trò này chỉ có quyền dùng màn hình kiosk, không sửa được trên ma trận quyền.
2. *Quản trị → Cấu hình hệ thống → Kiosk người bệnh tự làm phiếu*: chọn mẫu **theo thứ tự làm**, đề xuất `QT-KNGT-BM02`, `TTNB-NGOAITRU`, `TUVAN-CONGTT` → **Lưu mẫu kiosk**. Mẫu có bảng nhiều dòng hoặc ngày giờ không chọn được; BM10 (nhiều mục chuyên môn) để điều dưỡng làm.
3. Nâng cấp từ bản trước: chạy migration như mục 4.2 (bản này thêm 3 cột: `ConsentForms.KioskIdentity`, `Users.KioskLookupFailures`, `Users.KioskLookupLockedUntilUtc`). Vai trò Kiosk tự được tạo khi API khởi động.

**Mở Chrome toàn màn hình khi bật máy**

Tạo lối tắt trong thư mục khởi động (`Win + R` → `shell:startup`), đích:

```text
"C:\Program Files\Google\Chrome\Application\chrome.exe" --kiosk "https://<địa chỉ phần mềm>/kiosk" --no-first-run --disable-pinch --overscroll-history-navigation=0 --disable-features=TranslateUI
```

Edge dùng tương tự: `msedge.exe --kiosk "https://<địa chỉ phần mềm>/kiosk" --edge-kiosk-type=fullscreen`.

**Hằng ngày**

1. Mỗi sáng nhân viên bật máy, đăng nhập **tài khoản kiosk** (lần đầu đổi mật khẩu). Phiên đăng nhập giữ trong ngày (12 giờ, `Auth:RefreshTokenLifetimeHours`).
2. Đăng xuất máy: **giữ biểu tượng bệnh viện ở góc trên bên trái 3 giây** → *Đăng xuất*. Thoát Chrome toàn màn hình: `Alt + F4` bằng bàn phím rời.
3. **60 giây không chạm** thì màn hình tự xóa thông tin và về trang đầu (10 giây cuối có nhắc).
4. Người bệnh nhập mã/CCCD sai ngày sinh **5 lần** thì máy tạm khóa tra cứu **5 phút** (quét thẻ cũng phải chờ; tự khai không kèm CCCD vẫn làm được). Mở khóa sớm: *Quản trị → Người dùng*, tài khoản kiosk có trạng thái **Tạm khóa tra cứu** → **⋯** → *Mở khóa*.
5. Người bệnh đã làm cùng phiếu gần đây (trong số ngày đặt ở *Cấu hình hệ thống → Kế thừa phiếu lần trước*, quyết định 27): kiosk hỏi *"Thông tin có thay đổi so với lần trước không?"*; *Không thay đổi* thì không hỏi lại các câu đã trả lời (không hiện câu trả lời cũ), Đồng ý/Không đồng ý và chữ ký vẫn làm lại.
6. Phiếu từ kiosk có nhãn **"Lập tại kiosk"** trong danh sách bộ phiếu và chi tiết phiếu. Nhân viên **đối chiếu CCCD** rồi bấm *Xác nhận cả bộ*; nhãn **"tự khai"** (màu cam) thì kiểm tra kỹ giấy tờ và thông tin hành chính trước khi xác nhận.

## 10. Danh sách kiểm tra sau triển khai

1. `https://…/health/ready` trả `Healthy`; `http://` chuyển sang `https://` (hoặc không mở).
2. Đăng nhập `admin`, đổi mật khẩu; đã xóa `Seed__AdminPassword`.
3. **Quản trị → Cấu hình hệ thống**: nhập cơ quan chủ quản, tên bệnh viện, địa danh; mở một phiếu nháp → PDF in đúng tên. Đặt số ngày **Kế thừa phiếu lần trước** nếu dùng (0 là tắt). Tải logo và **khung chữ ký** (ảnh thiết kế của bệnh viện, **PNG** tối đa 1 MB, nên ngang 4:3; ảnh khác thì mở bằng Paint rồi lưu lại PNG): khung ký trên điện thoại, kiosk, máy tính và khung chữ ký mẫu dùng ảnh này làm nền, và mỗi chỗ ký trên PDF in khung này. Xem PDF một phiếu nháp → chỗ ký có khung trống; ký hoàn tất một phiếu thử → chữ ký nằm giữa khung. Phiếu ký xong trước khi tải khung giữ PDF cũ, không có khung.
4. **Mẫu phiếu**: Phòng KHTH rà soát 4 mẫu phiếu của bệnh viện (được tạo ở trạng thái nháp khi không có dữ liệu demo, xem [mẫu phiếu](mau-phieu.md)) và **phát hành**.
5. Tạo khoa/phòng và tài khoản thật; đăng nhập thử từng vai trò (menu chỉ hiện đúng chức năng).
6. Trên điện thoại (mục 9a): quét QR CCCD bằng camera (cần HTTPS), lập một phiếu thử với người bệnh giả lập, ký, hoàn tất, xem PDF, rồi **thu hồi** phiếu thử (ghi lý do "kiểm tra triển khai").
7. Kiosk (mục 9b): quét thử vài thẻ CCCD thật (họ tên có dấu đọc đúng), làm trọn một bộ phiếu với người bệnh giả lập, nhân viên tiếp đón **Xác nhận cả bộ**; nhập sai ngày sinh 5 lần thấy máy tạm khóa tra cứu; để yên 60 giây thấy màn hình tự về trang đầu.
8. Lượt tiếp nhận (mục 5.6, nếu dùng): bật công tắc ở Cấu hình hệ thống; hoàn tất một phiếu thử của người bệnh có lượt tiếp nhận hôm nay trên HIS; danh sách đúng thời gian, nơi tiếp nhận; chân PDF có số tiếp nhận và người tiếp nhận; mở lại hộp ký phiếu khác của người bệnh đó thấy phiếu vừa gắn ở cột *Các phiếu đã gắn vào*. Tắt thử công tắc: hộp ký không còn bảng lượt. Thu hồi phiếu thử.
9. Báo cáo điều dưỡng (mục 5.7): Phòng Điều dưỡng cài khoa báo cáo và số giường; một điều dưỡng lập, lưu nháp, gửi báo cáo khoa mình (gửi xong thấy khóa); Phòng Điều dưỡng xem bảng tổng hợp, in PDF, xuất Excel, bấm **Trả lại khoa** một báo cáo thử kèm lý do (khoa thấy ai trả lại và lý do); giao khoa nhập số liệu HIS, một khoa được giao nhập thử ở trang Báo cáo của khoa và bảng tổng hợp hiện số đó kèm tên khoa; lấy số liệu HIS (nếu đã cài).
10. Mã QR (mục 9a1): trên máy tính mở phần mềm bằng tên miền chính thức, bấm **Mã QR cho người bệnh** ở một phiếu thử; một điện thoại **bắt Wi-Fi dành cho người bệnh** quét mã, nhập ngày sinh, ký; màn hình nhân viên báo đã ký. Quét lại mã bằng điện thoại khác thấy bị từ chối. Thu hồi phiếu thử.
11. Chạy sao lưu đầu tiên (CSDL + thư mục PDF) và **khôi phục thử** sang máy chủ thử nghiệm.
12. Lưu 3 khóa vào kho mật khẩu và bản niêm phong; ghi lại người giữ.
13. Thiết lập giám sát `/health/ready` và cảnh báo dung lượng đĩa.
14. Lưu lại phiên bản đã triển khai (mã commit/`APP_VERSION`) vào biên bản bàn giao.

## 11. Sự cố thường gặp

| Hiện tượng | Nguyên nhân | Xử lý |
|---|---|---|
| API không khởi động, log ghi "Thiếu khóa ký JWT" / "Thiếu khóa bảo vệ dữ liệu" | Thiếu hoặc sai định dạng biến môi trường | Kiểm tra 3 khóa (Base64 32 byte cho `DataProtection__*`), khởi động lại |
| "Dữ liệu minh họa (Seed:DemoData) không được bật ở môi trường Production" | `Seed__DemoData=true` | Đặt `false` |
| HTTP 500.19, mã `0x8007000d`, file `D:\Apps\ConsentApi\web.config` | IIS chưa có mô-đun ASP.NET Core: chưa cài Hosting Bundle 10, hoặc cài trước IIS | Cài (hoặc *Repair*) **ASP.NET Core Hosting Bundle 10.0**, rồi `iisreset`. Kiểm tra: `(Get-WebGlobalModule -Name AspNetCoreModuleV2).Name` in ra tên mô-đun; `dotnet --list-runtimes` có `Microsoft.AspNetCore.App 10.0.x` |
| `Bad Request - Invalid Hostname` (400) khi gọi `/api` hoặc `/health` qua site `ConsentWeb` | Rule *API proxy* chuyển tới `http://localhost:5080`; Windows có thể hiểu `localhost` là IPv6 `::1`, trong khi site `ConsentApi` chỉ nhận `127.0.0.1` | Sửa `web.config` của `ConsentWeb` thành `http://127.0.0.1:5080/{R:1}` (gói từ ngày 14/11/2026 đã sửa sẵn) |
| HTTP 500.19, file `D:\Apps\ConsentWeb\web.config` | Chưa cài URL Rewrite | Cài URL Rewrite và ARR, rồi `iisreset` |
| HTTP 500.30, nhật ký *.NET Runtime* ghi `Seed:AdminPassword … chưa đủ mạnh` | Mật khẩu tạm của `admin` không đạt quy tắc (10–128 ký tự, có chữ thường, chữ hoa, chữ số, ký tự đặc biệt, **không chứa `admin`**) | Đặt lại biến `Seed__AdminPassword`, rồi `Restart-WebAppPool ConsentApi`. Xem lỗi: `$e = Get-WinEvent -LogName Application -MaxEvents 50 \| Where-Object ProviderName -eq '.NET Runtime' \| Select-Object -First 1; ($e.Message -split "`r?`n")[0..14]` |
| HTTP 500.30 ở `http://127.0.0.1:5081/health`, Event Log (*IIS AspNetCore Module V2*) ghi `failed to load coreclr … CLR worker thread exited prematurely` | API thông tin người bệnh dừng khi khởi động vì thiếu hoặc sai biến môi trường, thường là thiếu `PatientInfo__Query` | Xem tên các biến đã đặt (không in giá trị): `(Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "system.applicationHost/applicationPools/add[@name='PatientInfoApi']/environmentVariables/add").name`. Đặt lại biến còn thiếu như mục 5.5 bước 4, rồi `Restart-WebAppPool PatientInfoApi` |
| Hộp ký báo *Không lấy được danh sách tiếp nhận từ HIS* | Site `PatientInfoApi` không chạy; chưa đặt `PatientInfo__EncounterQuery` (nhật ký `ConsentApi`: *trả mã 501 khi tra lượt tiếp nhận*); câu truy vấn lỗi hoặc CSDL HIS không truy cập được (mã 503, mã lỗi SQL trong Event Log) | Kiểm tra như mục 5.6 bước 4; sửa câu truy vấn hoặc quyền `SELECT`, `Restart-WebAppPool PatientInfoApi`. Trong lúc chờ sửa, quản trị tắt công tắc ở *Cấu hình hệ thống* để nhân viên hoàn tất tiếp (bấm *Thử lại* trong hộp ký) |
| Công tắc *Bắt buộc chọn lượt tiếp nhận* ở Cấu hình hệ thống bị khóa (màu xám) | `ConsentApi` chưa cấu hình API thông tin người bệnh | Đặt `PatientSource__Provider`, `PatientSource__BaseUrl`, `PatientSource__ApiKey` như mục 5.5 bước 6, rồi `Restart-WebAppPool ConsentApi` |
| Tra cứu trả 503, hoặc màn hình báo "Chưa kết nối được hệ thống thông tin người bệnh" | API không truy vấn được CSDL bệnh viện, hoặc phần mềm phiếu không gọi được API | Xem Event Log: `Get-WinEvent -LogName Application -MaxEvents 100 \| Where-Object { $_.Message -like '*PatientInfo.Lookup*' -or $_.Message -like '*ApiPatientSource*' } \| Select-Object -First 3 \| Format-List TimeCreated, Message`. Mã lỗi SQL: `18456` sai tài khoản/mật khẩu, `208` sai tên bảng, `207` sai tên cột, `229` thiếu quyền `SELECT`, `53`/`-1` không tới được máy chủ SQL. *API thông tin người bệnh trả mã 401*: khóa của `ConsentApi` khác khóa của `PatientInfoApi` |
| `/health/ready` trả 503 | Không kết nối được SQL Server | Kiểm tra chuỗi kết nối, tường lửa cổng 1433, chứng chỉ SQL (`TrustServerCertificate`) |
| `Named Pipes Provider, error: 40` / `The network path was not found` (khi chạy `efbundle.exe` hoặc API) | Không tới được SQL Server: sai tên máy, bản cài theo tên riêng, TCP/IP tắt, tường lửa chặn 1433, dịch vụ SQL chưa chạy | Dùng `Server=tcp:<máy>,1433` (hoặc `<máy>\<TÊN>` + SQL Server Browser); `Get-Service *SQL*`; bật TCP/IP, cổng 1433 trong SQL Server Configuration Manager; `Test-NetConnection <máy-SQL> -Port 1433` |
| `Process should have elevated status to access IIS configuration data`, rồi `Cannot find drive 'IIS'` và `No mapping between account names and security IDs` | PowerShell không chạy bằng quyền quản trị nên không tạo được Application Pool; `icacls` lỗi theo vì tài khoản `IIS AppPool\…` chưa tồn tại | Mở PowerShell bằng *Run as administrator*, chạy lại nguyên khối lệnh |
| Không đọc được CCCD/PDF, lỗi "không khớp mã băm" sau khôi phục | Đặt sai khóa `FieldEncryptionKey` hoặc thư mục PDF khác thời điểm với CSDL | Đặt lại đúng khóa cũ; khôi phục thư mục PDF cùng thời điểm |
| Camera không bật khi quét CCCD trên máy tính (khung đen, không báo lỗi) | Lỗi phần mềm ở các gói cài trước ngày 16/11/2026: camera không bao giờ bật (quyết định 34) | Cài gói mới. Từ gói mới, hộp thoại ghi *"Đang mở camera…"* rồi bật camera, hoặc ghi rõ lý do như dòng dưới |
| Hộp thoại quét bằng camera báo lỗi | *"…chỉ hoạt động khi truy cập qua HTTPS"*: trang mở bằng `http://`. *"Bạn chưa cho phép…"*: trình duyệt hoặc Windows chặn camera. *"Không tìm thấy camera"*: máy không có webcam hoặc webcam chưa nhận. *"…ứng dụng khác sử dụng"*: Zalo, Teams, Zoom… đang giữ camera. *"Không mở được camera (tên lỗi)"*: lỗi khác của trình duyệt | Dùng HTTPS với chứng chỉ được tin cậy. Cho phép camera: biểu tượng bên trái thanh địa chỉ → *Camera* → *Cho phép*, tải lại trang; Windows: *Settings → Privacy & security → Camera*, bật cả *Let desktop apps access your camera*. Kiểm tra webcam trong *Device Manager → Cameras*. Đóng ứng dụng đang dùng camera |
| Kiosk ghi *"Camera của kiosk chưa dùng được (…)"* | Trình duyệt kiosk chưa được cho phép dùng camera, không thấy camera, hoặc camera đang bị ứng dụng khác dùng | Cho phép camera cho địa chỉ phần mềm (mục 9b, *Phần cứng* bước 3); người bệnh vẫn dùng máy quét |
| Kiosk có webcam nhưng màn hình **Quét CCCD** chỉ hiện hướng dẫn máy quét | Trang không chạy HTTPS, hoặc trình duyệt đang chặn camera cho trang (đã có người chọn *Chặn*, hoặc chính sách trình duyệt) | Mở kiosk bằng địa chỉ `https://`; bỏ chặn: biểu tượng bên trái thanh địa chỉ → *Camera* → *Cho phép*, hoặc đặt `VideoCaptureAllowedUrls` (mục 9b) để không bị hỏi lại |
| PDF không hiển thị trong ứng dụng (trang trắng) | Reverse proxy ghi đè CSP, thiếu `frame-src blob:` | Dùng CSP như `deploy/iis/web.frontend.config` / `frontend/nginx.conf` |
| 429 "Bạn thao tác quá nhiều lần" | Vượt giới hạn đăng nhập (10 lần/phút/IP), xuất dữ liệu (10 lần/phút/người) hoặc mở mã QR ký trên điện thoại (30 lần/phút/IP) | Chờ 1 phút; nếu nhiều máy chung IP qua NAT, tăng `RateLimiting__AuthPermitLimit` (hoặc `RateLimiting__RemoteSigningPermitLimit` cho Wi-Fi người bệnh) |
| Nhật ký ghi IP là `127.0.0.1` | Reverse proxy không gửi `X-Forwarded-For` hoặc `ReverseProxy__TrustAllProxies=false` | Cấu hình proxy gửi header và bật biến trên (chỉ khi API không mở cổng ra ngoài) |
