-- Khôi phục CSDL từ bản sao lưu đầy đủ (+ các bản nhật ký giao dịch nếu có). Diễn tập khôi phục định kỳ (ít nhất mỗi quý).
-- Sau khi khôi phục CSDL, khôi phục thư mục PDF đã ký từ cùng thời điểm và đặt lại đúng FIELD_ENCRYPTION_KEY,
-- BLIND_INDEX_KEY, JWT_SIGNING_KEY cũ: khóa khác thì không đọc được dữ liệu đã mã hóa.

USE [master];
GO
ALTER DATABASE [ConsentManagement] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO
RESTORE DATABASE [ConsentManagement]
    FROM DISK = N'D:\Backup\Consent\ConsentManagement_FULL_20260101_0200.bak'
    WITH CHECKSUM, REPLACE, NORECOVERY, STATS = 10;
GO
-- Lặp lại cho từng file .trn theo thứ tự thời gian (bỏ qua nếu chỉ khôi phục bản đầy đủ):
-- RESTORE LOG [ConsentManagement] FROM DISK = N'D:\Backup\Consent\ConsentManagement_LOG_20260101_0215.trn' WITH CHECKSUM, NORECOVERY;
RESTORE DATABASE [ConsentManagement] WITH RECOVERY;
GO
ALTER DATABASE [ConsentManagement] SET MULTI_USER;
GO
-- Gắn lại user với login trên máy chủ mới (nếu khôi phục sang máy chủ khác):
-- USE [ConsentManagement]; ALTER USER [consent_app] WITH LOGIN = [consent_app];
