using System.Net;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Infrastructure.PatientSource;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Tests;

/// <summary>Tra lượt tiếp nhận qua API thông tin người bệnh (quyết định 25).</summary>
public sealed class ApiEncounterSourceTests
{
    private static readonly PatientSourceOptions Settings = new()
    {
        Provider = "Api",
        BaseUrl = "https://thongtinbn.gia-lap.local/goc/",
        ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001",
    };

    private static readonly DateOnly Day = new(2026, 11, 11);

    [Fact]
    public async Task Found_MapsItemsNewestFirst_AndSendsKeyInHeaderAndIdentifiersInBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """
            {"items":[
              {"encounterNo":"TN-0001","receivedAt":"2026-11-11T07:30:00","place":" Phòng khám 1 ","receivedBy":"Người tiếp đón giả lập"},
              {"encounterNo":" TN-0002 ","receivedAt":"2026-11-11T10:45:00","place":null,"receivedBy":"  "},
              {"encounterNo":"","receivedAt":"2026-11-11T11:00:00"},
              {"encounterNo":"TN-0003","receivedAt":null}
            ]}
            """);

        var result = await Create(handler).FindAsync("BN-GL-001", null, Day, Day, CancellationToken.None);

        Assert.Equal(EncounterSourceStatus.Found, result.Status);
        Assert.Equal(
            [
                new ExternalEncounter("TN-0002", new DateTime(2026, 11, 11, 10, 45, 0), null, null),
                new ExternalEncounter("TN-0001", new DateTime(2026, 11, 11, 7, 30, 0), "Phòng khám 1", "Người tiếp đón giả lập"),
            ],
            result.Encounters);
        Assert.Equal("https://thongtinbn.gia-lap.local/goc/api/v1/encounters/search", handler.Url);
        Assert.Equal(Settings.ApiKey, handler.ApiKey);
        Assert.Contains("\"patientCode\":\"BN-GL-001\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"from\":\"2026-11-11\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"to\":\"2026-11-11\"", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotImplemented)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ErrorStatuses_AreUnavailable(HttpStatusCode status)
    {
        var result = await Create(new StubHandler(status, "")).FindAsync(null, "079070000123", Day, Day, CancellationToken.None);

        Assert.Equal(EncounterSourceStatus.Unavailable, result.Status);
        Assert.Empty(result.Encounters);
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("{}")]
    [InlineData("""{"items":"khong-phai-danh-sach"}""")]
    public async Task UnexpectedBodies_AreUnavailable(string body)
    {
        var result = await Create(new StubHandler(HttpStatusCode.OK, body)).FindAsync("BN", null, Day, Day, CancellationToken.None);

        Assert.Equal(EncounterSourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task NetworkError_IsUnavailable()
    {
        var result = await Create(new StubHandler(null, "")).FindAsync("BN", null, Day, Day, CancellationToken.None);

        Assert.Equal(EncounterSourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task DisabledSource_NeverCallsOut()
    {
        var source = new DisabledEncounterSource();

        Assert.False(source.IsEnabled);
        Assert.Equal(EncounterSourceStatus.Disabled, (await source.FindAsync("BN", null, Day, Day, CancellationToken.None)).Status);
    }

    private static ApiEncounterSource Create(HttpMessageHandler handler) =>
        new(new PatientSourceHttp(handler), Options.Create(Settings), NullLogger<ApiEncounterSource>.Instance);

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
