using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
}
