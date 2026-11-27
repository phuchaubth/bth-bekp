using System.Globalization;
using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Sổ theo dõi kết quả nội kiểm đường huyết (quyết định 57) qua API, với SQL Server thật: Khoa HSH khai báo máy và lô; điều dưỡng
/// khoa Nội ghi kết quả, ghi điều chỉnh, gửi sổ tháng; Khoa HSH xem xét, trả lại ngày, xác nhận sổ; mọi thay đổi có trong nhật ký
/// hệ thống. Dùng sổ tháng trước (mọi ngày đã qua) để không phụ thuộc ngày chạy test. Dữ liệu giả lập.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class QualityControlApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;

    /// <summary>Ngày đầu tháng trước theo giờ bệnh viện (UTC+7).</summary>
    private static DateOnly FirstOfLastMonth
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            return new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        }
    }

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

    private static JsonElement DayOf(JsonElement book, DateOnly date) =>
        book.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("date").GetString() == Day(date));

    private static object Run(DateOnly date, string strip, string level1, decimal value1, string level2, decimal value2) => new
    {
        runDate = Day(date),
        stripLotId = strip,
        stripOpenedOn = Day(FirstOfLastMonth),
        control1LotId = level1,
        control1OpenedOn = Day(FirstOfLastMonth),
        control1Value = value1,
        control2LotId = level2,
        control2OpenedOn = Day(FirstOfLastMonth),
        control2Value = value2,
    };

    [Fact]
    public async Task LabDeclares_WardRecords_LabReviewsAndConfirms_AllAudited()
    {
        var lab = (await _client.LoginAsync("hsh.demo", TestSettings.DemoPassword)).AccessToken!;
        var nurse = (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!;
        var reception = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var admin = await _client.LoginAsAdministratorAsync();

        // Danh mục: chỉ Khoa HSH (và quản trị). Dữ liệu demo có sẵn một máy của khoa Nội và ba lô.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/qc/catalog", nurse)).Status);
        var (_, catalog) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/qc/catalog", lab);
        var demoMeter = Assert.Single(catalog.GetProperty("meters").EnumerateArray());
        Assert.Equal("Khoa Nội tổng hợp", demoMeter.GetProperty("departmentName").GetString());
        var expiry = Day(FirstOfLastMonth.AddYears(2));
        var (stripStatus, strip) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/qc/lots", lab, new
        {
            kind = "TestStrip", productName = "Que thử B (giả lập)", lotNumber = "QT-B-01", expiryDate = expiry,
        });
        Assert.True(stripStatus == HttpStatusCode.OK, strip.ToString());
        var (_, level1) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/qc/lots", lab, new
        {
            kind = "ControlSolution", productName = "Dung dịch chứng B (giả lập)", lotNumber = "DC-B-01", expiryDate = expiry,
            level = 1, rangeLow = 4.0m, rangeHigh = 7.4m, unit = "MmolPerL",
        });
        var (_, level2) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/qc/lots", lab, new
        {
            kind = "ControlSolution", productName = "Dung dịch chứng B (giả lập)", lotNumber = "DC-B-02", expiryDate = expiry,
            level = 2, rangeLow = 13.1m, rangeHigh = 19.7m, unit = "MmolPerL",
        });
        var (duplicate, _) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/qc/lots", lab, new
        {
            kind = "TestStrip", productName = "Que thử B (giả lập)", lotNumber = "QT-B-01", expiryDate = expiry,
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate);
        var stripId = strip.GetProperty("id").GetString()!;
        var level1Id = level1.GetProperty("id").GetString()!;
        var level2Id = level2.GetProperty("id").GetString()!;

        // Khoa Nội mở sổ tháng trước của máy; người không có quyền ghi không xem được danh sách.
        var month = FirstOfLastMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/qc/books?month={month}", reception)).Status);
        var (_, list) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/qc/books?month={month}", nurse);
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.True(row.GetProperty("canOpen").GetBoolean());
        var (openStatus, book) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/qc/books", nurse, new { meterId = row.GetProperty("meterId").GetString(), month });
        Assert.True(openStatus == HttpStatusCode.OK, book.ToString());
        var bookId = book.GetProperty("id").GetString()!;
        Assert.Equal("Open", book.GetProperty("status").GetString());

        // Ghi kết quả: ngày 1 đạt; ngày 2 không đạt, ghi nguyên nhân và chạy lại; ngày 3 ghi nhầm rồi ghi điều chỉnh.
        var day1 = FirstOfLastMonth;
        var day2 = day1.AddDays(1);
        var day3 = day1.AddDays(2);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs", nurse, Run(day1, stripId, level1Id, 5.6m, level2Id, 16.2m))).Status);
        var (_, failed) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs", nurse, Run(day2, stripId, level1Id, 8.1m, level2Id, 16.2m));
        var failedRun = DayOf(failed, day2).GetProperty("run");
        Assert.False(failedRun.GetProperty("passed").GetBoolean());
        var (followStatus, followed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs/{failedRun.GetProperty("id").GetString()}/follow-up", nurse,
            new { reason = "Lọ dung dịch chứng để ngoài tủ mát (giả lập)", control1RepeatValue = 5.9m, control2RepeatValue = 15.8m });
        Assert.True(followStatus == HttpStatusCode.OK, followed.ToString());
        Assert.True(DayOf(followed, day2).GetProperty("run").GetProperty("repeatPassed").GetBoolean());
        var (_, wrong) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs", nurse, Run(day3, stripId, level1Id, 5.6m, level2Id, 61.2m));
        var wrongId = DayOf(wrong, day3).GetProperty("run").GetProperty("id").GetString();
        var (dup, dupBody) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs", nurse, Run(day3, stripId, level1Id, 5.6m, level2Id, 16.2m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, dup);
        Assert.Equal("qc.rule", dupBody.GetProperty("code").GetString());
        var (_, corrected) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs/{wrongId}/correction", nurse, new
        {
            reason = "Ghi nhầm kết quả mức 2",
            replacement = Run(day3, stripId, level1Id, 5.6m, level2Id, 16.2m),
        });
        var day3Json = DayOf(corrected, day3);
        Assert.Equal("Ghi nhầm kết quả mức 2", Assert.Single(day3Json.GetProperty("cancelledRuns").EnumerateArray()).GetProperty("cancelReason").GetString());
        Assert.True(day3Json.GetProperty("run").GetProperty("passed").GetBoolean());

        // Khoa HSH xem xét cả tháng theo gợi ý; điều dưỡng không xem xét được.
        var reviewAll = new
        {
            days = corrected.GetProperty("days").EnumerateArray()
                .Select(d => new { date = d.GetProperty("date").GetString(), outcome = d.GetProperty("suggestedOutcome").GetString() })
                .ToArray(),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/reviews", nurse, reviewAll)).Status);
        var (reviewStatus, reviewed) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/reviews", lab, reviewAll);
        Assert.True(reviewStatus == HttpStatusCode.OK, reviewed.ToString());
        Assert.Equal("Passed", DayOf(reviewed, day2).GetProperty("review").GetProperty("outcome").GetString());

        // Ngày đã xem xét: khoa không điều chỉnh được; Khoa HSH trả lại ngày, khoa điều chỉnh, Khoa HSH xem xét lại.
        var day1Run = DayOf(reviewed, day1).GetProperty("run").GetProperty("id").GetString();
        var correction = new { reason = "Ghi nhầm kết quả mức 1", replacement = Run(day1, stripId, level1Id, 5.7m, level2Id, 16.2m) };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs/{day1Run}/correction", nurse, correction)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/reviews/return", lab, new { date = Day(day1), reason = "Khoa báo ghi nhầm (giả lập)" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs/{day1Run}/correction", nurse, correction)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/reviews", lab, new { days = new[] { new { date = Day(day1), outcome = "Passed" } } })).Status);

        // Gửi sổ, xác nhận sổ; sổ đã xác nhận không thay đổi được nữa.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/submit", lab)).Status);
        var (submitStatus, submitted) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/submit", nurse);
        Assert.True(submitStatus == HttpStatusCode.OK, submitted.ToString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/runs", nurse, Run(day1.AddDays(5), stripId, level1Id, 5.6m, level2Id, 16.2m))).Status);
        var (confirmStatus, confirmed) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/confirm", lab);
        Assert.True(confirmStatus == HttpStatusCode.OK, confirmed.ToString());
        Assert.Equal(("Confirmed", "Khoa HSH Demo"), (confirmed.GetProperty("status").GetString(), confirmed.GetProperty("confirmedBy").GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/return", lab, new { reason = "Muộn" })).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/qc/books/{bookId}/reviews/return", admin, new { date = Day(day1), reason = "Muộn" })).Status);

        // PDF theo mẫu cho khoa và Khoa HSH; người không có quyền không tải được.
        foreach (var token in new[] { nurse, lab })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/qc/books/{bookId}/pdf");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var pdf = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.Equal("%PDF"u8.ToArray(), (await pdf.Content.ReadAsByteArrayAsync())[..4]);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/qc/books/{bookId}/pdf", reception)).Status);

        // Không có lệnh xóa; mọi lần ghi, điều chỉnh, xem xét, trả lại, gửi, xác nhận đều có trong nhật ký hệ thống, dữ liệu cũ còn nguyên.
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using (var runs = new SqlCommand("SELECT COUNT(*), SUM(CASE WHEN CancelledAtUtc IS NULL THEN 0 ELSE 1 END) FROM QcRuns", connection))
        await using (var reader = await runs.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal((5, 2), (reader.GetInt32(0), reader.GetInt32(1)));
        }

        await using var audit = new SqlCommand(
            "SELECT EntityType, Action, COUNT(*) FROM AuditLogs WHERE EntityType LIKE 'Qc%' GROUP BY EntityType, Action", connection);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var auditReader = await audit.ExecuteReaderAsync())
        {
            while (await auditReader.ReadAsync())
            {
                counts[$"{auditReader.GetString(0)}.{auditReader.GetString(1)}"] = auditReader.GetInt32(2);
            }
        }

        Assert.Equal(5, counts.GetValueOrDefault("QcRun.Create"));
        Assert.True(counts.GetValueOrDefault("QcRun.Update") >= 3, string.Join(", ", counts));
        Assert.Equal(DateTime.DaysInMonth(day1.Year, day1.Month) + 1, counts.GetValueOrDefault("QcDayReview.Create"));
        Assert.Equal(1, counts.GetValueOrDefault("QcDayReview.Update"));
        Assert.True(counts.GetValueOrDefault("QcBook.Update") >= 10, string.Join(", ", counts));
        Assert.True(counts.GetValueOrDefault("QcLot.Create") >= 3, string.Join(", ", counts));
    }
}
