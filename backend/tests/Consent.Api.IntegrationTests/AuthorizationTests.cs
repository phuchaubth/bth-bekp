using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Domain.Authorization;
using Microsoft.Extensions.Options;

namespace Consent.Api.IntegrationTests;

/// <summary>Xác thực và phân quyền ở tầng HTTP (không cần cơ sở dữ liệu: bị chặn trước khi vào controller).</summary>
public sealed class AuthorizationTests(ConsentApiFactory factory) : IClassFixture<ConsentApiFactory>
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/v1/users" },
        { "POST", "/api/v1/departments" },
        { "PUT", $"/api/v1/roles/{Guid.Empty}/permissions" },
        { "GET", "/api/v1/audit-logs" },
        { "GET", "/api/v1/auth/me" },
        { "GET", "/api/v1/departments" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ProtectedEndpoint_WithoutToken_Returns401_WithVietnameseProblem(string method, string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.", json.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("POST", "/api/v1/users")]
    [InlineData("POST", "/api/v1/departments")]
    [InlineData("GET", "/api/v1/audit-logs")]
    public async Task TokenWithoutRequiredPermission_Returns403(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateAccessToken(Permissions.ConsentsRead, Permissions.ConsentsCreate));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Bạn không có quyền thực hiện thao tác này.", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        using var client = factory.CreateClient();
        var forged = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().CreateToken(
            new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Issuer = "consent-management",
                Audience = "consent-management-web",
                Expires = DateTime.UtcNow.AddMinutes(5),
                Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["perm"] = Permissions.UsersRead },
                SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey("khoa-gia-mao-khong-dung-0123456789abcdef"u8.ToArray()),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256),
            });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CookieEndpoints_WithoutRequestedWithHeader_AreRejected()
    {
        using var client = factory.CreateClient();

        using var refresh = await client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);
        using var logout = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_ReturnsVietnameseValidationProblem()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{ khong phai json", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/api/v1/auth/login", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Dữ liệu gửi lên không hợp lệ.", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Startup_WithoutSigningKey_Fails()
    {
        using var insecure = new MissingSigningKeyFactory();

        var exception = Assert.ThrowsAny<Exception>(() => insecure.CreateClient());

        Assert.IsType<OptionsValidationException>(exception.GetBaseException());
    }

    private sealed class MissingSigningKeyFactory : ConsentApiFactory
    {
        protected override IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(TestSettings.RequiredSecrets())
        {
            ["ConnectionStrings:Default"] = "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = "",
        };
    }
}
