using System.Net;
using System.Net.Sockets;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// The account and session endpoints. Each one translates: it reads the request, calls a use case,
/// and turns the outcome into the contract's response. No endpoint in this file decides what is
/// legal and none builds an error body — the wording table in
/// <see cref="AccountProblems"/> is the only thing that shapes a refusal (sad §8).
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCurrentAccount();

        endpoints.MapPost("/api/v1/accounts", async (
            RegisterAccountRequest request,
            HttpContext context,
            RegisterAccount register,
            RegistrationRateLimit limit,
            ILogger<RegistrationRateLimit> logger,
            CancellationToken cancellationToken) =>
        {
            var source = RequestSource.Of(context, logger);

            // Before the use case, so a refused registration cannot have touched the store — which
            // is flow 3's postcondition of "no account, no session, no counter".
            // The slot is reserved rather than merely checked, so concurrent submissions from one
            // source cannot overrun it.
            var decision = limit.Reserve(source);
            if (!decision.IsPermitted)
            {
                // sad §7 asks for the source on this line. The submitted address is never logged.
                logger.LogWarning(
                    "module=accounts event=registration_rate_limited source={Source}", source);

                await context.WriteAccountProblemAsync(
                    "accounts.registration_rate_limited", decision.RetryAfterSeconds);
                return;
            }

            Result<RegisteredAccount, AccountError> result;
            try
            {
                result = await register.ExecuteAsync(
                    request.Email ?? string.Empty,
                    request.Password ?? string.Empty,
                    request.DisplayName ?? string.Empty,
                    cancellationToken);
            }
            catch
            {
                limit.Release(source, decision);
                throw;
            }

            if (!result.IsSuccess)
            {
                // AC-01b counts accounts registered. A refused submission created none, so the
                // visitor correcting it keeps every slot they had.
                limit.Release(source, decision);
                await context.WriteAccountProblemAsync(result.Error!);
                return;
            }

            // AC-01: the session is carried back immediately, so nothing asks them to sign in.
            SessionCookie.Issue(context, result.Value.SessionId);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                new AccountView(
                    result.Value.AccountId, result.Value.Email, result.Value.DisplayName),
                cancellationToken);
        })
        // security: [] — the one endpoint a stranger has to be able to reach.
        .AllowAnonymous()
        .WithName("registerAccount");

        endpoints.MapPost("/api/v1/sessions", async (
            CreateSessionRequest request,
            HttpContext context,
            SignIn signIn,
            SignInRateLimit limit,
            ILogger<SignInRateLimit> logger,
            CancellationToken cancellationToken) =>
        {
            var source = RequestSource.Of(context, logger);

            // Reserved before the password is verified at all (review 2026-09-22-2 N-01): a client
            // that hangs up, or several running in parallel, still consume the slot they took,
            // because none of that changes what the reservation itself already recorded. Keyed by
            // (source, normalised address) with a looser per-source ceiling across all addresses
            // (review 2026-09-23 P-01), and skipped entirely when the source is unresolved.
            var reservation = limit.Reserve(source, request.Email ?? string.Empty);
            if (reservation.Skipped)
            {
                // spec §6.1: a missing remote address must not make every such caller share one
                // budget, so the cap is skipped rather than keyed on the literal "unknown" — said
                // loudly, since it means this caller's failures are never capped at all.
                logger.LogWarning(
                    "module=accounts event=sign_in_rate_limit_skipped "
                    + "consequence=uncapped_failures_for_this_source");
            }
            else if (!reservation.IsPermitted)
            {
                logger.LogWarning(
                    "module=accounts event=sign_in_rate_limited source={Source}", source);

                await context.WriteAccountProblemAsync(
                    "accounts.sign_in_rate_limited", reservation.RetryAfterSeconds);
                return;
            }

            var result = await signIn.ExecuteAsync(
                request.Email ?? string.Empty, request.Password ?? string.Empty, cancellationToken);

            if (!result.IsSuccess)
            {
                // Whatever happened — wrong password, unknown address, or an attempt AC-12 held
                // back for thirty seconds — this is the same call producing the same response. The
                // delay is spent before we get here and leaves no trace in what is written, which
                // is what keeps it from announcing that the account is under attack. The reserved
                // slot is left exactly as it is: a failure is what the cap exists to count.
                await context.WriteAccountProblemAsync(result.Error!);
                return;
            }

            // A correct password is never counted against the cap, from any source (AC-12).
            limit.Release(reservation);

            SessionCookie.Issue(context, result.Value.SessionId);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                new AccountView(
                    result.Value.AccountId, result.Value.Email, result.Value.DisplayName),
                cancellationToken);
        })
        // security: [] — signing in is what a visitor with no session comes here to do.
        .AllowAnonymous()
        .WithName("createSession");

        endpoints.MapDelete("/api/v1/sessions/current", async (
            HttpContext context,
            SignOut signOut,
            CancellationToken cancellationToken) =>
        {
            // The session the request arrived on, taken from what recognition established rather
            // than from anything the caller said. That is how AC-08's "and only that one" holds:
            // there is no way to name someone else's session.
            var sessionId = RecognisedSession.SessionId(context.User);
            if (sessionId is null)
            {
                await context.WriteAccountProblemAsync("accounts.session_not_recognised");
                return;
            }

            await signOut.ExecuteAsync(sessionId.Value, cancellationToken);

            SessionCookie.Clear(context);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        })
        .RequireAuthorization()
        .WithName("deleteCurrentSession");

        return endpoints;
    }
}

/// <summary>
/// The sign-in body. Note what is absent: no format or length constraint on either field. A
/// pre-check here would refuse a malformed address faster than a wrong password, and that
/// difference in speed is itself an answer to "is this address registered?".
/// </summary>
/// <param name="Email">The address as typed; normalised by the application.</param>
/// <param name="Password">Verified against the stored hash, or against a dummy one (AC-05b).</param>
public sealed record CreateSessionRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("email")] string? Email,
    [property: System.Text.Json.Serialization.JsonPropertyName("password")] string? Password);

/// <summary>The registration body, exactly as the contract's RegisterAccountRequest states it.</summary>
/// <param name="Email">Normalised by the application; the normalised form carries the unique index.</param>
/// <param name="Password">Bounded by the acceptance criteria, not by a column. Only the hash is stored.</param>
/// <param name="DisplayName">What other board members see (AC-11).</param>
public sealed record RegisterAccountRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("email")] string? Email,
    [property: System.Text.Json.Serialization.JsonPropertyName("password")] string? Password,
    [property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string? DisplayName);

/// <summary>
/// Where a request came from, for the purpose of rate-limiting it.
/// </summary>
public static class RequestSource
{
    /// <summary>
    /// The value <see cref="Of"/> returns when no request address could be resolved — never a
    /// real request source, so a caller can compare against it directly (review 2026-09-23 P-01:
    /// <see cref="SignInRateLimit"/> skips its cap entirely for this value, rather than keying a
    /// cap on it and giving every such caller one shared budget).
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// The key both AC-01b's registration limit and <see cref="SignInRateLimit"/>'s two sign-in
    /// caps count against — no longer registration only (review 2026-09-23 P-03).
    /// </summary>
    /// <remarks>
    /// The forwarded headers middleware has already decided whether to believe a proxy's report:
    /// it rewrites <c>RemoteIpAddress</c> only for a request that arrived from a configured proxy,
    /// and leaves the connection's own address otherwise. So reading the connection address here
    /// is exactly "the proxy's report only when the request came from the proxy" — anyone else
    /// presenting a forwarded header is ignored, without which the limit would be decorative.
    /// </remarks>
    public static string Of(HttpContext context, ILogger logger)
    {
        if (context.Connection.RemoteIpAddress is { } address)
        {
            return Key(address);
        }

        // Every visitor then shares one key, which spec §6.1 names as a failure mode rather than a
        // detail — so it is said loudly instead of passed over. Affects every rate limit keyed on
        // request source, registration and sign-in alike (review 2026-09-23 P-03).
        logger.LogError(
            "module=accounts event=request_source_unknown "
            + "consequence=rate_limits_shared_by_all_callers");

        return Unknown;
    }

    /// <summary>
    /// The key one address contributes: an IPv4-mapped IPv6 address (<c>::ffff:a.b.c.d</c>) is
    /// keyed by its plain IPv4 form, so it shares a budget with a client seen as <c>a.b.c.d</c>
    /// directly (review 2026-09-23 P-02); any other IPv6 address is keyed by its /64 prefix — the
    /// block an ISP hands one customer — so rotating within it does not buy a fresh budget per
    /// address; a plain IPv4 address is keyed as itself.
    /// </summary>
    private static string Key(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        var prefix = new byte[bytes.Length];
        Array.Copy(bytes, prefix, 8);

        return new IPAddress(prefix).ToString();
    }
}
