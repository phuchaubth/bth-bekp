-- Tài khoản SQL CHỈ ĐỌC cho API thông tin người bệnh (PatientInfo.Api), trên CSDL của bệnh viện (HIS).
-- Chạy bằng tài khoản quản trị SQL Server của CSDL đó. Trước khi chạy:
--   1. Thay [CSDL_BENH_VIEN] bằng tên CSDL thật, [dbo].[BANG_BENH_NHAN] bằng bảng/view chứa thông tin hành chính.
--   2. Thay mật khẩu (≥ 16 ký tự ngẫu nhiên; KHÔNG commit, KHÔNG gửi qua chat).
-- Chỉ cấp SELECT trên đúng bảng/view cần dùng (không cấp db_datareader cho cả CSDL nếu không cần).

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'patientinfo_reader')
BEGIN
    CREATE LOGIN [patientinfo_reader] WITH PASSWORD = N'DOI-MAT-KHAU-NAY-TRUOC-KHI-CHAY', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
END
GO

USE [CSDL_BENH_VIEN];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'patientinfo_reader')
BEGIN
    CREATE USER [patientinfo_reader] FOR LOGIN [patientinfo_reader] WITH DEFAULT_SCHEMA = dbo;
END
GO

GRANT SELECT ON [dbo].[BANG_BENH_NHAN] TO [patientinfo_reader];
-- Nếu câu truy vấn nối thêm bảng (dân tộc, nghề nghiệp…), cấp SELECT từng bảng đó:
-- GRANT SELECT ON [dbo].[DM_DAN_TOC] TO [patientinfo_reader];
-- Gắn phiếu với lượt tiếp nhận (quyết định 25, tùy chọn): cấp SELECT bảng/view tiếp nhận và các bảng câu truy vấn
-- PatientInfo__EncounterQuery nối thêm (phòng khám, nhân viên…):
-- GRANT SELECT ON [dbo].[BANG_TIEP_NHAN] TO [patientinfo_reader];
-- GRANT SELECT ON [dbo].[DM_PHONG_KHAM] TO [patientinfo_reader];
-- GRANT SELECT ON [dbo].[DM_NHAN_VIEN] TO [patientinfo_reader];
-- Báo cáo điều dưỡng hằng ngày (quyết định 31, tùy chọn): cấp SELECT các bảng câu truy vấn
-- PatientInfo__DailyStatisticsQuery dùng (tiếp nhận khám, chờ nhập viện, chỉ định cận lâm sàng…):
-- GRANT SELECT ON [dbo].[BANG_CHO_NHAP_VIEN] TO [patientinfo_reader];
-- GRANT SELECT ON [dbo].[BANG_CHI_DINH_CLS] TO [patientinfo_reader];
-- Danh mục khoa/phòng (quyết định 42, tùy chọn): cấp SELECT bảng/view danh mục khoa mà câu truy vấn
-- PatientInfo__DepartmentQuery dùng:
-- GRANT SELECT ON [dbo].[BANG_KHOA_PHONG] TO [patientinfo_reader];
GO

-- Chặn ghi tuyệt đối (phòng khi tài khoản lỡ được thêm vào nhóm có quyền ghi).
DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE TO [patientinfo_reader];
GO
