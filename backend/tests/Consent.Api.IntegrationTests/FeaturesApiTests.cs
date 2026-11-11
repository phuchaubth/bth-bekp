using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Consent.Api.IntegrationTests;

/// <summary>Cờ tính năng cho giao diện và cấu hình HIS (hiện chưa kết nối HIS và SmartCA: mặc định tắt).</summary>
public sealed class FeaturesApiTests
{
    [Fact]
    public async Task ByDefault_HisAndSmartCaAreNotConnected()
    {
        await using var factory = new HisFactory(provider: null);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateAccessToken());

        using var response = await client.GetAsync(new Uri("/api/v1/features", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("his").GetBoolean());
        Assert.False(body.RootElement.GetProperty("smartCa").GetBoolean());
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        await using var factory = new HisFactory(provider: null);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/features", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FakeHis_InTesting_IsReportedAsConnected()
    {
        await using var factory = new HisFactory(provider: "Fake");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateAccessToken());

        using var response = await client.GetAsync(new Uri("/api/v1/features", UriKind.Relative));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("his").GetBoolean());
    }

    [Fact]
    public async Task FakeHis_IsRefusedInProduction()
    {
        await using var factory = new HisFactory(provider: "Fake", environment: "Production");

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("không được dùng ở môi trường Production", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownProvider_IsRefusedAtStartup()
    {
        await using var factory = new HisFactory(provider: "FptEHospital");

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("His:Provider chỉ nhận", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class HisFactory(string? provider, string environment = "Testing") : ConsentApiFactory
    {
        protected override IReadOnlyDictionary<string, string?> Settings
        {
            get
            {
                var settings = new Dictionary<string, string?>(base.Settings);
                if (provider is not null)
                {
                    settings["His:Provider"] = provider;
                }

                return settings;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
        }
    }
}
