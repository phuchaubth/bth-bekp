using System.Globalization;
using System.Net;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Phân công kiểm thử tính năng mới (quyết định 49) qua API, với SQL Server thật: quản trị phân công, người kiểm thử báo cáo,
/// quản trị ghi hướng xử lý và xuất Excel; lỗi phát hiện lưu mã hóa. Dữ liệu giả lập.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TestingApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string Findings = "Người được ủy quyền không thấy khoa Nhi (giả lập)";

    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;

    /// <summary>Ngày hôm nay theo giờ bệnh viện (UTC+7), như máy chủ tính.</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task InitializeAsync()
    {
        _client = _factory.CreateHttpsClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ManagerAssigns_TesterReports_ManagerReviewsAndExports()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var nurse = (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!;
        var doctor = (await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword)).AccessToken!;

        // Chỉ người có quyền testing.manage (vai trò Quản trị) phân công được.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/testing/assignments", nurse)).Status);
        var (_, candidates) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/testing/assignments/candidates", admin);
        var nurseId = candidates.EnumerateArray().Single(c => c.GetProperty("fullName").GetString() == "Điều dưỡng Demo").GetProperty("userId").GetString();
        var doctorId = candidates.EnumerateArray().Single(c => c.GetProperty("fullName").GetString() == "Bác sĩ Demo").GetProperty("userId").GetString();

        var (createdStatus, created) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/testing/assignments", admin, new
        {
            version = "Gói 71 (giả lập)",
            feature = "Ủy quyền báo cáo điều dưỡng",
            instructions = "Ủy quyền rồi đăng nhập người được ủy quyền, gửi báo cáo.",
            assignees = new[]
            {
                new { userId = nurseId, startDate = Day(Today), dueDate = Day(Today.AddDays(2)) },
                new { userId = doctorId, startDate = Day(Today.AddDays(1)), dueDate = Day(Today.AddDays(3)) },
            },
        });
        Assert.True(createdStatus == HttpStatusCode.OK, created.ToString());
        Assert.Equal(["InProgress", "Upcoming"], created.EnumerateArray().Select(a => a.GetProperty("status").GetString()).Order(StringComparer.Ordinal));
        var nurseWork = created.EnumerateArray().Single(a => a.GetProperty("assigneeUserId").GetString() == nurseId).GetProperty("id").GetString();
        var doctorWork = created.EnumerateArray().Single(a => a.GetProperty("assigneeUserId").GetString() == doctorId).GetProperty("id").GetString();

        // Người kiểm thử thấy việc của mình; người khác không báo cáo được; chưa tới ngày thì chưa báo cáo được.
        var (_, mine) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/testing/assignments/mine", nurse);
        var work = Assert.Single(mine.EnumerateArray());
        Assert.True(work.GetProperty("canReport").GetBoolean());
        var report = new { result = "Failed", findings = Findings, suggestions = "Hiện khoa được ủy quyền ngay khi đăng nhập." };
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/testing/assignments/{nurseWork}/report", doctor, report)).Status);
        var (early, earlyBody) = await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/testing/assignments/{doctorWork}/report", doctor, new { result = "Passed" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, early);
        Assert.Equal("testing.not_started", earlyBody.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/testing/assignments/{nurseWork}/report", nurse, new { result = "Failed" })).Status);

        var (reportedStatus, reported) = await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/testing/assignments/{nurseWork}/report", nurse, report);
        Assert.True(reportedStatus == HttpStatusCode.OK, reported.ToString());
        Assert.Equal(("Reported", "Failed", false), (reported.GetProperty("status").GetString(), reported.GetProperty("result").GetString(), reported.GetProperty("late").GetBoolean()));

        // Lỗi phát hiện lưu mã hóa.
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT FindingsEncrypted FROM TestAssignments WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@id", Guid.Parse(nurseWork!));
            var stored = (string)(await command.ExecuteScalarAsync())!;
            Assert.DoesNotContain("khoa Nhi", stored, StringComparison.Ordinal);
        }

        // Nhật ký hệ thống không chép lỗi phát hiện (chỉ ghi ***).
        var (_, audit) = await _client.SendJsonAsync(
            HttpMethod.Get, $"/api/v1/audit-logs?entityType=TestAssignment&entityId={nurseWork}&pageSize=50", admin);
        Assert.NotEmpty(audit.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("khoa Nhi", audit.ToString(), StringComparison.Ordinal);

        // Đợt kiểm thử khác: lọc theo đợt chỉ lấy đúng đợt.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/testing/assignments", admin, new
        {
            version = "Gói 72 (giả lập)",
            feature = "Dashboard điều dưỡng",
            assignees = new[] { new { userId = nurseId, startDate = Day(Today), dueDate = Day(Today) } },
        })).Status);

        // Quản trị xem, ghi hướng xử lý; báo cáo khóa; xuất Excel.
        var (_, list) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/testing/assignments?version=G%C3%B3i%2071%20(gi%E1%BA%A3%20l%E1%BA%ADp)", admin);
        Assert.Equal(["Gói 72 (giả lập)", "Gói 71 (giả lập)"], list.GetProperty("versions").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
        Assert.All(list.GetProperty("items").EnumerateArray(), a => Assert.Equal("Gói 71 (giả lập)", a.GetProperty("version").GetString()));
        Assert.Equal(Findings, list.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("id").GetString() == nurseWork).GetProperty("findings").GetString());
        var (reviewStatus, reviewed) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/testing/assignments/{nurseWork}/review", admin, new { note = "Sửa trong gói 72." });
        Assert.True(reviewStatus == HttpStatusCode.OK, reviewed.ToString());
        var (lockedStatus, locked) = await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/testing/assignments/{nurseWork}/report", nurse, report);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lockedStatus);
        Assert.Equal("testing.reviewed", locked.GetProperty("code").GetString());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/testing/assignments/xlsx");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);
        using var workbook = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, workbook.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", workbook.Content.Headers.ContentType!.MediaType);
        var bytes = await workbook.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        // Hủy phân công chưa báo cáo; người kiểm thử thấy đã hủy.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/testing/assignments/{doctorWork}/cancel", admin)).Status);
        var (_, doctorMine) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/testing/assignments/mine", doctor);
        Assert.Equal("Cancelled", Assert.Single(doctorMine.EnumerateArray()).GetProperty("status").GetString());
    }
}
