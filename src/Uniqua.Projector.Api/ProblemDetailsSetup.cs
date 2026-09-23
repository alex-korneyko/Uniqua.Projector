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
                    Apply(context, problem, problem.Status!.Value);
                }

                // A request the framework rejects before any endpoint reads it — a body over
                // Kestrel's configured MaxRequestBodySize (413), one trickling in below its
                // MinRequestBodyDataRate (408), and the like — often never reaches an
                // IExceptionHandler at all: minimal API's own body binding catches the
                // BadHttpRequestException Kestrel's transport throws, the response keeps the
                // exception's status and completes without rethrowing, and UseStatusCodePages
                // (wired to this same customization hook once AddProblemDetails is registered) is
                // what turns that bare status into a body — with no `code`, since the branch above
                // adds one for 400/415 on accounts and sessions only (review 2026-09-23 (third
                // re-review) T-02: confirmed against a real Kestrel socket, not TestServer, which
                // never enforces this limit). Every status such a rejection can carry gets a code
                // here, not only 413 (review of T58, finding 2): 413 its own api.request_too_large,
                // every other one the api.request_rejected family at that same status. This is
                // feature-neutral by design, same as api.unexpected: the limits are Kestrel's own,
                // ahead of any accounts-or-sessions routing.
                if (!context.ProblemDetails.Extensions.ContainsKey("code")
                    && context.ProblemDetails.Status is { } rejectedStatus
                    && RejectionStatuses.Contains(rejectedStatus))
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(ProblemDetailsSetup).FullName!);

                    LogRequestRejected(logger, context.HttpContext, rejectedStatus);
                    Apply(context, AccountProblems.For(CodeForRejectedRequest(rejectedStatus)), rejectedStatus);
                }
            });

        services.AddExceptionHandler<UnhandledExceptionHandler>();
        return services;
    }

    internal static bool IsAccountsOrSessionsRequest(PathString path) =>
        path.StartsWithSegments("/api/v1/accounts") || path.StartsWithSegments("/api/v1/sessions");

    /// <summary>
    /// Every status a <see cref="BadHttpRequestException"/> can carry when Kestrel or minimal API's
    /// own binding rejects a request before an endpoint reads it — Kestrel's RequestRejectionReason
    /// set (400, 405, 408, 411, 413, 414, 431) and minimal API's 415. When one reaches the
    /// CustomizeProblemDetails hook as a bare status with no code yet, it is answered as a coded
    /// rejection (review of T58, finding 2). A status outside this set — a 404 for a missing static
    /// file, say — is not a rejected request and is left to whoever set it.
    /// </summary>
    private static readonly HashSet<int> RejectionStatuses =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status405MethodNotAllowed,
        StatusCodes.Status408RequestTimeout,
        StatusCodes.Status411LengthRequired,
        StatusCodes.Status413PayloadTooLarge,
        StatusCodes.Status414UriTooLong,
        StatusCodes.Status415UnsupportedMediaType,
        StatusCodes.Status431RequestHeaderFieldsTooLarge,
    ];

    /// <summary>
    /// The declared code for a request rejected with a 4xx <paramref name="status"/>: 413 has its
    /// own api.request_too_large (review 2026-09-23 (third re-review) T-02); every other 4xx is the
    /// api.request_rejected family, written at that same status (review of T58, finding 2).
    /// </summary>
    internal static string CodeForRejectedRequest(int status) =>
        status == StatusCodes.Status413PayloadTooLarge ? "api.request_too_large" : "api.request_rejected";

    /// <summary>
    /// A rejected request is a routine client mistake, not a server failure — logged so the traceId
    /// in the response still has a line behind it, but at Warning and never as
    /// event=unhandled_exception (review 2026-09-23 (third re-review) T-02).
    /// </summary>
    internal static void LogRequestRejected(ILogger logger, HttpContext httpContext, int status) =>
        logger.LogWarning(
            "module=api event=request_rejected status={Status} method={Method} "
                + "path={Path} traceId={TraceId}",
            status,
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

    /// <summary>Reshapes the framework's own problem into one row of the wording table.</summary>
    private static void Apply(ProblemDetailsContext context, AccountProblem problem, int status)
    {
        context.ProblemDetails.Type = problem.Type;
        context.ProblemDetails.Title = problem.Title;
        context.ProblemDetails.Status = status;
        context.ProblemDetails.Detail = problem.Detail;
        context.ProblemDetails.Extensions["code"] = problem.Code;
        context.HttpContext.Response.StatusCode = status;
    }

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

        // A family row has no status of its own to write; only WriteRequestRejectedAsync, which is
        // handed the rejection's status, may publish one.
        var status = problem.Status ?? throw new InvalidOperationException(
            $"{code} takes its status from the rejected request; use WriteRequestRejectedAsync.");

        return context.WriteProblemAsync(problem, status, retryAfterSeconds);
    }

    /// <summary>
    /// Writes a request rejected with a 4xx <paramref name="status"/> — 413 as
    /// api.request_too_large, every other one as the api.request_rejected family at that same
    /// status (review of T58, finding 2) — through the same path every other refusal takes.
    /// </summary>
    public static Task WriteRequestRejectedAsync(this HttpContext context, int status)
    {
        if (status is < 400 or > 499)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status), status, "A rejected request answers a 4xx status.");
        }

        return context.WriteProblemAsync(
            AccountProblems.For(ProblemDetailsSetup.CodeForRejectedRequest(status)), status, null);
    }

    /// <inheritdoc cref="WriteAccountProblemAsync(HttpContext, string, int?)"/>
    public static Task WriteAccountProblemAsync(this HttpContext context, AccountError error) =>
        context.WriteAccountProblemAsync(error.Code);

    private static Task WriteProblemAsync(
        this HttpContext context,
        AccountProblem problem,
        int status,
        int? retryAfterSeconds)
    {
        var details = new ProblemDetails
        {
            Type = problem.Type,
            Title = problem.Title,
            Status = status,
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

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            details, options: null, contentType: "application/problem+json");
    }
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
        // StatusCode == 400 shape — an unreadable body — is that same routine refusal; a 415 is
        // folded in with it for the same reason the CustomizeProblemDetails hook folds it in
        // (openapi.yaml pairs a wrong Content-Type with this same 400 on both operations).
        var badRequest = exception as BadHttpRequestException;
        var isMalformedBody = badRequest?.StatusCode
                is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType
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

        // Every other 4xx BadHttpRequestException — a body over Kestrel's configured
        // MaxRequestBodySize is 413, one trickling in below its MinRequestBodyDataRate is 408, and
        // 411 and 431 are similar — is not "malformed JSON", but it is still a routine client
        // mistake, not a server failure, and it answers its own status as a coded problem rather
        // than falling into the generic 500 path below (review 2026-09-23 (third re-review) T-02).
        // Not only 413 (review of T58, finding 2): an unmapped status used to drop through to
        // event=unhandled_exception here, the exact outage-shaped log T-02 named, so the
        // api.request_rejected family answers every status that has no row of its own. The 413
        // case mostly arrives as a bare status in the CustomizeProblemDetails hook instead, which
        // maps it the same way through CodeForRejectedRequest.
        if (badRequest is { StatusCode: >= 400 and < 500 })
        {
            ProblemDetailsSetup.LogRequestRejected(logger, httpContext, badRequest.StatusCode);
            await httpContext.WriteRequestRejectedAsync(badRequest.StatusCode);
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
