<#
.SYNOPSIS
  Cài một gói triển khai IIS lên môi trường UAT (kiểm thử) hoặc Prod (chạy thật) của phần mềm Phiếu đồng thuận.

.DESCRIPTION
  Quyết định 50: cùng một gói (cùng mã SHA256) được cài lên UAT trước, kiểm thử theo phân công (quyết định 49),
  đạt thì cài đúng gói đó lên Prod. Script làm theo thứ tự:
    1. Kiểm tra mã SHA256 của file gói (mã ghi ở trang GitHub Actions); sai thì dừng.
    2. Prod: gói phải đã cài thành công lên UAT (theo nhật ký cài đặt), trừ khi có -BoQuaKiemTraUAT.
    3. Kiểm tra hai môi trường tách biệt: UAT chạy Staging và có nhãn App__EnvironmentLabel; Prod chạy Production, không
       có nhãn; hai môi trường không dùng chung CSDL, khóa hay thư mục PDF; web.config của API không ghi đè sang môi
       trường khác (ASPNETCORE_ENVIRONMENT trong web.config được ưu tiên hơn biến của Application Pool).
    4. Hỏi xác nhận (gõ đúng tên môi trường), hỏi mật khẩu tài khoản migrator (không hiện, không ghi ra file).
    5. Sao lưu CSDL (COPY_ONLY, vào thư mục sao lưu mặc định của SQL Server) và các thư mục ứng dụng đang chạy.
    6. Dừng API (giao diện vẫn chạy: người dùng thấy màn hình "Hệ thống đang bảo trì" vài phút, quyết định 53), cập nhật lược
       đồ CSDL (efbundle.exe), chép file mới (giữ web.config và thư mục logs; giao diện chỉ dừng vài giây lúc chép file giao
       diện), chạy lại.
    7. Kiểm tra /health/ready, ghi nhật ký cài đặt (lich-su-cai-dat.csv).
  Không chứa bí mật. Chạy trong Windows PowerShell mở bằng "Run as Administrator" trên máy chủ IIS.
  Hướng dẫn đầy đủ: docs/trien-khai.md, mục 5.10.

.EXAMPLE
  .\cap-nhat-moi-truong.ps1 -MoiTruong UAT -Goi C:\Setup\goi-trien-khai-iis-1a2b3c4.zip -Sha256 <64 ký tự>

.EXAMPLE
  .\cap-nhat-moi-truong.ps1 -MoiTruong Prod -Goi C:\Setup\goi-trien-khai-iis-1a2b3c4.zip -Sha256 <cùng mã đã cài UAT>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('UAT', 'Prod')][string]$MoiTruong,
    [Parameter(Mandatory = $true)][string]$Goi,
    [Parameter(Mandatory = $true)][string]$Sha256,
    # Chỉ dùng khi sửa lỗi khẩn cấp trên Prod mà chưa kịp cài UAT; ghi lý do vào biên bản.
    [switch]$BoQuaKiemTraUAT,
    [string]$ThuMucGoc = 'C:\Apps',
    [string]$ThuMucSaoLuu = 'C:\Backup\Consent',
    [string]$ThuMucGiaiNen = 'C:\Setup'
)

$ErrorActionPreference = 'Stop'

# Cấu hình hai môi trường. Sửa ở đây nếu bệnh viện đặt tên thư mục, Application Pool, cổng khác (docs/trien-khai.md, mục 5.10).
$CauHinh = @{
    Prod = @{
        ThuMuc        = $ThuMucGoc
        PoolApi       = 'ConsentApi'
        PoolWeb       = 'ConsentWeb'
        PoolHis       = 'PatientInfoApi'
        SucKhoe       = 'http://127.0.0.1:5080/health/ready'
        CongApi       = 5080
        Migrator      = 'consent_migrator'
        MoiTruongAsp  = 'Production'
    }
    UAT  = @{
        ThuMuc        = (Join-Path $ThuMucGoc 'UAT')
        PoolApi       = 'ConsentApi-UAT'
        PoolWeb       = 'ConsentWeb-UAT'
        PoolHis       = 'PatientInfoApi-UAT'
        SucKhoe       = 'http://127.0.0.1:5090/health/ready'
        CongApi       = 5090
        Migrator      = 'consent_migrator_uat'
        MoiTruongAsp  = 'Staging'
    }
}

# Các biến môi trường không được dùng chung giữa UAT và Prod (khóa, thư mục PDF).
$BienRieng = @('Jwt__SigningKey', 'DataProtection__FieldEncryptionKey', 'DataProtection__BlindIndexKey', 'Storage__DocumentsPath')

function Write-Buoc([string]$noiDung) {
    Write-Host ''
    Write-Host "== $noiDung" -ForegroundColor Cyan
}

function Test-QuanTri {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Mã SHA256 viết hoa, đúng 64 ký tự 0-9A-F; sai định dạng thì báo lỗi.
function Get-MaSha256([string]$ma) {
    $chuan = ($ma -replace '\s', '').ToUpperInvariant()
    if ($chuan -notmatch '^[0-9A-F]{64}$') {
        throw 'Mã SHA256 phải gồm đúng 64 ký tự 0-9, A-F (chép từ dòng "SHA256 digest of uploaded artifact zip is ..." trên GitHub Actions).'
    }
    return $chuan
}

# Máy chủ và tên CSDL trong chuỗi kết nối (không trả mật khẩu).
function Get-ThongTinKetNoi([string]$ketNoi) {
    if ([string]::IsNullOrWhiteSpace($ketNoi)) {
        throw 'Application Pool chưa có biến ConnectionStrings__Default.'
    }
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder.set_ConnectionString($ketNoi)
    $mayChu = $null
    foreach ($khoa in @('Server', 'Data Source', 'Address', 'Addr', 'Network Address')) {
        if ($builder.ContainsKey($khoa)) { $mayChu = [string]$builder[$khoa]; break }
    }
    $csdl = $null
    foreach ($khoa in @('Database', 'Initial Catalog')) {
        if ($builder.ContainsKey($khoa)) { $csdl = [string]$builder[$khoa]; break }
    }
    if (-not $mayChu -or -not $csdl) {
        throw 'Chuỗi kết nối ConnectionStrings__Default thiếu máy chủ (Server) hoặc tên CSDL (Database).'
    }
    return @{ MayChu = $mayChu; Csdl = $csdl }
}

# Chuỗi kết nối cho tài khoản migrator: cùng máy chủ, cùng CSDL với ứng dụng, chỉ thay tài khoản và mật khẩu.
function New-KetNoiMigrator([string]$ketNoiUngDung, [string]$taiKhoan, [string]$matKhau) {
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder.set_ConnectionString($ketNoiUngDung)
    foreach ($khoa in @('User Id', 'UID', 'User', 'Username', 'Password', 'PWD', 'Integrated Security', 'Trusted_Connection')) {
        if ($builder.ContainsKey($khoa)) { [void]$builder.Remove($khoa) }
    }
    $builder['User Id'] = $taiKhoan
    $builder['Password'] = $matKhau
    return $builder.ConnectionString
}

# Gói đã cài thành công lên UAT chưa (theo nhật ký cài đặt).
function Test-DaQuaUat([string]$nhatKy, [string]$sha) {
    if (-not (Test-Path -LiteralPath $nhatKy)) { return $false }
    $dong = @(Import-Csv -LiteralPath $nhatKy -Encoding UTF8 | Where-Object { $_.MoiTruong -eq 'UAT' -and $_.Sha256 -eq $sha -and $_.KetQua -eq 'ThanhCong' })
    return $dong.Count -gt 0
}

function Add-NhatKy([string]$nhatKy, [string]$moiTruong, [string]$sha, [string]$maCommit, [string]$ketQua, [string]$ghiChu) {
    $dong = [pscustomobject]@{
        ThoiGian  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        MoiTruong = $moiTruong
        Sha256    = $sha
        MaCommit  = $maCommit
        NguoiCai  = "$env:USERDOMAIN\$env:USERNAME"
        KetQua    = $ketQua
        GhiChu    = $ghiChu
    }
    $thuMuc = Split-Path -Parent $nhatKy
    if ($thuMuc -and -not (Test-Path -LiteralPath $thuMuc)) { New-Item -ItemType Directory -Path $thuMuc | Out-Null }
    $dong | Export-Csv -LiteralPath $nhatKy -Append -NoTypeInformation -Encoding UTF8
}

# Kiểm tra biến môi trường của Application Pool API: đúng loại môi trường, có/không có nhãn, không dùng chung với môi trường kia.
# Trả danh sách lỗi (rỗng là đạt). $bienKhac: biến của Application Pool API môi trường kia, $null nếu chưa có.
function Get-LoiTachBiet([string]$moiTruong, [hashtable]$bien, $bienKhac, [string]$moiTruongAsp) {
    $loi = New-Object System.Collections.Generic.List[string]
    $asp = [string]$bien['ASPNETCORE_ENVIRONMENT']
    if ($asp -ne $moiTruongAsp) {
        $loi.Add("ASPNETCORE_ENVIRONMENT của $moiTruong phải là $moiTruongAsp (đang là '$asp').")
    }
    $nhan = [string]$bien['App__EnvironmentLabel']
    if ($moiTruong -eq 'UAT' -and [string]::IsNullOrWhiteSpace($nhan)) {
        $loi.Add('UAT phải có App__EnvironmentLabel (ví dụ UAT) để người dùng thấy dải báo môi trường kiểm thử.')
    }
    if ($moiTruong -eq 'Prod' -and -not [string]::IsNullOrWhiteSpace($nhan)) {
        $loi.Add('Prod không được có App__EnvironmentLabel (dải báo môi trường kiểm thử sẽ hiện cho người dùng thật).')
    }
    foreach ($ten in $BienRieng) {
        if ([string]::IsNullOrWhiteSpace([string]$bien[$ten])) {
            $loi.Add("Thiếu biến $ten.")
        }
    }
    if ($null -ne $bienKhac) {
        foreach ($ten in $BienRieng) {
            $giaTri = [string]$bien[$ten]
            if (-not [string]::IsNullOrWhiteSpace($giaTri) -and $giaTri -eq [string]$bienKhac[$ten]) {
                $loi.Add("UAT và Prod dùng chung $ten. Mỗi môi trường phải có giá trị riêng.")
            }
        }
        if ($bien['ConnectionStrings__Default'] -and $bienKhac['ConnectionStrings__Default']) {
            $mot = Get-ThongTinKetNoi $bien['ConnectionStrings__Default']
            $hai = Get-ThongTinKetNoi $bienKhac['ConnectionStrings__Default']
            if ($mot.MayChu -eq $hai.MayChu -and $mot.Csdl -eq $hai.Csdl) {
                $loi.Add("UAT và Prod cùng dùng CSDL $($mot.Csdl) trên $($mot.MayChu).")
            }
        }
    }
    return , $loi.ToArray()
}

# Biến môi trường của một Application Pool (đọc bằng thư viện quản trị IIS), $null nếu chưa có pool đó.
function Get-BienPool([string]$pool) {
    $duongDan = Join-Path $env:SystemRoot 'System32\inetsrv\Microsoft.Web.Administration.dll'
    Add-Type -Path $duongDan
    $quanLy = New-Object Microsoft.Web.Administration.ServerManager
    try {
        $phan = $quanLy.GetApplicationHostConfiguration().GetSection('system.applicationHost/applicationPools')
        $phanTu = $null
        foreach ($p in $phan.GetCollection()) {
            if ([string]$p['name'] -eq $pool) { $phanTu = $p; break }
        }
        if ($null -eq $phanTu) { return $null }
        $bien = @{}
        foreach ($b in $phanTu.GetCollection('environmentVariables')) {
            $bien[[string]$b['name']] = [string]$b['value']
        }
        return $bien
    }
    finally {
        $quanLy.Dispose()
    }
}

function Stop-PoolCho([string]$pool) {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { return }
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool }
    for ($i = 0; $i -lt 60; $i++) {
        if ((Get-WebAppPoolState -Name $pool).Value -eq 'Stopped') { return }
        Start-Sleep -Seconds 1
    }
    throw "Application Pool $pool không dừng sau 60 giây."
}

function Start-PoolNeuCo([string]$pool) {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { return }
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool -Name $pool }
}

# robocopy: mã 0-7 là thành công, từ 8 trở lên là lỗi.
function Copy-ThuMuc([string]$nguon, [string]$dich, [string[]]$boQuaFile = @(), [string[]]$boQuaThuMuc = @()) {
    $thamSo = @($nguon, $dich, '/MIR', '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($boQuaFile.Count -gt 0) { $thamSo += '/XF'; $thamSo += $boQuaFile }
    if ($boQuaThuMuc.Count -gt 0) { $thamSo += '/XD'; $thamSo += $boQuaThuMuc }
    & robocopy.exe @thamSo | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Chép $nguon sang $dich lỗi (robocopy mã $LASTEXITCODE)." }
}

# Cài lần đầu (thư mục chưa có web.config): lấy web.config mẫu trong gói; web.config của giao diện chuyển tiếp /api tới cổng API
# của môi trường (gói ghi sẵn cổng 5080 của Prod); web.config của API ghi ASPNETCORE_ENVIRONMENT của môi trường (gói ghi sẵn
# Production, giá trị này ghi đè biến của Application Pool). Đã có web.config thì giữ nguyên (cấu hình riêng của máy chủ).
function Copy-WebConfigLanDau([string]$nguon, [string]$dich, [int]$congApi, [string]$moiTruongAsp) {
    $fileDich = Join-Path $dich 'web.config'
    if (Test-Path -LiteralPath $fileDich) { return $false }
    $noiDung = [System.IO.File]::ReadAllText((Join-Path $nguon 'web.config'))
    $noiDung = $noiDung.Replace('http://127.0.0.1:5080/', "http://127.0.0.1:$congApi/")
    $noiDung = [regex]::Replace($noiDung, '(<environmentVariable\s+name="ASPNETCORE_ENVIRONMENT"\s+value=")[^"]*(")', ('${1}' + $moiTruongAsp + '${2}'))
    [System.IO.File]::WriteAllText($fileDich, $noiDung, (New-Object System.Text.UTF8Encoding($false)))
    return $true
}

# ASPNETCORE_ENVIRONMENT đặt trong web.config của API ghi đè biến của Application Pool (ASP.NET Core Module): UAT có web.config
# chép từ Prod sẽ chạy như Production. Trả câu báo lỗi khi web.config đặt môi trường khác; $null khi đúng, không đặt hoặc chưa có file.
function Get-LoiWebConfig([string]$thuMuc, [string]$moiTruongAsp) {
    $file = Join-Path $thuMuc 'web.config'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    $xml = New-Object System.Xml.XmlDocument
    $xml.XmlResolver = $null
    $xml.Load($file)
    $nut = $xml.SelectSingleNode("//aspNetCore/environmentVariables/environmentVariable[@name='ASPNETCORE_ENVIRONMENT']")
    if ($null -eq $nut) { return $null }
    $giaTri = $nut.GetAttribute('value')
    if ($giaTri -eq $moiTruongAsp) { return $null }
    return "web.config trong $thuMuc đặt ASPNETCORE_ENVIRONMENT=$giaTri, ghi đè biến của Application Pool (ứng dụng sẽ chạy như $giaTri). Sửa thành $moiTruongAsp."
}

function Invoke-SaoLuuCsdl([string]$ketNoi, [string]$csdl, [string]$tenFile) {
    $lenh = "BACKUP DATABASE [$($csdl -replace '\]', ']]')] TO DISK = N'$($tenFile -replace "'", "''")' WITH COPY_ONLY, CHECKSUM, INIT"
    $sql = New-Object System.Data.SqlClient.SqlConnection $ketNoi
    try {
        $sql.Open()
        $cmd = $sql.CreateCommand()
        $cmd.CommandText = $lenh
        $cmd.CommandTimeout = 1800
        [void]$cmd.ExecuteNonQuery()
    }
    finally {
        $sql.Dispose()
    }
}

function Wait-SucKhoe([string]$url) {
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $phanHoi = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
            if ($phanHoi.StatusCode -eq 200 -and $phanHoi.Content -match 'Healthy') { return }
        }
        catch {
            # Chưa sẵn sàng (đang khởi động hoặc 503): chờ rồi thử lại.
            $null = $_
        }
        Start-Sleep -Seconds 2
    }
    throw "Ứng dụng chưa sẵn sàng sau 60 giây: $url"
}

function Invoke-CapNhat {
    $cfg = $CauHinh[$MoiTruong]
    $khac = if ($MoiTruong -eq 'UAT') { 'Prod' } else { 'UAT' }
    $nhatKy = Join-Path $ThuMucGoc 'lich-su-cai-dat.csv'

    if (-not (Test-QuanTri)) { throw 'Mở Windows PowerShell bằng "Run as Administrator" rồi chạy lại.' }
    Import-Module WebAdministration

    Write-Buoc "1. Kiểm tra mã SHA256 của gói"
    $sha = Get-MaSha256 $Sha256
    if (-not (Test-Path -LiteralPath $Goi)) { throw "Không thấy file gói: $Goi" }
    $thuc = (Get-FileHash -LiteralPath $Goi -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($thuc -ne $sha) { throw "Mã SHA256 của file ($thuc) khác mã đã nhập. Không cài; tải lại gói từ GitHub Actions." }
    Write-Host "Khớp: $sha"

    if ($MoiTruong -eq 'Prod') {
        if (Test-DaQuaUat $nhatKy $sha) {
            Write-Host 'Gói này đã cài thành công lên UAT.'
        }
        elseif ($BoQuaKiemTraUAT) {
            Write-Warning 'Bỏ qua kiểm tra UAT (-BoQuaKiemTraUAT): chỉ dùng khi sửa lỗi khẩn cấp, ghi lý do vào biên bản.'
        }
        else {
            throw 'Gói này chưa cài thành công lên UAT. Cài lên UAT và kiểm thử trước (docs/trien-khai.md, mục 5.10).'
        }
    }

    Write-Buoc "2. Giải nén gói"
    $giaiNen = Join-Path $ThuMucGiaiNen ('goi-' + $sha.Substring(0, 12).ToLowerInvariant())
    if (-not (Test-Path -LiteralPath $giaiNen)) {
        Expand-Archive -LiteralPath $Goi -DestinationPath $giaiNen
    }
    foreach ($phan in @('ConsentApi', 'ConsentWeb', 'efbundle.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $giaiNen $phan))) { throw "Gói thiếu $phan (thư mục $giaiNen)." }
    }
    $maCommit = ''
    $phienBan = Join-Path $giaiNen 'PHIEN-BAN.txt'
    if (Test-Path -LiteralPath $phienBan) {
        $dongCommit = Get-Content -LiteralPath $phienBan | Where-Object { $_ -like 'Ma commit:*' } | Select-Object -First 1
        if ($dongCommit) { $maCommit = ($dongCommit -replace '^Ma commit:\s*', '').Trim() }
    }
    Write-Host "Thư mục gói: $giaiNen  (mã commit: $maCommit)"

    Write-Buoc "3. Kiểm tra cấu hình môi trường $MoiTruong"
    $bien = Get-BienPool $cfg.PoolApi
    if ($null -eq $bien) { throw "Chưa có Application Pool $($cfg.PoolApi). Tạo môi trường $MoiTruong trước (docs/trien-khai.md, mục 5.10)." }
    $bienKhac = Get-BienPool $CauHinh[$khac].PoolApi
    $thuMucApi = Join-Path $cfg.ThuMuc 'ConsentApi'
    $loi = Get-LoiTachBiet $MoiTruong $bien $bienKhac $cfg.MoiTruongAsp
    $loiWebConfig = Get-LoiWebConfig $thuMucApi $cfg.MoiTruongAsp
    if ($loiWebConfig) { $loi += $loiWebConfig }
    if ($loi.Count -gt 0) {
        $loi | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        throw "Cấu hình môi trường $MoiTruong chưa đúng; sửa biến môi trường của Application Pool $($cfg.PoolApi) hoặc web.config như trên rồi chạy lại."
    }
    $ketNoi = Get-ThongTinKetNoi $bien['ConnectionStrings__Default']
    $thuMucWeb = Join-Path $cfg.ThuMuc 'ConsentWeb'
    $thuMucHis = Join-Path $cfg.ThuMuc 'PatientInfoApi'
    $coHis = (Test-Path -LiteralPath $thuMucHis) -and (Test-Path "IIS:\AppPools\$($cfg.PoolHis)")
    Write-Host "Môi trường : $MoiTruong ($($cfg.MoiTruongAsp))"
    Write-Host "CSDL       : $($ketNoi.Csdl) trên $($ketNoi.MayChu)"
    Write-Host "Thư mục    : $thuMucApi, $thuMucWeb$(if ($coHis) { ", $thuMucHis" })"
    Write-Host "Kiểm tra   : $($cfg.SucKhoe)"

    $tenXacNhan = $MoiTruong.ToUpperInvariant()
    if ($MoiTruong -eq 'Prod') {
        Write-Host 'Chỉ cài lên Prod khi UAT đã kiểm thử xong và đạt (xem trang Phân công kiểm thử trên UAT).' -ForegroundColor Yellow
    }
    $traLoi = Read-Host "Gõ $tenXacNhan để cài gói lên môi trường $MoiTruong (gõ khác để hủy)"
    if ($traLoi -cne $tenXacNhan) { throw 'Đã hủy, chưa thay đổi gì.' }

    $matKhauAn = Read-Host "Mật khẩu tài khoản $($cfg.Migrator) (không hiện khi gõ)" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($matKhauAn)
    try {
        $ketNoiMigrator = New-KetNoiMigrator $bien['ConnectionStrings__Default'] $cfg.Migrator ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr))
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }

    $thoiDiem = Get-Date -Format 'yyyyMMdd-HHmmss'
    $saoLuu = Join-Path $ThuMucSaoLuu (Join-Path $MoiTruong "truoc-$($sha.Substring(0, 12).ToLowerInvariant())-$thoiDiem")
    try {
        Write-Buoc "4. Sao lưu CSDL và thư mục ứng dụng"
        $fileCsdl = "$($ketNoi.Csdl)_truoc-cap-nhat_$thoiDiem.bak"
        Invoke-SaoLuuCsdl $ketNoiMigrator $ketNoi.Csdl $fileCsdl
        Write-Host "CSDL: $fileCsdl (thư mục sao lưu mặc định của SQL Server, sao lưu COPY_ONLY)"
        New-Item -ItemType Directory -Path $saoLuu -Force | Out-Null
        # Cài lần đầu thì chưa có thư mục để sao lưu.
        if (Test-Path -LiteralPath $thuMucApi) { Copy-ThuMuc $thuMucApi (Join-Path $saoLuu 'ConsentApi') }
        if (Test-Path -LiteralPath $thuMucWeb) { Copy-ThuMuc $thuMucWeb (Join-Path $saoLuu 'ConsentWeb') }
        if ($coHis) { Copy-ThuMuc $thuMucHis (Join-Path $saoLuu 'PatientInfoApi') }
        Write-Host "Thư mục: $saoLuu"

        # Giao diện vẫn chạy: người dùng thấy màn hình "Hệ thống đang bảo trì" thay vì trang lỗi của IIS (quyết định 53).
        Write-Buoc "5. Dừng API (người dùng thấy màn hình bảo trì), cập nhật lược đồ CSDL"
        Stop-PoolCho $cfg.PoolApi
        if ($coHis) { Stop-PoolCho $cfg.PoolHis }
        # efbundle đọc chuỗi kết nối từ biến môi trường của tiến trình này (không đưa mật khẩu lên dòng lệnh).
        $env:ConnectionStrings__Default = $ketNoiMigrator
        try {
            & (Join-Path $giaiNen 'efbundle.exe')
            if ($LASTEXITCODE -ne 0) { throw "efbundle.exe lỗi (mã $LASTEXITCODE). API đang dừng (người dùng thấy màn hình bảo trì); xem mục khôi phục ở docs/trien-khai.md, mục 5.10." }
        }
        finally {
            Remove-Item Env:\ConnectionStrings__Default -ErrorAction SilentlyContinue
        }

        Write-Buoc "6. Chép file mới (giữ web.config) và khởi động lại"
        Copy-ThuMuc (Join-Path $giaiNen 'ConsentApi') $thuMucApi @('web.config') @('logs')
        if (Copy-WebConfigLanDau (Join-Path $giaiNen 'ConsentApi') $thuMucApi $cfg.CongApi $cfg.MoiTruongAsp) {
            Write-Host "Cài lần đầu: đã chép web.config mẫu vào $thuMucApi (ASPNETCORE_ENVIRONMENT=$($cfg.MoiTruongAsp))"
        }
        # Giao diện chỉ dừng trong lúc chép file giao diện (vài giây).
        Stop-PoolCho $cfg.PoolWeb
        Copy-ThuMuc (Join-Path $giaiNen 'ConsentWeb') $thuMucWeb @('web.config')
        if (Copy-WebConfigLanDau (Join-Path $giaiNen 'ConsentWeb') $thuMucWeb $cfg.CongApi $cfg.MoiTruongAsp) {
            Write-Host "Cài lần đầu: đã chép web.config mẫu vào $thuMucWeb (chuyển tiếp /api tới cổng $($cfg.CongApi))"
        }
        Start-PoolNeuCo $cfg.PoolWeb
        if ($coHis -and (Test-Path -LiteralPath (Join-Path $giaiNen 'PatientInfoApi'))) {
            Copy-ThuMuc (Join-Path $giaiNen 'PatientInfoApi') $thuMucHis @('web.config') @('logs')
            Start-PoolNeuCo $cfg.PoolHis
        }
        Start-PoolNeuCo $cfg.PoolApi

        Write-Buoc "7. Kiểm tra ứng dụng"
        Wait-SucKhoe $cfg.SucKhoe
        Add-NhatKy $nhatKy $MoiTruong $sha $maCommit 'ThanhCong' $(if ($BoQuaKiemTraUAT) { 'Bo qua kiem tra UAT' } else { '' })
        Write-Host ''
        Write-Host "Đã cài gói $sha lên $MoiTruong. Đăng nhập thử và xem dải báo môi trường (UAT) hoặc không có dải (Prod)." -ForegroundColor Green
    }
    catch {
        Add-NhatKy $nhatKy $MoiTruong $sha $maCommit 'Loi' $_.Exception.Message
        Write-Host ''
        Write-Host "Lỗi: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Bản sao lưu thư mục: $saoLuu. Khôi phục theo docs/trien-khai.md, mục 5.10." -ForegroundColor Yellow
        throw
    }
    finally {
        $ketNoiMigrator = $null
    }
}

# Chạy khi gọi script; khi nạp bằng dot-source (kiểm thử các hàm) thì không chạy.
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-CapNhat
}
