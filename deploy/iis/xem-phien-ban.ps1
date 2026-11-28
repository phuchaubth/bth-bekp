<#
.SYNOPSIS
  So sánh phiên bản đang cài trên máy chủ với gói triển khai vừa giải nén (quyết định 67). Chỉ đọc, không thay đổi gì.

.DESCRIPTION
  Mỗi gói ghi file version.json (số gói, mã commit, thời điểm đóng gói) trong các thư mục ConsentApi, ConsentWeb,
  PatientInfoApi. Số gói là số lần chạy quy trình "Gói triển khai IIS" trên GitHub Actions; trong phần mềm xem ở
  Quản trị -> Bảo trì hệ thống, thẻ "Phiên bản đã cài".
  Script đọc version.json của từng thư mục đang cài và của gói (mặc định là thư mục chứa script), in bảng so sánh: gói này mới
  hơn, giống hay cũ hơn bản đang cài. Thư mục cài từ gói chưa ghi phiên bản hiện "Chưa rõ".
  Không cần quyền quản trị, không cần dừng ứng dụng. Hướng dẫn: docs/trien-khai.md, mục 5.17.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File C:\Setup\goi-1a2b3c4\xem-phien-ban.ps1

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File C:\Setup\goi-1a2b3c4\xem-phien-ban.ps1 -ThuMucCai C:\Apps\UAT
#>
[CmdletBinding()]
param(
    # Thư mục chứa ConsentApi, ConsentWeb, PatientInfoApi đang cài (Prod: C:\Apps, UAT: C:\Apps\UAT).
    [string]$ThuMucCai = 'C:\Apps',
    # Thư mục gói đã giải nén; mặc định là thư mục chứa script.
    [string]$Goi = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

$CacPhan = @('ConsentApi', 'ConsentWeb', 'PatientInfoApi')

# Kết quả so sánh gói này với bản đang cài của một thư mục.
$MoTaKetQua = @{
    MoiHon     = 'Gói này mới hơn'
    Giong      = 'Giống bản đang cài'
    CuHon      = 'GÓI NÀY CŨ HƠN'
    KhacCommit = 'Cùng số gói, khác mã commit'
    ChuaRo     = 'Chưa so được'
    ChuaCai    = 'Chưa cài thư mục này'
    GoiThieu   = 'Gói không có phiên bản'
}

# Phiên bản ghi trong version.json của một thư mục: Goi (số gói), Commit (40 ký tự 0-9a-f, hoặc chuỗi rỗng), DongGoiUtc
# ([datetime] UTC, hoặc $null). Trả $null khi không có file, file hỏng hoặc không có số gói (cài từ gói chưa ghi phiên bản).
function Read-PhienBan([string]$thuMuc) {
    $file = Join-Path $thuMuc 'version.json'
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $null }
    # File do quy trình đóng gói ghi chỉ khoảng 100 byte.
    if ((Get-Item -LiteralPath $file).Length -gt 4096) { return $null }
    try {
        # Get-Content (không dùng [System.IO.File]) để đường dẫn tương đối tính theo thư mục hiện tại của PowerShell.
        $json = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
    if ($null -eq $json) { return $null }
    $so = 0
    if (-not [int]::TryParse([string]$json.package, [ref]$so) -or $so -le 0) { return $null }
    $commit = [string]$json.commit
    if ($commit -cnotmatch '^[0-9a-f]{40}$') { $commit = '' }
    # Windows PowerShell 5.1 giữ chuỗi "2026-11-28T07:05:09Z"; PowerShell 7 tự đổi sang [datetime].
    $dongGoi = $null
    $giaTri = $json.builtAtUtc
    if ($giaTri -is [datetime]) {
        $dongGoi = $giaTri.ToUniversalTime()
    }
    else {
        $thoiDiem = [datetime]::MinValue
        $kieu = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
        if ([datetime]::TryParseExact([string]$giaTri, "yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture, $kieu, [ref]$thoiDiem)) {
            $dongGoi = $thoiDiem
        }
    }
    return [pscustomobject]@{ Goi = $so; Commit = $commit; DongGoiUtc = $dongGoi }
}

# "Gói 97 · 1a2b3c4 · 28/11/2026 14:05" (giờ của máy chủ).
function Format-PhienBan($phienBan) {
    $phan = @("Gói $($phienBan.Goi)")
    if ($phienBan.Commit) { $phan += $phienBan.Commit.Substring(0, 7) }
    if ($null -ne $phienBan.DongGoiUtc) { $phan += $phienBan.DongGoiUtc.ToLocalTime().ToString('dd/MM/yyyy HH:mm') }
    return $phan -join ' · '
}

# Gói này so với bản đang cài: MoiHon, Giong, CuHon, KhacCommit, ChuaRo (bản đang cài chưa ghi phiên bản), ChuaCai (chưa có thư
# mục), GoiThieu (gói không ghi phiên bản).
function Compare-PhienBan($dangCai, $goiNay, [bool]$daCai) {
    if (-not $daCai) { return 'ChuaCai' }
    if ($null -eq $goiNay) { return 'GoiThieu' }
    if ($null -eq $dangCai) { return 'ChuaRo' }
    if ($goiNay.Goi -gt $dangCai.Goi) { return 'MoiHon' }
    if ($goiNay.Goi -lt $dangCai.Goi) { return 'CuHon' }
    if ($goiNay.Commit -and $dangCai.Commit -and $goiNay.Commit -cne $dangCai.Commit) { return 'KhacCommit' }
    return 'Giong'
}

# Một dòng cho mỗi thư mục ConsentApi, ConsentWeb, PatientInfoApi.
function Get-SoSanh([string]$thuMucCai, [string]$goi) {
    foreach ($ten in $CacPhan) {
        $cai = Join-Path $thuMucCai $ten
        $daCai = Test-Path -LiteralPath $cai -PathType Container
        $dangCai = $null
        if ($daCai) { $dangCai = Read-PhienBan $cai }
        $goiNay = Read-PhienBan (Join-Path $goi $ten)
        $ketQua = Compare-PhienBan $dangCai $goiNay $daCai
        $moTaDangCai = 'Chưa cài'
        if ($null -ne $dangCai) { $moTaDangCai = Format-PhienBan $dangCai }
        elseif ($daCai) { $moTaDangCai = 'Chưa rõ (gói cũ)' }
        $moTaGoi = 'Không có version.json'
        if ($null -ne $goiNay) { $moTaGoi = Format-PhienBan $goiNay }
        [pscustomobject]@{
            'Thư mục'  = $ten
            'Đang cài' = $moTaDangCai
            'Gói này'  = $moTaGoi
            'So sánh'  = $MoTaKetQua[$ketQua]
            KetQua     = $ketQua
            DangCai    = $dangCai
            GoiNay     = $goiNay
        }
    }
}

# Kết luận cho người cài, theo ConsentApi và ConsentWeb (PatientInfoApi chỉ chép khi hướng dẫn cài gói yêu cầu).
function Get-KetLuan($dong) {
    $chinh = @($dong | Where-Object { $_.'Thư mục' -in @('ConsentApi', 'ConsentWeb') })
    $ketLuan = New-Object System.Collections.Generic.List[string]
    $cuHon = @($chinh | Where-Object { $_.KetQua -eq 'CuHon' })
    if ($cuHon.Count -gt 0) {
        $ketLuan.Add("GÓI NÀY (gói $($cuHon[0].GoiNay.Goi)) CŨ HƠN BẢN ĐANG CÀI (gói $($cuHon[0].DangCai.Goi)). Chỉ cài khi cố ý quay về bản cũ; quay về bản cũ nên khôi phục bản sao lưu (docs/trien-khai.md, mục 5.10).")
    }
    elseif (@($chinh | Where-Object { $_.KetQua -eq 'MoiHon' }).Count -gt 0) {
        $ketLuan.Add('Gói này mới hơn bản đang cài.')
    }
    elseif (@($chinh | Where-Object { $_.KetQua -eq 'Giong' }).Count -eq $chinh.Count) {
        $ketLuan.Add('ConsentApi và ConsentWeb đã là gói này, không cần cài lại.')
    }
    elseif (@($chinh | Where-Object { $_.KetQua -eq 'ChuaCai' }).Count -eq $chinh.Count) {
        $ketLuan.Add('Thư mục đang cài chưa có ConsentApi, ConsentWeb: kiểm tra lại tham số -ThuMucCai, hoặc cài lần đầu theo docs/trien-khai.md, mục 5.')
    }
    if (@($chinh | Where-Object { $_.KetQua -eq 'ChuaRo' }).Count -gt 0) {
        $ketLuan.Add('Thư mục "Chưa rõ" cài từ gói chưa ghi phiên bản: chưa so được. Cài gói này xong thì lần sau so được.')
    }
    $api = $chinh | Where-Object { $_.'Thư mục' -eq 'ConsentApi' }
    $web = $chinh | Where-Object { $_.'Thư mục' -eq 'ConsentWeb' }
    if ($null -ne $api.DangCai -and $null -ne $web.DangCai -and $api.DangCai.Goi -ne $web.DangCai.Goi) {
        $ketLuan.Add("ConsentApi (gói $($api.DangCai.Goi)) và ConsentWeb (gói $($web.DangCai.Goi)) đang khác gói: lần cài trước chưa chép đủ thư mục.")
    }
    $his = $dong | Where-Object { $_.'Thư mục' -eq 'PatientInfoApi' }
    if ($his.KetQua -in @('MoiHon', 'ChuaRo')) {
        $ketLuan.Add('PatientInfoApi chỉ cần chép khi hướng dẫn cài gói yêu cầu.')
    }
    return , $ketLuan.ToArray()
}

function Show-SoSanh {
    if (-not (Test-Path -LiteralPath $Goi -PathType Container)) { throw "Không thấy thư mục gói: $Goi" }
    Write-Host "Thư mục đang cài: $ThuMucCai"
    Write-Host "Gói             : $Goi"
    $dong = @(Get-SoSanh $ThuMucCai $Goi)
    # Out-String có độ rộng cố định: bảng vẫn in ra khi kết quả được ghi ra file (không có cửa sổ console).
    $bang = $dong | Format-Table 'Thư mục', 'Đang cài', 'Gói này', 'So sánh' -AutoSize | Out-String -Width 200
    Write-Host $bang.TrimEnd()
    Write-Host ''
    foreach ($cau in (Get-KetLuan $dong)) {
        $mau = 'Green'
        if ($cau -like 'GÓI NÀY*' -or $cau -like '*khác gói*') { $mau = 'Red' }
        elseif ($cau -like '*Chưa rõ*' -or $cau -like 'PatientInfoApi*' -or $cau -like '*chưa có ConsentApi*') { $mau = 'Yellow' }
        Write-Host $cau -ForegroundColor $mau
    }
}

# Chạy khi gọi script; khi nạp bằng dot-source (kiểm thử các hàm) thì không chạy.
if ($MyInvocation.InvocationName -ne '.') {
    Show-SoSanh
}
