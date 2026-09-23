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
                // Content-Type on the same two endpoints is refused the same way — the framework
                // answers it with a 415, but openapi.yaml declares no 415 on either operation and
                // pairs `accounts.request_malformed` with 400 only (contracts/openapi.yaml lines
                // 103-106, 276-278), so the status is rewritten to 400 alongside the code rather
                // than shipping a 415 whose body claims a code the contract says is a 400 (review
                // 2026-09-23, fix round, P-01).
                if ((context.ProblemDetails.Status == StatusCodes.Status400BadRequest
                        || context.ProblemDetails.Status == StatusCodes.Status415UnsupportedMediaType)
                    && !context.ProblemDetails.Extensions.ContainsKey("code")
                    && IsAccountsOrSessionsRequest(context.HttpContext.Request.Path))
                {
                    var problem = AccountProblems.For("accounts.request_malformed");
                    context.ProblemDetails.Type = problem.Type;
                    context.ProblemDetails.Title = problem.Title;
                    context.ProblemDetails.Status = problem.Status;
                    context.ProblemDetails.Detail = problem.Detail;
                    context.ProblemDetails.Extensions["code"] = problem.Code;
                    context.HttpContext.Response.StatusCode = problem.Status;
                }

                // A body over Kestrel's configured MaxRequestBodySize never reaches an
                // IExceptionHandler at all: minimal API's own JSON body binding catches the
                // BadHttpRequestException Kestrel's transport throws, sets the response status
                // from it and completes without rethrowing, and UseStatusCodePages (wired to this
                // same customization hook once AddProblemDetails is registered) is what turns that
                // bare status into a body — with no `code`, since only the branch above adds one,
                // and only for 400/415 (review 2026-09-23 (third re-review) T-02: confirmed against
                // a real Kestrel socket, not TestServer, which never enforces this limit). This is
                // feature-neutral by design, same as api.unexpected: the limit is Kestrel's own,
                // ahead of any accounts-or-sessions routing, so every route gets the same coded 413.
                if (!context.ProblemDetails.Extensions.ContainsKey("code")
                    && context.ProblemDetails.Status is { } otherStatus
                    && CodeForOtherBadRequestStatus(otherStatus) is { } otherCode)
                {
                    var problem = AccountProblems.For(otherCode);
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(ProblemDetailsSetup).FullName!);

                    // A routine client mistake, not a server failure — logged so the traceId in the
                    // response still has a line behind it, but at Warning and never as
                    // event=unhandled_exception (review 2026-09-23 (third re-review) T-02).
                    logger.LogWarning(
                        "module=api event=request_rejected status={Status} method={Method} "
                            + "path={Path} traceId={TraceId}",
                        problem.Status,
                        context.HttpContext.Request.Method,
                        context.HttpContext.Request.Path,
                        context.HttpContext.TraceIdentifier);

                    context.ProblemDetails.Type = problem.Type;
                    context.ProblemDetails.Title = problem.Title;
                    context.ProblemDetails.Status = problem.Status;
                    context.ProblemDetails.Detail = problem.Detail;
                    context.ProblemDetails.Extensions["code"] = problem.Code;
                    context.HttpContext.Response.StatusCode = problem.Status;
                }
            });

        services.AddExceptionHandler<UnhandledExceptionHandler>();
        return services;
    }

    internal static bool IsAccountsOrSessionsRequest(PathString path) =>
        path.StartsWithSegments("/api/v1/accounts") || path.StartsWithSegments("/api/v1/sessions");

    /// <summary>
    /// The declared code for a <see cref="BadHttpRequestException"/> status this application gives
    /// its own coded 4xx rather than folding into the generic 500 path — only 413 today (review
    /// 2026-09-23 (third re-review) T-02). Shared by the CustomizeProblemDetails hook above (the
    /// path a body-too-large 413 actually takes) and <see cref="UnhandledExceptionHandler"/> below
    /// (a defensive fallback, in case some other 4xx status other than 400 ever does propagate as
    /// an exception instead of a bare status). An unmapped status returns <see langword="null"/> so
    /// it is left alone rather than shipping a status the body's own code disagrees with.
    /// </summary>
    internal static string? CodeForOtherBadRequestStatus(int status) => status switch
    {
        StatusCodes.Status413PayloadTooLarge => "api.request_too_large",
        _ => null,
    };

    /// <summary>
    /// Writes one row of the wording table as the response. This is the only path a refusal takes,
    /// so no endpoint can accidentally publish a different shape for the same code.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// AC-01b's registration limit and AC-12's sign-in limit supply this — no longer registration
    /// only (review 2026-09-23 P-03); it is written as the contract's <c>retry_after_seconds</c>
    /// member and as the standard <c>Retry-After</c> header.
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
        // Development sets minimal APIs' ThrowOnBadRequest=true, so there a malformed body never
        // reaches the CustomizeProblemDetails hook above as a plain 400 to reshape — it throws a
        // BadHttpRequestException instead, and lands here (review 2026-09-23 P-05a). Only its
        // StatusCode == 400 shape — an unreadable body — is that same routine refusal.
        var badRequest = exception as BadHttpRequestException;
        var isMalformedBody = badRequest is { StatusCode: StatusCodes.Status400BadRequest }
            && ProblemDetailsSetup.IsAccountsOrSessionsRequest(httpContext.Request.Path);

        if (isMalformedBody)
        {
            // A malformed body is a routine client mistake, not a server failure — the same
            // refusal the CustomizeProblemDetails hook answers with in every other environment.
            // Logged for traceability under the same traceId the response carries, but at
            // Information and without event=unhandled_exception, so it does not read as an outage
            // when someone greps the log for one (review 2026-09-23, fix round, P-03).
            logger.LogInformation(
                "module=api event=malformed_request_body method={Method} path={Path} traceId={TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);

            await httpContext.WriteAccountProblemAsync("accounts.request_malformed");
            return true;
        }

        // A BadHttpRequestException carrying a status other than 400 — a body over Kestrel's
        // configured MaxRequestBodySize answers 413, among others — is not "malformed JSON", but it
        // is still a routine client mistake, not a server failure, and it still deserves its own
        // declared 4xx rather than falling into the generic 500 path below (review 2026-09-23
        // (third re-review) T-02). In practice the 413 case never reaches here — minimal API's own
        // JSON body binding catches it before it can propagate this far, and the
        // CustomizeProblemDetails hook above is what actually answers it — but this stays as a
        // defensive fallback for any other 4xx BadHttpRequestException that does propagate as an
        // exception, so it still cannot fall through to the unhandled-exception path below.
        if (badRequest is { StatusCode: >= 400 and < 500 and not StatusCodes.Status400BadRequest }
            && ProblemDetailsSetup.CodeForOtherBadRequestStatus(badRequest.StatusCode) is { } badRequestCode)
        {
            logger.LogWarning(
                "module=api event=request_rejected status={Status} method={Method} "
                    + "path={Path} traceId={TraceId}",
                badRequest.StatusCode,
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);

            // WriteAccountProblemAsync sets the response status from the wording table's own row,
            // which is why CodeForOtherBadRequestStatus only ever returns a code whose declared
            // status matches badRequest.StatusCode — the two can never disagree.
            await httpContext.WriteAccountProblemAsync(badRequestCode);
            return true;
        }

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

        // Written directly through the wording table rather than IProblemDetailsService: the
        // caller must learn nothing about what failed beyond the fixed sentence that code names,
        // and this is the same path every other declared refusal in this file takes.
        //
        // api.unexpected, not accounts.unexpected (review 2026-09-23 (third re-review) T-06): this
        // handler answers for every route in the application, including /boom and any future board
        // or card endpoint, so its 500 cannot be named after one feature.
        await httpContext.WriteAccountProblemAsync("api.unexpected");
        return true;
    }
}
