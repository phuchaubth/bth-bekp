using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Consent.Api.Infrastructure;

/// <summary>
/// Chống CSRF cho endpoint xác thực bằng cookie (làm mới phiên, đăng xuất): bắt buộc header tùy chỉnh
/// <c>X-Requested-With</c>. Trình duyệt không cho trang web khác gửi header này nếu không qua CORS preflight.
/// Đây là lớp bảo vệ bổ sung cho cookie SameSite=Strict.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequireRequestedWithHeaderAttribute : Attribute, IResourceFilter
{
    public const string HeaderName = "X-Requested-With";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers[HeaderName]))
        {
            return;
        }

        var details = ProblemDetailsMapping.ToProblemDetails(
            Shared.Error.Forbidden("request.missing_requested_with", "Yêu cầu không hợp lệ."), context.HttpContext);
        context.Result = new ObjectResult(details)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { ProblemDetailsMapping.ProblemJson },
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
