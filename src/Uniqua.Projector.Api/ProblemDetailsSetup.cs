using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Uniqua.Projector.Api;

/// <summary>
/// The one place a failure becomes a response body. Every error leaves this application as an
/// RFC 9457 <c>application/problem+json</c> document produced here — endpoints never shape an
/// error of their own (see docs/architecture-map.md § Conventions).
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddProblemDetailsHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            });

        services.AddExceptionHandler<UnhandledExceptionHandler>();
        return services;
    }
}

/// <summary>
/// Turns an unhandled exception into a 500 ProblemDetails without leaking the exception itself.
/// </summary>
internal sealed class UnhandledExceptionHandler(IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
            },
        });
    }
}
