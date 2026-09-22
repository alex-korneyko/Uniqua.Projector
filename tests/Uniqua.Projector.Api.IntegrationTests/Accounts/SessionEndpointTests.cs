using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.Antiforgery;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T14 — the sign-in and sign-out endpoints. The load-bearing assertion here is negative: the
/// response to a sign-in that AC-12 held back for thirty seconds has to be byte-identical to one
/// that waited nothing at all. Anything that distinguished them — a header, a field, a different
/// status — would tell an attacker they had found a real account and were being throttled, which
/// is the one thing the delay must not announce.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionEndpointTests(ApiFactory factory)
{
    private const string Accounts = "/api/v1/accounts";
    private const string Sessions = "/api/v1/sessions";
    private const string CurrentSession = "/api/v1/sessions/current";
    private const string Me = "/api/v1/accounts/me";
    private const string GoodPassword = "a-long-enough-password";

    // ---- AC-04: signing in ------------------------------------------------------------------------

    [Fact]
    public async Task Signing_in_returns_the_account_and_a_cookie_that_already_works()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(account.DisplayName, body.GetProperty("display_name").GetString());

        var signedIn = factory.CreateClient();
        signedIn.DefaultRequestHeaders.Add("Cookie", SessionCookieFrom(response));

        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_while_already_holding_a_session_opens_a_second_one()
    {
        // Sessions are per-browser and nothing in the spec forbids two; the first must stay live.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        var first = await SignInAsync(account.Email);
        var second = await SignInAsync(account.Email);

        Assert.NotEqual(first, second);

        foreach (var cookie in new[] { first, second })
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
        }
    }

    [Fact]
    public async Task Signing_in_is_reachable_with_no_session()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = GoodPassword })).StatusCode);
    }

    [Fact]
    public async Task Signing_in_without_the_antiforgery_token_is_refused()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(
            Sessions, new { email = account.Email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- AC-05 / AC-05b / AC-12: one response, however it was reached ----------------------------

    [Fact]
    public async Task The_two_refusals_are_identical_in_status_headers_and_body()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        var wrongPassword = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });
        var unknownAddress = await client.PostAsJsonAsync(
            Sessions, new { email = $"{Guid.NewGuid():N}@example.test", password = GoodPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(unknownAddress.StatusCode, wrongPassword.StatusCode);
        Assert.Equal(
            await NormalisedBodyAsync(unknownAddress), await NormalisedBodyAsync(wrongPassword));
        Assert.Equal(
            InterestingHeaders(unknownAddress), InterestingHeaders(wrongPassword));
    }

    [Fact]
    public async Task A_refusal_held_for_thirty_seconds_reads_exactly_like_one_held_for_nothing()
    {
        // AC-12's delay must be invisible in the response. If a delayed refusal differed in any
        // way, guessing would have found a signal: this address is real and is being throttled.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        var undelayed = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        factory.Clock.ClearRequestedDelays();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await client.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" });
        }

        var delayed = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        // The attempt really was held — otherwise this test would be comparing two identical
        // undelayed responses and proving nothing.
        Assert.True(factory.Clock.LongestRequestedDelay >= TimeSpan.FromSeconds(30));

        Assert.Equal(undelayed.StatusCode, delayed.StatusCode);
        Assert.Equal(await NormalisedBodyAsync(undelayed), await NormalisedBodyAsync(delayed));
        Assert.Equal(InterestingHeaders(undelayed), InterestingHeaders(delayed));
        Assert.Null(delayed.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_refused_sign_in_hands_back_no_cookie()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = $"{Guid.NewGuid():N}@example.test", password = GoodPassword });

        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));
    }

    // ---- AC-08: signing out --------------------------------------------------------------------

    [Fact]
    public async Task Signing_out_ends_this_session_clears_the_cookie_and_returns_no_content()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var cookie = await SignInAsync(account.Email);
        var client = await ClientAsync(cookie);

        var response = await client.DeleteAsync(CurrentSession);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());

        // The cookie is expired rather than merely forgotten by the client.
        var cleared = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);

        // And the session itself is over, whatever a browser still presents (AC-10).
        var stale = factory.CreateClient();
        stale.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_on_one_device_leaves_the_other_device_signed_in()
    {
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var phone = await SignInAsync(account.Email);
        var laptop = await SignInAsync(account.Email);

        var client = await ClientAsync(phone);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(CurrentSession)).StatusCode);

        var stillSignedIn = factory.CreateClient();
        stillSignedIn.DefaultRequestHeaders.Add("Cookie", laptop);

        Assert.Equal(HttpStatusCode.OK, (await stillSignedIn.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_announces_exactly_the_session_that_ended()
    {
        // The AC-09 obligation that is testable today. The channel itself arrives at roadmap
        // step 8; what can be held now is that the revocation is announced, once, for this session.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var cookie = await SignInAsync(account.Email);
        var client = await ClientAsync(cookie);

        var notifier = factory.Services.GetRequiredService<HubSessionRevocationNotifier>();
        notifier.Forget();

        await client.DeleteAsync(CurrentSession);

        Assert.Single(notifier.Recent);
    }

    [Fact]
    public async Task Signing_out_with_no_session_is_refused()
    {
        var client = await ClientAsync();

        var response = await client.DeleteAsync(CurrentSession);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "accounts.session_not_recognised",
            JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Signing_out_twice_is_refused_the_second_time()
    {
        // The handler refuses before the use case is reached: the cookie is now a revoked record.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var cookie = await SignInAsync(account.Email);

        var client = await ClientAsync(cookie);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(CurrentSession)).StatusCode);

        var again = await ClientAsync(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.DeleteAsync(CurrentSession)).StatusCode);
    }

    [Fact]
    public async Task Signing_out_without_the_antiforgery_token_leaves_the_session_live()
    {
        // A forgery attempt must not become a way of signing its target out.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var cookie = await SignInAsync(account.Email);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(CurrentSession)).StatusCode);

        var stillSignedIn = factory.CreateClient();
        stillSignedIn.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await stillSignedIn.GetAsync(Me)).StatusCode);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>Headers a client could read a difference out of. Date and traceId are not those.</summary>
    private static string InterestingHeaders(HttpResponseMessage response) =>
        string.Join(
            '|',
            response.Headers
                .Concat(response.Content.Headers)
                .Where(header => header.Key is not ("Date" or "Server" or "Set-Cookie"))
                .OrderBy(header => header.Key, StringComparer.Ordinal)
                .Select(header => $"{header.Key}={string.Join(',', header.Value)}"));

    private static async Task<string> NormalisedBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        return string.Join('|', document.RootElement.EnumerateObject()
            .Where(member => member.Name is not "traceId")
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}"));
    }

    private static string SessionCookieFrom(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal))
            .Split(';')[0];

    private async Task<string> SignInAsync(string email)
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync(Sessions, new { email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return SessionCookieFrom(response);
    }

    /// <summary>A client holding the antiforgery pair, its own peer, and optionally a session.</summary>
    private async Task<HttpClient> ClientAsync(string? sessionCookie = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestPeerAddress.HeaderName, TestPeerAddress.Fresh());

        var response = await client.GetAsync("/health");
        foreach (var cookie in response.Headers.GetValues("Set-Cookie"))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        }

        if (sessionCookie is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", sessionCookie);
        }

        var token = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        client.DefaultRequestHeaders.Add(
            AntiforgerySetup.HeaderName, token["XSRF-TOKEN=".Length..].Split(';')[0]);

        return client;
    }

    private sealed record Registered(string Email, string DisplayName);

    private async Task<Registered> ARegisteredAccountAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var displayName = $"se-{Guid.NewGuid():N}";

        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = displayName });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new Registered(email, displayName);
    }
}
