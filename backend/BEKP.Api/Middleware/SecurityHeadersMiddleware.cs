namespace BEKP.Api.Middleware;

internal static class SecurityHeadersMiddleware
{
    /// <summary>API, realtime hub and health responses never render HTML.</summary>
    internal const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>The React single-page app served from wwwroot (MUI/Emotion injects styles at runtime).</summary>
    internal const string SpaPolicy =
        "default-src 'self'; script-src 'self'; connect-src 'self' ws: wss:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public static bool IsApiPath(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = IsApiPath(context.Request.Path) ? ApiPolicy : SpaPolicy;
        if (!context.Request.Path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "no-store";
        }

        await next(context).ConfigureAwait(false);
    });
}
