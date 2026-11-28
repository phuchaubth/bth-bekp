-- Xóa dữ liệu chạy thử trước khi dùng thật (quyết định 52). KHÔNG HOÀN TÁC ĐƯỢC (chỉ khôi phục được từ bản sao lưu).
--
-- Không chạy file này trực tiếp: dùng script xoa-du-lieu-chay-thu.ps1 trong gói triển khai (docs/trien-khai.md, mục 5.11).
-- Script đó đếm cho xem trước, hỏi xác nhận, sao lưu CSDL, dừng ứng dụng, chạy file này, rồi chuyển file PDF đã ký của các
-- phiếu đã xóa sang thư mục sao lưu.
--
-- XÓA: phiếu đồng thuận (mọi trạng thái) và chữ ký trên phiếu; liên kết ký qua mã QR; hồ sơ người bệnh và người đại diện
--      (trừ khi @GiuNguoiBenh = 1); báo cáo điều dưỡng của khoa; số liệu HIS của báo cáo điều dưỡng (toàn viện và theo khoa).
-- GIỮ: người dùng, vai trò, khoa/phòng, mẫu phiếu, cấu hình hệ thống (logo, khung chữ ký, kiosk), khoa báo cáo và số giường,
--      ủy quyền báo cáo, phân công kiểm thử, lịch sử xuất dữ liệu, nhật ký hệ thống (chỉ ghi thêm: thêm một dòng
--      Maintenance.TrialDataPurged ghi người chạy và số dòng đã xóa).
--
-- Tham số do người gọi truyền vào (chạy file này mà không khai báo tham số thì SQL Server báo lỗi, không xóa gì):
--   @ThucHien      bit            0: chỉ đếm, không thay đổi gì. 1: xóa, trong một giao dịch (lỗi ở đâu thì không xóa gì).
--   @GiuNguoiBenh  bit            1: giữ hồ sơ người bệnh và người đại diện.
--   @DanhSoLaiTu1  bit            1: phiếu lập sau đó đánh số lại từ PDT-<năm>-000001.
--   @NguoiThucHien nvarchar(100)  tài khoản chạy script, ghi vào nhật ký hệ thống.
-- Kết quả trả về:
--   1. Số dòng sẽ xóa (trước khi xóa) của từng bảng, kèm ngày sớm nhất, muộn nhất (giờ Việt Nam): Bang, SoDong, TuNgay, DenNgay.
--   2. Khóa file PDF đã ký của các phiếu (SignedPdfKey, đường dẫn tương đối trong thư mục Storage__DocumentsPath).
-- Không có lệnh GO: cả file là một lô lệnh duy nhất.

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Bảng có chỉ mục lọc (filtered index): sửa, xóa phải chạy với các thiết lập này (sqlcmd mặc định tắt QUOTED_IDENTIFIER).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF OBJECT_ID(N'dbo.ConsentForms', N'U') IS NULL
    OR OBJECT_ID(N'dbo.NursingDailyReports', N'U') IS NULL
    OR OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
    THROW 51020, N'Đây không phải CSDL của phần mềm Phiếu đồng thuận (thiếu bảng ConsentForms, NursingDailyReports hoặc AuditLogs). Không xóa gì.', 1;

IF @ThucHien IS NULL OR @GiuNguoiBenh IS NULL OR @DanhSoLaiTu1 IS NULL
    THROW 51021, N'Thiếu giá trị cho @ThucHien, @GiuNguoiBenh hoặc @DanhSoLaiTu1. Không xóa gì.', 1;

DECLARE @Dem TABLE (ThuTu int NOT NULL, Bang sysname NOT NULL, SoDong int NOT NULL, TuNgay date NULL, DenNgay date NULL);

BEGIN TRANSACTION;

IF @ThucHien = 1
BEGIN
    -- Giữ khóa độc quyền các bảng sẽ xóa tới hết giao dịch: không ai thêm, sửa dữ liệu trong lúc đếm và xóa
    -- (script PowerShell đã dừng ứng dụng; khóa này phòng khi ứng dụng vẫn chạy).
    DECLARE @Khoa int;
    SELECT @Khoa = COUNT(*) FROM dbo.RemoteSigningLinks WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.ConsentForms WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.ConsentSignatures WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.LegalRepresentatives WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.Patients WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.NursingDailyReports WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.NursingDailyStatistics WITH (TABLOCKX, HOLDLOCK);
    SELECT @Khoa = COUNT(*) FROM dbo.NursingDepartmentStatistics WITH (TABLOCKX, HOLDLOCK);
END;

INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
SELECT 1, N'ConsentForms', COUNT(*),
       CAST(MIN(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date), CAST(MAX(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date)
FROM dbo.ConsentForms;

INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
SELECT 2, N'ConsentSignatures', COUNT(*),
       CAST(MIN(SWITCHOFFSET(SignedAtUtc, '+07:00')) AS date), CAST(MAX(SWITCHOFFSET(SignedAtUtc, '+07:00')) AS date)
FROM dbo.ConsentSignatures;

INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
SELECT 3, N'RemoteSigningLinks', COUNT(*),
       CAST(MIN(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date), CAST(MAX(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date)
FROM dbo.RemoteSigningLinks;

IF @GiuNguoiBenh = 0
BEGIN
    INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
    SELECT 4, N'Patients', COUNT(*),
           CAST(MIN(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date), CAST(MAX(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date)
    FROM dbo.Patients;

    INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
    SELECT 5, N'LegalRepresentatives', COUNT(*),
           CAST(MIN(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date), CAST(MAX(SWITCHOFFSET(CreatedAtUtc, '+07:00')) AS date)
    FROM dbo.LegalRepresentatives;
END;

INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
SELECT 6, N'NursingDailyReports', COUNT(*), MIN(ReportDate), MAX(ReportDate) FROM dbo.NursingDailyReports;

INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
SELECT 7, N'NursingDailyStatistics', COUNT(*), MIN(ReportDate), MAX(ReportDate) FROM dbo.NursingDailyStatistics;

INSERT INTO @Dem (ThuTu, Bang, SoDong, TuNgay, DenNgay)
SELECT 8, N'NursingDepartmentStatistics', COUNT(*), MIN(ReportDate), MAX(ReportDate) FROM dbo.NursingDepartmentStatistics;

SELECT Bang, SoDong, TuNgay, DenNgay FROM @Dem ORDER BY ThuTu;

SELECT SignedPdfKey FROM dbo.ConsentForms WHERE SignedPdfKey IS NOT NULL ORDER BY SignedPdfKey;

IF @ThucHien = 1
BEGIN
    DELETE FROM dbo.RemoteSigningLinks;

    -- Chữ ký đã tự xóa theo phiếu (ON DELETE CASCADE); xóa trước cho rõ, không phụ thuộc thiết lập đó. Phiếu thay thế,
    -- phiếu điền sẵn theo phiếu lần trước tham chiếu phiếu khác trong cùng bảng: một lệnh DELETE xóa hết cả hai phía.
    DELETE FROM dbo.ConsentSignatures;
    DELETE FROM dbo.ConsentForms;

    IF @GiuNguoiBenh = 0
    BEGIN
        DELETE FROM dbo.LegalRepresentatives;
        DELETE FROM dbo.Patients;
    END;

    DELETE FROM dbo.NursingDepartmentStatistics;
    DELETE FROM dbo.NursingDailyStatistics;
    DELETE FROM dbo.NursingDailyReports;

    IF @DanhSoLaiTu1 = 1
        ALTER SEQUENCE dbo.ConsentFormNumbers RESTART WITH 1;

    -- Nhật ký hệ thống chỉ ghi thêm (trigger TR_AuditLogs_AppendOnly chặn sửa, xóa): các dòng cũ giữ nguyên.
    INSERT INTO dbo.AuditLogs (Id, OccurredAtUtc, Action, EntityType, EntityId, UserId, UserName, IpAddress, UserAgent, CorrelationId, Details)
    VALUES (
        NEWID(), TODATETIMEOFFSET(SYSUTCDATETIME(), 0), N'Maintenance.TrialDataPurged', NULL, NULL, NULL,
        LEFT(NULLIF(LTRIM(RTRIM(@NguoiThucHien)), N''), 100), NULL, N'06-xoa-du-lieu-chay-thu.sql', NULL,
        (SELECT CAST(@GiuNguoiBenh AS bit) AS keepPatients,
                CAST(@DanhSoLaiTu1 AS bit) AS restartFormNumbers,
                (SELECT Bang AS [table], SoDong AS [rows] FROM @Dem ORDER BY ThuTu FOR JSON PATH) AS deleted
         FOR JSON PATH, WITHOUT_ARRAY_WRAPPER));

    COMMIT TRANSACTION;
END
ELSE
BEGIN
    -- Chỉ đếm: không thay đổi gì.
    ROLLBACK TRANSACTION;
END;
