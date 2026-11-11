using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Consent.Api.Infrastructure;

/// <summary>Cấu hình giới hạn tần suất (mục <c>RateLimiting</c>).</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Số lần đăng nhập/đổi mật khẩu tối đa mỗi địa chỉ IP trong một cửa sổ thời gian.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    /// <summary>Số lần làm mới phiên tối đa mỗi địa chỉ IP trong một cửa sổ thời gian.</summary>
    public int RefreshPermitLimit { get; set; } = 30;

    public int WindowSeconds { get; set; } = 60;
}

internal static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Refresh = "refresh";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        var window = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(Auth, context => FixedWindowByIp(context, options.AuthPermitLimit, window));
            limiter.AddPolicy(Refresh, context => FixedWindowByIp(context, options.RefreshPermitLimit, window));
            limiter.OnRejected = async (rejected, cancellationToken) =>
            {
                var http = rejected.HttpContext;
                if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var problemDetailsService = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = ProblemDetailsMapping.VietnameseTitle(StatusCodes.Status429TooManyRequests),
                        Extensions = { ["code"] = "rate_limited" },
                    },
                }).ConfigureAwait(false);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindowByIp(HttpContext context, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, permitLimit),
                Window = window,
                QueueLimit = 0,
            });
}
