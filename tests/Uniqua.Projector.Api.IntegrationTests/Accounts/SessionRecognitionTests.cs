using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T12 — recognising a session, one branch of sad §6 flow 5 per test: live, absent, revoked,
/// 14 days idle, 90 days old. The point of the set is that the four refusals are one outcome: a
/// client is shown the sign-in form and told nothing about which of the four reasons applied,
/// because "regardless of what their browser still holds" (AC-10) means the browser learns
/// nothing from the difference.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionRecognitionTests(ApiFactory factory)
{
    private const string Me = "/api/v1/accounts/me";

    // ---- AC-06: the live branch -----------------------------------------------------------------

    [Fact]
    public async Task A_live_session_is_recognised_and_shown_its_own_account()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = ClientCarrying(account.SessionId);

        var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(account.AccountId.ToString(), body.GetProperty("id").GetString());
        Assert.Equal(account.DisplayName, body.GetProperty("display_name").GetString());
        // Its own account may see its own address; other members never do (AC-11).
        Assert.Equal(account.Email, body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Recognition_costs_one_primary_key_read_and_no_write_inside_the_hour()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = ClientCarrying(account.SessionId);

        // One request to settle anything lazy, then measure the second.
        await client.GetAsync(Me);
        factory.Commands.Clear();

        await client.GetAsync(Me);

        var sessionReads = factory.Commands.Statements
            .Where(sql => sql.Contains("[Sessions]", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(sessionReads);
        Assert.DoesNotContain(
            factory.Commands.Statements,
            sql => sql.Contains("UPDATE [Sessions]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_ordinary_read_after_an_hour_stamps_activity_once_and_extends_the_session()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = ClientCarrying(account.SessionId);

        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);

        // Now push almost the whole idle window past that stamp: still live, because the stamp
        // moved with the request.
        factory.Clock.Advance(Session.IdleLifetime - TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);

        factory.Clock.Reset();
    }

    // ---- The four refusals, which must be one outcome -------------------------------------------

    [Fact]
    public async Task A_request_with_no_cookie_is_not_recognised()
    {
        await AssertNotRecognisedAsync(factory.CreateClient());
    }

    [Fact]
    public async Task A_cookie_that_is_not_a_session_reference_is_not_recognised()
    {
        // Nothing is looked up on a value that cannot be unprotected — and the refusal is the same
        // one a real-but-unknown session gets, so a forged cookie teaches nothing.
        await AssertNotRecognisedAsync(ClientCarryingRaw("not-a-protected-reference"));
    }

    [Fact]
    public async Task A_reference_for_a_session_that_was_never_issued_is_not_recognised()
    {
        await AssertNotRecognisedAsync(ClientCarrying(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task A_revoked_session_is_not_recognised_however_valid_the_cookie_looks()
    {
        // AC-10. The cookie is intact and correctly signed; the record says the session ended.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ISessionStore>()
                .RevokeAsync(account.SessionId, CancellationToken.None);
        }

        await AssertNotRecognisedAsync(ClientCarrying(account.SessionId));
    }

    [Fact]
    public async Task A_session_idle_for_fourteen_days_is_not_recognised()
    {
        // AC-07.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = ClientCarrying(account.SessionId);

        factory.Clock.Advance(Session.IdleLifetime);

        await AssertNotRecognisedAsync(client);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_session_idle_for_fourteen_days_and_thirty_minutes_is_not_recognised()
    {
        // The spec §6 accuracy row allows the ending to be up to an hour late, because the last
        // stamp may be that stale. It does not allow it to be early or indefinite.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = ClientCarrying(account.SessionId);

        factory.Clock.Advance(Session.IdleLifetime + TimeSpan.FromMinutes(30));

        await AssertNotRecognisedAsync(client);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_session_used_continuously_for_ninety_days_is_not_recognised()
    {
        // AC-07b: activity is irrelevant to the absolute ceiling. The session is kept warm the
        // whole way and still ends.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = ClientCarrying(account.SessionId);

        for (var day = 0; day < 89; day++)
        {
            factory.Clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromDays(1));

        await AssertNotRecognisedAsync(client);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task All_four_refusal_causes_produce_byte_identical_bodies()
    {
        factory.Clock.Reset();
        var revoked = await ARegisteredAccountAsync();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ISessionStore>()
                .RevokeAsync(revoked.SessionId, CancellationToken.None);
        }

        var idle = await ARegisteredAccountAsync();
        var idleClient = ClientCarrying(idle.SessionId);

        var bodies = new List<string>
        {
            await BodyOfRefusalAsync(factory.CreateClient()),
            await BodyOfRefusalAsync(ClientCarryingRaw("nonsense")),
            await BodyOfRefusalAsync(ClientCarrying(Guid.CreateVersion7())),
            await BodyOfRefusalAsync(ClientCarrying(revoked.SessionId)),
        };

        factory.Clock.Advance(Session.IdleLifetime);
        bodies.Add(await BodyOfRefusalAsync(idleClient));
        factory.Clock.Reset();

        // instance and traceId are per-request, so they are the only things allowed to differ.
        Assert.Single(bodies.Select(Normalise).Distinct());
    }

    // ---- The cookie itself -----------------------------------------------------------------------

    [Fact]
    public async Task The_session_cookie_cannot_be_read_by_a_page_script_or_sent_cross_site()
    {
        factory.Clock.Reset();
        var setCookie = await IssuedSessionCookieAsync();

        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_reference_in_the_cookie_is_opaque_and_appears_in_no_response_body()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var cookieValue = ProtectedReference(account.SessionId);

        // Not the session id in any readable form: what the browser holds must not be usable as a
        // database key by anything that sees it.
        Assert.DoesNotContain(account.SessionId.ToString(), cookieValue, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            account.SessionId.ToString("N"), cookieValue, StringComparison.OrdinalIgnoreCase);

        var body = await (await ClientCarrying(account.SessionId).GetAsync(Me))
            .Content.ReadAsStringAsync();

        Assert.DoesNotContain(account.SessionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cookieValue, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_opened_before_a_redeploy_is_still_recognised_afterwards()
    {
        // AC-06 and KPI 3. The replacement application shares only the database, so the cookie it
        // accepts was protected by a key ring that genuinely outlived the first process.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        await using var replacement = new ApiFactory.Replacement(factory.ConnectionString, factory.Clock);
        var reference = replacement.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(SessionCookie.ProtectorPurpose)
            .Protect(account.SessionId.ToString());

        var client = replacement.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={reference}");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
    }

    // ---- The handler holds no arithmetic of its own ---------------------------------------------

    [Fact]
    public void The_expiry_rules_appear_nowhere_in_the_api_project()
    {
        // sad §5 puts the two rules on the Session entity so the handler asks rather than
        // re-derives. This is that promise, checked against the source rather than trusted.
        var apiSources = Directory.EnumerateFiles(
            ApiProjectDirectory(), "*.cs", SearchOption.AllDirectories);

        foreach (var file in apiSources)
        {
            var code = File.ReadAllText(file);

            Assert.DoesNotContain("FromDays(14)", code, StringComparison.Ordinal);
            Assert.DoesNotContain("FromDays(90)", code, StringComparison.Ordinal);
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static string ApiProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Uniqua.Projector.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "Uniqua.Projector.Api");
    }

    private static string Normalise(string body)
    {
        using var document = JsonDocument.Parse(body);

        return string.Join('|', document.RootElement.EnumerateObject()
            .Where(member => member.Name is not ("traceId" or "instance"))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}"));
    }

    private async Task AssertNotRecognisedAsync(HttpClient client)
    {
        var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("accounts.session_not_recognised", problem.GetProperty("code").GetString());
        Assert.Equal("Sign in to continue.", problem.GetProperty("detail").GetString());
    }

    private static async Task<string> BodyOfRefusalAsync(HttpClient client)
    {
        var response = await client.GetAsync(Me);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    private string ProtectedReference(Guid sessionId) =>
        factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(SessionCookie.ProtectorPurpose)
            .Protect(sessionId.ToString());

    /// <summary>A client carrying the cookie the application itself would have issued.</summary>
    private HttpClient ClientCarrying(Guid sessionId) => ClientCarryingRaw(ProtectedReference(sessionId));

    private HttpClient ClientCarryingRaw(string cookieValue)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={cookieValue}");

        return client;
    }

    /// <summary>The Set-Cookie header the application produces when it issues a session.</summary>
    private async Task<string> IssuedSessionCookieAsync()
    {
        var account = await ARegisteredAccountAsync();

        using var scope = factory.Services.CreateScope();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        context.Request.IsHttps = true;

        SessionCookie.Issue(context, account.SessionId);

        return context.Response.Headers.SetCookie.ToString();
    }

    private sealed record Registered(Guid AccountId, string Email, string DisplayName, Guid SessionId);

    private async Task<Registered> ARegisteredAccountAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.test";

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<RegisterAccount>()
            .ExecuteAsync(email, "a-long-enough-password", $"me-{Guid.NewGuid():N}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return new Registered(
            result.Value.AccountId, email, result.Value.DisplayName, result.Value.SessionId);
    }
}
