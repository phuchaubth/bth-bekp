using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PatientInfo.Api;

/// <summary>
/// Bắt buộc header <c>X-Api-Key</c> khớp một khóa đã cấu hình cho mọi đường dẫn trừ <c>/health</c>. So sánh thời gian
/// hằng (theo băm SHA-256) để không lộ khóa qua thời gian phản hồi.
/// </summary>
internal sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<PatientInfoOptions> options)
{
    public const string HeaderName = "X-Api-Key";

    private readonly byte[][] keyHashes = options.Value.ApiKeys.Select(Hash).ToArray();

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) || IsAuthorized(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Thiếu hoặc sai khóa API (header X-Api-Key)." }).ConfigureAwait(false);
    }

    private bool IsAuthorized(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1 || string.IsNullOrEmpty(values[0]))
        {
            return false;
        }

        var presented = Hash(values[0]!);
        var match = false;
        foreach (var hash in keyHashes)
        {
            match |= CryptographicOperations.FixedTimeEquals(presented, hash);
        }

        return match;
    }

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}
