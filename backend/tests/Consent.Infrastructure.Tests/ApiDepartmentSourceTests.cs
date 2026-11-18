using System.Net;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Infrastructure.PatientSource;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Tests;

/// <summary>Lấy danh mục khoa/phòng của HIS qua API thông tin người bệnh (quyết định 42). Dữ liệu giả lập.</summary>
public sealed class ApiDepartmentSourceTests
{
    private static readonly PatientSourceOptions Settings = new()
    {
        Provider = "Api",
        BaseUrl = "https://thongtinbn.gia-lap.local/goc/",
        ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001",
    };

    [Fact]
    public async Task Found_KeepsRowsWithACode_UsesTheCodeForMissingNames_AndSendsTheKeyInTheHeader()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """
            {"items":[
              {"code":" K01 ","name":" Khoa Khám bệnh (giả lập) "},
              {"code":"K02","name":null},
              {"code":"","name":"Khoa không mã"},
              {"name":"Khoa không mã"}
            ]}
            """);

        var result = await Create(handler).GetAsync(CancellationToken.None);

        Assert.Equal(DepartmentSourceStatus.Found, result.Status);
        Assert.Equal([new ExternalDepartment("K01", "Khoa Khám bệnh (giả lập)"), new ExternalDepartment("K02", "K02")], result.Items);
        Assert.Equal("https://thongtinbn.gia-lap.local/goc/api/v1/departments", handler.Url);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(Settings.ApiKey, handler.ApiKey);
    }

    [Fact]
    public async Task NotImplemented_MeansTheQueryIsNotConfigured()
    {
        var result = await Create(new StubHandler(HttpStatusCode.NotImplemented, "")).GetAsync(CancellationToken.None);

        Assert.Equal(DepartmentSourceStatus.NotConfigured, result.Status);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OtherErrorStatuses_AreUnavailable(HttpStatusCode status)
    {
        var result = await Create(new StubHandler(status, "")).GetAsync(CancellationToken.None);

        Assert.Equal(DepartmentSourceStatus.Unavailable, result.Status);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("{}")]
    [InlineData("""{"items":"khong-phai-danh-sach"}""")]
    public async Task UnexpectedBodies_AreUnavailable(string body)
    {
        var result = await Create(new StubHandler(HttpStatusCode.OK, body)).GetAsync(CancellationToken.None);

        Assert.Equal(DepartmentSourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task NetworkError_IsUnavailable()
    {
        var result = await Create(new StubHandler(null, "")).GetAsync(CancellationToken.None);

        Assert.Equal(DepartmentSourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task WithoutTheApi_TheSourceIsDisabled()
    {
        var source = new DisabledDepartmentSource();

        Assert.False(source.IsEnabled);
        Assert.Equal(DepartmentSourceStatus.Disabled, (await source.GetAsync(CancellationToken.None)).Status);
    }

    private static ApiDepartmentSource Create(StubHandler handler) =>
        new(new PatientSourceHttp(handler), Options.Create(Settings), NullLogger<ApiDepartmentSource>.Instance);

    private sealed class StubHandler(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public HttpMethod? Method { get; private set; }

        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Method = request.Method;
            ApiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.Single() : null;
            if (status is null)
            {
                throw new HttpRequestException("Không kết nối được (giả lập).");
            }

            return Task.FromResult(new HttpResponseMessage(status.Value) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
