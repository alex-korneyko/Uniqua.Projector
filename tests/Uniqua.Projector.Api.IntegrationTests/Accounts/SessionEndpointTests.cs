using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.Antiforgery;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Domain.Accounts;

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

    [Fact]
    public async Task A_malformed_body_is_refused_with_the_contracts_code_and_no_framework_message()
    {
        // review 2026-09-22-2 N-06c: /sessions declared no 400 at all, and model binding used to
        // fail with no declared `code`. This is the one declared problem for an unreadable body.
        factory.Clock.Reset();
        var client = await ClientAsync();

        var response = await client.PostAsync(
            Sessions,
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(
            "accounts.request_malformed",
            JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("JsonException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Text.Json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_with_an_unknown_field_is_refused_and_takes_no_slot()
    {
        // review 2026-09-23 (fourth re-review) U-02: openapi declares additionalProperties: false
        // on this body, but nothing enforced it. The refusal must depend on the body's shape alone
        // — never on account state — and must happen before Reserve, so it takes no slot.
        factory.Clock.Reset();
        var client = await ClientAsync();
        var limit = factory.Services.GetRequiredService<SignInRateLimit>();
        var trackedAddressesBefore = limit.TrackedAddressKeyCount;
        var trackedSourcesBefore = limit.TrackedSourceCount;

        var response = await client.PostAsync(
            Sessions,
            new StringContent(
                """{"email":"someone@example.test","password":"a-long-enough-password","x":1}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "accounts.request_malformed",
            JsonDocument.Parse(await response.Content.ReadAsStringAsync())
                .RootElement.GetProperty("code").GetString());
        Assert.Equal(trackedAddressesBefore, limit.TrackedAddressKeyCount);
        Assert.Equal(trackedSourcesBefore, limit.TrackedSourceCount);
    }

    // ---- AC-05b / S-01: over-long credentials are refused before Reserve or a lookup -------------

    [Fact]
    public async Task An_overlong_email_gets_the_same_refusal_as_a_wrong_password_and_consumes_no_slot()
    {
        // review 2026-09-23 (third re-review) S-01: openapi's maxLength: 256 on email was not
        // enforced, so an attacker could grow the per-address key without bound just by typing a
        // longer address. The refusal must depend on length alone (never on account state, so it
        // adds no AC-05b oracle) and must happen before SignInRateLimit.Reserve is ever called.
        factory.Clock.Reset();
        var client = await ClientAsync();
        var limit = factory.Services.GetRequiredService<SignInRateLimit>();

        var overlongEmail = new string('a', 250) + "@example.test"; // 261 chars, past the 256 cap.
        var wrongPassword = await client.PostAsJsonAsync(
            Sessions, new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });

        // Captured only after the ordinary wrong-credentials attempt above, which does legitimately
        // reserve a slot — the over-long attempt below must add nothing further to either count.
        var trackedAddressesBefore = limit.TrackedAddressKeyCount;
        var trackedSourcesBefore = limit.TrackedSourceCount;

        var overlong = await client.PostAsJsonAsync(
            Sessions, new { email = overlongEmail, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, overlong.StatusCode);
        Assert.Equal(
            "accounts.credentials_invalid",
            JsonDocument.Parse(await overlong.Content.ReadAsStringAsync())
                .RootElement.GetProperty("code").GetString());

        // Byte-identical to the ordinary wrong-credentials refusal — the same problem, not a
        // length-specific one, so the two are indistinguishable to whoever is probing.
        Assert.Equal(
            await NormalisedBodyAsync(wrongPassword), await NormalisedBodyAsync(overlong));
        Assert.Equal(InterestingHeaders(wrongPassword), InterestingHeaders(overlong));

        // Reserve was never called for this attempt, so it consumed neither the per-address nor
        // the per-source slot.
        Assert.Equal(trackedAddressesBefore, limit.TrackedAddressKeyCount);
        Assert.Equal(trackedSourcesBefore, limit.TrackedSourceCount);
    }

    [Fact]
    public async Task An_overlong_password_gets_the_same_refusal_as_a_wrong_password_and_consumes_no_slot()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();
        var limit = factory.Services.GetRequiredService<SignInRateLimit>();

        var overlongPassword = new string('a', 129); // past the 128-character cap.
        var wrongPassword = await client.PostAsJsonAsync(
            Sessions, new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });

        var trackedAddressesBefore = limit.TrackedAddressKeyCount;
        var trackedSourcesBefore = limit.TrackedSourceCount;

        // A brand-new address, never named before in this test, so a slot mistakenly reserved for
        // this attempt would show up as a new entry rather than hiding behind one already tracked.
        var overlong = await client.PostAsJsonAsync(
            Sessions,
            new { email = $"{Guid.NewGuid():N}@example.test", password = overlongPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, overlong.StatusCode);
        Assert.Equal(
            "accounts.credentials_invalid",
            JsonDocument.Parse(await overlong.Content.ReadAsStringAsync())
                .RootElement.GetProperty("code").GetString());
        Assert.Equal(
            await NormalisedBodyAsync(wrongPassword), await NormalisedBodyAsync(overlong));
        Assert.Equal(InterestingHeaders(wrongPassword), InterestingHeaders(overlong));

        Assert.Equal(trackedAddressesBefore, limit.TrackedAddressKeyCount);
        Assert.Equal(trackedSourcesBefore, limit.TrackedSourceCount);
    }

    // ---- AC-04 / AC-05b — V-01: the guard measures the trimmed address ----------------------------

    [Fact]
    public async Task A_256_character_address_signs_in_when_sent_padded_with_a_trailing_space()
    {
        // review 2026-09-23 (fourth re-review) V-01: registration, the store lookup and the cap key
        // all trim the address, but the guard above used to measure request.Email.Length before
        // trimming. An owner registered at exactly Account.MaxEmailLength who pastes their address
        // with a trailing space was refused every time, even though their real address is fine.
        factory.Clock.Reset();
        var email = AddressOfLength(Account.MaxEmailLength);
        await RegisterAsync(email);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = email + " ", password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_256_character_address_signs_in_when_sent_padded_with_a_leading_newline()
    {
        factory.Clock.Reset();
        var email = AddressOfLength(Account.MaxEmailLength);
        await RegisterAsync(email);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = "\n" + email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_257_character_address_after_trimming_is_still_refused_and_consumes_no_slot()
    {
        // The guard has to measure the trimmed length, not skip the check for anything padded: one
        // character over the cap once whitespace is removed must still be refused before Reserve is
        // ever called.
        factory.Clock.Reset();
        var client = await ClientAsync();
        var limit = factory.Services.GetRequiredService<SignInRateLimit>();

        var overlongAfterTrim = AddressOfLength(Account.MaxEmailLength + 1);
        var wrongPassword = await client.PostAsJsonAsync(
            Sessions, new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });

        var trackedAddressesBefore = limit.TrackedAddressKeyCount;
        var trackedSourcesBefore = limit.TrackedSourceCount;

        var overlong = await client.PostAsJsonAsync(
            Sessions, new { email = " " + overlongAfterTrim + " ", password = GoodPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, overlong.StatusCode);
        Assert.Equal(
            "accounts.credentials_invalid",
            JsonDocument.Parse(await overlong.Content.ReadAsStringAsync())
                .RootElement.GetProperty("code").GetString());
        Assert.Equal(
            await NormalisedBodyAsync(wrongPassword), await NormalisedBodyAsync(overlong));
        Assert.Equal(InterestingHeaders(wrongPassword), InterestingHeaders(overlong));

        Assert.Equal(trackedAddressesBefore, limit.TrackedAddressKeyCount);
        Assert.Equal(trackedSourcesBefore, limit.TrackedSourceCount);
    }

    // ---- AC-12 / AC-05b: the per-source failed-sign-in cap (review 2026-09-22-2 N-01) -----------

    [Fact]
    public async Task The_21st_failure_from_one_source_is_refused_without_a_verification()
    {
        // The slot is reserved before the password is checked, so the 21st attempt never reaches
        // SignIn.ExecuteAsync at all — provable because AccessFailedCount, which only Identity's
        // verification path writes, stays at 20 rather than climbing to 21.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var accessFailedBeforeTheCap = await factory.ScalarAsync<int>(
            $"SELECT [AccessFailedCount] FROM [dbo].[AspNetUsers] WHERE [Id] = '{account.Id}'");

        var capped = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);

        var problem = JsonDocument.Parse(await capped.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("accounts.sign_in_rate_limited", problem.GetProperty("code").GetString());

        var retryAfterSeconds = problem.GetProperty("retry_after_seconds").GetInt32();
        Assert.InRange(retryAfterSeconds, 1, (int)SignInRateLimit.Window.TotalSeconds);
        Assert.Equal(retryAfterSeconds, capped.Headers.RetryAfter?.Delta?.TotalSeconds);

        // No verification ran for the 21st attempt — the count did not move past what the first
        // 20 real verifications already wrote.
        Assert.Equal(accessFailedBeforeTheCap, await factory.ScalarAsync<int>(
            $"SELECT [AccessFailedCount] FROM [dbo].[AspNetUsers] WHERE [Id] = '{account.Id}'"));
    }

    [Fact]
    public async Task A_correct_password_from_a_different_source_is_still_accepted_immediately()
    {
        // AC-12: the cap is per source, never per account, so the owner signing in from anywhere
        // else is unaffected by an attacker's source having exhausted its own cap.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var attacker = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            await attacker.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" });
        }
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await attacker.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" })).StatusCode);

        var owner = await ClientAsync();
        var response = await owner.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_unregistered_address_is_capped_identically()
    {
        // AC-05b: the cap must not become a second oracle that answers "is this address real" by
        // behaving differently for one that never was.
        factory.Clock.Reset();
        var unknown = $"{Guid.NewGuid():N}@example.test";
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                Sessions, new { email = unknown, password = "any-password-at-all" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var capped = await client.PostAsJsonAsync(
            Sessions, new { email = unknown, password = "any-password-at-all" });

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Equal(
            "accounts.sign_in_rate_limited",
            JsonDocument.Parse(await capped.Content.ReadAsStringAsync())
                .RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_correct_password_for_a_different_address_from_a_capped_source_is_accepted()
    {
        // review 2026-09-23 P-01: the cap re-keyed to (source, normalised address), so a source
        // that has capped one address must still admit a correct password for a different one —
        // the shared-address, one-oracle failure the second re-review found is what this pins.
        factory.Clock.Reset();
        var owner = await ARegisteredAccountAsync();
        var attackedAddress = $"{Guid.NewGuid():N}@example.test";
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                Sessions, new { email = attackedAddress, password = "not-the-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync(
                Sessions, new { email = attackedAddress, password = "not-the-password" })).StatusCode);

        var response2 = await client.PostAsJsonAsync(
            Sessions, new { email = owner.Email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
    }

    [Fact]
    public async Task The_capped_address_still_refuses_the_correct_password_from_the_same_source()
    {
        // review 2026-09-23 P-01: the accepted residual, recorded in spec §6.1 and the ADR 0010
        // amendment — a guesser sharing the owner's source and targeting the owner's own address
        // can still block them for up to 15 minutes. Re-keying the cap must not accidentally
        // remove this; it only narrows who else the block reaches.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            await client.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" });
        }

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task The_per_source_ceiling_refuses_once_reached_across_many_addresses()
    {
        // review 2026-09-23 P-01: the looser per-source ceiling bounds address rotation from one
        // source, so a guesser spreading failures across many addresses — none of which alone
        // reaches the per-address cap of 20 — is still refused once the source's own ceiling is
        // reached.
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerSourceWindow; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                Sessions,
                new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var capped = await client.PostAsJsonAsync(
            Sessions,
            new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
    }

    [Fact]
    public async Task A_per_address_refusal_logs_which_cap_refused_it()
    {
        // review 2026-09-23 (third re-review) S-04: sad §7 lists which cap refused each sign-in as
        // a monitored metric, so a refusal by the tighter per-(source, address) pair must be
        // distinguishable in the log from one by the looser per-source ceiling. The 429 body itself
        // stays silent about which cap fired (AC-12) — only the log line carries it.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerWindow; attempt++)
        {
            await client.PostAsJsonAsync(
                Sessions, new { email = account.Email, password = "not-the-password" });
        }

        factory.Logs.Clear();

        var capped = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = "not-the-password" });

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Message.Contains("event=sign_in_rate_limited", StringComparison.Ordinal)
            && entry.Message.Contains("cap=per_address", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_per_source_refusal_logs_which_cap_refused_it()
    {
        // The mirror of the per-address case above: once the looser per-source ceiling itself is
        // what refuses the attempt — none of the individual addresses alone reached the
        // per-address cap — the log line must name that cap instead, so sad §7's per-cap breakdown
        // can be built from the logs without re-deriving it from the request shape.
        factory.Clock.Reset();
        var client = await ClientAsync();

        for (var attempt = 0; attempt < SignInRateLimit.PermittedFailuresPerSourceWindow; attempt++)
        {
            await client.PostAsJsonAsync(
                Sessions,
                new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });
        }

        factory.Logs.Clear();

        var capped = await client.PostAsJsonAsync(
            Sessions,
            new { email = $"{Guid.NewGuid():N}@example.test", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Contains(factory.Logs.Entries, entry =>
            entry.Message.Contains("event=sign_in_rate_limited", StringComparison.Ordinal)
            && entry.Message.Contains("cap=per_source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_source_is_never_capped()
    {
        // review 2026-09-23 P-01: when RequestSource.Of cannot resolve a source (RemoteIpAddress
        // is null), applying the cap would make every caller with no resolvable address share one
        // budget — a missing remote address must not do that, so the cap is skipped for it.
        factory.Clock.Reset();
        var address = $"{Guid.NewGuid():N}@example.test";
        var client = await ClientWithNoPeerAsync();

        const int attempts = SignInRateLimit.PermittedFailuresPerWindow + 5;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                Sessions, new { email = address, password = "not-the-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
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

    [Fact]
    public async Task Signing_in_issues_a_cookie_that_survives_closing_the_browser()
    {
        // AC-06: "close the browser entirely and return the next day". A cookie with neither
        // Max-Age nor Expires is a browser-session cookie and is discarded on exit, whatever the
        // server still thinks of the session. The server-side rules still decide validity; the
        // cookie only has to live as long as the longest a session can.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = account.Email, password = GoodPassword });

        var setCookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal));

        Assert.Contains(
            $"max-age={(long)Domain.Accounts.Session.AbsoluteLifetime.TotalSeconds}",
            setCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    // ---- The account shown back, the same way everywhere (review 2026-09-22 R-15, R-16) --------

    [Fact]
    public async Task Signing_in_shows_the_address_exactly_as_the_account_holds_it()
    {
        // The contract's Account.email is the account's address. Echoing what was typed at sign-in
        // (another case, stray spaces) would show the same account two ways depending on the path.
        factory.Clock.Reset();
        var account = await ARegisteredAccountAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            Sessions, new { email = $"  {account.Email.ToUpperInvariant()} ", password = GoodPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            await EmailFromMeAsync(SessionCookieFrom(response)),
            await EmailInAsync(response));
    }

    [Fact]
    public async Task Registering_shows_the_address_exactly_as_the_account_holds_it()
    {
        factory.Clock.Reset();
        var client = await ClientAsync();
        var email = $"{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync(
            Accounts, new { email = $"  {email}  ", password = GoodPassword, display_name = $"se-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            await EmailFromMeAsync(SessionCookieFrom(response)),
            await EmailInAsync(response));
    }

    [Fact]
    public async Task An_account_keeps_one_identity_through_its_whole_life()
    {
        // AC-13, as test-plan.md row AC-13 describes it: the id is unchanged across sign-out,
        // failed attempts and a fresh sign-in, so anything keyed to it (board membership, cards)
        // survives every one of them.
        factory.Clock.Reset();
        var email = $"{Guid.NewGuid():N}@example.test";
        var client = await ClientAsync();

        var registered = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = $"se-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var id = await IdInAsync(registered);
        var firstSession = SessionCookieFrom(registered);

        var signedIn = await ClientAsync(firstSession);
        Assert.Equal(id, await IdInAsync(await signedIn.GetAsync(Me)));
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.DeleteAsync(CurrentSession)).StatusCode);

        var visitor = await ClientAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await visitor.PostAsJsonAsync(Sessions, new { email, password = "not-the-password" })).StatusCode);
        }

        var again = await visitor.PostAsJsonAsync(Sessions, new { email, password = GoodPassword });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(id, await IdInAsync(again));

        var secondSession = await ClientAsync(SessionCookieFrom(again));
        Assert.Equal(id, await IdInAsync(await secondSession.GetAsync(Me)));
    }

    private async Task<string?> EmailFromMeAsync(string sessionCookie)
    {
        var client = await ClientAsync(sessionCookie);
        return await EmailInAsync(await client.GetAsync(Me));
    }

    private static async Task<string?> EmailInAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("email").GetString();

    private static async Task<string?> IdInAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

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

    /// <summary>
    /// A client with the antiforgery pair but no <see cref="TestPeerAddress"/> header, so
    /// <c>Connection.RemoteIpAddress</c> stays null and <c>RequestSource.Of</c> resolves to
    /// "unknown" — review 2026-09-23 P-01.
    /// </summary>
    private async Task<HttpClient> ClientWithNoPeerAsync()
    {
        var client = factory.CreateClient();

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

    private async Task RegisterAsync(string email)
    {
        var client = await ClientAsync();
        var response = await client.PostAsJsonAsync(
            Accounts, new { email, password = GoodPassword, display_name = $"se-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// A registrable address of exactly <paramref name="length"/> characters — unique per call, so
    /// two calls in the same test never collide on the store's unique index.
    /// </summary>
    private static string AddressOfLength(int length)
    {
        const string domain = "@example.test";
        var unique = Guid.NewGuid().ToString("N");
        var local = unique.PadRight(length - domain.Length, 'a');
        return local + domain;
    }
}
