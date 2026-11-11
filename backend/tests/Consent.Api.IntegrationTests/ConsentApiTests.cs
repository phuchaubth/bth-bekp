using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>Mẫu phiếu và phiếu đồng thuận với SQL Server thật: phiên bản mẫu, vòng đời phiếu, phạm vi khoa, mã hóa, audit.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConsentApiTests(SqlServerFixture sql) : IAsyncLifetime
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

    private async Task<string> LoginDemoAsync(string userName)
    {
        var login = await _client.LoginAsync(userName, TestSettings.DemoPassword);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        return login.AccessToken!;
    }

    private async Task<string> DemoPatientIdAsync(string token)
    {
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", token);
        return patients.GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    /// <summary>Mọi giá trị lồng bên trong một phần tử JSON.</summary>
    private static IEnumerable<JsonElement> Descendants(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Descendants(p.Value).Prepend(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(i => Descendants(i).Prepend(i)),
        _ => [],
    };

    private static string TemplateId(JsonElement list, string code) =>
        list.EnumerateArray().Single(t => t.GetProperty("templateCode").GetString() == code).GetProperty("templateId").GetString()!;

    private static object MorseData(bool complete) => new
    {
        assessmentReason = "initial",
        morseFallHistory = "no",
        morseSecondaryDiagnosis = "yes",
        morseAmbulatoryAid = "crutches",
        morseIntravenous = "no",
        morseGait = "weak",
        morseMentalStatus = "oriented",
        measuresStandard = new[] { "callBell", "lowBed" },
        measuresMedium = complete ? new[] { "marker", "assist" } : null,
        reassessAt = complete ? "2026-11-12T08:00" : null,
    };

    [Fact]
    public async Task DemoTemplates_ArePublished_AndManagerCanVersionThem()
    {
        var manager = await LoginDemoAsync("mauphieu.demo");
        var nurse = await LoginDemoAsync("dieuduong.demo");

        var (_, list) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates", manager);
        var ck01 = list.EnumerateArray().Single(t => t.GetProperty("code").GetString() == "CK-01");
        var id = ck01.GetProperty("id").GetString();
        var patientId = await DemoPatientIdAsync(nurse);
        var (_, formV1) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = id });

        var (draftStatus, draft) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-templates/{id}/versions", manager);
        var (invalidStatus, invalid) = await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/consent-templates/{id}/versions/2", manager, new { definition = new { title = "", sections = Array.Empty<object>() } });
        var definitionV2 = JsonNode.Parse(draft.GetProperty("definition").GetRawText())!.AsObject();
        definitionV2["title"] = "Giấy cam kết (phiên bản 2)";
        var (validStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/consent-templates/{id}/versions/2", manager, new { definition = definitionV2 });
        var (_, published) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-templates/{id}/versions/2/publish", manager);
        var (forbidden, _) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-templates", nurse, new { code = "X-01", name = "X", hospitalSignerRoleCode = "NURSE" });

        // Phiếu lập trước khi phát hành phiên bản 2 vẫn theo phiên bản 1; phiếu lập sau dùng phiên bản 2.
        var (_, formV1Reopened) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{formV1.GetProperty("id").GetString()}", nurse);
        var (_, formV2) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = id });
        Assert.Equal(1, formV1Reopened.GetProperty("templateVersionNo").GetInt32());
        Assert.NotEqual("Giấy cam kết (phiên bản 2)", formV1Reopened.GetProperty("definition").GetProperty("title").GetString());
        Assert.Equal(2, formV2.GetProperty("templateVersionNo").GetInt32());
        Assert.Equal("Giấy cam kết (phiên bản 2)", formV2.GetProperty("definition").GetProperty("title").GetString());

        Assert.Equal(["CK-01", "TN-01", "TT-01"], list.EnumerateArray().Select(t => t.GetProperty("code").GetString()));
        Assert.All(list.EnumerateArray(), t => Assert.Equal(1, t.GetProperty("publishedVersionNo").GetInt32()));
        Assert.Equal(HttpStatusCode.Created, draftStatus);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus);
        Assert.Equal("consent_templates.invalid_definition", invalid.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, validStatus);
        var versions = published.GetProperty("versions").EnumerateArray().ToDictionary(v => v.GetProperty("versionNo").GetInt32(), v => v.GetProperty("status").GetString());
        Assert.Equal("Published", versions[2]);
        Assert.Equal("Retired", versions[1]);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
    }

    [Fact]
    public async Task ConsentForm_DraftToPendingSignature_IsValidatedEncryptedAndAudited()
    {
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var patientId = await DemoPatientIdAsync(nurse);
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", nurse);

        var (createdStatus, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = TemplateId(available, "TN-01") });
        Assert.True(createdStatus == HttpStatusCode.Created, created.ToString());
        var formId = created.GetProperty("id").GetString();
        var (_, draft) = await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/consent-forms/{formId}", nurse, new { data = MorseData(complete: false), signerKind = "Patient" });
        var (incompleteStatus, incomplete) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{formId}/submit", nurse);
        await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/consent-forms/{formId}", nurse, new { data = MorseData(complete: true), signerKind = "Patient" });
        var (submittedStatus, submitted) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{formId}/submit", nurse);
        var (_, list) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms?status=PendingSignature", nurse);

        Assert.Equal(HttpStatusCode.Created, createdStatus);
        Assert.Matches("^PDT-\\d{4}-\\d{6}$", created.GetProperty("formNo").GetString()!);
        // Định nghĩa mẫu trả về không có thuộc tính trống (null/false), nhưng vẫn giữ điểm 0 của lựa chọn.
        var definition = created.GetProperty("definition");
        Assert.DoesNotContain(Descendants(definition), v => v.ValueKind is JsonValueKind.Null or JsonValueKind.False);
        Assert.Contains(Descendants(definition), v => v.ValueKind == JsonValueKind.Object
            && v.TryGetProperty("score", out var score) && score.GetInt32() == 0);
        Assert.Equal(40, draft.GetProperty("scores").GetProperty("morseTotal").GetProperty("total").GetInt32());
        Assert.Equal("medium", draft.GetProperty("scores").GetProperty("morseTotal").GetProperty("levelCode").GetString());
        Assert.True(draft.GetProperty("issues").TryGetProperty("measuresMedium", out _));
        Assert.Equal(HttpStatusCode.BadRequest, incompleteStatus);
        Assert.Equal("consent_forms.incomplete", incomplete.GetProperty("code").GetString());
        Assert.True(incomplete.GetProperty("errors").TryGetProperty("reassessAt", out _));
        Assert.Equal(HttpStatusCode.OK, submittedStatus);
        Assert.Equal("PendingSignature", submitted.GetProperty("status").GetString());
        Assert.False(submitted.GetProperty("actions").GetProperty("canEdit").GetBoolean());
        Assert.Equal(formId, list.GetProperty("items")[0].GetProperty("id").GetString());

        // Nội dung phiếu và thông tin đóng băng (có dữ liệu cá nhân) được mã hóa trong CSDL.
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT DataEncrypted, SnapshotEncrypted FROM ConsentForms WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", Guid.Parse(formId!));
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.StartsWith("v1:", reader.GetString(0), StringComparison.Ordinal);
            Assert.DoesNotContain("morse", reader.GetString(0), StringComparison.Ordinal);
            Assert.StartsWith("v1:", reader.GetString(1), StringComparison.Ordinal);
        }

        await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{formId}", nurse);
        using (var pdfRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/consent-forms/{formId}/pdf"))
        {
            pdfRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", nurse);
            using var pdf = await _client.SendAsync(pdfRequest);
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }

        await using var audit = new SqlCommand(
            "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'ConsentForms.Viewed' AND EntityId = @id", connection);
        audit.Parameters.AddWithValue("@id", formId);
        Assert.Equal(1, (int)(await audit.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task FormsOfAnotherDepartment_AreHidden_ExceptForHospitalWideRoles()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var reporter = await LoginDemoAsync("baocao.demo");
        var patientId = await DemoPatientIdAsync(receptionist);
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var (createdStatus, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId = TemplateId(available, "CK-01") });
        Assert.True(createdStatus == HttpStatusCode.Created, created.ToString());
        var formId = created.GetProperty("id").GetString();

        var (nurseStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{formId}", nurse);
        var (_, nurseList) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms", nurse);
        var (reporterStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{formId}", reporter);
        var (reporterCreate, _) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", reporter, new { patientId, templateId = TemplateId(available, "CK-01") });

        Assert.Equal(HttpStatusCode.NotFound, nurseStatus);
        Assert.Equal(0, nurseList.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, reporterStatus);
        Assert.Equal(HttpStatusCode.Forbidden, reporterCreate);
    }
}
