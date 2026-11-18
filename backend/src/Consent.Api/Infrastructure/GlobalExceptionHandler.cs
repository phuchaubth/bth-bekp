using Consent.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Consent.Api.Infrastructure;

/// <summary>
/// Chuyển mọi ngoại lệ chưa xử lý thành ProblemDetails (RFC 9457) tiếng Việt.
/// Không trả chi tiết ngoại lệ (stack trace, câu lệnh SQL…) cho client; chi tiết chỉ nằm trong log.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        ProblemDetails problem;
        if (exception is ConflictException conflict)
        {
            LogConflict(logger, httpContext.TraceIdentifier);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = conflict.Message,
                Extensions = { ["code"] = "conflict" },
            };
        }
        else
        {
            LogUnhandledException(logger, exception, httpContext.TraceIdentifier);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Đã xảy ra lỗi hệ thống",
                Detail = "Vui lòng thử lại sau hoặc liên hệ bộ phận CNTT kèm mã truy vết.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lỗi chưa xử lý. TraceId: {TraceId}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string traceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Xung đột dữ liệu khi lưu. TraceId: {TraceId}")]
    private static partial void LogConflict(ILogger logger, string traceId);
}
