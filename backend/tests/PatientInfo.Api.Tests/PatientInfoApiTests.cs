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
        "DienThoai AS PhoneNumber, DiaChi AS PermanentAddress, DanToc AS Ethnicity, SoTheBHYT AS HealthInsuranceNumber " +
        "FROM dbo.BenhNhan WHERE MaBN = @PatientCode OR SoCCCD = @CitizenId";

    private static readonly string SqlServerImage =
        Environment.GetEnvironmentVariable("CONSENT_TEST_SQL_IMAGE") is { Length: > 0 } image ? image : "mcr.microsoft.com/mssql/server:2025-latest";

    private MsSqlContainer? _sql;
    private WebApplication? _app;
    private HttpClient? _client;

    public HttpClient Client => _client ?? throw new InvalidOperationException("API thử nghiệm chưa khởi động.");

    public async Task InitializeAsync()
    {
        var sql = _sql = await SqlServerContainers.StartAsync(() => new MsSqlBuilder(SqlServerImage).Build());
        var connectionString = new SqlConnectionStringBuilder(sql.GetConnectionString()) { InitialCatalog = "BenhVienGiaLap" }.ConnectionString;
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
                    NgayCapCCCD nvarchar(10), SoCMND nvarchar(12));
                INSERT INTO dbo.BenhNhan VALUES
                    (N'BN-GL-001', N'Lê Văn Thử (giả lập)', '1970-02-03', N'Nam', N'079070000123', N'0900000123', N'Số 1 Đường Giả Lập', N'Kinh', N'gd479079070000123', N'01/06/2021', N'070123456'),
                    (N'BN-GL-002', N'Trần Thị Mẫu (giả lập)', '1985-03-12', N'Nữ', N'001085000009', NULL, NULL, NULL, NULL, NULL, NULL);
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
        Assert.Equal(JsonValueKind.Null, byCode.GetProperty("currentAddress").ValueKind);
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

    private async Task<(HttpStatusCode Status, JsonElement Body)> LookupAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/lookup") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Api-Key", ApiKey);
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}
