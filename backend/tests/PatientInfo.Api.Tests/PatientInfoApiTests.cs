using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using TestSupport;

namespace PatientInfo.Api.Tests;

/// <summary>
/// API thông tin người bệnh chạy thật trên SQL Server (Testcontainers). CSDL "bệnh viện" giả lập: bảng và cột đặt tên
/// tiếng Việt, dữ liệu giả lập; câu truy vấn đổi tên cột bằng AS như khi cấu hình thật.
/// </summary>
public sealed class PatientInfoApiFixture : IAsyncLifetime
{
    public const string ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001";

    private const string Query =
        "SELECT MaBN AS PatientCode, HoTen AS FullName, NgaySinh AS DateOfBirth, GioiTinh AS Gender, SoCCCD AS CitizenId, " +
        "NgayCapCCCD AS CitizenIdIssueDate, SoCMND AS OldIdNumber, " +
        "DienThoai AS PhoneNumber, DiaChi AS PermanentAddress, DiaChiHienTai AS CurrentAddress, DanToc AS Ethnicity, " +
        "SoTheBHYT AS HealthInsuranceNumber " +
        "FROM dbo.BenhNhan WHERE MaBN = @PatientCode OR SoCCCD = @CitizenId";

    /// <summary>Lượt tiếp nhận (quyết định 25): bảng tiếp nhận nối bảng người bệnh để tra được theo cả mã y tế lẫn CCCD.</summary>
    private const string EncounterQuery =
        "SELECT t.SoTiepNhan AS EncounterNo, t.ThoiGian AS ReceivedAt, t.NoiTiepNhan AS Place, t.NguoiTiepNhan AS ReceivedBy " +
        "FROM dbo.TiepNhan t JOIN dbo.BenhNhan b ON b.MaBN = t.MaBN " +
        "WHERE (b.MaBN = @PatientCode OR b.SoCCCD = @CitizenId) AND t.ThoiGian >= @From AND t.ThoiGian < @To";

    /// <summary>
    /// Số liệu trong ngày cho bảng tổng hợp điều dưỡng (quyết định 31): nhiều câu SELECT nối bằng UNION ALL, mỗi dòng một chỉ
    /// số; người bệnh chờ nhập viện tính theo khoa.
    /// </summary>
    private const string DailyStatisticsQuery =
        "SELECT 'outpatientVisits' AS Indicator, NULL AS DepartmentCode, COUNT(*) AS Value FROM dbo.TiepNhan " +
        "WHERE ThoiGian >= @From AND ThoiGian < @To " +
        "UNION ALL SELECT 'waitingAdmission', MaKhoa, COUNT(*) FROM dbo.ChoNhapVien WHERE ThoiGian >= @From AND ThoiGian < @To GROUP BY MaKhoa " +
        "UNION ALL SELECT LoaiChiDinh, NULL, COUNT(*) FROM dbo.ChiDinhCLS WHERE ThoiGian >= @From AND ThoiGian < @To GROUP BY LoaiChiDinh";

    /// <summary>Danh mục khoa/phòng (quyết định 42): chỉ khoa/phòng đang hoạt động.</summary>
    private const string DepartmentQuery =
        "SELECT MaKhoa AS Code, TenKhoa AS Name FROM dbo.KhoaPhong WHERE HoatDong = 1";

    private static readonly string SqlServerImage =
        Environment.GetEnvironmentVariable("CONSENT_TEST_SQL_IMAGE") is { Length: > 0 } image ? image : "mcr.microsoft.com/mssql/server:2025-latest";

    private MsSqlContainer? _sql;
    private WebApplication? _app;
    private HttpClient? _client;

    public HttpClient Client => _client ?? throw new InvalidOperationException("API thử nghiệm chưa khởi động.");

    /// <summary>Chuỗi kết nối tới CSDL "bệnh viện" giả lập, cho các kịch bản cần cấu hình API khác.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var sql = _sql = await SqlServerContainers.StartAsync(() => new MsSqlBuilder(SqlServerImage).Build());
        var connectionString = ConnectionString =
            new SqlConnectionStringBuilder(sql.GetConnectionString()) { InitialCatalog = "BenhVienGiaLap" }.ConnectionString;
        await using (var master = new SqlConnection(sql.GetConnectionString()))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand("CREATE DATABASE BenhVienGiaLap", master);
            await create.ExecuteNonQueryAsync();
        }

        await using (var source = new SqlConnection(connectionString))
        {
            await source.OpenAsync();
            await using var setup = new SqlCommand(
                """
                CREATE TABLE dbo.BenhNhan (MaBN nvarchar(20) PRIMARY KEY, HoTen nvarchar(200), NgaySinh date, GioiTinh nvarchar(5),
                    SoCCCD nvarchar(12), DienThoai nvarchar(15), DiaChi nvarchar(300), DanToc nvarchar(50), SoTheBHYT nvarchar(20),
                    NgayCapCCCD nvarchar(10), SoCMND nvarchar(12), DiaChiHienTai nvarchar(300));
                INSERT INTO dbo.BenhNhan VALUES
                    (N'BN-GL-001', N'Lê Văn Thử (giả lập)', '1970-02-03', N'Nam', N'079070000123', N'0900000123', N'Số 1 Đường Giả Lập', N'Kinh', N'gd479079070000123', N'01/06/2021', N'070123456', N'Số 2 Đường Hiện Tại'),
                    (N'BN-GL-002', N'Trần Thị Mẫu (giả lập)', '1985-03-12', N'Nữ', N'001085000009', NULL, NULL, NULL, NULL, NULL, NULL, NULL);
                CREATE TABLE dbo.TiepNhan (SoTiepNhan nvarchar(30) PRIMARY KEY, MaBN nvarchar(20), ThoiGian datetime,
                    NoiTiepNhan nvarchar(200), NguoiTiepNhan nvarchar(200));
                INSERT INTO dbo.TiepNhan VALUES
                    (N'TN-GL-0001', N'BN-GL-001', '2026-11-10T09:00:00', N'Khoa Cấp cứu', N'Người tiếp đón A (giả lập)'),
                    (N'TN-GL-0002', N'BN-GL-001', '2026-11-11T07:30:00', N'Phòng khám 1', N'Người tiếp đón B (giả lập)'),
                    (N'TN-GL-0003', N'BN-GL-001', '2026-11-11T23:59:00', N'Phòng khám 3', NULL),
                    (N'TN-GL-0004', N'BN-GL-001', '2026-11-12T00:00:00', N'Phòng khám 2', NULL),
                    (N'TN-GL-0005', N'BN-GL-002', '2026-11-11T08:00:00', N'Phòng khám 1', N'Người tiếp đón B (giả lập)');
                CREATE TABLE dbo.ChoNhapVien (Id int IDENTITY PRIMARY KEY, MaKhoa nvarchar(20), ThoiGian datetime);
                INSERT INTO dbo.ChoNhapVien (MaKhoa, ThoiGian) VALUES
                    (N'NOIA', '2026-11-11T08:00:00'), (N'NOIA', '2026-11-11T23:59:00'), (N'NGOAIB', '2026-11-11T09:00:00'),
                    (N'NOIA', '2026-11-12T00:00:00');
                CREATE TABLE dbo.ChiDinhCLS (Id int IDENTITY PRIMARY KEY, LoaiChiDinh nvarchar(30), ThoiGian datetime);
                INSERT INTO dbo.ChiDinhCLS (LoaiChiDinh, ThoiGian) VALUES
                    (N'xray', '2026-11-11T10:00:00'), (N'xray', '2026-11-11T11:00:00'), (N'mri', '2026-11-11T12:00:00'),
                    (N'xray', '2026-11-10T10:00:00');
                CREATE TABLE dbo.KhoaPhong (MaKhoa nvarchar(20) PRIMARY KEY, TenKhoa nvarchar(200), HoatDong bit);
                INSERT INTO dbo.KhoaPhong VALUES
                    (N'K01', N'Khoa Khám bệnh (giả lập)', 1), (N'K02', N' Khoa Nội tổng hợp (giả lập) ', 1),
                    (N'K99', N'Khoa đã giải thể (giả lập)', 0), (N' k03 ', NULL, 1);
                """,
                source);
            await setup.ExecuteNonQueryAsync();
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PatientInfo:ConnectionString"] = connectionString,
            ["PatientInfo:Query"] = Query,
            ["PatientInfo:EncounterQuery"] = EncounterQuery,
            ["PatientInfo:DailyStatisticsQuery"] = DailyStatisticsQuery,
            ["PatientInfo:DepartmentQuery"] = DepartmentQuery,
            ["PatientInfo:ApiKeys:0"] = ApiKey,
        });
        var app = _app = PatientInfoProgram.Build(builder);
        await app.StartAsync();
        _client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        // Khởi tạo có thể dừng giữa chừng (ví dụ SQL Server không khởi động được): chỉ dọn những gì đã tạo.
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }
    }
}

/// <summary>Các kịch bản dùng chung một máy chủ SQL thử nghiệm và một phiên bản API.</summary>
public sealed class PatientInfoApiTests(PatientInfoApiFixture fixture) : IClassFixture<PatientInfoApiFixture>
{
    private const string ApiKey = PatientInfoApiFixture.ApiKey;
    private readonly HttpClient _client = fixture.Client;
    private readonly string _connectionString = fixture.ConnectionString;

    [Fact]
    public async Task Lookup_ByPatientCodeOrCitizenId_ReturnsAdministrativeInfo()
    {
        var (byCodeStatus, byCode) = await LookupAsync(new { patientCode = "BN-GL-001" });
        var (byIdStatus, byId) = await LookupAsync(new { citizenId = "001085000009" });

        Assert.Equal(HttpStatusCode.OK, byCodeStatus);
        Assert.Equal("Lê Văn Thử (giả lập)", byCode.GetProperty("fullName").GetString());
        Assert.Equal("1970-02-03", byCode.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Male", byCode.GetProperty("gender").GetString());
        Assert.Equal("GD479079070000123", byCode.GetProperty("healthInsuranceNumber").GetString());
        Assert.Equal("Số 1 Đường Giả Lập", byCode.GetProperty("permanentAddress").GetString());
        Assert.Equal("Số 2 Đường Hiện Tại", byCode.GetProperty("currentAddress").GetString());
        Assert.Equal(JsonValueKind.Null, byId.GetProperty("currentAddress").ValueKind);
        // Ngày cấp CCCD lưu dạng chữ dd/MM/yyyy trong CSDL giả lập: trả về theo chuẩn yyyy-MM-dd.
        Assert.Equal("2021-06-01", byCode.GetProperty("citizenIdIssueDate").GetString());
        Assert.Equal("070123456", byCode.GetProperty("oldIdNumber").GetString());
        Assert.Equal(JsonValueKind.Null, byId.GetProperty("citizenIdIssueDate").ValueKind);
        Assert.Equal(HttpStatusCode.OK, byIdStatus);
        Assert.Equal("Female", byId.GetProperty("gender").GetString());
        Assert.Equal("BN-GL-002", byId.GetProperty("patientCode").GetString());
    }

    [Fact]
    public async Task Lookup_ValidatesInput_AndReturns404WhenMissing()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(new { patientCode = "KHONG-CO" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await LookupAsync(new { patientCode = "BN-GL-001", citizenId = "079070000123" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await LookupAsync(new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await LookupAsync(new { citizenId = "123" })).Status);

        // Giá trị như câu lệnh SQL chỉ là tham số: không tìm thấy, bảng vẫn còn.
        Assert.Equal(HttpStatusCode.NotFound, (await LookupAsync(new { patientCode = "x' OR 1=1; DROP TABLE dbo.BenhNhan --" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await LookupAsync(new { patientCode = "BN-GL-001" })).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("khoa-sai-khoa-sai-khoa-sai-khoa-sai-0001")]
    public async Task Lookup_RequiresAValidApiKey(string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/lookup") { Content = JsonContent.Create(new { patientCode = "BN-GL-001" }) };
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Lê Văn Thử", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_IsOpenWithoutKey()
    {
        using var response = await _client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Encounters_ByPatientCode_ReturnTheDayNewestFirst_EndOfDayIncluded_NextMidnightExcluded()
    {
        var (status, body) = await SearchEncountersAsync(new { patientCode = "BN-GL-001", from = "2026-11-11", to = "2026-11-11" });

        Assert.Equal(HttpStatusCode.OK, status);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["TN-GL-0003", "TN-GL-0002"], items.Select(i => i.GetProperty("encounterNo").GetString()).ToList());
        Assert.Equal("2026-11-11T07:30:00", items[1].GetProperty("receivedAt").GetString());
        Assert.Equal("Phòng khám 1", items[1].GetProperty("place").GetString());
        Assert.Equal("Người tiếp đón B (giả lập)", items[1].GetProperty("receivedBy").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("receivedBy").ValueKind);
    }

    [Fact]
    public async Task Encounters_ByCitizenId_OrOverSeveralDays()
    {
        var (byIdStatus, byId) = await SearchEncountersAsync(new { citizenId = "001085000009", from = "2026-11-11", to = "2026-11-11" });
        var (rangeStatus, range) = await SearchEncountersAsync(new { patientCode = "BN-GL-001", from = "2026-11-10", to = "2026-11-12" });

        Assert.Equal(HttpStatusCode.OK, byIdStatus);
        Assert.Equal(["TN-GL-0005"], byId.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("encounterNo").GetString()).ToList());
        Assert.Equal(HttpStatusCode.OK, rangeStatus);
        Assert.Equal(
            ["TN-GL-0004", "TN-GL-0003", "TN-GL-0002", "TN-GL-0001"],
            range.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("encounterNo").GetString()).ToList());
    }

    [Fact]
    public async Task Encounters_ValidateInput_AndTreatValuesAsParametersOnly()
    {
        var day = "2026-11-11";
        Assert.Equal(HttpStatusCode.BadRequest, (await SearchEncountersAsync(new { patientCode = "BN-GL-001", citizenId = "079070000123", from = day, to = day })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SearchEncountersAsync(new { from = day, to = day })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SearchEncountersAsync(new { citizenId = "123", from = day, to = day })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SearchEncountersAsync(new { patientCode = "BN-GL-001", to = day })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SearchEncountersAsync(new { patientCode = "BN-GL-001", from = "2026-11-12", to = day })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await SearchEncountersAsync(new { patientCode = "BN-GL-001", from = "2025-11-10", to = day })).Status);
        Assert.Equal(HttpStatusCode.OK, (await SearchEncountersAsync(new { patientCode = "BN-GL-001", from = "2025-11-11", to = day })).Status);

        // Giá trị như câu lệnh SQL chỉ là tham số: không có lượt nào, bảng vẫn còn.
        var (status, body) = await SearchEncountersAsync(new { patientCode = "x' OR 1=1; DROP TABLE dbo.TiepNhan --", from = day, to = day });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, (await SearchEncountersAsync(new { patientCode = "BN-GL-001", from = day, to = day })).Body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Encounters_RequireAValidApiKey()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/encounters/search")
        {
            Content = JsonContent.Create(new { patientCode = "BN-GL-001", from = "2026-11-11", to = "2026-11-11" }),
        };

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("TN-GL", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DailyStatistics_CountTheHospitalDay_EndOfDayIncluded_NextMidnightExcluded()
    {
        var (status, body) = await DailyStatisticsAsync(new { date = "2026-11-11" });

        Assert.Equal(HttpStatusCode.OK, status);
        var items = body.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("indicator").GetString(), i.GetProperty("departmentCode").GetString(), i.GetProperty("value").GetInt32()))
            .OrderBy(i => i.Item1, StringComparer.Ordinal)
            .ThenBy(i => i.Item2, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            [("mri", null, 1), ("outpatientVisits", null, 3), ("waitingAdmission", "NGOAIB", 1), ("waitingAdmission", "NOIA", 2), ("xray", null, 2)],
            items);
    }

    [Fact]
    public async Task DailyStatistics_RequireADate_AndAValidApiKey()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await DailyStatisticsAsync(new { })).Status);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/daily-statistics")
        {
            Content = JsonContent.Create(new { date = "2026-11-11" }),
        };
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DailyStatistics_WithoutAConfiguredQuery_AreNotImplemented()
    {
        // Phòng CNTT chưa đặt PatientInfo__DailyStatisticsQuery: bảng tổng hợp điều dưỡng nhập tay số liệu HIS.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PatientInfo:ConnectionString"] = "Server=khong-dung.gia-lap.local;Database=BenhVienGiaLap",
            ["PatientInfo:Query"] = "SELECT MaBN AS PatientCode FROM dbo.BenhNhan WHERE MaBN = @PatientCode",
            ["PatientInfo:ApiKeys:0"] = ApiKey,
        });
        await using var app = PatientInfoProgram.Build(builder);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/daily-statistics")
        {
            Content = JsonContent.Create(new { date = "2026-11-11" }),
        };
        request.Headers.Add("X-Api-Key", ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task Departments_ReturnTheActiveHospitalDepartments_TrimmedWithTheCodeAsAMissingName()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/departments");
        request.Headers.Add("X-Api-Key", ApiKey);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = body.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("code").GetString(), i.GetProperty("name").GetString()))
            .OrderBy(i => i.Item1, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            [("K01", "Khoa Khám bệnh (giả lập)"), ("K02", "Khoa Nội tổng hợp (giả lập)"), ("k03", "k03")],
            items);
    }

    [Fact]
    public async Task Departments_RequireAValidApiKey()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/departments");
        request.Headers.Add("X-Api-Key", "khoa-sai-gia-lap-chi-dung-trong-kiem-thu-0002");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Khoa", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PatientInfoProgram.MaxDepartments, HttpStatusCode.OK)]
    [InlineData(PatientInfoProgram.MaxDepartments + 1, HttpStatusCode.UnprocessableEntity)]
    public async Task Departments_OverTheLimit_AreRefused_NotTruncated(int rows, HttpStatusCode expected)
    {
        // Cắt bớt thì phần mềm phiếu sẽ coi các khoa bị cắt là ngừng sử dụng: quá giới hạn thì báo lỗi.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PatientInfo:ConnectionString"] = _connectionString,
            ["PatientInfo:Query"] = "SELECT MaBN AS PatientCode FROM dbo.BenhNhan WHERE MaBN = @PatientCode",
            ["PatientInfo:DepartmentQuery"] =
                $"SELECT TOP ({rows}) CONCAT(N'K', n) AS Code, CONCAT(N'Khoa giả lập ', n) AS Name " +
                "FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) t",
            ["PatientInfo:ApiKeys:0"] = ApiKey,
        });
        await using var app = PatientInfoProgram.Build(builder);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/departments");
        request.Headers.Add("X-Api-Key", ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(rows, body.GetProperty("items").GetArrayLength());
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task Departments_WithoutAConfiguredQuery_AreNotImplemented()
    {
        // Phòng CNTT chưa đặt PatientInfo__DepartmentQuery: phần mềm phiếu báo chưa cấu hình, danh mục khoa giữ nguyên.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PatientInfo:ConnectionString"] = "Server=khong-dung.gia-lap.local;Database=BenhVienGiaLap",
            ["PatientInfo:Query"] = "SELECT MaBN AS PatientCode FROM dbo.BenhNhan WHERE MaBN = @PatientCode",
            ["PatientInfo:ApiKeys:0"] = ApiKey,
        });
        await using var app = PatientInfoProgram.Build(builder);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/departments");
        request.Headers.Add("X-Api-Key", ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task Encounters_WithoutAConfiguredQuery_AreNotImplemented()
    {
        // Bệnh viện chưa đặt PatientInfo__EncounterQuery: tra người bệnh vẫn chạy, tra lượt tiếp nhận báo 501.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PatientInfo:ConnectionString"] = "Server=khong-dung.gia-lap.local;Database=BenhVienGiaLap",
            ["PatientInfo:Query"] = "SELECT MaBN AS PatientCode FROM dbo.BenhNhan WHERE MaBN = @PatientCode",
            ["PatientInfo:ApiKeys:0"] = ApiKey,
        });
        await using var app = PatientInfoProgram.Build(builder);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/encounters/search")
        {
            Content = JsonContent.Create(new { patientCode = "BN-GL-001", from = "2026-11-11", to = "2026-11-11" }),
        };
        request.Headers.Add("X-Api-Key", ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        await app.StopAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SearchEncountersAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/encounters/search") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Api-Key", ApiKey);
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> DailyStatisticsAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/daily-statistics") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Api-Key", ApiKey);
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> LookupAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/lookup") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Api-Key", ApiKey);
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}
