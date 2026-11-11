using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>Người bệnh với SQL Server thật: mã hóa cột, chỉ mục mù, che dữ liệu, HIS giả lập, phân quyền, audit.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PatientsApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    // Số CCCD thử nghiệm (đúng cấu trúc, không phải của người thật). Số cuối lẻ → HIS giả lập trả "Dịch vụ".
    private const string NewCitizenId = "036188000125";
    private const string DemoCitizenId = "001085000001";

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

    private static object NewPatient(string citizenId = NewCitizenId, string phone = "0900000123") => new
    {
        fullName = "Phạm Thị Dung (giả lập)",
        dateOfBirth = "1988-04-05",
        gender = "Female",
        citizenId,
        citizenIdIssueDate = "2021-06-01",
        permanentAddress = "Số 9 Đường Thử Nghiệm, Phường Mẫu",
        phoneNumber = phone,
        ethnicity = "Kinh",
    };

    [Fact]
    public async Task Create_StoresSensitiveFieldsEncrypted_AndVerifiesWithHis()
    {
        var (status, patient) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, NewPatient("036188000123"));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(patient.GetProperty("hisVerified").GetBoolean());
        Assert.Equal("BN88000123", patient.GetProperty("patientCode").GetString());
        Assert.Equal("036188000123", patient.GetProperty("citizenId").GetString());

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT CitizenIdEncrypted, PhoneNumberEncrypted, CitizenIdHash FROM Patients WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", Guid.Parse(patient.GetProperty("id").GetString()!));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var encryptedCitizenId = reader.GetString(0);
        var encryptedPhone = reader.GetString(1);
        Assert.StartsWith("v1:", encryptedCitizenId, StringComparison.Ordinal);
        Assert.DoesNotContain("036188000123", encryptedCitizenId, StringComparison.Ordinal);
        Assert.DoesNotContain("0900000123", encryptedPhone, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", reader.GetString(2));
    }

    [Fact]
    public async Task List_MasksCitizenId_AndFindsByFullCitizenIdOrNameWithoutAccents()
    {
        var (_, byCitizenId) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/patients?keyword={DemoCitizenId}", _admin);
        var (_, byName) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=nguyen%20van%20an", _admin);
        var (_, all) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients", _admin);

        var found = Assert.Single(byCitizenId.GetProperty("items").EnumerateArray());
        Assert.Equal("0010******01", found.GetProperty("citizenIdMasked").GetString());
        Assert.Equal("Nguyễn Văn An (giả lập)", byName.GetProperty("items")[0].GetProperty("fullName").GetString());
        Assert.DoesNotContain(DemoCitizenId, all.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_WithExistingCitizenId_Returns409_WithExistingPatientId()
    {
        var (_, first) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, NewPatient());
        var (status, problem) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, NewPatient());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("patients.duplicate_citizen_id", problem.GetProperty("code").GetString());
        Assert.Equal(first.GetProperty("id").GetString(), problem.GetProperty("existingPatientId").GetString());
    }

    [Fact]
    public async Task Create_WithCitizenIdNotMatchingBirthYear_Returns400()
    {
        var request = new { fullName = "Người giả lập", dateOfBirth = "1999-01-01", gender = "Female", citizenId = NewCitizenId };

        var (status, problem) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, request);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Số CCCD không khớp với năm sinh.", problem.GetProperty("errors").GetProperty("citizenId")[0].GetString());
    }

    [Fact]
    public async Task Lookup_ReturnsExistingPatient_AndHisStatus()
    {
        var (status, existing) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients/lookup", _admin, new { citizenId = DemoCitizenId });
        var (_, notFound) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients/lookup", _admin, new { citizenId = "036188000120" });
        var (_, unavailable) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients/lookup", _admin, new { citizenId = "036188000999" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Nguyễn Văn An (giả lập)", existing.GetProperty("existingPatient").GetProperty("fullName").GetString());
        Assert.Equal("Found", existing.GetProperty("hisStatus").GetString());
        Assert.Equal("Dịch vụ", existing.GetProperty("hisPatient").GetProperty("coverageType").GetString());
        Assert.Equal(JsonValueKind.Null, notFound.GetProperty("existingPatient").ValueKind);
        Assert.Equal("NotFound", notFound.GetProperty("hisStatus").GetString());
        Assert.Equal("Unavailable", unavailable.GetProperty("hisStatus").GetString());
    }

    [Fact]
    public async Task HisUnavailable_PatientIsStillCreated_ButMarkedNotVerified()
    {
        var (status, patient) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, NewPatient("036188000999"));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(patient.GetProperty("hisVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, patient.GetProperty("patientCode").ValueKind);
    }

    [Fact]
    public async Task ViewingAndLookups_AreAudited_WithoutSensitiveValues()
    {
        var (_, created) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, NewPatient());
        var id = created.GetProperty("id").GetString();
        await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/patients/{id}", _admin);
        await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/patients/{id}/representatives", _admin, new
        {
            fullName = "Phạm Văn Chồng (giả lập)", relationship = "Spouse", citizenId = "036085000777", dateOfBirth = "1985-01-01",
            gender = "Male", phoneNumber = "0900000777",
        });
        await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients/lookup", _admin, new { citizenId = NewCitizenId });

        var (_, viewed) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/audit-logs?action=Patients.Viewed&entityId={id}", _admin);
        Assert.Equal(1, viewed.GetProperty("totalCount").GetInt32());

        var (_, logs) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/audit-logs?pageSize=100", _admin);
        var text = logs.GetRawText();
        foreach (var secret in new[] { NewCitizenId, "0900000123", "036085000777", "0900000777", DemoCitizenId })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }

        Assert.Contains(logs.GetProperty("items").EnumerateArray(), a =>
            a.GetProperty("entityType").GetString() == "LegalRepresentative" && a.GetProperty("action").GetString() == "Create");
    }

    [Fact]
    public async Task Permissions_ReadOnlyRoleCannotCreate_ClinicalRoleCan()
    {
        var auditor = await _client.LoginAsync("kiemtra.demo", TestSettings.DemoPassword);
        var nurse = await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword);

        var (auditorRead, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients", auditor.AccessToken);
        var (auditorCreate, _) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", auditor.AccessToken, NewPatient());
        var (nurseCreate, _) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", nurse.AccessToken, NewPatient());

        Assert.Equal(HttpStatusCode.OK, auditorRead);
        Assert.Equal(HttpStatusCode.Forbidden, auditorCreate);
        Assert.Equal(HttpStatusCode.Created, nurseCreate);
    }

    [Fact]
    public async Task Update_ChangesData_AndRepresentativeCanBeRemoved()
    {
        var (_, created) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", _admin, NewPatient());
        var id = created.GetProperty("id").GetString();

        var (updatedStatus, updated) = await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/patients/{id}", _admin, new
        {
            fullName = "Phạm Thị Dung (giả lập)", dateOfBirth = "1988-04-05", gender = "Female", citizenId = NewCitizenId,
            currentAddress = "Địa chỉ tạm trú giả lập", occupation = "Kế toán",
        });
        var (_, withRepresentative) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/patients/{id}/representatives", _admin,
            new { fullName = "Người thân (giả lập)", relationship = "Sibling" });
        var representativeId = withRepresentative.GetProperty("representatives")[0].GetProperty("id").GetString();
        var (removedStatus, removed) = await _client.SendJsonAsync(
            HttpMethod.Delete, $"/api/v1/patients/{id}/representatives/{representativeId}", _admin);

        Assert.Equal(HttpStatusCode.OK, updatedStatus);
        Assert.Equal("Kế toán", updated.GetProperty("occupation").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("phoneNumber").ValueKind);
        Assert.Equal(HttpStatusCode.OK, removedStatus);
        Assert.Empty(removed.GetProperty("representatives").EnumerateArray());
    }
}
