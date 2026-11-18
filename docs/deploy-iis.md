# Triển khai BEKP trên IIS + SQL Server

Mô hình: **một site IIS** chạy ASP.NET Core (in-process). Site này phục vụ API `/api`, realtime `/hubs/dashboard` và giao diện React (thư mục `wwwroot`). Database riêng `BEKP` nằm trên SQL Server dùng chung với app Giấy đồng thuận.

## 1. Chuẩn bị máy chủ (một lần)

1. Windows Server có IIS 10.
2. Bật tính năng **IIS → Application Development → WebSocket Protocol** (bắt buộc cho realtime SignalR).
3. Cài **ASP.NET Core 10.0 Hosting Bundle**, sau đó chạy `iisreset`.
4. Chứng chỉ HTTPS cho tên miền nội bộ (ví dụ `bekp.benhvien.local`).

## 2. Database (DBA)

```sql
CREATE DATABASE BEKP;
GO
-- Chạy script idempotent trong repo:
-- database/migrations/001_initial_inpatient_operations.sql
```

Khuyến nghị dùng **Windows Authentication** bằng danh tính Application Pool, để không lưu mật khẩu trong cấu hình:

```sql
CREATE LOGIN [IIS APPPOOL\BEKP] FROM WINDOWS;   -- nếu SQL Server cùng máy; khác máy: dùng tài khoản dịch vụ domain
USE BEKP;
CREATE USER [IIS APPPOOL\BEKP] FOR LOGIN [IIS APPPOOL\BEKP];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\BEKP];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\BEKP];
```

Nếu SQL Server nằm trên máy khác, đặt Application Pool chạy bằng một **tài khoản dịch vụ domain** (gMSA được khuyến nghị) và cấp quyền cho tài khoản đó.

## 3. Đóng gói

Trên máy build (cần .NET SDK 10 và Node.js ≥ 22 với npm ≥ 11):

```powershell
# Site riêng (https://bekp.benhvien.local/)
./scripts/publish-iis.ps1 -Output C:\deploy\bekp

# Hoặc sub-application (https://intranet.benhvien.local/bekp/)
./scripts/publish-iis.ps1 -Output C:\deploy\bekp -BasePath /bekp/
```

Thư mục kết quả chứa `BEKP.Api.dll`, `web.config`, `wwwroot/` (giao diện) và `logs/`.

## 4. Tạo site / Application Pool

```powershell
Import-Module WebAdministration
New-WebAppPool -Name "BEKP"
Set-ItemProperty IIS:\AppPools\BEKP -Name managedRuntimeVersion -Value ""      # No Managed Code
Set-ItemProperty IIS:\AppPools\BEKP -Name startMode -Value AlwaysRunning
Set-ItemProperty IIS:\AppPools\BEKP -Name processModel.idleTimeout -Value "00:00:00"   # giữ kết nối realtime

# Site riêng:
New-Website -Name "BEKP" -PhysicalPath "D:\sites\bekp" -ApplicationPool "BEKP" -Port 443 -Ssl -HostHeader "bekp.benhvien.local"
# hoặc sub-application trong site có sẵn:
# New-WebApplication -Site "Default Web Site" -Name "bekp" -PhysicalPath "D:\sites\bekp" -ApplicationPool "BEKP"
```

Sao chép nội dung `C:\deploy\bekp` vào `D:\sites\bekp`. Cấp quyền đọc cho `IIS APPPOOL\BEKP`, và quyền ghi cho thư mục `logs`.

## 5. Cấu hình (không ghi bí mật vào web.config)

Biến môi trường được lưu trong `applicationHost.config` của máy chủ, nên các lần deploy sau không ghi đè:

```powershell
$appcmd = "$env:windir\system32\inetsrv\appcmd.exe"
$site = "BEKP"   # hoặc "Default Web Site/bekp"
& $appcmd set config $site -section:system.webServer/aspNetCore /+"environmentVariables.[name='ConnectionStrings__Bekp',value='Server=SQLSRV01;Database=BEKP;Integrated Security=true;Encrypt=true']" /commit:apphost
& $appcmd set config $site -section:system.webServer/aspNetCore /+"environmentVariables.[name='Authentication__Authority',value='https://sso.benhvien.local/realms/bv']" /commit:apphost
& $appcmd set config $site -section:system.webServer/aspNetCore /+"environmentVariables.[name='Authentication__Audience',value='bekp-api']" /commit:apphost
```

Tùy chọn (ví dụ mục tiêu thời gian theo quy định đã được Phòng QLCL xác nhận):

```powershell
& $appcmd set config $site -section:system.webServer/aspNetCore /+"environmentVariables.[name='TimeStandards__Standards__BloodIssueEmergency__TargetMinutes',value='30']" /commit:apphost
& $appcmd set config $site -section:system.webServer/aspNetCore /+"environmentVariables.[name='TimeStandards__Standards__BloodIssueEmergency__Reference',value='<số hiệu văn bản>']" /commit:apphost
& $appcmd set config $site -section:system.webServer/aspNetCore /+"environmentVariables.[name='TimeStandards__Standards__BloodIssueEmergency__ReferenceConfirmed',value='true']" /commit:apphost
```

`web.config` mặc định đặt `ASPNETCORE_ENVIRONMENT=Production`. Ở môi trường này, ứng dụng **từ chối** tài khoản dev, seeder và bộ mô phỏng. Muốn dựng **site demo**, đặt `ASPNETCORE_ENVIRONMENT=Staging` cùng `Database__SeedSyntheticData=true` và `Simulation__Enabled=true`.

## 6. Kiểm tra sau triển khai

- `https://bekp.benhvien.local/health` → `Healthy`.
- Mở `/`: biểu tượng trạng thái ở góc trên phải hiện **Trực tuyến**. Nếu hiện "Mất kết nối", kiểm tra lại tính năng WebSocket Protocol.
- Gọi `/api/dashboard/overview` khi chưa có token → `401`.

## 7. Cập nhật phiên bản

1. Chạy lại `publish-iis.ps1`.
2. Đặt file `app_offline.htm` vào thư mục site (IIS sẽ dừng ứng dụng).
3. Sao chép các file mới, rồi xóa `app_offline.htm`.
4. Nếu có migration mới, DBA chạy script trong `database/migrations/` trước.

## Ghi chú

- Realtime dùng WebSocket. Nếu có proxy hoặc tường lửa đứng giữa, phải cho phép header `Upgrade` tới đường dẫn `/hubs/`.
- Log lỗi khởi động: tạm đặt `stdoutLogEnabled="true"` trong `web.config` và xem thư mục `logs/` (nhớ tắt lại sau khi xử lý).
- Ứng dụng chạy in-process nên mỗi Application Pool chỉ host **một** ứng dụng ASP.NET Core.
