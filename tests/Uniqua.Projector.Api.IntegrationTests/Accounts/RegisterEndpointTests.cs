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
    public async Task A_password_that_is_too_long_is_refused_with_the_same_code_as_too_short()
    {
        // AC-02's upper bound (review 2026-09-22-2 N-02): R-21 removed the column's maxLength, so
        // this case is reachable, and the wording names both bounds either way it is missed.
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Accounts,
            new
            {
                email = NewEmail(),
                password = new string('a', 129),
                display_name = NewDisplayName(),
            });

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
    public async Task A_malformed_body_is_refused_with_the_contracts_code_and_no_framework_message()
    {
        // review 2026-09-22-2 N-06c: model binding used to fail before any endpoint code ran, with
        // no declared `code` at all. This is now the one declared problem either account endpoint
        // returns for an unreadable body.
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsync(
            Accounts,
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("accounts.request_malformed", await CodeOfAsync(response));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JsonException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonReaderException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_body_is_refused_with_the_same_declared_code()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsync(Accounts, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("accounts.request_malformed", await CodeOfAsync(response));
    }

    [Fact]
    public async Task A_body_with_an_unknown_field_is_refused_and_creates_no_account()
    {
        // review 2026-09-23 (fourth re-review) U-02: openapi declares additionalProperties: false
        // on this body, but nothing enforced it, so a body carrying an extra field was accepted.
        factory.Clock.Reset();
        var client = await ClientAsync();
        var email = NewEmail();
        var before = await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetUsers]");

        var response = await client.PostAsync(
            Accounts,
            new StringContent(
                $$"""{"email":"{{email}}","password":"{{GoodPassword}}","display_name":"{{NewDisplayName()}}","x":1}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("accounts.request_malformed", await CodeOfAsync(response));
        Assert.Equal(before, await factory.ScalarAsync<int>("SELECT COUNT(*) FROM [dbo].[AspNetUsers]"));
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
    public async Task More_than_five_parallel_reservations_from_one_source_permit_exactly_five()
    {
        // N-11: the limit is enforced by a lock inside SlidingWindowLimiter.Reserve (shared with
        // SignInRateLimit since T35), not by the sequential-request tests above, which can never
        // exercise a race. A fixed test clock keeps every call in the same window, so this is
        // purely a concurrency proof, not a timing one.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);
        var source = $"198.51.100.{Random.Shared.Next(1, 254)}";

        var decisions = await Task.WhenAll(Enumerable.Range(0, 25)
            .Select(_ => Task.Run(() => limit.Reserve(source))));

        Assert.Equal(RegistrationRateLimit.PermittedPerWindow, decisions.Count(decision => decision.IsPermitted));
    }

    [Fact]
    public void Releasing_a_refused_reservation_frees_no_slot()
    {
        // N-11: Release must be a no-op for a refusal — RateLimitDecision.Refused carries a
        // default ReservedAt, so releasing it must not free a slot another registration could
        // then take.
        //
        // Q-12: a refused decision's ReservedAt is always the struct's default, which never
        // matches a *permitted* decision's real timestamp — so the old version of this test
        // stayed green whether or not SlidingWindowLimiter.Release actually checked
        // `!reservation.IsPermitted` first: either way, "remove `default`" found nothing to
        // remove. Forging a refusal that carries a real, held ReservedAt (one of the five slots
        // just taken, `IsPermitted: false` and all) is what makes the guard itself load-bearing:
        // with it, Release still does nothing for a decision it is told is a refusal; without it,
        // Release frees that slot regardless of what it actually holds.
        var clock = new TestClock();
        var limit = new RegistrationRateLimit(clock);
        var source = $"198.51.100.{Random.Shared.Next(1, 254)}";

        RateLimitDecision firstHeldSlot = default;
        for (var accepted = 0; accepted < RegistrationRateLimit.PermittedPerWindow; accepted++)
        {
            var decision = limit.Reserve(source);
            Assert.True(decision.IsPermitted);
            if (accepted == 0)
            {
                firstHeldSlot = decision;
            }
        }

        Assert.False(limit.Reserve(source).IsPermitted);

        var forgedRefusal = new RateLimitDecision(false, 0, firstHeldSlot.ReservedAt);
        limit.Release(source, forgedRefusal);

        Assert.False(limit.Reserve(source).IsPermitted);
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

    [Theory]
    [InlineData("o'brien")]
    [InlineData("zoë")]
    [InlineData("first+tag")]
    public async Task Every_address_the_account_accepts_can_be_registered(string localPartPrefix)
    {
        // Review 2026-09-22 R-14 (AC-02b, AC-11b). The account decides what an address is; the
        // store must not refuse a valid one on its own character rules, and above all must not
        // report that refusal as "display name taken", which sends the visitor to change the wrong
        // field.
        factory.Clock.Reset();
        var client = await ClientAsync();
        var email = $"{localPartPrefix}.{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = NewDisplayName() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_forwarded_address_from_the_configured_proxy_is_the_request_source()
    {
        // spec §6.1: ignoring the proxy's report "would key every visitor to the proxy's own
        // address and close registration for the whole world after five accounts". Two visitors
        // behind the one proxy must each get their own five.
        factory.Clock.Reset();
        var viaProxy = await ClientAsync(ApiFactory.TrustedProxy);
        var first = $"203.0.113.{Random.Shared.Next(1, 254)}";
        var second = $"198.51.100.{Random.Shared.Next(1, 254)}";

        for (var accepted = 0; accepted < RegistrationRateLimit.PermittedPerWindow; accepted++)
        {
            Assert.Equal(HttpStatusCode.Created, (await RegisterForwardedAsync(viaProxy, first)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterForwardedAsync(viaProxy, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterForwardedAsync(viaProxy, second)).StatusCode);
    }

    private static Task<HttpResponseMessage> RegisterForwardedAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Accounts)
        {
            Content = JsonContent.Create(
                new { email = NewEmail(), password = GoodPassword, display_name = NewDisplayName() }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return client.SendAsync(request);
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
    private async Task<HttpClient> ClientAsync(string? peer = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestPeerAddress.HeaderName, peer ?? TestPeerAddress.Fresh());

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
