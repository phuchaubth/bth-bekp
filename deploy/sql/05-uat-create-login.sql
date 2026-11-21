-- Môi trường UAT (kiểm thử, quyết định 50): CSDL và tài khoản SQL RIÊNG, không dùng chung với môi trường chạy thật.
-- Chạy bằng tài khoản quản trị SQL Server. Thay hai mật khẩu mẫu trước khi chạy (≥ 16 ký tự ngẫu nhiên, KHÁC mật khẩu của
-- consent_app / consent_migrator; KHÔNG commit). Tài khoản UAT không có quyền gì trên CSDL ConsentManagement (chạy thật).
-- UAT chỉ chứa dữ liệu giả lập: không chép CSDL chạy thật sang UAT.

IF DB_ID(N'ConsentManagement_UAT') IS NULL
BEGIN
    CREATE DATABASE [ConsentManagement_UAT] COLLATE Vietnamese_CI_AS;
END
GO

-- Dữ liệu kiểm thử không cần sao lưu nhật ký giao dịch.
ALTER DATABASE [ConsentManagement_UAT] SET RECOVERY SIMPLE;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'consent_app_uat')
BEGIN
    CREATE LOGIN [consent_app_uat] WITH PASSWORD = N'DOI-MAT-KHAU-UAT-NAY-TRUOC-KHI-CHAY', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF,
        DEFAULT_DATABASE = [ConsentManagement_UAT];
END
GO

USE [ConsentManagement_UAT];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'consent_app_uat')
BEGIN
    CREATE USER [consent_app_uat] FOR LOGIN [consent_app_uat] WITH DEFAULT_SCHEMA = dbo;
END
GO

ALTER ROLE db_datareader ADD MEMBER [consent_app_uat];
ALTER ROLE db_datawriter ADD MEMBER [consent_app_uat];
-- Số phiếu lấy từ sequence dbo.ConsentFormNumbers (tạo bởi migration).
GRANT UPDATE ON SCHEMA::dbo TO [consent_app_uat];
GO

-- Tài khoản chạy migration của UAT (script deploy/iis/cap-nhat-moi-truong.ps1 hỏi mật khẩu khi chạy; sao lưu COPY_ONLY trước
-- khi cập nhật, db_owner có quyền BACKUP DATABASE).
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'consent_migrator_uat')
BEGIN
    CREATE LOGIN [consent_migrator_uat] WITH PASSWORD = N'DOI-MAT-KHAU-UAT-NAY-TRUOC-KHI-CHAY-2', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF,
        DEFAULT_DATABASE = [ConsentManagement_UAT];
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'consent_migrator_uat')
BEGIN
    CREATE USER [consent_migrator_uat] FOR LOGIN [consent_migrator_uat];
END
GO
ALTER ROLE db_owner ADD MEMBER [consent_migrator_uat];
GO
