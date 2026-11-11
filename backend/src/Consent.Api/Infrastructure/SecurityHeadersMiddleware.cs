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
        return next(context);
    }
}
