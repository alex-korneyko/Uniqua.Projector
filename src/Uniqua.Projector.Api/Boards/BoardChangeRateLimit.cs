using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Boards;

/// <summary>
/// AC-17: at most 120 board change attempts per account per rolling minute — every create, rename,
/// reorder, edit or delete of a board, column or card, whichever board it names, whether it is
/// then accepted or refused for any other reason. An endpoint filter on the <c>/api/v1/boards</c>
/// route group (sad.md §5), so it runs after the session is recognised and before the membership
/// check, and it depends on the account alone: the refusal is identical whichever board was named.
/// </summary>
/// <remarks>
/// <para>
/// Reserve-then-keep: a slot is taken before the endpoint runs and is never handed back, whatever
/// the outcome. An attempt this limit refuses takes no slot (<see cref="SlidingWindowLimiter"/>), so
/// an account that stops is admitted again once its earlier attempts leave the minute.
/// </para>
/// <para>
/// Not counted: reads (<c>GET</c>); a request with no recognised session (AC-28), which the
/// authorization middleware answers before any endpoint filter runs; a refused antiforgery token,
/// answered earlier still; and a body that is not JSON, which minimal API's binding refuses before
/// the filter pipeline is entered (sad.md §8).
/// </para>
/// <para>
/// In memory and per instance, like the registration and sign-in limits (sad.md §7).
/// </para>
/// </remarks>
public sealed class BoardChangeRateLimit : IEndpointFilter
{
    /// <summary>AC-17's figure.</summary>
    public const int PermittedPerWindow = 120;

    /// <summary>AC-17's "within the past minute".</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly SlidingWindowLimiter _limiter;
    private readonly ILogger<BoardChangeRateLimit> _logger;

    public BoardChangeRateLimit(IClock clock, ILogger<BoardChangeRateLimit> logger)
    {
        _logger = logger;
        _limiter = new SlidingWindowLimiter(
            clock, PermittedPerWindow, Window, logger, "board_change_per_account", "new_accounts_uncapped");
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (HttpMethods.IsGet(httpContext.Request.Method)
            || RecognisedSession.AccountId(httpContext.User) is not { } accountId)
        {
            return await next(context);
        }

        var decision = _limiter.Reserve(accountId.ToString());
        if (!decision.IsPermitted)
        {
            // sad.md §7 asks for this refusal by account. Nothing about the named board is logged.
            _logger.LogWarning(
                "module=boards event=change_rate_limited account={AccountId}", accountId);

            await httpContext.WriteBoardProblemAsync(
                BoardProblems.ChangeRateLimited, retryAfterSeconds: decision.RetryAfterSeconds);
            return Results.Empty;
        }

        return await next(context);
    }
}
