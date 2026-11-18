using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>Kết quả đăng nhập/làm mới: access token, cookie refresh token và body JSON.</summary>
public sealed record AuthCall(HttpStatusCode Status, string? AccessToken, string? RefreshCookie, string? SetCookieHeader, JsonElement Body)
{
    public string? Code => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("code", out var c) ? c.GetString() : null;
}

/// <summary>Tiện ích gọi API trong test. Cookie được xử lý thủ công để kiểm tra rõ hành vi xoay vòng refresh token.</summary>
public static class ApiClient
{
    public const string RefreshCookieName = "consent_rt";

    public static HttpClient CreateHttpsClient(this ConsentApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    public static async Task<AuthCall> LoginAsync(this HttpClient client, string userName, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName, password });
        return await ReadAuthCallAsync(response);
    }

    public static async Task<AuthCall> RefreshAsync(this HttpClient client, string? refreshCookie, bool withCsrfHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshCookie}");
        }

        if (withCsrfHeader)
        {
            request.Headers.Add("X-Requested-With", "fetch");
        }

        using var response = await client.SendAsync(request);
        return await ReadAuthCallAsync(response);
    }

    public static async Task<HttpStatusCode> LogoutAsync(this HttpClient client, string refreshCookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshCookie}");
        request.Headers.Add("X-Requested-With", "fetch");
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Đăng nhập quản trị viên khởi tạo và đổi mật khẩu bắt buộc; trả về access token có đủ quyền.</summary>
    public static async Task<string> LoginAsAdministratorAsync(this HttpClient client)
    {
        var login = await client.LoginAsync("admin", TestSettings.AdminPassword);
        if (login.Status != HttpStatusCode.OK)
        {
            // Mật khẩu đã được đổi trong test trước đó với cùng database.
            login = await client.LoginAsync("admin", TestSettings.AdminNewPassword);
            return login.AccessToken!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = TestSettings.AdminPassword, newPassword = TestSettings.AdminNewPassword }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var response = await client.SendAsync(request);
        var changed = await ReadAuthCallAsync(response);
        return changed.Status == HttpStatusCode.OK
            ? changed.AccessToken!
            : throw new InvalidOperationException($"Đổi mật khẩu quản trị thất bại: {changed.Body}");
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> SendJsonAsync(
        this HttpClient client, HttpMethod method, string path, string? accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, json);
    }

    private static async Task<AuthCall> ReadAuthCallAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(RefreshCookieName + "=", StringComparison.Ordinal))
            : null;
        var cookieValue = setCookie?.Split(';')[0][(RefreshCookieName.Length + 1)..];
        var accessToken = response.IsSuccessStatusCode && body.ValueKind == JsonValueKind.Object
            ? body.GetProperty("accessToken").GetString()
            : null;
        return new AuthCall(response.StatusCode, accessToken, string.IsNullOrEmpty(cookieValue) ? null : cookieValue, setCookie, body);
    }

    /// <summary>
    /// Quản lý mẫu phiếu phát hành phiên bản mới của phiếu tư vấn (TUVAN-CONGTT) có thêm bước xác nhận trung gian "người tư
    /// vấn" (tiếp đón) trước xác nhận cuối của điều dưỡng, như trước quyết định 43 (bản giấy QĐ-VHTT-BM02 nay chỉ một người tư
    /// vấn ký), để thử luồng có bước trung gian qua API.
    /// </summary>
    public static async Task PublishPortalConsultationWithConsultantStepAsync(this HttpClient client, string manager)
    {
        var (_, list) = await client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates", manager);
        var id = list.EnumerateArray().Single(t => t.GetProperty("code").GetString() == "TUVAN-CONGTT").GetProperty("id").GetString();
        var (draftStatus, draft) = await client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-templates/{id}/versions", manager);
        Assert.True(draftStatus == HttpStatusCode.Created, draft.ToString());
        var versionNo = draft.GetProperty("versionNo").GetInt32();
        var definition = JsonNode.Parse(draft.GetProperty("definition").GetRawText())!.AsObject();
        definition["signatures"]!["steps"] = new JsonArray(
            new JsonObject { ["key"] = "consultant", ["roleCode"] = "RECEPTIONIST", ["label"] = "Người tư vấn (Consultant)" });
        definition["signatures"]!["hospital"] = "Điều dưỡng khoa (Ward Nurse)";
        var (saved, savedBody) = await client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/consent-templates/{id}/versions/{versionNo}", manager, new { definition });
        Assert.True(saved == HttpStatusCode.OK, savedBody.ToString());
        var (published, publishedBody) = await client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-templates/{id}/versions/{versionNo}/publish", manager);
        Assert.True(published == HttpStatusCode.OK, publishedBody.ToString());
    }
}
