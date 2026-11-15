using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Kiosk người bệnh tự làm phiếu (quyết định 24) qua API với SQL Server thật: tài khoản kiosk chỉ dùng được API kiosk,
/// người bệnh quét thẻ và ký một lần cho cả bộ, nhân viên thấy bộ phiếu kèm lời nhắc đối chiếu; tra cứu sai 5 lần thì khóa.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class KioskApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string Statement = "Tôi đã nghe, hiểu và đồng ý";
    private const string TemporaryPassword = "Kiosk@TamThoi2026";
    private const string KioskPassword = "Kiosk@KiemThu2026";

    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;
    private string _admin = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateHttpsClient();
        _admin = await _client.LoginAsAdministratorAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static string Image() => Convert.ToBase64String(TestPng.Signature());

    /// <summary>Tài khoản kiosk mới (tên riêng mỗi test) của Khoa Khám bệnh, đã đổi mật khẩu lần đầu.</summary>
    private async Task<string> NewKioskAccountAsync()
    {
        var (_, roles) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/roles", _admin);
        var kioskRole = roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "KIOSK");
        Assert.True(kioskRole.GetProperty("isLocked").GetBoolean());
        var (_, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments", _admin);
        var kkb = departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "KKB").GetProperty("id").GetString();

        var userName = $"kiosk.{Guid.NewGuid():N}"[..20];
        var (created, user) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/users", _admin, new
        {
            userName,
            fullName = "Kiosk Khoa Khám bệnh",
            departmentId = kkb,
            roleIds = new[] { kioskRole.GetProperty("id").GetString() },
            password = TemporaryPassword,
        });
        Assert.True(created == HttpStatusCode.Created, user.ToString());

        var login = await _client.LoginAsync(userName, TemporaryPassword);
        var (changed, session) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/auth/change-password", login.AccessToken,
            new { currentPassword = TemporaryPassword, newPassword = KioskPassword });
        Assert.True(changed == HttpStatusCode.OK, session.ToString());
        Assert.Equal(["kiosk.use"], session.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        return session.GetProperty("accessToken").GetString()!;
    }

    private async Task<Dictionary<string, string>> ConfigureKioskTemplatesAsync()
    {
        var (_, templates) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates", _admin);
        var ids = new[] { "QT-KNGT-BM02", "TTNB-NGOAITRU", "TUVAN-CONGTT" }.ToDictionary(
            code => code,
            code => templates.EnumerateArray().Single(t => t.GetProperty("code").GetString() == code).GetProperty("id").GetString()!);
        var (saved, settings) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/settings/kiosk", _admin, new { templateIds = ids.Values.ToArray() });
        Assert.True(saved == HttpStatusCode.OK, settings.ToString());
        return ids;
    }

    [Fact]
    public async Task KioskAccount_CanOnlyUseTheKiosk_AndStaffCannotUseTheKioskApi()
    {
        var kiosk = await NewKioskAccountAsync();
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;

        foreach (var path in new[] { "/api/v1/patients", "/api/v1/consent-forms", "/api/v1/consent-templates", "/api/v1/settings/kiosk", "/api/v1/users" })
        {
            var (status, _) = await _client.SendJsonAsync(HttpMethod.Get, path, kiosk);
            Assert.True(status == HttpStatusCode.Forbidden, $"{path}: {status}");
        }

        var (staffStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/kiosk/config", receptionist);
        Assert.Equal(HttpStatusCode.Forbidden, staffStatus);
        var (kioskStatus, config) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/kiosk/config", kiosk);
        Assert.Equal(HttpStatusCode.OK, kioskStatus);
        Assert.Equal("Khoa Khám bệnh", config.GetProperty("departmentName").GetString());
    }

    [Fact]
    public async Task PatientScansTheCard_SignsTheWholeBundleOnce_AndStaffAreToldToCheckTheCard()
    {
        var templates = await ConfigureKioskTemplatesAsync();
        var kiosk = await NewKioskAccountAsync();
        var identity = new
        {
            method = "CitizenIdCard",
            dateOfBirth = "1985-03-12",
            citizenId = $"079085{Random.Shared.Next(100000, 999999)}",
            card = new { fullName = "Hoàng Văn Kiosk (giả lập)", gender = "Male", permanentAddress = "Số 10 Đường Thử Nghiệm, Phường Mẫu" },
        };

        var (configStatus, config) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/kiosk/config", kiosk);
        Assert.Equal(HttpStatusCode.OK, configStatus);
        Assert.True(config.GetProperty("ready").GetBoolean());
        Assert.Equal(3, config.GetProperty("templates").GetArrayLength());

        var (identifiedStatus, identified) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/kiosk/identify", kiosk, identity);
        Assert.True(identifiedStatus == HttpStatusCode.OK, identified.ToString());
        Assert.Equal("Hoàng Văn Kiosk (giả lập)", identified.GetProperty("fullName").GetString());
        Assert.Equal(["phoneNumber"], identified.GetProperty("missingFields").EnumerateArray().Select(f => f.GetString()));

        var (submittedStatus, submitted) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/kiosk/submit", kiosk, new
        {
            identity,
            phoneNumber = "0900000456",
            forms = new object[]
            {
                new { templateId = templates["QT-KNGT-BM02"], data = new { careScheme = "nonInsurance" } },
                new
                {
                    templateId = templates["TTNB-NGOAITRU"],
                    data = new
                    {
                        ethnicity = "Kinh",
                        religion = "buddhism",
                        educationLevel = "12/12",
                        occupation = "Kỹ sư",
                        familyName = "Hoàng Thị Nhà (giả lập)",
                        familyRelationship = "Vợ",
                        familyPhone = "0900000457",
                        familyIsEmergencyContact = "yes",
                    },
                },
                new { templateId = templates["TUVAN-CONGTT"], data = new { }, decision = "Agreed" },
            },
            statement = Statement,
            image = Image(),
        });
        Assert.True(submittedStatus == HttpStatusCode.OK, submitted.ToString());
        Assert.Equal(3, submitted.GetProperty("forms").GetArrayLength());
        var bundleId = submitted.GetProperty("bundleId").GetString();

        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var (bundleStatus, bundle) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/bundles/{bundleId}", receptionist);
        Assert.True(bundleStatus == HttpStatusCode.OK, bundle.ToString());
        Assert.Equal("CitizenIdCard", bundle.GetProperty("kioskIdentity").GetString());
        Assert.Contains(bundle.GetProperty("notices").EnumerateArray(), n => n.GetString()!.Contains("kiosk", StringComparison.Ordinal));
        Assert.True(bundle.GetProperty("canConfirm").GetBoolean());
        Assert.All(bundle.GetProperty("forms").EnumerateArray(), f =>
        {
            Assert.Equal("PendingSignature", f.GetProperty("status").GetString());
            Assert.True(f.GetProperty("patientSigned").GetBoolean());
        });
    }

    [Fact]
    public async Task LookupByCitizenId_WithAWrongBirthDate_IsLockedAfterFiveTries()
    {
        var kiosk = await NewKioskAccountAsync();
        var wrong = new { method = "LookupCode", citizenId = "001085000001", dateOfBirth = "1985-03-13" };

        for (var attempt = 1; attempt < 5; attempt++)
        {
            var (status, problem) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/kiosk/identify", kiosk, wrong);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("kiosk.lookup_failed", problem.GetProperty("code").GetString());
            Assert.Equal(5 - attempt, problem.GetProperty("remainingAttempts").GetInt32());
        }

        var (lockedStatus, locked) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/kiosk/identify", kiosk, wrong);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, lockedStatus);
        Assert.Equal("kiosk.lookup_locked", locked.GetProperty("code").GetString());

        var (rightStatus, right) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/kiosk/identify", kiosk, new { method = "LookupCode", citizenId = "001085000001", dateOfBirth = "1985-03-12" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rightStatus);
        Assert.Equal("kiosk.lookup_locked", right.GetProperty("code").GetString());
    }
}
