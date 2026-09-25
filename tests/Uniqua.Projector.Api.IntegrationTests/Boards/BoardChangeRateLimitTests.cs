using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T12 — <c>BoardChangeRateLimit</c>: 120 change attempts per rolling minute per account, reserved
/// before the membership check, kept whatever the outcome except its own refusal (AC-17); a change
/// with no recognised session belongs to no account and does not count (AC-28). Modelled on
/// <see cref="Accounts.SignInRateLimitTests"/> but driven through the real HTTP surface, since the
/// limit is wired as an endpoint filter over the whole <c>/api/v1/boards</c> change surface rather
/// than a type a unit test can reach directly.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BoardChangeRateLimitTests(ApiFactory factory)
{
    private const string Boards = "/api/v1/boards";
    private const int PermittedChangesPerWindow = 120;

    // ---- AC-17 / edge case: 120 refused attempts on a board the caller is not a member of are all
    // counted; the 121st is refused by the limit itself ------------------------------------------

    [Fact]
    public async Task The_121st_change_attempt_within_a_minute_is_refused_as_rate_limited()
    {
        factory.Clock.Reset();
        var caller = await factory.AnAccountAsync();
        var owner = await factory.AnAccountAsync();
        var foreignBoard = await factory.ABoardAsync(owner, "Not the caller's board");

        var client = await factory.AWritingClientAsync(caller);

        // 120 attempts, every one refused boards.not_available (the caller is not a member) — every
        // refusal other than the limit's own must still count (AC-17).
        for (var attempt = 0; attempt < PermittedChangesPerWindow; attempt++)
        {
            var response = await client.PatchAsJsonAsync(
                $"{Boards}/{foreignBoard.Id}", new { name = $"attempt-{attempt}" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("boards.not_available", await CodeOfAsync(response));
        }

        var limited = await client.PatchAsJsonAsync(
            $"{Boards}/{foreignBoard.Id}", new { name = "one-too-many" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("boards.change_rate_limited", await CodeOfAsync(limited));

        Assert.True(limited.Headers.TryGetValues("Retry-After", out var retryAfterValues));
        var retryAfterSeconds = int.Parse(retryAfterValues!.Single());
        Assert.True(retryAfterSeconds > 0);

        var body = await BodyAsync(limited);
        Assert.Equal(retryAfterSeconds, body.GetProperty("retry_after_seconds").GetInt32());
    }

    // ---- AC-17: the refusal answers identically whichever board it names -------------------------

    [Fact]
    public async Task The_rate_limited_refusal_is_identical_whether_the_board_is_owned_or_never_existed()
    {
        factory.Clock.Reset();
        var caller = await factory.AnAccountAsync();
        var ownedBoard = await factory.ABoardAsync(caller, "Owned by the caller");
        var client = await factory.AWritingClientAsync(caller);

        // Creating the owned board above already spent one slot; spend the rest on it too, so every
        // attempt in this test targets an account genuinely at its ceiling. Every one of these is a
        // legitimate rename of the caller's own board, so it is accepted and does change the name —
        // the last accepted one is what the board must still show once the limit refuses the next.
        var lastAcceptedName = ownedBoard.Name;
        for (var attempt = 0; attempt < PermittedChangesPerWindow - 1; attempt++)
        {
            lastAcceptedName = $"attempt-{attempt}";
            await client.PatchAsJsonAsync($"{Boards}/{ownedBoard.Id}", new { name = lastAcceptedName });
        }

        var toOwnedBoard = await client.PatchAsJsonAsync(
            $"{Boards}/{ownedBoard.Id}", new { name = "one-too-many" });
        var toNeverExisted = await client.PatchAsJsonAsync(
            $"{Boards}/{Guid.CreateVersion7()}", new { name = "one-too-many" });

        Assert.Equal(HttpStatusCode.TooManyRequests, toOwnedBoard.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, toNeverExisted.StatusCode);

        var ownedBody = await BodyAsync(toOwnedBoard);
        var neverExistedBody = await BodyAsync(toNeverExisted);

        foreach (var name in new[] { "type", "title", "status", "detail", "code" })
        {
            Assert.Equal(
                ownedBody.TryGetProperty(name, out var a) ? a.ToString() : null,
                neverExistedBody.TryGetProperty(name, out var b) ? b.ToString() : null);
        }

        // Nothing changed on the caller's own board while limited — it still shows the name from the
        // last accepted rename above, not the rejected "one-too-many".
        var reopened = await client.GetAsync($"{Boards}/{ownedBoard.Id}");
        Assert.Equal(
            lastAcceptedName, (await BodyAsync(reopened)).GetProperty("name").GetString());
    }

    // ---- AC-17 / edge case: a refusal by the limit itself is never counted, so the account is ------
    // admitted again once its earlier attempts fall out of the minute ------------------------------

    [Fact]
    public async Task An_account_that_stops_is_admitted_again_once_its_earlier_attempts_leave_the_minute()
    {
        factory.Clock.Reset();
        var caller = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(caller, "Board under the limit"); // spends 1 of 120
        var client = await factory.AWritingClientAsync(caller);

        for (var attempt = 0; attempt < PermittedChangesPerWindow - 1; attempt++)
        {
            await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = $"attempt-{attempt}" });
        }

        // At the ceiling: refused, and retried a few times — none of those retries may count.
        for (var retry = 0; retry < 5; retry++)
        {
            var stillLimited = await client.PatchAsJsonAsync(
                $"{Boards}/{board.Id}", new { name = "still-limited" });
            Assert.Equal(HttpStatusCode.TooManyRequests, stillLimited.StatusCode);
        }

        // Once every counted attempt has aged out of the rolling minute, the account is admitted
        // again — proving the refusals above never counted against it.
        factory.Clock.Advance(TimeSpan.FromSeconds(61));

        var admittedAgain = await client.PatchAsJsonAsync(
            $"{Boards}/{board.Id}", new { name = "Admitted again" });

        Assert.Equal(HttpStatusCode.OK, admittedAgain.StatusCode);
        Assert.Equal(
            "Admitted again", (await BodyAsync(admittedAgain)).GetProperty("name").GetString());

        factory.Clock.Reset();
    }

    // ---- Edge case: 500 reads in a minute are never limited ----------------------------------------

    [Fact]
    public async Task Reads_are_never_counted_toward_the_change_limit()
    {
        factory.Clock.Reset();
        var caller = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(caller, "Read all you like");
        var client = await factory.AWritingClientAsync(caller);

        for (var read = 0; read < 500; read++)
        {
            var response = await client.GetAsync($"{Boards}/{board.Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // A change is still admitted: 500 reads spent none of the 120-per-minute change budget.
        var change = await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = "Still admitted" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        factory.Clock.Reset();
    }

    // ---- AC-28: a change with an ended session is refused, changes nothing, and is not counted ------

    [Fact]
    public async Task A_change_attempt_with_an_ended_session_is_refused_and_not_counted()
    {
        factory.Clock.Reset();
        var caller = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(caller, "Before signing out");
        var client = await factory.AWritingClientAsync(caller);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<ISessionStore>()
                .RevokeAsync(caller.SessionId, CancellationToken.None);
        }

        // Flood far past what would be a change ceiling with an ended session — none of it may count,
        // because an attempt made with no active session belongs to no account (AC-17, AC-28).
        for (var attempt = 0; attempt < PermittedChangesPerWindow + 5; attempt++)
        {
            var response = await client.PatchAsJsonAsync(
                $"{Boards}/{board.Id}", new { name = $"after-sign-out-{attempt}" });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("accounts.session_not_recognised", await CodeOfAsync(response));
        }

        // Nothing changed on the board.
        var freshSessionId = await factory.ALiveSessionAsync(caller);
        var freshClient = factory.ClientCarrying(freshSessionId);
        var reopened = await freshClient.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal("Before signing out", (await BodyAsync(reopened)).GetProperty("name").GetString());

        // Not counted: a fresh, still-valid session for the same account can still make a change.
        var freshWritingClient = await factory.AWritingClientAsync(
            caller with { SessionId = freshSessionId });
        var change = await freshWritingClient.PatchAsJsonAsync(
            $"{Boards}/{board.Id}", new { name = "Admitted after sign-in again" });

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        factory.Clock.Reset();
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
