using System.Net;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Infrastructure.PatientSource;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Tests;

/// <summary>Lấy số liệu trong ngày cho bảng tổng hợp điều dưỡng qua API thông tin người bệnh (quyết định 31).</summary>
public sealed class ApiDailyStatisticsSourceTests
{
    private static readonly PatientSourceOptions Settings = new()
    {
        Provider = "Api",
        BaseUrl = "https://thongtinbn.gia-lap.local/goc/",
        ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001",
    };

    private static readonly DateOnly Day = new(2026, 11, 16);

    [Fact]
    public async Task Found_KeepsValidRows_AndSendsTheKeyInTheHeaderAndTheDateInTheBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """
            {"items":[
              {"indicator":"outpatientVisits","departmentCode":null,"value":686},
              {"indicator":" waitingAdmission ","departmentCode":" NOIA ","value":2},
              {"indicator":"","value":1},
              {"indicator":"xray","value":-1},
              {"indicator":"mri","value":null}
            ]}
            """);

        var result = await Create(handler).GetAsync(Day, CancellationToken.None);

        Assert.Equal(DailyStatisticsSourceStatus.Found, result.Status);
        Assert.Equal(
            [new ExternalDailyStatistic("outpatientVisits", null, 686), new ExternalDailyStatistic("waitingAdmission", "NOIA", 2)],
            result.Items);
        Assert.Equal("https://thongtinbn.gia-lap.local/goc/api/v1/daily-statistics", handler.Url);
        Assert.Equal(Settings.ApiKey, handler.ApiKey);
        Assert.Contains("\"date\":\"2026-11-16\"", handler.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotImplemented)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ErrorStatuses_AreUnavailable(HttpStatusCode status)
    {
        var result = await Create(new StubHandler(status, "")).GetAsync(Day, CancellationToken.None);

        Assert.Equal(DailyStatisticsSourceStatus.Unavailable, result.Status);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("{}")]
    [InlineData("""{"items":"khong-phai-danh-sach"}""")]
    public async Task UnexpectedBodies_AreUnavailable(string body)
    {
        var result = await Create(new StubHandler(HttpStatusCode.OK, body)).GetAsync(Day, CancellationToken.None);

        Assert.Equal(DailyStatisticsSourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task NetworkError_IsUnavailable()
    {
        var result = await Create(new StubHandler(null, "")).GetAsync(Day, CancellationToken.None);

        Assert.Equal(DailyStatisticsSourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task WithoutTheApi_TheSourceIsDisabled()
    {
        var source = new DisabledDailyStatisticsSource();

        Assert.False(source.IsEnabled);
        Assert.Equal(DailyStatisticsSourceStatus.Disabled, (await source.GetAsync(Day, CancellationToken.None)).Status);
    }

    private static ApiDailyStatisticsSource Create(StubHandler handler) =>
        new(new PatientSourceHttp(handler), Options.Create(Settings), NullLogger<ApiDailyStatisticsSource>.Instance);

    private sealed class StubHandler(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? ApiKey { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            ApiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.Single() : null;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (status is null)
            {
                throw new HttpRequestException("Không kết nối được (giả lập).");
            }

            return new HttpResponseMessage(status.Value) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
