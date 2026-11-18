using BEKP.Application.Common;
using BEKP.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BEKP.Api.Middleware;

/// <summary>Maps application and domain errors to RFC 7807 problem details without leaking internals.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            RequestValidationException validation => new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
            },
            ResourceNotFoundException notFound => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = notFound.Message },
            DomainException domain => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Business rule violated", Detail = domain.Message },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception }).ConfigureAwait(false);
    }
}
