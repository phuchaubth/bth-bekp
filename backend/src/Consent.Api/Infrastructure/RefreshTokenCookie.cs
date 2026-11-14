namespace Consent.Api.Infrastructure;

/// <summary>
/// Refresh token được gửi trong cookie HttpOnly (JavaScript không đọc được), SameSite=Strict,
/// chỉ gửi kèm các yêu cầu tới <c>/api/v1/auth</c>. Bắt buộc HTTPS ngoài môi trường Development.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "consent_rt";
    public const string Path = "/api/v1/auth";

    public static string? Read(HttpContext context) => context.Request.Cookies[Name];

    public static void Write(HttpContext context, string refreshToken, DateTimeOffset expiresAtUtc, IHostEnvironment environment) =>
        context.Response.Cookies.Append(Name, refreshToken, CreateOptions(context, environment, expiresAtUtc));

    public static void Clear(HttpContext context, IHostEnvironment environment) =>
        context.Response.Cookies.Delete(Name, CreateOptions(context, environment, expiresAtUtc: null));

    private static CookieOptions CreateOptions(HttpContext context, IHostEnvironment environment, DateTimeOffset? expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps || !environment.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAtUtc,
        IsEssential = true,
    };
}
