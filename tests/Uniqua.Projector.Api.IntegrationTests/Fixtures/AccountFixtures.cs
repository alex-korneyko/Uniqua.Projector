using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// The fixture builders data-model.md § Test fixtures names, so the quality suites state which
/// situation they are testing rather than assembling it inline each time.
/// </summary>
/// <remarks>
/// Every address is on <c>example.test</c>, which is reserved by RFC 2606 and can never route.
/// That is the PII guard the data model asks for: a fixture that used a real-looking domain would
/// eventually be pasted into something that tries to send to it.
/// </remarks>
public static class AccountFixtures
{
    public const string Password = "a-long-enough-password";

    /// <summary>An account, registered through the real use case.</summary>
    public static async Task<TestAccount> AnAccountAsync(this ApiFactory factory)
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var displayName = $"qg-{Guid.NewGuid():N}";

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<RegisterAccount>()
            .ExecuteAsync(email, Password, displayName, CancellationToken.None);

        Assert.True(result.IsSuccess);

        return new TestAccount(result.Value.AccountId, email, displayName, result.Value.SessionId);
    }

    /// <summary>
    /// A session that is live as of the clock's present instant. The store writes its timestamps
    /// from IClock, so the fixtures below can shift them relatively and stay in the same frame of
    /// reference the rules are evaluated in.
    /// </summary>
    public static async Task<Guid> ALiveSessionAsync(this ApiFactory factory, TestAccount account)
    {
        using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider
            .GetRequiredService<ISessionStore>()
            .OpenAsync(account.Id, CancellationToken.None);

        return session.Id;
    }

    /// <summary>
    /// A session one minute past the AC-07 boundary: idle for 14 days and a minute, so it must not
    /// be recognised.
    /// </summary>
    public static async Task<Guid> AnIdleSessionAsync(this ApiFactory factory, TestAccount account)
    {
        var sessionId = await factory.ALiveSessionAsync(account);
        var idleFor = Session.IdleLifetime + TimeSpan.FromMinutes(1);

        await factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Sessions]
            SET [LastSeenAt] = DATEADD(minute, -{(int)idleFor.TotalMinutes}, [LastSeenAt])
            WHERE [Id] = '{sessionId}';
            """);

        return sessionId;
    }

    /// <summary>
    /// A session one minute past the AC-07b ceiling and <em>actively used</em> — last seen now.
    /// This is the boundary that proves activity cannot lift the absolute lifetime.
    /// </summary>
    public static async Task<Guid> AnAgedSessionAsync(this ApiFactory factory, TestAccount account)
    {
        var sessionId = await factory.ALiveSessionAsync(account);
        var age = Session.AbsoluteLifetime + TimeSpan.FromMinutes(1);

        await factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Sessions]
            SET [CreatedAt] = DATEADD(minute, -{(int)age.TotalMinutes}, [CreatedAt])
            WHERE [Id] = '{sessionId}';
            """);

        return sessionId;
    }

    /// <summary>A session the account signed out of.</summary>
    public static async Task<Guid> ARevokedSessionAsync(this ApiFactory factory, TestAccount account)
    {
        var sessionId = await factory.ALiveSessionAsync(account);

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<ISessionStore>()
            .RevokeAsync(sessionId, CancellationToken.None);

        return sessionId;
    }

    /// <summary>
    /// An account with a guessing history, written straight into the two columns the delay curve
    /// reads — so a test can stand at the 10th failure without paying for ten verifications.
    /// </summary>
    public static async Task<TestAccount> AnAccountUnderGuessingAsync(
        this ApiFactory factory,
        int failures)
    {
        var account = await factory.AnAccountAsync();

        // The clock's instant, never the database's. data-model.md § Test fixtures requires it, and
        // the difference is not cosmetic: with the database's "now" the stored attempt would sit in
        // the test clock's future, and the 15-minute reset would never be reached however far the
        // test advanced the clock.
        await factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[AspNetUsers]
            SET [AccessFailedCount] = {failures},
                [LastFailedAttemptAt] = '{factory.Clock.UtcNow:O}'
            WHERE [Id] = '{account.Id}';
            """);

        return account;
    }

    /// <summary>A client carrying this session's cookie, as the browser would present it.</summary>
    public static HttpClient ClientCarrying(this ApiFactory factory, Guid sessionId)
    {
        var reference = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(SessionCookie.ProtectorPurpose)
            .Protect(sessionId.ToString());

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={reference}");

        return client;
    }

    /// <summary>A client with its own apparent peer and the antiforgery pair a write needs.</summary>
    public static async Task<HttpClient> AWritingClientAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestPeerAddress.HeaderName, TestPeerAddress.Fresh());

        var response = await client.GetAsync("/health");
        foreach (var cookie in response.Headers.GetValues("Set-Cookie"))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        }

        var token = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        client.DefaultRequestHeaders.Add(
            Antiforgery.AntiforgerySetup.HeaderName, token["XSRF-TOKEN=".Length..].Split(';')[0]);

        return client;
    }
}

/// <summary>An account and the things a test needs to act as it.</summary>
/// <param name="Id">The account's stable identity.</param>
/// <param name="Email">Always on <c>example.test</c>.</param>
/// <param name="DisplayName">What other members would see.</param>
/// <param name="SessionId">The session registration opened.</param>
public sealed record TestAccount(Guid Id, string Email, string DisplayName, Guid SessionId);
