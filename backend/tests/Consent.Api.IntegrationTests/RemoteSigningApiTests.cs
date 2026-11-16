using System.Net;
using System.Security.Cryptography;
using System.Text;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Ký trên điện thoại riêng của người bệnh qua mã QR (quyết định 32) với SQL Server thật: chỉ lưu mã băm, mở bằng ngày sinh
/// không cần đăng nhập, một điện thoại, ký bằng token phiên ký, nhân viên theo dõi trạng thái. Dữ liệu giả lập.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class RemoteSigningApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Người bệnh demo có CCCD 001085000001, sinh ngày 12/03/1985 (dữ liệu giả lập).</summary>
    private const string DemoBirthDate = "1985-03-12";

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

    private async Task<string> PendingCk01Async(string receptionist)
    {
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", receptionist);
        var patientId = patients.GetProperty("items")[0].GetProperty("id").GetString();
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var templateId = available.EnumerateArray().Single(t => t.GetProperty("templateCode").GetString() == "CK-01").GetProperty("templateId").GetString();
        var (_, created) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId });
        var id = created.GetProperty("id").GetString()!;
        var data = new
        {
            examType = "bhyt",
            insuranceNumber = "DN4790000000001",
            insuranceValidFrom = "2026-01-01",
            insuranceValidTo = "2026-12-31",
            primaryCareFacility = "Trạm Y tế Phường Mẫu",
            referralPaper = "no",
            acknowledged = true,
        };
        await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/consent-forms/{id}", receptionist, new { data, signerKind = "Patient" });
        var (submitted, body) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{id}/submit", receptionist);
        Assert.True(submitted == HttpStatusCode.OK, body.ToString());
        return id;
    }

    [Fact]
    public async Task Staff_ShowsAQrCode_ThePatientOpensItWithTheBirthDate_AndSignsOnTheirOwnPhone()
    {
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var formId = await PendingCk01Async(receptionist);

        var (createdStatus, link) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{formId}/remote-signing", receptionist);
        Assert.True(createdStatus == HttpStatusCode.OK, link.ToString());
        var code = link.GetProperty("code").GetString()!;
        var linkId = link.GetProperty("id").GetString()!;

        // Máy chủ chỉ giữ mã băm SHA-256 của mã trong QR; nhật ký không có mã.
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var query = new SqlCommand("SELECT CodeHash FROM RemoteSigningLinks WHERE Id = @id", connection);
            query.Parameters.AddWithValue("@id", Guid.Parse(linkId));
            var stored = (string)(await query.ExecuteScalarAsync())!;
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(code))), stored);
            await using var audit = new SqlCommand("SELECT COUNT(*) FROM AuditLogs WHERE Details LIKE @code", connection);
            audit.Parameters.AddWithValue("@code", $"%{code}%");
            Assert.Equal(0, (int)(await audit.ExecuteScalarAsync())!);
        }

        // Trạng thái chỉ dành cho nhân viên đã đăng nhập.
        var (anonymousStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/remote-signing/{linkId}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus);
        var (_, waiting) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/remote-signing/{linkId}", receptionist);
        Assert.Equal("Waiting", waiting.GetProperty("state").GetString());

        // Điện thoại người bệnh (không đăng nhập): sai ngày sinh rồi đúng ngày sinh. Nhật ký ghi trình duyệt của điện thoại.
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("DienThoaiNguoiBenh/1.0");
        var (wrongStatus, wrong) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/remote-signing/claim", null, new { code, dateOfBirth = "1985-03-13" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongStatus);
        Assert.Equal("remote_signing.wrong_birth_date", wrong.GetProperty("code").GetString());
        Assert.Equal(4, wrong.GetProperty("remainingAttempts").GetInt32());

        var (openedStatus, opened) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/remote-signing/claim", null, new { code, dateOfBirth = DemoBirthDate });
        Assert.True(openedStatus == HttpStatusCode.OK, opened.ToString());
        var signingToken = opened.GetProperty("token").GetString()!;
        Assert.False(string.IsNullOrEmpty(opened.GetProperty("deviceKey").GetString()));
        var form = opened.GetProperty("forms")[0];
        Assert.Equal(formId, form.GetProperty("id").GetString());
        Assert.Contains('*', form.GetProperty("patient").GetProperty("citizenId").GetString()!);
        Assert.Equal(string.Empty, form.GetProperty("createdByName").GetString());

        // Điện thoại khác cầm cùng mã không mở được nữa.
        var (otherPhone, other) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/remote-signing/claim", null, new { code, dateOfBirth = DemoBirthDate });
        Assert.Equal(HttpStatusCode.Conflict, otherPhone);
        Assert.Equal("remote_signing.opened_elsewhere", other.GetProperty("code").GetString());

        // Token phiên ký không đọc được phiếu, chỉ ký được.
        var (readWithToken, _) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{formId}", signingToken);
        Assert.True(readWithToken is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, readWithToken.ToString());
        var (signed, signedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{formId}/patient-signature", signingToken,
            new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signed == HttpStatusCode.OK, signedBody.ToString());

        // Nhân viên thấy người bệnh đã ký, rồi xác nhận như thường lệ.
        var (_, done) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/remote-signing/{linkId}", receptionist);
        Assert.Equal("Signed", done.GetProperty("state").GetString());
        var (completed, completedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{formId}/complete", receptionist, new { image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.True(completed == HttpStatusCode.OK, completedBody.ToString());

        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var opens = new SqlCommand(
                "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'ConsentForms.RemoteSigningOpened' AND EntityId = @form AND UserAgent LIKE 'DienThoaiNguoiBenh%'",
                connection);
            opens.Parameters.AddWithValue("@form", formId);
            Assert.Equal(1, (int)(await opens.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Cancel_StopsAPhoneThatAlreadyOpenedTheCode()
    {
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var formId = await PendingCk01Async(receptionist);
        var (_, link) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{formId}/remote-signing", receptionist);
        var (_, opened) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/remote-signing/claim", null, new { code = link.GetProperty("code").GetString(), dateOfBirth = DemoBirthDate });

        var (cancelStatus, cancelled) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/remote-signing/{link.GetProperty("id").GetString()}/cancel", receptionist);
        var (signed, signedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{formId}/patient-signature", opened.GetProperty("token").GetString(),
            new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });

        Assert.Equal(HttpStatusCode.OK, cancelStatus);
        Assert.Equal("Cancelled", cancelled.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, signed);
        Assert.Equal("consent_forms.invalid_status", signedBody.GetProperty("code").GetString());
    }
}
