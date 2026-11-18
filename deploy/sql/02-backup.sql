-- Sao lưu CSDL: đầy đủ hằng ngày + nhật ký giao dịch mỗi 15–30 phút (chế độ RECOVERY FULL).
-- Lên lịch bằng SQL Server Agent (Standard trở lên) hoặc Task Scheduler + sqlcmd (Express).
-- Đổi đường dẫn D:\Backup\Consent thành thư mục sao lưu của bệnh viện (ổ đĩa khác ổ dữ liệu, có sao chép ra ngoài máy chủ).
-- Nhớ sao lưu THÊM thư mục PDF đã ký (Storage:DocumentsPath) và giữ khóa FIELD_ENCRYPTION_KEY ở nơi an toàn riêng.

DECLARE @stamp nvarchar(20) = FORMAT(SYSDATETIME(), 'yyyyMMdd_HHmm');
DECLARE @full nvarchar(400) = N'D:\Backup\Consent\ConsentManagement_FULL_' + @stamp + N'.bak';

BACKUP DATABASE [ConsentManagement]
    TO DISK = @full
    WITH COMPRESSION, CHECKSUM, INIT, STATS = 10;

-- Kiểm tra bản sao lưu đọc lại được.
RESTORE VERIFYONLY FROM DISK = @full WITH CHECKSUM;
GO

-- Nhật ký giao dịch (chạy theo lịch riêng, ví dụ mỗi 15 phút):
-- DECLARE @stamp nvarchar(20) = FORMAT(SYSDATETIME(), 'yyyyMMdd_HHmm');
-- DECLARE @log nvarchar(400) = N'D:\Backup\Consent\ConsentManagement_LOG_' + @stamp + N'.trn';
-- BACKUP LOG [ConsentManagement] TO DISK = @log WITH COMPRESSION, CHECKSUM, INIT;
