using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.Antiforgery;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T13 — <c>POST /api/v1/accounts</c>. The end-to-end reading of AC-01: a stranger arrives with no
/// session, submits the form once, and is signed in — proven by the cookie that comes back being
/// immediately accepted by a request reserved for a signed-in account, rather than by inspecting
/// the cookie and hoping.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RegisterEndpointTests(ApiFactory factory)
{
    private const string Accounts = "/api/v1/accounts";
    private const string Me = "/api/v1/accounts/me";
    private const string GoodPassword = "a-long-enough-password";

    // ---- AC-01, end to end -----------------------------------------------------------------------

    [Fact]
    public async Task Registering_returns_the_account_and_a_cookie_that_already_works()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();
        var email = NewEmail();
        var displayName = NewDisplayName();

        var response = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = displayName });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(displayName, body.GetProperty("display_name").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.True(Guid.TryParse(body.GetProperty("id").GetString(), out _));

        // The whole of "without asking them to sign in again": the cookie just issued is enough.
        var cookie = SessionCookieFrom(response);
        var signedIn = factory.CreateClient();
        signedIn.DefaultRequestHeaders.Add("Cookie", cookie);

        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task The_endpoint_is_reachable_with_no_session_at_all()
    {
        // security: [] in the contract. A registration form that required a session would be
        // unreachable by the only people who need it.
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Registering_without_the_antiforgery_token_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("accounts.antiforgery_failed", await CodeOfAsync(response));
    }

    // ---- Each refusal, with the contract's status and code ---------------------------------------

    [Fact]
    public async Task A_password_that_is_too_short_is_refused_with_the_contracts_code()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = "short", display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("accounts.password_invalid", await CodeOfAsync(response));
    }

    [Fact]
    public async Task An_address_that_cannot_be_an_address_is_refused_with_the_contracts_code()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Accounts,
            new { email = "not-an-address", password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("accounts.email_invalid", await CodeOfAsync(response));
    }

    [Fact]
    public async Task A_registered_address_is_refused_with_a_conflict()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();
        var email = NewEmail();
        await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = NewDisplayName() });

        var response = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("accounts.email_taken", await CodeOfAsync(response));
    }

    [Fact]
    public async Task A_taken_display_name_is_refused_with_a_conflict()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();
        var displayName = NewDisplayName();
        await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = displayName });

        var response = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = displayName });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("accounts.display_name_taken", await CodeOfAsync(response));
    }

    [Fact]
    public async Task A_refused_registration_hands_back_no_session_cookie()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = "short", display_name = NewDisplayName() });

        Assert.False(response.StatusCode is HttpStatusCode.Created);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_malformed_body_is_refused_without_echoing_a_framework_message()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsync(
            Accounts,
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.True((int)response.StatusCode >= 400);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JsonException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonReaderException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", body, StringComparison.Ordinal);
    }

    // ---- AC-01b: the per-source limit -------------------------------------------------------------

    [Fact]
    public async Task The_sixth_registration_from_one_source_within_a_minute_is_refused()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var accepted = 0; accepted < RegistrationRateLimit.PermittedPerWindow; accepted++)
        {
            var allowed = await client.PostAsJsonAsync(
                Accounts,
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("accounts.registration_rate_limited", problem.GetProperty("code").GetString());

        // AC-01b: "tells them when they may try again" — in the body and in the standard header.
        var retryAfterSeconds = problem.GetProperty("retry_after_seconds").GetInt32();
        Assert.InRange(retryAfterSeconds, 1, (int)RegistrationRateLimit.Window.TotalSeconds);
        Assert.Equal(retryAfterSeconds, refused.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Fact]
    public async Task A_refused_registration_writes_nothing_at_all()
    {
        // flow 3's postcondition, on the limit path: the limit is evaluated before the use case,
        // so a refusal cannot have touched the store.
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < RegistrationRateLimit.PermittedPerWindow; attempt++)
        {
            await client.PostAsJsonAsync(
                Accounts,
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });
        }

        var email = NewEmail();
        var before = await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetUsers]");

        var refused = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(before, await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetUsers]"));
    }

    [Fact]
    public async Task The_window_moves_so_a_later_registration_is_accepted_again()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < RegistrationRateLimit.PermittedPerWindow; attempt++)
        {
            await client.PostAsJsonAsync(
                Accounts,
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });
        }

        factory.Clock.Advance(RegistrationRateLimit.Window + TimeSpan.FromSeconds(1));

        var afterTheWindow = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.Created, afterTheWindow.StatusCode);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_retry_while_refused_is_refused_again_without_extending_the_window()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < RegistrationRateLimit.PermittedPerWindow; attempt++)
        {
            await client.PostAsJsonAsync(
                Accounts,
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });
        }

        var first = await RetryAfterOfRefusalAsync(client);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        var second = await RetryAfterOfRefusalAsync(client);

        // Smaller, not reset: being refused must not push the wait further out.
        Assert.True(
            second < first,
            $"the wait went from {first} s to {second} s, so the refusal extended the window");

        factory.Clock.Reset();
    }

    // ---- The request source: only the proxy may say where a request came from --------------------

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_caller_is_ignored()
    {
        // Otherwise the limit is decorative: anyone could present a fresh address per request and
        // never be counted at all.
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < RegistrationRateLimit.PermittedPerWindow; attempt++)
        {
            var allowed = await client.PostAsJsonAsync(
                Accounts,
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var spoofed = new HttpRequestMessage(HttpMethod.Post, Accounts)
        {
            Content = JsonContent.Create(
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() }),
        };
        spoofed.Headers.Add(AntiforgerySetup.HeaderName, await TokenAsync(client));
        spoofed.Headers.Add(
            TestPeerAddress.HeaderName,
            client.DefaultRequestHeaders.GetValues(TestPeerAddress.HeaderName).First());
        // The same peer, now claiming to be somewhere else. The claim must count for nothing.
        spoofed.Headers.Add("X-Forwarded-For", "198.51.100.77");
        foreach (var cookie in CookiesOf(client))
        {
            spoofed.Headers.Add("Cookie", cookie);
        }

        var response = await client.SendAsync(spoofed);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task Registering_issues_a_cookie_that_survives_closing_the_browser()
    {
        // AC-06: "close the browser entirely and return the next day". A cookie with neither
        // Max-Age nor Expires is a browser-session cookie and is discarded on exit, whatever the
        // server still thinks of the session. The server-side rules still decide validity; the
        // cookie only has to live as long as the longest a session can.
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));

        Assert.Contains(
            $"max-age={(long)Domain.Accounts.Session.AbsoluteLifetime.TotalSeconds}",
            setCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refused_registrations_do_not_use_up_the_limit()
    {
        // AC-01b's Given is "5 accounts have already been registered from the same request
        // source". A visitor corrected and resubmitting five times (AC-02, AC-03, AC-11b promise
        // exactly that path) has registered nothing, and must not be told to wait.
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var refused = 0; refused < RegistrationRateLimit.PermittedPerWindow; refused++)
        {
            var response = await client.PostAsJsonAsync(
                Accounts, new { email = NewEmail(), password = "short", display_name = NewDisplayName() });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var accepted = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public void Sources_whose_window_has_closed_are_forgotten()
    {
        // Review 2026-09-22 R-25: without pruning, the limiter keeps one entry per source address
        // ever seen, which IPv6 address rotation makes unbounded.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);

        for (var source = 0; source < 50; source++)
        {
            Assert.True(limit.Reserve($"198.51.100.{source}").IsPermitted);
        }

        clock.Advance(RegistrationRateLimit.Window + TimeSpan.FromSeconds(1));
        Assert.True(limit.Reserve("203.0.113.9").IsPermitted);

        Assert.Equal(1, limit.TrackedSourceCount);
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.test";

    private static string NewDisplayName() => $"ep-{Guid.NewGuid():N}";

    private static string SessionCookieFrom(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal))
            .Split(';')[0];

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private async Task<int> RetryAfterOfRefusalAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            Accounts, new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("retry_after_seconds").GetInt32();
    }

    /// <summary>
    /// A client that already holds the antiforgery pair a write needs, and that appears to come
    /// from a peer of its own so the per-source limit counts only this test's attempts.
    /// </summary>
    private async Task<HttpClient> ClientAsync()
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
            AntiforgerySetup.HeaderName, token["XSRF-TOKEN=".Length..].Split(';')[0]);

        return client;
    }

    private static IEnumerable<string> CookiesOf(HttpClient client) =>
        client.DefaultRequestHeaders.TryGetValues("Cookie", out var cookies) ? cookies : [];

    private static Task<string> TokenAsync(HttpClient client) =>
        Task.FromResult(client.DefaultRequestHeaders.GetValues(AntiforgerySetup.HeaderName).First());
}
