using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// Recognises a session on every authenticated request — sad §6 flow 5, in order: read the
/// reference out of the cookie, read the record through the port, ask the entity whether it is
/// still good, then stamp the request as activity.
/// </summary>
/// <remarks>
/// <para>
/// This lives in Api because it runs in the request pipeline, and it reaches the store only
/// through <see cref="ISessionReader"/> because only Infrastructure may read a database. The
/// arithmetic is emphatically not here: whether a session has expired is
/// <see cref="Domain.Accounts.Session.IsExpired"/>'s answer, so this handler asks rather than
/// re-derives. A test greps this whole project for the two figures to keep it that way.
/// </para>
/// <para>
/// All four ways of not being recognised — no cookie, an unreadable cookie, an unknown or revoked
/// session, an expired one — produce one identical refusal. AC-10 refuses "regardless of what
/// their browser still holds", and a browser that could tell the four apart would be learning
/// something from the difference.
/// </para>
/// </remarks>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISessionReader reader,
    ISessionStore store,
    IClock clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ProjectorSession";

    /// <summary>The account this request is recognised as.</summary>
    public const string AccountIdClaim = "projector:account_id";

    /// <summary>The session it arrived on, so sign-out can end exactly that one (AC-08).</summary>
    public const string SessionIdClaim = "projector:session_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (SessionCookie.Read(Context) is not { } sessionId)
        {
            // No cookie, or one that cannot be unprotected. Nothing is looked up: a malformed
            // value is not worth a database round trip, and refusing without one also means a
            // forged cookie cannot be used to probe timing.
            return AuthenticateResult.NoResult();
        }

        var session = await reader.FindAsync(sessionId, Context.RequestAborted);

        if (session is null || session.IsRevoked)
        {
            return AuthenticateResult.NoResult();
        }

        // The entity owns the verdict. Note that revocation is checked separately above, because
        // the two are independent facts and conflating them would hide which one happened from
        // the logs as well as from the caller.
        if (session.IsExpired(clock.UtcNow))
        {
            return AuthenticateResult.NoResult();
        }

        // The store decides whether this costs a write; inside the hour it does not (spec §6
        // allows the ending to be up to an hour late, and spending that allowance buys one write
        // per session per hour instead of one per request).
        await store.StampActivityAsync(session, Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [
                new Claim(AccountIdClaim, session.AccountId.ToString()),
                new Claim(SessionIdClaim, session.Id.ToString()),
            ],
            SchemeName);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>
    /// The one refusal. It goes through the same wording table every other error in this feature
    /// does, so no endpoint and no handler shapes an error of its own (sad §8).
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Context.WriteAccountProblemAsync("accounts.session_not_recognised");
}

/// <summary>Reads the recognised account and session off a request.</summary>
public static class RecognisedSession
{
    /// <summary>The account id this request was recognised as, or <c>null</c>.</summary>
    public static Guid? AccountId(ClaimsPrincipal principal) =>
        Read(principal, SessionAuthenticationHandler.AccountIdClaim);

    /// <summary>The session this request arrived on, which is the only one sign-out may end.</summary>
    public static Guid? SessionId(ClaimsPrincipal principal) =>
        Read(principal, SessionAuthenticationHandler.SessionIdClaim);

    private static Guid? Read(ClaimsPrincipal principal, string claim) =>
        Guid.TryParse(principal.FindFirst(claim)?.Value, out var value) ? value : null;
}
