-- Tạo CSDL và tài khoản SQL riêng cho ứng dụng với quyền tối thiểu (không dùng sa).
-- Chạy bằng tài khoản quản trị SQL Server. Thay mật khẩu trước khi chạy (≥ 16 ký tự ngẫu nhiên; KHÔNG commit).
-- Ứng dụng cần đọc/ghi dữ liệu và sequence; migration (tạo bảng, chỉ mục, trigger) chạy bằng tài khoản
-- quản trị riêng ở bước triển khai, nên tài khoản ứng dụng không cần db_owner.

IF DB_ID(N'ConsentManagement') IS NULL
BEGIN
    CREATE DATABASE [ConsentManagement] COLLATE Vietnamese_CI_AS;
END
GO

ALTER DATABASE [ConsentManagement] SET RECOVERY FULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'consent_app')
BEGIN
    CREATE LOGIN [consent_app] WITH PASSWORD = N'DOI-MAT-KHAU-NAY-TRUOC-KHI-CHAY', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
END
GO

USE [ConsentManagement];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'consent_app')
BEGIN
    CREATE USER [consent_app] FOR LOGIN [consent_app] WITH DEFAULT_SCHEMA = dbo;
END
GO

ALTER ROLE db_datareader ADD MEMBER [consent_app];
ALTER ROLE db_datawriter ADD MEMBER [consent_app];
-- Số phiếu lấy từ sequence dbo.ConsentFormNumbers (tạo bởi migration).
GRANT UPDATE ON SCHEMA::dbo TO [consent_app];
GO

-- Tài khoản chạy migration (chỉ dùng lúc triển khai/nâng cấp; có thể vô hiệu hóa sau đó).
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'consent_migrator')
BEGIN
    CREATE LOGIN [consent_migrator] WITH PASSWORD = N'DOI-MAT-KHAU-NAY-TRUOC-KHI-CHAY-2', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'consent_migrator')
BEGIN
    CREATE USER [consent_migrator] FOR LOGIN [consent_migrator];
END
GO
ALTER ROLE db_owner ADD MEMBER [consent_migrator];
GO
