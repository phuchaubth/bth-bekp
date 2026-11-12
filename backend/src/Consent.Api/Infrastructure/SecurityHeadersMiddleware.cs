namespace Consent.Api.Infrastructure;

/// <summary>Thêm các security header mặc định cho mọi phản hồi của API.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // API không bao giờ trả HTML: trình duyệt không được lưu đệm phản hồi có dữ liệu cá nhân.
        if (!headers.ContainsKey("Cache-Control"))
        {
            headers.CacheControl = "no-store";
        }
        return next(context);
    }
}
