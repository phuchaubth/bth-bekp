using System.Net;
using System.Text;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Infrastructure.PatientSource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Lấy thông tin hành chính qua API thông tin người bệnh (PatientInfo.Api). API được giả lập bằng bộ xử lý HTTP trả đúng
/// hợp đồng của dịch vụ thật (dữ liệu giả lập); dịch vụ thật có bộ kiểm thử riêng với SQL Server.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class PatientSourceApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001";
    private readonly FakePatientInfoApi _api = new();
    private DatabaseApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new DatabaseApiFactory(
            sql,
            new Dictionary<string, string?>
            {
                ["PatientSource:Provider"] = "Api",
                ["PatientSource:BaseUrl"] = "https://thongtinbn.gia-lap.local/",
                ["PatientSource:ApiKey"] = ApiKey,
            },
            services => services.Replace(ServiceDescriptor.Singleton(new PatientSourceHttp(_api))));
        _client = _factory.CreateHttpsClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task LookupByPatientCodeOrCitizenId_FillsAdministrativeInfo_AndPointsToAnExistingRecord()
    {
        var nurse = (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!;

        var (_, features) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/features", nurse);
        var (byCodeStatus, byCode) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/patients/lookup-source", nurse, new { patientCode = "BN-GL-001" });
        var (_, byCitizenId) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/patients/lookup-source", nurse, new { citizenId = "001085000001" });
        var (_, missing) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/patients/lookup-source", nurse, new { patientCode = "KHONG-CO" });
        var (bothStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/patients/lookup-source", nurse, new { patientCode = "BN-GL-001", citizenId = "079070000123" });

        Assert.True(features.GetProperty("patientSource").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, byCodeStatus);
        Assert.Equal("Found", byCode.GetProperty("status").GetString());
        var patient = byCode.GetProperty("patient");
        Assert.Equal("Lê Văn Thử (giả lập)", patient.GetProperty("fullName").GetString());
        Assert.Equal("1970-02-03", patient.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Male", patient.GetProperty("gender").GetString());
        Assert.Equal("079070000123", patient.GetProperty("citizenId").GetString());
        Assert.Equal("2021-06-01", patient.GetProperty("citizenIdIssueDate").GetString());
        Assert.Equal("070123456", patient.GetProperty("oldIdNumber").GetString());
        Assert.Equal(JsonValueKind.Null, byCode.GetProperty("existingPatient").ValueKind);

        // Người bệnh đã có hồ sơ trong hệ thống (cùng CCCD): trả kèm hồ sơ để mở thay vì tạo trùng.
        Assert.Equal("Female", byCitizenId.GetProperty("patient").GetProperty("gender").GetString());
        Assert.NotEqual(JsonValueKind.Null, byCitizenId.GetProperty("existingPatient").ValueKind);
        Assert.Equal("NotFound", missing.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bothStatus);

        // Khóa API đi trong header, mã/CCCD đi trong thân (không nằm trên URL).
        Assert.All(_api.Requests, r =>
        {
            Assert.Equal("https://thongtinbn.gia-lap.local/api/v1/patients/lookup", r.Url);
            Assert.Equal(ApiKey, r.ApiKey);
        });
        Assert.Equal(3, _api.Requests.Count);
    }

    [Fact]
    public async Task ApiDown_ReportsUnavailable_SoStaffCanTypeManually()
    {
        _api.Down = true;
        var nurse = (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!;

        var (status, body) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/patients/lookup-source", nurse, new { patientCode = "BN-GL-001" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Unavailable", body.GetProperty("status").GetString());
    }

    /// <summary>Giả lập API thông tin người bệnh theo đúng hợp đồng của PatientInfo.Api (dữ liệu giả lập).</summary>
    private sealed class FakePatientInfoApi : HttpMessageHandler
    {
        public List<(string Url, string? ApiKey)> Requests { get; } = [];

        public bool Down { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new HttpRequestException("Không kết nối được (giả lập).");
            }

            Requests.Add((request.RequestUri!.ToString(), request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var code = body.RootElement.TryGetProperty("patientCode", out var c) ? c.GetString() : null;
            var citizenId = body.RootElement.TryGetProperty("citizenId", out var id) ? id.GetString() : null;
            string? json = (code, citizenId) switch
            {
                ("BN-GL-001", _) => """{"patientCode":"BN-GL-001","fullName":"Lê Văn Thử (giả lập)","dateOfBirth":"1970-02-03","gender":"Male","citizenId":"079070000123","phoneNumber":"0900000123","permanentAddress":"Số 1 Đường Giả Lập","currentAddress":null,"ethnicity":"Kinh","occupation":null,"nationality":null,"healthInsuranceNumber":null,"citizenIdIssueDate":"2021-06-01","oldIdNumber":"070123456"}""",
                (_, "001085000001") => """{"patientCode":"BN-GL-002","fullName":"Nguyễn Văn An (giả lập)","dateOfBirth":"1985-03-12","gender":"Female","citizenId":"001085000001","phoneNumber":null,"permanentAddress":null,"currentAddress":null,"ethnicity":null,"occupation":null,"nationality":null,"healthInsuranceNumber":null}""",
                _ => null,
            };
            return json is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
