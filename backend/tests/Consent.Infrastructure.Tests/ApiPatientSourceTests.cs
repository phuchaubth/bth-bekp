using System.Net;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Domain.Patients;
using Consent.Infrastructure.PatientSource;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Tests;

public sealed class ApiPatientSourceTests
{
    private static readonly PatientSourceOptions Settings = new()
    {
        Provider = "Api",
        BaseUrl = "https://thongtinbn.gia-lap.local/goc/",
        ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001",
    };

    [Fact]
    public async Task Found_MapsResponse_AndSendsKeyInHeaderAndIdentifiersInBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"patientCode":"BN-GL-001","fullName":"Lê Văn Thử (giả lập)","dateOfBirth":"1970-02-03","gender":"female","citizenId":"079070000123"}""");

        var result = await Create(handler).FindAsync("BN-GL-001", null, CancellationToken.None);

        Assert.Equal(PatientSourceStatus.Found, result.Status);
        Assert.Equal("Lê Văn Thử (giả lập)", result.Patient!.FullName);
        Assert.Equal(new DateOnly(1970, 2, 3), result.Patient.DateOfBirth);
        Assert.Equal(Gender.Female, result.Patient.Gender);
        Assert.Equal("https://thongtinbn.gia-lap.local/goc/api/v1/patients/lookup", handler.Url);
        Assert.Equal(Settings.ApiKey, handler.ApiKey);
        Assert.Contains("\"patientCode\":\"BN-GL-001\"", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PatientSourceStatus.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, PatientSourceStatus.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PatientSourceStatus.Unavailable)]
    public async Task ErrorStatuses_AreMapped(HttpStatusCode status, PatientSourceStatus expected)
    {
        var result = await Create(new StubHandler(status, "")).FindAsync(null, "079070000123", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task InvalidJsonOrUnknownGender_DoesNotThrow()
    {
        Assert.Equal(PatientSourceStatus.Unavailable,
            (await Create(new StubHandler(HttpStatusCode.OK, "<html>")).FindAsync("BN", null, CancellationToken.None)).Status);
        var odd = await Create(new StubHandler(HttpStatusCode.OK, """{"gender":"99"}""")).FindAsync("BN", null, CancellationToken.None);
        Assert.Null(odd.Patient!.Gender);
    }

    [Fact]
    public async Task NetworkError_IsUnavailable()
    {
        var result = await Create(new StubHandler(null, "")).FindAsync("BN", null, CancellationToken.None);

        Assert.Equal(PatientSourceStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData("https://thongtinbn.benhvien.local/", true)]
    [InlineData("http://localhost:5081/", true)]
    [InlineData("http://thongtinbn.benhvien.local/", false)]
    [InlineData("https://user:pass@thongtinbn.benhvien.local/", false)]
    [InlineData("thongtinbn", false)]
    public void BaseUrl_RequiresHttpsExceptLoopback(string url, bool valid)
    {
        Assert.Equal(valid, new PatientSourceOptions { BaseUrl = url }.HasValidBaseUrl);
    }

    private static ApiPatientSource Create(HttpMessageHandler handler) =>
        new(new PatientSourceHttp(handler), Options.Create(Settings), NullLogger<ApiPatientSource>.Instance);

    private sealed class StubHandler(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? ApiKey { get; private set; }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            ApiKey = request.Headers.GetValues("X-Api-Key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return status is null
                ? throw new HttpRequestException("Không kết nối được (giả lập).")
                : new HttpResponseMessage(status.Value) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
