using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;

namespace Consent.Api.IntegrationTests;

/// <summary>Kế thừa phiếu lần trước (quyết định 27) qua API, với SQL Server thật (nội dung phiếu lưu mã hóa).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class FormInheritanceApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;

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

    private static string Image() => Convert.ToBase64String(TestPng.Signature());

    /// <summary>Tiếp đón lập phiếu BM02 cho người bệnh demo (CCCD giả lập 001085000001); trả về phiếu vừa lập.</summary>
    private async Task<JsonElement> CreateBm02Async(string receptionist)
    {
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", receptionist);
        var patientId = patients.GetProperty("items")[0].GetProperty("id").GetString()!;
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var templateId = available.EnumerateArray()
            .Single(t => t.GetProperty("templateCode").GetString() == "QT-KNGT-BM02").GetProperty("templateId").GetString()!;
        var (status, created) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId });
        Assert.True(status == HttpStatusCode.Created, created.ToString());
        return created;
    }

    /// <summary>Phiếu BM02 khám dịch vụ: người bệnh ký, tiếp đón xác nhận hoàn tất. Trả về số phiếu.</summary>
    private async Task<string> CompletedBm02Async(string receptionist)
    {
        var created = await CreateBm02Async(receptionist);
        var form = $"/api/v1/consent-forms/{created.GetProperty("id").GetString()}";
        await _client.SendJsonAsync(HttpMethod.Put, form, receptionist, new { data = new { careScheme = "nonInsurance" }, signerKind = "Patient" });
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", receptionist)).Status);
        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", receptionist);
        var (signedStatus, signed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString()!,
            new { image = Image(), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signedStatus == HttpStatusCode.OK, signed.ToString());
        var (completedStatus, completed) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Image() });
        Assert.True(completedStatus == HttpStatusCode.OK, completed.ToString());
        return created.GetProperty("formNo").GetString()!;
    }

    [Fact]
    public async Task AdministratorSetsTheDays_ThenANewFormIsFilledFromTheLastCompletedOne()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var previous = await CompletedBm02Async(receptionist);

        // Chưa đặt: tắt, phiếu mới để trống.
        var (_, initially) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/settings/form-inheritance", admin);
        var before = await CreateBm02Async(receptionist);
        var (staffStatus, _) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/form-inheritance", receptionist, new { days = 30 });
        var (invalidStatus, invalid) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/form-inheritance", admin, new { days = 400 });
        var (savedStatus, saved) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/form-inheritance", admin, new { days = 30 });
        var after = await CreateBm02Async(receptionist);

        Assert.Equal(0, initially.GetProperty("days").GetInt32());
        Assert.False(before.GetProperty("data").TryGetProperty("careScheme", out _));
        Assert.Equal(HttpStatusCode.Forbidden, staffStatus);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("days", out _));
        Assert.Equal(HttpStatusCode.OK, savedStatus);
        Assert.Equal(30, saved.GetProperty("days").GetInt32());
        Assert.Equal("nonInsurance", after.GetProperty("data").GetProperty("careScheme").GetString());
        Assert.Contains(after.GetProperty("notices").EnumerateArray(), n => n.GetString()!.Contains(previous, StringComparison.Ordinal));

        // Nội dung kế thừa đã được lưu (mã hóa) cùng phiếu mới.
        var (_, reopened) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{after.GetProperty("id").GetString()}", receptionist);
        Assert.Equal("nonInsurance", reopened.GetProperty("data").GetProperty("careScheme").GetString());
    }
}
