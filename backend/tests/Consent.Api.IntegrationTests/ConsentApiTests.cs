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

        // Bốn mẫu thật do ứng dụng khởi tạo, cộng ba mẫu minh họa do bộ kiểm thử thêm.
        Assert.Equal(
            ["CK-01", "QT-KNGT-BM02", "QT-KNGT-BM10", "TN-01", "TT-01", "TTNB-NGOAITRU", "TUVAN-CONGTT"],
            list.EnumerateArray().Select(t => t.GetProperty("code").GetString()).Order(StringComparer.Ordinal));
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

    [Fact]
    public async Task HospitalInsuranceConsent_IsSignedWithoutAgreeOrRefuse_AndCompletedByReception()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        var patientId = await DemoPatientIdAsync(receptionist);
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var (createdStatus, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId = TemplateId(available, "QT-KNGT-BM02") });
        Assert.True(createdStatus == HttpStatusCode.Created, created.ToString());
        Assert.Equal("none", created.GetProperty("definition").GetProperty("signatures").GetProperty("decision").GetString());
        var form = $"/api/v1/consent-forms/{created.GetProperty("id").GetString()}";
        await _client.SendJsonAsync(HttpMethod.Put, form, receptionist, new { data = new { careScheme = "nonInsurance" }, signerKind = "Patient" });
        var (submittedStatus, submitted) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", receptionist);
        Assert.True(submittedStatus == HttpStatusCode.OK, submitted.ToString());

        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", receptionist);
        // Mẫu thật bắt buộc lời xác nhận của người ký.
        var (noStatementStatus, noStatement) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString()!,
            new { image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.Equal(HttpStatusCode.BadRequest, noStatementStatus);
        Assert.True(noStatement.GetProperty("errors").TryGetProperty("statement", out _));
        var (signedStatus, signed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString()!,
            new { image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        var (completedStatus, completed) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Convert.ToBase64String(TestPng.Signature()) });

        Assert.True(signedStatus == HttpStatusCode.OK, signed.ToString());
        Assert.True(completedStatus == HttpStatusCode.OK, completed.ToString());
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.True(
            !completed.TryGetProperty("decision", out var decision) || decision.ValueKind == JsonValueKind.Null,
            completed.ToString());
    }

    [Fact]
    public async Task PortalConsultation_ConsultantConfirmsStep_ThenOnlyAWardNurseMayComplete()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        var patientId = await DemoPatientIdAsync(receptionist);
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var (_, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId = TemplateId(available, "TUVAN-CONGTT") });
        var formId = created.GetProperty("id").GetString()!;
        var form = $"/api/v1/consent-forms/{formId}";
        await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", receptionist);
        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", receptionist);
        var (signedStatus, signed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString()!,
            new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signedStatus == HttpStatusCode.OK, signed.ToString());

        var (completeTooEarly, early) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Convert.ToBase64String(TestPng.Signature()) });
        var (stepStatus, afterStep) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/confirm-step", receptionist, new { stepKey = "consultant", image = Convert.ToBase64String(TestPng.Signature()) });
        var (againStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/confirm-step", receptionist, new { stepKey = "consultant", image = Convert.ToBase64String(TestPng.Signature()) });
        var (completeWrongRole, wrongRole) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Convert.ToBase64String(TestPng.Signature()) });

        Assert.Equal(HttpStatusCode.Conflict, completeTooEarly);
        Assert.Contains("Người tư vấn", early.GetProperty("title").GetString()!, StringComparison.Ordinal);
        Assert.True(stepStatus == HttpStatusCode.OK, afterStep.ToString());
        var step = afterStep.GetProperty("steps")[0];
        Assert.Equal("consultant", step.GetProperty("key").GetString());
        Assert.Equal("Tiếp đón Demo", step.GetProperty("confirmation").GetProperty("signerName").GetString());
        Assert.False(afterStep.GetProperty("actions").GetProperty("canConfirm").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, againStatus);
        Assert.Equal(HttpStatusCode.Forbidden, completeWrongRole);
        Assert.Equal("consent_forms.signer_role_required", wrongRole.GetProperty("code").GetString());

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Role, StepKey FROM ConsentSignatures WHERE FormId = @id AND StepKey IS NOT NULL", connection);
        command.Parameters.AddWithValue("@id", Guid.Parse(formId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Staff", reader.GetString(0));
        Assert.Equal("consultant", reader.GetString(1));
    }

    [Fact]
    public async Task HandwrittenSigning_OnATablet_CompletesWithAStoredHashedPdf_ThenRevokeAndReplace()
    {
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var head = await LoginDemoAsync("truongkhoa.demo");
        var patientId = await DemoPatientIdAsync(nurse);
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", nurse);
        var (_, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = TemplateId(available, "TN-01") });
        var formId = created.GetProperty("id").GetString()!;
        var form = $"/api/v1/consent-forms/{formId}";
        await _client.SendJsonAsync(HttpMethod.Put, form, nurse, new { data = MorseData(complete: true), signerKind = "Patient" });
        var (submittedStatus, _) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", nurse);
        Assert.Equal(HttpStatusCode.OK, submittedStatus);

        // Mở phiên ký: token phiên ký chỉ dùng để người bệnh ký đúng phiếu này.
        var (sessionStatus, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", nurse);
        Assert.Equal(HttpStatusCode.OK, sessionStatus);
        var signingToken = session.GetProperty("token").GetString()!;
        var (listWithSigningToken, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms", signingToken);
        var (pdfWithSigningToken, _) = await _client.SendJsonAsync(HttpMethod.Get, $"{form}/pdf", signingToken);
        var (signWithLoginToken, _) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", nurse, new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.Equal(HttpStatusCode.Unauthorized, listWithSigningToken);
        Assert.Equal(HttpStatusCode.Unauthorized, pdfWithSigningToken);
        Assert.Equal(HttpStatusCode.Unauthorized, signWithLoginToken);

        // Khung ký trống bị từ chối (422); ký thật thì được nhận đúng một lần.
        var (blankStatus, blank) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", signingToken, new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Blank()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        var (signedStatus, signed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", signingToken, new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        var (againStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", signingToken, new { decision = "Refused", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankStatus);
        Assert.Equal("consent_forms.signature_blank", blank.GetProperty("code").GetString());
        Assert.True(signedStatus == HttpStatusCode.OK, signed.ToString());
        Assert.Equal(HttpStatusCode.Conflict, againStatus);

        // Phía bệnh viện (điều dưỡng: vai trò xác nhận của TN-01) xác nhận bằng tài khoản → hoàn tất.
        var (completedStatus, completed) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", nurse, new { image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.True(completedStatus == HttpStatusCode.OK, completed.ToString());
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal("Agreed", completed.GetProperty("decision").GetString());
        Assert.Equal(2, completed.GetProperty("signatures").GetArrayLength());
        var sha256 = completed.GetProperty("signedPdfSha256").GetString()!;

        // PDF tải về đúng là file đã lưu (khớp SHA-256); file trên đĩa được mã hóa.
        using (var pdfRequest = new HttpRequestMessage(HttpMethod.Get, $"{form}/pdf"))
        {
            pdfRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", nurse);
            using var pdf = await _client.SendAsync(pdfRequest);
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal(sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        }

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using (var command = new SqlCommand(
            "SELECT f.SignedPdfKey, f.SignedPdfSha256, s.ImagePngEncrypted FROM ConsentForms f " +
            "JOIN ConsentSignatures s ON s.FormId = f.Id AND s.Method = 'Handwritten' WHERE f.Id = @id", connection))
        {
            command.Parameters.AddWithValue("@id", Guid.Parse(formId));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var file = Path.Combine(_factory.DocumentsPath, reader.GetString(0).Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(sha256, reader.GetString(1));
            Assert.StartsWith("v1:", reader.GetString(2), StringComparison.Ordinal);
            var onDisk = System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file));
            Assert.StartsWith("CDOC", onDisk, StringComparison.Ordinal);
            Assert.DoesNotContain("%PDF", onDisk, StringComparison.Ordinal);
        }

        // Phiếu hoàn tất không sửa được; điều dưỡng không có quyền thu hồi; trưởng khoa thu hồi phải có lý do.
        var (editStatus, _) = await _client.SendJsonAsync(HttpMethod.Put, form, nurse, new { data = MorseData(complete: true), signerKind = "Patient" });
        var (nurseRevoke, _) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/revoke", nurse, new { reason = "Ghi sai thông tin" });
        var (noReason, _) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/revoke", head, new { reason = "" });
        var (revokedStatus, revoked) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/revoke", head, new { reason = "Ghi sai thông tin" });
        Assert.Equal(HttpStatusCode.Conflict, editStatus);
        Assert.Equal(HttpStatusCode.Forbidden, nurseRevoke);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        Assert.Equal("Revoked", revoked.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, revokedStatus);

        // Lập phiếu thay thế tham chiếu phiếu cũ.
        var (replacementStatus, replacement) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = TemplateId(available, "TN-01"), replacesFormId = formId });
        Assert.True(replacementStatus == HttpStatusCode.Created, replacement.ToString());
        Assert.Equal(formId, replacement.GetProperty("replacesForm").GetProperty("id").GetString());

        await using var audit = new SqlCommand(
            "SELECT COUNT(DISTINCT Action) FROM AuditLogs WHERE EntityId = @id AND Action IN " +
            "('ConsentForms.SigningStarted', 'ConsentForms.PatientSigned', 'ConsentForms.Completed', 'ConsentForms.Revoked')", connection);
        audit.Parameters.AddWithValue("@id", formId);
        Assert.Equal(4, (int)(await audit.ExecuteScalarAsync())!);
    }
}
