using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;

namespace Consent.Api.IntegrationTests;

/// <summary>Nhãn môi trường UAT/Production (quyết định 50): giao diện đọc khi chưa đăng nhập để hiện dải "môi trường kiểm thử".</summary>
public sealed class EnvironmentApiTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" UAT ", "UAT")]
    [InlineData("UAT\r\n<script>", "UAT<script>")]
    // Quá 30 ký tự: cắt còn 30 ký tự, bỏ khoảng trắng cuối.
    [InlineData("UAT - môi trường kiểm thử của bệnh viện", "UAT - môi trường kiểm thử của")]
    public async Task ReturnsTheCleanedLabel_WithoutLogin(string? configured, string? expected)
    {
        await using var factory = new LabelFactory(configured);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/environment", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var label = body.RootElement.GetProperty("label");
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, label.ValueKind);
        }
        else
        {
            Assert.Equal(expected, label.GetString());
        }
    }

    private sealed class LabelFactory(string? label) : ConsentApiFactory
    {
        protected override IReadOnlyDictionary<string, string?> Settings
        {
            get
            {
                var settings = new Dictionary<string, string?>(base.Settings);
                if (label is not null)
                {
                    settings["App:EnvironmentLabel"] = label;
                }

                return settings;
            }
        }
    }
}
