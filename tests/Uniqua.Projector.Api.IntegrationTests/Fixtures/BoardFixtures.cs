using System.Net.Http.Json;
using System.Text.Json;
using Uniqua.Projector.Api.Antiforgery;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// The board-feature fixture builders data-model.md § Test fixtures names, so T9's endpoint tests
/// (and T10-T12's) state which situation they are testing rather than assembling it inline every
/// time.
/// </summary>
public static class BoardFixtures
{
    /// <summary>
    /// Creates a board through the real API, as an owner writing client would (data-model.md § Test
    /// fixtures: "creates a board through the API").
    /// </summary>
    public static async Task<TestBoard> ABoardAsync(this ApiFactory factory, TestAccount owner, string name)
    {
        var client = await factory.AWritingClientAsync(owner);
        var response = await client.PostAsJsonAsync("/api/v1/boards", new { name });

        response.EnsureSuccessStatusCode();

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return new TestBoard(
            Guid.Parse(body.GetProperty("id").GetString()!),
            body.GetProperty("name").GetString()!);
    }

    /// <summary>
    /// Inserts a <c>BoardMemberships</c> row with <c>Role = Member</c> directly through
    /// <c>AppDbContext</c> (in fact, a raw statement against the same store) — the only way a
    /// non-owner member exists in this feature, since nothing in the API adds a member ahead of
    /// invitations (roadmap step 7).
    /// </summary>
    public static Task AMemberOfAsync(this ApiFactory factory, Guid boardId, TestAccount account) =>
        factory.ExecuteAsync(
            $"""
            INSERT INTO [dbo].[BoardMemberships] ([Id], [BoardId], [AccountId], [Role])
            VALUES ('{Guid.CreateVersion7()}', '{boardId}', '{account.Id}', N'Member');
            """);

    /// <summary>
    /// An account whose <c>OwnedBoardCounters</c> row already reads <paramref name="count"/> —
    /// written directly rather than by creating that many boards, so AC-03's ceiling test does not
    /// pay for fifty real board creations.
    /// </summary>
    public static async Task<TestAccount> AnAccountOwningBoardsAsync(this ApiFactory factory, int count)
    {
        var account = await factory.AnAccountAsync();

        await factory.ExecuteAsync(
            $"""
            MERGE [dbo].[OwnedBoardCounters] AS target
            USING (SELECT '{account.Id}' AS AccountId) AS source
            ON target.[AccountId] = source.[AccountId]
            WHEN MATCHED THEN UPDATE SET [OwnedBoardCount] = {count}
            WHEN NOT MATCHED THEN INSERT ([AccountId], [OwnedBoardCount]) VALUES (source.[AccountId], {count});
            """);

        return account;
    }

    /// <summary>
    /// A client carrying <paramref name="account"/>'s session cookie and the antiforgery pair a
    /// write needs, with its own apparent peer so per-account limits do not spill across tests.
    /// </summary>
    public static async Task<HttpClient> AWritingClientAsync(this ApiFactory factory, TestAccount account)
    {
        var client = factory.ClientCarrying(account.SessionId);
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
}

/// <summary>A board created through the API, and the little a test needs to act on it.</summary>
public sealed record TestBoard(Guid Id, string Name);
