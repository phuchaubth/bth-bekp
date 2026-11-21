<#
.SYNOPSIS
  Xóa dữ liệu chạy thử (phiếu đồng thuận, báo cáo điều dưỡng) của phần mềm Phiếu đồng thuận trước khi dùng thật.

.DESCRIPTION
  Quyết định 52. Script làm theo thứ tự:
    1. Đọc cấu hình Application Pool API của môi trường (CSDL, thư mục PDF đã ký), hỏi mật khẩu tài khoản migrator
       (không hiện, không ghi ra file).
    2. Đếm và in số dòng sẽ xóa, số file PDF (chưa thay đổi gì). Không có gì để xóa thì dừng.
    3. Hỏi xác nhận: gõ đúng XOA <tên CSDL>.
    4. Sao lưu CSDL (COPY_ONLY, vào thư mục sao lưu mặc định của SQL Server).
    5. Dừng ứng dụng; xóa trong một giao dịch (sql\06-xoa-du-lieu-chay-thu.sql): lỗi ở đâu thì CSDL giữ nguyên.
    6. Chuyển file PDF đã ký của các phiếu vừa xóa sang thư mục sao lưu (không xóa hẳn; logo, khung chữ ký giữ nguyên).
    7. Khởi động lại ứng dụng, kiểm tra /health/ready, ghi kết quả vào thư mục sao lưu (ket-qua.txt).
  XÓA: phiếu đồng thuận (mọi trạng thái) và chữ ký trên phiếu; liên kết ký qua mã QR; hồ sơ người bệnh và người đại diện
  (trừ khi có -GiuNguoiBenh); báo cáo điều dưỡng của khoa; số liệu HIS của báo cáo điều dưỡng.
  GIỮ: người dùng, vai trò, khoa/phòng, mẫu phiếu, cấu hình hệ thống, khoa báo cáo và số giường, ủy quyền báo cáo, phân
  công kiểm thử, lịch sử xuất dữ liệu, nhật ký hệ thống (ghi thêm một dòng "Xóa dữ liệu chạy thử").
  Không chứa bí mật. Chạy trong Windows PowerShell mở bằng "Run as Administrator" trên máy chủ IIS.
  Hướng dẫn đầy đủ: docs/trien-khai.md, mục 5.11.

.EXAMPLE
  .\xoa-du-lieu-chay-thu.ps1 -MoiTruong Prod

.EXAMPLE
  .\xoa-du-lieu-chay-thu.ps1 -MoiTruong Prod -DanhSoLaiTu1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('UAT', 'Prod')][string]$MoiTruong,
    # Giữ hồ sơ người bệnh và người đại diện (mặc định xóa cùng phiếu).
    [switch]$GiuNguoiBenh,
    # Phiếu lập sau khi xóa được đánh số lại từ PDT-<năm>-000001 (mặc định số phiếu chạy tiếp, không trùng số phiếu chạy thử).
    [switch]$DanhSoLaiTu1,
    [string]$ThuMucGoc = 'C:\Apps',
    [string]$ThuMucSaoLuu = 'C:\Backup\Consent'
)

$ErrorActionPreference = 'Stop'

# Giống cap-nhat-moi-truong.ps1. Sửa ở đây nếu bệnh viện đặt tên thư mục, Application Pool, cổng khác (docs/trien-khai.md, mục 5.10).
$CauHinh = @{
    Prod = @{
        ThuMuc   = $ThuMucGoc
        PoolApi  = 'ConsentApi'
        PoolWeb  = 'ConsentWeb'
        SucKhoe  = 'http://127.0.0.1:5080/health/ready'
        Migrator = 'consent_migrator'
    }
    UAT  = @{
        ThuMuc   = (Join-Path $ThuMucGoc 'UAT')
        PoolApi  = 'ConsentApi-UAT'
        PoolWeb  = 'ConsentWeb-UAT'
        SucKhoe  = 'http://127.0.0.1:5090/health/ready'
        Migrator = 'consent_migrator_uat'
    }
}

$FileSql = '06-xoa-du-lieu-chay-thu.sql'

# Khóa lưu trữ do ứng dụng đặt (EncryptedFileDocumentStore): năm/tháng/tên.pdf.enc. Khóa khác dạng thì không đụng tới.
# \z (không dùng $): $ của .NET còn khớp trước ký tự xuống dòng cuối chuỗi.
$MauKhoaPdf = '^\d{4}/\d{2}/[A-Za-z0-9-]+\.pdf\.enc\z'

# Tên dễ hiểu của từng bảng (bảng chưa có ở đây thì in tên bảng).
$TenBang = [ordered]@{
    ConsentForms                = 'Phiếu đồng thuận (mọi trạng thái)'
    ConsentSignatures           = 'Chữ ký trên phiếu'
    RemoteSigningLinks          = 'Liên kết ký qua mã QR'
    Patients                    = 'Hồ sơ người bệnh'
    LegalRepresentatives        = 'Người đại diện của người bệnh'
    NursingDailyReports         = 'Báo cáo điều dưỡng của khoa'
    NursingDailyStatistics      = 'Số liệu HIS của bảng tổng hợp điều dưỡng'
    NursingDepartmentStatistics = 'Số liệu HIS do khoa nhập khi HIS lỗi'
}

function Write-Buoc([string]$noiDung) {
    Write-Host ''
    Write-Host "== $noiDung" -ForegroundColor Cyan
}

function Test-QuanTri {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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

# Thư mục sao lưu mặc định của SQL Server (nơi có file .bak); $null nếu SQL Server không cho biết.
function Get-ThuMucSaoLuuSql([string]$ketNoi) {
    $sql = New-Object System.Data.SqlClient.SqlConnection $ketNoi
    try {
        $sql.Open()
        $cmd = $sql.CreateCommand()
        $cmd.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))"
        $giaTri = $cmd.ExecuteScalar()
        if ($giaTri -is [DBNull]) { return $null }
        return [string]$giaTri
    }
    finally {
        $sql.Dispose()
    }
}

# File SQL đi kèm: trong gói triển khai nằm ở sql\ cạnh script; trong mã nguồn nằm ở deploy\sql.
function Get-DuongDanFileSql([string]$thuMucScript) {
    $ungVien = @(
        (Join-Path (Join-Path $thuMucScript 'sql') $FileSql),
        (Join-Path (Join-Path (Split-Path -Parent $thuMucScript) 'sql') $FileSql)
    )
    foreach ($duongDan in $ungVien) {
        if (Test-Path -LiteralPath $duongDan -PathType Leaf) { return $duongDan }
    }
    throw "Không thấy file sql\$FileSql cạnh script. Giải nén đủ gói triển khai rồi chạy lại."
}

# Thư mục PDF đã ký (Storage__DocumentsPath); đường dẫn tương đối tính từ thư mục API (thư mục chạy của ứng dụng).
function Get-ThuMucPdf([string]$giaTri, [string]$thuMucApi) {
    if ([string]::IsNullOrWhiteSpace($giaTri)) {
        throw 'Application Pool chưa có biến Storage__DocumentsPath (thư mục PDF đã ký).'
    }
    if ([System.IO.Path]::IsPathRooted($giaTri)) { return [System.IO.Path]::GetFullPath($giaTri) }
    return [System.IO.Path]::GetFullPath((Join-Path $thuMucApi $giaTri))
}

# Đường dẫn file của một khóa lưu trữ trong thư mục gốc; $null khi khóa không đúng dạng (không bao giờ ra ngoài thư mục gốc).
function Get-DuongDanPdf([string]$goc, [string]$khoa) {
    if ($khoa -cnotmatch $MauKhoaPdf) { return $null }
    return Join-Path $goc ($khoa.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
}

# Chạy file SQL. Trả về Dem (số dòng từng bảng, trước khi xóa) và Khoa (khóa file PDF đã ký của các phiếu).
# $thucHien = $false: chỉ đếm, không thay đổi gì. $thucHien = $true: xóa trong một giao dịch (lỗi thì không xóa gì).
function Invoke-SqlXoa([string]$ketNoi, [string]$lenh, [bool]$thucHien, [bool]$giuNguoiBenh, [bool]$danhSoLai, [string]$nguoi) {
    $sql = New-Object System.Data.SqlClient.SqlConnection $ketNoi
    try {
        $sql.Open()
        $cmd = $sql.CreateCommand()
        $cmd.CommandText = $lenh
        $cmd.CommandTimeout = 3600
        ($cmd.Parameters.Add('@ThucHien', [System.Data.SqlDbType]::Bit)).Value = $thucHien
        ($cmd.Parameters.Add('@GiuNguoiBenh', [System.Data.SqlDbType]::Bit)).Value = $giuNguoiBenh
        ($cmd.Parameters.Add('@DanhSoLaiTu1', [System.Data.SqlDbType]::Bit)).Value = $danhSoLai
        ($cmd.Parameters.Add('@NguoiThucHien', [System.Data.SqlDbType]::NVarChar, 100)).Value = $nguoi
        $boDuLieu = New-Object System.Data.DataSet
        $doc = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
        [void]$doc.Fill($boDuLieu)
        if ($boDuLieu.Tables.Count -lt 2) { throw "File $FileSql không trả về đủ kết quả (số dòng, khóa file PDF)." }
        $dem = @(foreach ($dong in $boDuLieu.Tables[0].Rows) {
                [pscustomobject]@{
                    Bang    = [string]$dong['Bang']
                    SoDong  = [int]$dong['SoDong']
                    TuNgay  = $(if ($dong['TuNgay'] -is [DBNull]) { $null } else { [datetime]$dong['TuNgay'] })
                    DenNgay = $(if ($dong['DenNgay'] -is [DBNull]) { $null } else { [datetime]$dong['DenNgay'] })
                }
            })
        $khoa = @(foreach ($dong in $boDuLieu.Tables[1].Rows) { [string]$dong['SignedPdfKey'] })
        return [pscustomobject]@{ Dem = $dem; Khoa = $khoa }
    }
    finally {
        $sql.Dispose()
    }
}

# Số file PDF có trong thư mục, tổng dung lượng, số phiếu không thấy file, số khóa sai dạng.
function Get-ThongKePdf([string]$goc, [string[]]$danhSach) {
    $co = 0
    $khongCo = 0
    $saiDang = 0
    [long]$byte = 0
    foreach ($khoa in $danhSach) {
        $duongDan = Get-DuongDanPdf $goc $khoa
        if ($null -eq $duongDan) { $saiDang++; continue }
        if (Test-Path -LiteralPath $duongDan -PathType Leaf) {
            $co++
            $byte += (Get-Item -LiteralPath $duongDan).Length
        }
        else {
            $khongCo++
        }
    }
    return [pscustomobject]@{ Co = $co; KhongCo = $khongCo; SaiDang = $saiDang; Byte = $byte }
}

# Chuyển file PDF của các khóa sang thư mục đích, giữ cấu trúc năm\tháng. Không ghi đè file đã có ở đích.
function Move-FilePdf([string]$goc, [string[]]$danhSach, [string]$dich) {
    $daChuyen = 0
    $khongCo = 0
    $loi = New-Object System.Collections.Generic.List[string]
    foreach ($khoa in $danhSach) {
        $nguon = Get-DuongDanPdf $goc $khoa
        if ($null -eq $nguon) { $loi.Add("Khóa không đúng dạng, không đụng tới: $khoa"); continue }
        if (-not (Test-Path -LiteralPath $nguon -PathType Leaf)) { $khongCo++; continue }
        $fileDich = Get-DuongDanPdf $dich $khoa
        try {
            $thuMuc = Split-Path -Parent $fileDich
            if (-not (Test-Path -LiteralPath $thuMuc)) { New-Item -ItemType Directory -Path $thuMuc -Force | Out-Null }
            Move-Item -LiteralPath $nguon -Destination $fileDich
            $daChuyen++
        }
        catch {
            $loi.Add("${khoa}: $($_.Exception.Message)")
        }
    }
    return [pscustomobject]@{ DaChuyen = $daChuyen; KhongCo = $khongCo; Loi = $loi.ToArray() }
}

function Format-Ngay($ngay) {
    if ($null -eq $ngay) { return '' }
    return $ngay.ToString('dd/MM/yyyy')
}

# Các dòng mô tả số dòng từng loại dữ liệu và số file PDF (in ra màn hình và ghi vào ket-qua.txt).
function Get-DongDem($dem, $thongKePdf) {
    $dong = New-Object System.Collections.Generic.List[string]
    foreach ($d in $dem) {
        $ten = if ($TenBang.Contains($d.Bang)) { $TenBang[$d.Bang] } else { $d.Bang }
        $khoang = ''
        if ($d.SoDong -gt 0 -and $null -ne $d.TuNgay) { $khoang = "  (từ $(Format-Ngay $d.TuNgay) đến $(Format-Ngay $d.DenNgay))" }
        $dong.Add(('  {0,-45} {1,8}{2}' -f $ten, $d.SoDong, $khoang))
    }
    $mb = [math]::Round($thongKePdf.Byte / 1MB, 1)
    $dong.Add(('  {0,-45} {1,8}  ({2} MB, chuyển sang thư mục sao lưu)' -f 'File PDF phiếu đã ký', $thongKePdf.Co, $mb))
    return $dong.ToArray()
}

function Invoke-XoaDuLieu {
    $cfg = $CauHinh[$MoiTruong]
    if (-not (Test-QuanTri)) { throw 'Mở Windows PowerShell bằng "Run as Administrator" rồi chạy lại.' }
    Import-Module WebAdministration

    Write-Buoc "1. Đọc cấu hình môi trường $MoiTruong"
    $lenh = [System.IO.File]::ReadAllText((Get-DuongDanFileSql $PSScriptRoot), [System.Text.Encoding]::UTF8)
    $bien = Get-BienPool $cfg.PoolApi
    if ($null -eq $bien) { throw "Chưa có Application Pool $($cfg.PoolApi) của môi trường $MoiTruong." }
    $ketNoi = Get-ThongTinKetNoi $bien['ConnectionStrings__Default']
    $thuMucPdf = Get-ThuMucPdf ([string]$bien['Storage__DocumentsPath']) (Join-Path $cfg.ThuMuc 'ConsentApi')
    $tuyChon = @(
        $(if ($GiuNguoiBenh) { 'giữ hồ sơ người bệnh' } else { 'xóa cả hồ sơ người bệnh' }),
        $(if ($DanhSoLaiTu1) { 'phiếu mới đánh số lại từ 000001' } else { 'số phiếu chạy tiếp' })
    ) -join '; '
    Write-Host "Môi trường : $MoiTruong"
    Write-Host "CSDL       : $($ketNoi.Csdl) trên $($ketNoi.MayChu)"
    Write-Host "Thư mục PDF: $thuMucPdf"
    Write-Host "Tùy chọn   : $tuyChon"

    $matKhauAn = Read-Host "Mật khẩu tài khoản $($cfg.Migrator) (không hiện khi gõ)" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($matKhauAn)
    try {
        $ketNoiMigrator = New-KetNoiMigrator $bien['ConnectionStrings__Default'] $cfg.Migrator ([Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr))
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
    $nguoi = "$env:USERDOMAIN\$env:USERNAME"

    try {
        Write-Buoc '2. Đếm dữ liệu sẽ xóa (chưa thay đổi gì)'
        $xem = Invoke-SqlXoa $ketNoiMigrator $lenh $false $GiuNguoiBenh.IsPresent $DanhSoLaiTu1.IsPresent $nguoi
        $thongKe = Get-ThongKePdf $thuMucPdf $xem.Khoa
        foreach ($d in (Get-DongDem $xem.Dem $thongKe)) { Write-Host $d }
        if ($thongKe.KhongCo -gt 0) {
            Write-Host "  $($thongKe.KhongCo) phiếu ghi có PDF nhưng không thấy file trong thư mục PDF (bỏ qua)." -ForegroundColor Yellow
        }
        if ($thongKe.SaiDang -gt 0) {
            Write-Host "  $($thongKe.SaiDang) phiếu có khóa file không đúng dạng: không đụng tới file đó." -ForegroundColor Yellow
        }
        Write-Host 'Giữ nguyên: người dùng, vai trò, khoa/phòng, mẫu phiếu, cấu hình hệ thống (logo, khung chữ ký, kiosk), khoa báo cáo'
        Write-Host '  và số giường, ủy quyền báo cáo, phân công kiểm thử, lịch sử xuất dữ liệu, nhật ký hệ thống.'
        $tong = ($xem.Dem | Measure-Object -Property SoDong -Sum).Sum
        if ($tong -eq 0 -and $xem.Khoa.Count -eq 0 -and -not $DanhSoLaiTu1) {
            Write-Host ''
            Write-Host 'Không có dữ liệu chạy thử để xóa. Chưa thay đổi gì.' -ForegroundColor Green
            return
        }

        Write-Buoc '3. Xác nhận'
        Write-Host 'Dữ liệu trên sẽ bị xóa khỏi phần mềm; chỉ khôi phục được từ bản sao lưu tạo ở bước sau.' -ForegroundColor Yellow
        Write-Host 'Người dùng sẽ thấy trang bảo trì trong lúc xóa (thường dưới một phút).' -ForegroundColor Yellow
        $xacNhan = "XOA $($ketNoi.Csdl)"
        $traLoi = Read-Host "Gõ $xacNhan để xóa (gõ khác để hủy)"
        if ($traLoi -cne $xacNhan) { throw 'Đã hủy, chưa thay đổi gì.' }

        $thoiDiem = Get-Date -Format 'yyyyMMdd-HHmmss'
        $saoLuu = Join-Path $ThuMucSaoLuu (Join-Path $MoiTruong "xoa-du-lieu-chay-thu-$thoiDiem")
        $thuMucPdfSaoLuu = Join-Path $saoLuu 'pdf'
        New-Item -ItemType Directory -Path $saoLuu -Force | Out-Null

        Write-Buoc '4. Sao lưu CSDL'
        $fileBak = "$($ketNoi.Csdl)_truoc-xoa-du-lieu-chay-thu_$thoiDiem.bak"
        Invoke-SaoLuuCsdl $ketNoiMigrator $ketNoi.Csdl $fileBak
        $thuMucBak = Get-ThuMucSaoLuuSql $ketNoiMigrator
        $noiBak = if ($thuMucBak) { Join-Path $thuMucBak $fileBak } else { "$fileBak (thư mục sao lưu mặc định của SQL Server)" }
        Write-Host "CSDL: $noiBak (sao lưu COPY_ONLY)"

        $daDung = $false
        $ketQua = $null
        $chuyen = $null
        try {
            Write-Buoc '5. Dừng ứng dụng, xóa dữ liệu'
            $daDung = $true
            Stop-PoolCho $cfg.PoolWeb
            Stop-PoolCho $cfg.PoolApi
            $ketQua = Invoke-SqlXoa $ketNoiMigrator $lenh $true $GiuNguoiBenh.IsPresent $DanhSoLaiTu1.IsPresent $nguoi
            Write-Host 'Đã xóa trong CSDL (một giao dịch); nhật ký hệ thống ghi thêm dòng "Xóa dữ liệu chạy thử".' -ForegroundColor Green

            Write-Buoc '6. Chuyển file PDF đã ký sang thư mục sao lưu'
            $chuyen = Move-FilePdf $thuMucPdf $ketQua.Khoa $thuMucPdfSaoLuu
            Write-Host "Đã chuyển $($chuyen.DaChuyen) file sang $thuMucPdfSaoLuu"
            if ($chuyen.KhongCo -gt 0) { Write-Host "$($chuyen.KhongCo) file không có trong thư mục PDF (bỏ qua)." -ForegroundColor Yellow }
            foreach ($l in $chuyen.Loi) { Write-Host "  Chưa chuyển được: $l" -ForegroundColor Yellow }

            $thongKeDaXoa = [pscustomobject]@{ Co = $chuyen.DaChuyen; KhongCo = $chuyen.KhongCo; SaiDang = 0; Byte = $thongKe.Byte }
            $baoCao = New-Object System.Collections.Generic.List[string]
            $baoCao.Add("Xóa dữ liệu chạy thử: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))")
            $baoCao.Add("Người chạy  : $nguoi")
            $baoCao.Add("Môi trường  : $MoiTruong")
            $baoCao.Add("CSDL        : $($ketNoi.Csdl) trên $($ketNoi.MayChu)")
            $baoCao.Add("Tùy chọn    : $tuyChon")
            $baoCao.Add("Sao lưu CSDL: $noiBak")
            $baoCao.Add("File PDF đã chuyển sang: $thuMucPdfSaoLuu (từ $thuMucPdf)")
            $baoCao.Add('Đã xóa:')
            foreach ($d in (Get-DongDem $ketQua.Dem $thongKeDaXoa)) { $baoCao.Add($d) }
            foreach ($l in $chuyen.Loi) { $baoCao.Add("Chưa chuyển được: $l") }
            $fileKetQua = Join-Path $saoLuu 'ket-qua.txt'
            [System.IO.File]::WriteAllLines($fileKetQua, $baoCao.ToArray(), (New-Object System.Text.UTF8Encoding($true)))
        }
        catch {
            if ($null -eq $ketQua) {
                Write-Host ''
                Write-Host "Lỗi khi xóa: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host 'CSDL giữ nguyên như trước (giao dịch đã hoàn tác), file PDF chưa bị đụng tới.' -ForegroundColor Yellow
            }
            throw
        }
        finally {
            if ($daDung) {
                Write-Buoc '7. Khởi động lại ứng dụng'
                Start-PoolNeuCo $cfg.PoolApi
                Start-PoolNeuCo $cfg.PoolWeb
            }
        }
        Wait-SucKhoe $cfg.SucKhoe

        Write-Host ''
        Write-Host "Xong: đã xóa dữ liệu chạy thử trên $MoiTruong." -ForegroundColor Green
        Write-Host "Sao lưu CSDL  : $noiBak"
        Write-Host "File PDF cũ   : $thuMucPdfSaoLuu"
        Write-Host "Kết quả       : $fileKetQua"
        Write-Host 'Đăng nhập kiểm tra: danh sách phiếu, người bệnh, báo cáo điều dưỡng trống; người dùng, mẫu phiếu, cấu hình còn nguyên.'
        Write-Host 'Bản sao lưu vẫn chứa dữ liệu chạy thử: giữ hoặc xóa theo quy định lưu trữ của bệnh viện.'
    }
    finally {
        $ketNoiMigrator = $null
    }
}

# Chạy khi gọi script; khi nạp bằng dot-source (kiểm thử các hàm) thì không chạy.
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-XoaDuLieu
}
