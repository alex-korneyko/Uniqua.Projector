using System.Net;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// QG-2 — session continuity with a bounded lifetime, driven through the endpoint a client actually
/// calls on load. Each case is one of the data model's named fixtures, so the test says which
/// situation it is about rather than assembling it inline.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionLifetimeTests(ApiFactory factory)
{
    private const string Me = "/api/v1/accounts/me";

    [Fact]
    public async Task A_live_session_is_recognised()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var sessionId = await factory.ALiveSessionAsync(account);

        var response = await factory.ClientCarrying(sessionId).GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_session_one_minute_past_the_idle_boundary_is_not_recognised()
    {
        // AC-07. The fixture sits a minute past the boundary (14 days and the hour spec §6 allows
        // since the last stamp) precisely so the test is about the boundary rather than about a
        // session that has obviously been abandoned.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var sessionId = await factory.AnIdleSessionAsync(account);

        var response = await factory.ClientCarrying(sessionId).GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_session_a_minute_short_of_the_idle_boundary_is_still_recognised()
    {
        // The other side of it, which is what makes the first assertion mean anything.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var sessionId = await factory.ALiveSessionAsync(account);

        factory.Clock.Advance(Session.IdleExpiryAfterLastStamp - TimeSpan.FromMinutes(1));

        var response = await factory.ClientCarrying(sessionId).GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.Clock.Reset();
    }

    [Fact]
    public async Task The_window_slides_with_use()
    {
        // The continuity half of QG-2: someone who keeps coming back is never signed out for it.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var sessionId = await factory.ALiveSessionAsync(account);
        var client = factory.ClientCarrying(sessionId);

        for (var week = 0; week < 8; week++)
        {
            factory.Clock.Advance(TimeSpan.FromDays(7));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
        }

        factory.Clock.Reset();
    }

    [Fact]
    public async Task A_session_past_the_absolute_ceiling_is_not_recognised_however_recently_used()
    {
        // AC-07b. The fixture's last-seen instant is now — the session is in active use and must
        // still be refused, because the ceiling ignores activity entirely.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var sessionId = await factory.AnAgedSessionAsync(account);

        var response = await factory.ClientCarrying(sessionId).GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_revoked_session_is_not_recognised()
    {
        // AC-08 and AC-10: refused by the record, whatever the browser still holds.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var sessionId = await factory.ARevokedSessionAsync(account);

        var response = await factory.ClientCarrying(sessionId).GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Every_way_of_being_unrecognised_answers_the_same()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();

        var idle = await factory.AnIdleSessionAsync(account);
        var aged = await factory.AnAgedSessionAsync(account);
        var revoked = await factory.ARevokedSessionAsync(account);

        var bodies = new List<string>();
        foreach (var sessionId in new[] { idle, aged, revoked })
        {
            var response = await factory.ClientCarrying(sessionId).GetAsync(Me);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            bodies.Add(await WithoutPerRequestFieldsAsync(response));
        }

        Assert.Single(bodies.Distinct());
    }

    /// <summary>
    /// The third QG-2 verification: that a session opened before a redeploy is still recognised
    /// after one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DEFERRED TO ROADMAP STEP 4, when a real deployment exists to redeploy. sad §10 records it as
    /// deferred rather than covered, and plan-tests agrees, so it appears here as a skipped test
    /// rather than being quietly left out — the gap is then visible in every test run instead of
    /// living only in a document.
    /// </para>
    /// <para>
    /// What <em>can</em> be verified today is verified: <c>DataProtectionKeyRingTests</c> builds a
    /// second application over the same database and proves it reads the first one's key ring, and
    /// <c>SessionRecognitionTests</c> proves a session opened by one instance is recognised by that
    /// second one. Those are the mechanism. What is missing is the real thing: a deployment.
    /// </para>
    /// </remarks>
    [Fact(Skip = "Deferred to roadmap step 4: needs a real deployment to redeploy. The mechanism "
        + "is covered by DataProtectionKeyRingTests and SessionRecognitionTests; this row is the "
        + "post-deployment check itself (sad §10 QG-2).")]
    public void An_unexpired_session_survives_a_real_redeploy()
    {
        Assert.Fail("Not reachable: this test is skipped until roadmap step 4.");
    }

    private static async Task<string> WithoutPerRequestFieldsAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        return string.Join('|', document.RootElement.EnumerateObject()
            .Where(member => member.Name is not ("traceId" or "instance"))
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => $"{member.Name}={member.Value}"));
    }
}
