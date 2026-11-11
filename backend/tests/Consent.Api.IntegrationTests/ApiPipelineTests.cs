using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;

namespace Consent.Api.IntegrationTests;

/// <summary>Pipeline HTTP chung: health, security header, lỗi, mã truy vết. Không cần cơ sở dữ liệu.</summary>
public sealed class ApiPipelineTests(ConsentApiFactory factory) : IClassFixture<ConsentApiFactory>
{
    [Fact]
    public async Task Health_IsAnonymous_AndReturnsHealthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidCorrelationId_IsEchoed_InvalidOneIsReplaced()
    {
        using var client = factory.CreateClient();

        using var valid = new HttpRequestMessage(HttpMethod.Get, "/health");
        valid.Headers.Add("X-Correlation-Id", "phieu-123_abc");
        using var validResponse = await client.SendAsync(valid);

        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/health");
        invalid.Headers.Add("X-Correlation-Id", "chèn nội dung\tgiả");
        using var invalidResponse = await client.SendAsync(invalid);

        Assert.Equal("phieu-123_abc", validResponse.Headers.GetValues("X-Correlation-Id").Single());
        var generated = invalidResponse.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual("chèn nội dung\tgiả", generated);
        Assert.Matches("^[A-Za-z0-9_-]+$", generated);
    }

    [Fact]
    public async Task UnhandledException_ReturnsVietnameseProblemDetails_WithoutInternalDetails()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ConsentApiFactory.ThrowingPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateAccessToken());
        request.Headers.Add("X-Correlation-Id", "loi-he-thong-1");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("SECRET-INTERNAL-DETAIL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Đã xảy ra lỗi hệ thống", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("loi-he-thong-1", json.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task UnknownRoute_ForAnonymous_Returns401_ForAuthenticated_Returns404()
    {
        using var client = factory.CreateClient();

        using var anonymous = await client.GetAsync(new Uri("/khong-ton-tai", UriKind.Relative));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/khong-ton-tai");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateAccessToken());
        using var authenticated = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, authenticated.StatusCode);
        Assert.Equal("application/problem+json", authenticated.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync());
        Assert.Equal("Không tìm thấy tài nguyên.", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task OpenApi_IsNotExposed_OutsideDevelopment()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateAccessToken());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
