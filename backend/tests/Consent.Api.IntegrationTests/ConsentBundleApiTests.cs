using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Bộ phiếu (quyết định 23) qua API với SQL Server thật: lập cả bộ, một phiên ký (token liệt kê nhiều phiếu), người bệnh ký
/// một lần cho cả bộ, nhân viên xác nhận cả bộ.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConsentBundleApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string Statement = "Tôi đã nghe, hiểu và đồng ý";

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

    private static string TemplateId(JsonElement list, string code) =>
        list.EnumerateArray().Single(t => t.GetProperty("templateCode").GetString() == code).GetProperty("templateId").GetString()!;

    private static string FormId(JsonElement bundle, string code) =>
        bundle.GetProperty("forms").EnumerateArray().Single(f => f.GetProperty("templateCode").GetString() == code).GetProperty("id").GetString()!;

    [Fact]
    public async Task Bundle_IsCreatedOnce_SignedOnceByThePatient_AndConfirmedTogether()
    {
        // Phiên bản phiếu tư vấn có bước trung gian "người tư vấn": tiếp đón xác nhận BM02 và bước đó trong một lần.
        await _client.PublishPortalConsultationWithConsultantStepAsync(
            (await _client.LoginAsync("mauphieu.demo", TestSettings.DemoPassword)).AccessToken!);
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", receptionist);
        var patientId = patients.GetProperty("items")[0].GetProperty("id").GetString()!;
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);

        var (createdStatus, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms/bundle", receptionist,
            new { patientId, templateIds = new[] { TemplateId(available, "QT-KNGT-BM02"), TemplateId(available, "TUVAN-CONGTT") } });
        Assert.True(createdStatus == HttpStatusCode.Created, created.ToString());
        var bundleId = created.GetProperty("bundleId").GetString()!;
        var bundle = $"/api/v1/consent-forms/bundles/{bundleId}";
        var bm02 = FormId(created, "QT-KNGT-BM02");
        var tuvan = FormId(created, "TUVAN-CONGTT");
        Assert.False(created.GetProperty("canStartSigning").GetBoolean());

        await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/consent-forms/{bm02}", receptionist, new { data = new { careScheme = "nonInsurance" }, signerKind = "Patient" });
        foreach (var id in new[] { bm02, tuvan })
        {
            var (submitted, body) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{id}/submit", receptionist);
            Assert.True(submitted == HttpStatusCode.OK, body.ToString());
        }

        var (_, ready) = await _client.SendJsonAsync(HttpMethod.Get, bundle, receptionist);
        Assert.True(ready.GetProperty("canStartSigning").GetBoolean(), ready.ToString());
        var (sessionStatus, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{bundle}/signing-session", receptionist);
        Assert.True(sessionStatus == HttpStatusCode.OK, session.ToString());
        var signingToken = session.GetProperty("token").GetString()!;
        Assert.Equal(2, session.GetProperty("formIds").GetArrayLength());

        var decisions = new Dictionary<string, string> { [tuvan] = "Agreed" };
        // Token của bộ phiếu không ký lẻ một phiếu; token đăng nhập không ký thay người bệnh.
        var (singleStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{tuvan}/patient-signature", signingToken,
            new { decision = "Agreed", image = Image(), statement = Statement });
        var (staffStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms/signing-bundle/patient-signature", receptionist,
            new { image = Image(), statement = Statement, decisions });
        var (noDecisionStatus, noDecision) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms/signing-bundle/patient-signature", signingToken,
            new { image = Image(), statement = Statement });
        var (signedStatus, signed) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms/signing-bundle/patient-signature", signingToken,
            new { image = Image(), statement = Statement, decisions });

        Assert.Equal(HttpStatusCode.Forbidden, singleStatus);
        Assert.NotEqual(HttpStatusCode.OK, staffStatus);
        Assert.Equal(HttpStatusCode.BadRequest, noDecisionStatus);
        Assert.True(noDecision.GetProperty("errors").TryGetProperty("decisions", out _), noDecision.ToString());
        Assert.True(signedStatus == HttpStatusCode.OK, signed.ToString());
        Assert.Equal(2, signed.GetProperty("forms").GetArrayLength());

        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT COUNT(*), COUNT(DISTINCT s.SignedAtUtc), MAX(f.BundleId) FROM ConsentSignatures s JOIN ConsentForms f ON f.Id = s.FormId " +
                "WHERE f.BundleId = @bundle AND s.Role = 'Patient'", connection);
            command.Parameters.AddWithValue("@bundle", Guid.Parse(bundleId));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        }

        // Tiếp đón xác nhận một lần: hoàn tất BM02, xác nhận bước người tư vấn của TUVAN; TUVAN còn chờ điều dưỡng khoa.
        var (confirmStatus, confirmed) = await _client.SendJsonAsync(HttpMethod.Post, $"{bundle}/confirm", receptionist, new { image = Image() });
        Assert.True(confirmStatus == HttpStatusCode.OK, confirmed.ToString());
        Assert.Equal(2, confirmed.GetProperty("confirmed").GetArrayLength());
        Assert.Equal(0, confirmed.GetProperty("failed").GetArrayLength());

        var (_, after) = await _client.SendJsonAsync(HttpMethod.Get, bundle, receptionist);
        var forms = after.GetProperty("forms").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetString()!);
        Assert.Equal("Completed", forms[bm02].GetProperty("status").GetString());
        Assert.Equal("PendingSignature", forms[tuvan].GetProperty("status").GetString());
        Assert.Contains("Điều dưỡng khoa", forms[tuvan].GetProperty("nextStepLabel").GetString()!, StringComparison.Ordinal);
        Assert.False(after.GetProperty("canConfirm").GetBoolean());

        using var pdf = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/consent-forms/{bm02}/pdf")
        {
            Headers = { Authorization = new("Bearer", receptionist) },
        });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Bundle_OfAnotherDepartment_IsNotFound()
    {
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var nurseOfAnotherDepartment = (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!;
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", receptionist);
        var patientId = patients.GetProperty("items")[0].GetProperty("id").GetString()!;
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var (_, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms/bundle", receptionist,
            new { patientId, templateIds = new[] { TemplateId(available, "QT-KNGT-BM02"), TemplateId(available, "TTNB-NGOAITRU") } });

        var (status, body) = await _client.SendJsonAsync(
            HttpMethod.Get, $"/api/v1/consent-forms/bundles/{created.GetProperty("bundleId").GetString()}", nurseOfAnotherDepartment);
        var (oneTemplate, single) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms/bundle", receptionist,
            new { patientId, templateIds = new[] { TemplateId(available, "QT-KNGT-BM02") } });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("consent_forms.bundle_not_found", body.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, oneTemplate);
        Assert.True(single.GetProperty("errors").TryGetProperty("templateIds", out _), single.ToString());
    }
}
