using System.Diagnostics;

namespace Consent.Api.Infrastructure;

/// <summary>
/// Gán mã truy vết cho mỗi yêu cầu (nhận từ header <c>X-Correlation-Id</c> nếu hợp lệ, nếu không thì tự sinh),
/// trả lại trong header phản hồi, đưa vào log và nhật ký audit.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (!IsValid(correlationId))
        {
            correlationId = Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");
        }

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    /// <summary>Chỉ chấp nhận ký tự an toàn để tránh chèn nội dung giả vào log.</summary>
    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
