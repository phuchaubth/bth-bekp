using Consent.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Consent.Api.Infrastructure;

/// <summary>Chuyển <see cref="Result"/>/<see cref="Error"/> sang phản hồi HTTP chuẩn ProblemDetails (RFC 9457).</summary>
internal static class ProblemDetailsMapping
{
    public const string ProblemJson = "application/problem+json";

    private static readonly Dictionary<int, string> VietnameseTitles = new()
    {
        [StatusCodes.Status400BadRequest] = "Yêu cầu không hợp lệ.",
        [StatusCodes.Status401Unauthorized] = "Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn.",
        [StatusCodes.Status403Forbidden] = "Bạn không có quyền thực hiện thao tác này.",
        [StatusCodes.Status404NotFound] = "Không tìm thấy tài nguyên.",
        [StatusCodes.Status405MethodNotAllowed] = "Phương thức không được hỗ trợ.",
        [StatusCodes.Status409Conflict] = "Dữ liệu bị xung đột.",
        [StatusCodes.Status415UnsupportedMediaType] = "Định dạng dữ liệu không được hỗ trợ.",
        [StatusCodes.Status429TooManyRequests] = "Bạn thao tác quá nhiều lần. Vui lòng thử lại sau ít phút.",
        [StatusCodes.Status500InternalServerError] = "Đã xảy ra lỗi hệ thống",
    };

    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails ToProblemDetails(Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);
        var status = StatusCodeFor(error.Type);
        ProblemDetails details = error.FieldErrors is { Count: > 0 } fieldErrors
            ? new ValidationProblemDetails(fieldErrors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();
        details.Status = status;
        details.Title = error.Message;
        details.Extensions["code"] = error.Code;
        details.Extensions["traceId"] = httpContext.TraceIdentifier;
        return details;
    }

    /// <summary>Dịch tiêu đề mặc định (tiếng Anh) của ASP.NET Core sang tiếng Việt.</summary>
    public static void Localize(ProblemDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Status is not { } status || !VietnameseTitles.TryGetValue(status, out var title))
        {
            return;
        }

        var isDefaultTitle = string.IsNullOrEmpty(details.Title)
            || details.Title == ReasonPhrases.GetReasonPhrase(status)
            || details.Title == "One or more validation errors occurred."
            || details.Title.StartsWith("An error occurred", StringComparison.Ordinal);
        if (isDefaultTitle)
        {
            details.Title = title;
        }
    }

    public static string VietnameseTitle(int status) =>
        VietnameseTitles.TryGetValue(status, out var title) ? title : ReasonPhrases.GetReasonPhrase(status);
}

internal static class ControllerResultExtensions
{
    public static IActionResult ToActionResult<T>(this ControllerBase controller, Result<T> result, Func<T, IActionResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? controller.Ok(result.Value)
            : controller.Problem(result.Error!);
    }

    public static IActionResult ToActionResult(this ControllerBase controller, Result result)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? controller.NoContent() : controller.Problem(result.Error!);
    }

    public static IActionResult Problem(this ControllerBase controller, Error error)
    {
        var details = ProblemDetailsMapping.ToProblemDetails(error, controller.HttpContext);
        return new ObjectResult(details)
        {
            StatusCode = details.Status,
            ContentTypes = { ProblemDetailsMapping.ProblemJson },
        };
    }
}
