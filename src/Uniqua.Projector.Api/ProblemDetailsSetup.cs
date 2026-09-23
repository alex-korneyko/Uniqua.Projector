using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api;

/// <summary>
/// The one place a failure becomes a response body. Every error leaves this application as an
/// RFC 9457 <c>application/problem+json</c> document produced here — endpoints never shape an
/// error of their own (see docs/architecture-map.md § Conventions). What each refusal <em>says</em>
/// is in <see cref="AccountProblems"/>; this file is only the machinery that writes it.
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

                // A missing or malformed JSON body never reaches an endpoint delegate at all —
                // minimal API's own parameter binding fails it before that, and ASP.NET Core
                // writes the framework's generic "Bad Request" ProblemDetails directly through
                // this same customization hook rather than throwing (so no IExceptionHandler ever
                // sees it). Caught here it would ship with no `code` at all (review 2026-09-22-2
                // N-06c); this reshapes it into the one declared problem instead. A wrong
                // Content-Type on the same two endpoints is refused the same way, as an
                // undeclared, uncoded 415 (review 2026-09-23 P-05c) — reshaped into the same
                // declared code rather than adding a second one for what is, from the caller's
                // side, the same "I could not read your body" refusal.
                if ((context.ProblemDetails.Status == StatusCodes.Status400BadRequest
                        || context.ProblemDetails.Status == StatusCodes.Status415UnsupportedMediaType)
                    && !context.ProblemDetails.Extensions.ContainsKey("code")
                    && IsAccountsOrSessionsRequest(context.HttpContext.Request.Path))
                {
                    var problem = AccountProblems.For("accounts.request_malformed");
                    context.ProblemDetails.Type = problem.Type;
                    context.ProblemDetails.Title = problem.Title;
                    context.ProblemDetails.Detail = problem.Detail;
                    context.ProblemDetails.Extensions["code"] = problem.Code;
                }
            });

        services.AddExceptionHandler<UnhandledExceptionHandler>();
        return services;
    }

    internal static bool IsAccountsOrSessionsRequest(PathString path) =>
        path.StartsWithSegments("/api/v1/accounts") || path.StartsWithSegments("/api/v1/sessions");

    /// <summary>
    /// Writes one row of the wording table as the response. This is the only path a refusal takes,
    /// so no endpoint can accidentally publish a different shape for the same code.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// Only AC-01b's rate limit supplies this; it is written as the contract's
    /// <c>retry_after_seconds</c> member and as the standard <c>Retry-After</c> header.
    /// </param>
    public static Task WriteAccountProblemAsync(
        this HttpContext context,
        string code,
        int? retryAfterSeconds = null)
    {
        var problem = AccountProblems.For(code);

        var details = new ProblemDetails
        {
            Type = problem.Type,
            Title = problem.Title,
            Status = problem.Status,
            // A fixed string per code — never a framework message, never an exception message and
            // never an echo of what was submitted.
            Detail = problem.Detail,
            Instance = context.Request.Path,
        };
        details.Extensions["code"] = problem.Code;
        details.Extensions["traceId"] = context.TraceIdentifier;

        if (problem.CarriesRetryAfter && retryAfterSeconds is > 0)
        {
            details.Extensions["retry_after_seconds"] = retryAfterSeconds;
            context.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
        }

        context.Response.StatusCode = problem.Status;
        return context.Response.WriteAsJsonAsync(
            details, options: null, contentType: "application/problem+json");
    }

    /// <inheritdoc cref="WriteAccountProblemAsync(HttpContext, string, int?)"/>
    public static Task WriteAccountProblemAsync(this HttpContext context, AccountError error) =>
        context.WriteAccountProblemAsync(error.Code);
}

/// <summary>
/// Turns an unhandled exception into a ProblemDetails response without leaking the exception
/// itself — the exception goes to the log instead, under the same traceId the response carries.
/// </summary>
internal sealed class UnhandledExceptionHandler(ILogger<UnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The caller is told nothing about what failed, and that is deliberate — but somebody has
        // to be told, or a 500 leaves no trace at all and the only way to learn what threw is to
        // reproduce it. The traceId written into the body below is repeated here, so a report of
        // one failure names exactly one log line.
        logger.LogError(
            exception,
            "module=api event=unhandled_exception method={Method} path={Path} traceId={TraceId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        // Development sets minimal APIs' ThrowOnBadRequest=true, so there a malformed body never
        // reaches the CustomizeProblemDetails hook above as a plain 400 to reshape — it throws
        // instead, and lands here as an unhandled exception (review 2026-09-23 P-05a). Reshaped
        // into the same declared code the "Testing"/"Production" path already answers with, so the
        // caller sees one contract regardless of environment.
        var code = exception is BadHttpRequestException
                && ProblemDetailsSetup.IsAccountsOrSessionsRequest(httpContext.Request.Path)
            ? "accounts.request_malformed"
            : "accounts.unexpected";

        // Written directly through the wording table rather than IProblemDetailsService: the
        // caller must learn nothing about what failed beyond the fixed sentence that code names,
        // and this is the same path every other declared refusal in this file takes.
        await httpContext.WriteAccountProblemAsync(code);
        return true;
    }
}
