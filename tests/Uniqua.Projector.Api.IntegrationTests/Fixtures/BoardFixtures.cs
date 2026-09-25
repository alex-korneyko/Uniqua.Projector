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
    /// A board (through the API, so it starts with the three default columns) filled directly with
    /// filler columns up to <paramref name="columnCount"/> total — one short of the 20-column ceiling
    /// for AC-11 and the spec §6 race pairs (data-model.md § Test fixtures). Inserted directly rather
    /// than through 17 API calls, keeping <c>Columns.Position</c> dense and each filler's
    /// <c>NameVersion</c> at 1, same as a column the API itself would have created.
    /// </summary>
    public static async Task<TestBoard> ABoardWithColumnsAsync(
        this ApiFactory factory, TestAccount owner, int columnCount, string name = "Test board")
    {
        var board = await factory.ABoardAsync(owner, name);

        for (var position = 3; position < columnCount; position++)
        {
            await factory.ExecuteAsync(
                $"""
                INSERT INTO [dbo].[Columns]
                    ([Id], [BoardId], [Name], [Position], [CardCount], [NextCardPosition], [NameVersion])
                VALUES ('{Guid.CreateVersion7()}', '{board.Id}', N'Filler {position}', {position}, 0, 0, 1);
                """);
        }

        return board;
    }

    /// <summary>
    /// A board (through the API, so it starts with the three default columns) with
    /// <paramref name="cardCount"/> cards inserted directly into its first column — up to one short
    /// of the 1,000-card ceiling for AC-15's tests (data-model.md § Test fixtures). Inserted directly
    /// rather than through <paramref name="cardCount"/> API calls, which would run into the
    /// 120-per-minute change limit; <c>Boards.CardCount</c>, <c>Columns.CardCount</c> and
    /// <c>Columns.NextCardPosition</c> are kept consistent with the rows inserted, same as the API
    /// itself would leave them.
    /// </summary>
    public static async Task<TestBoard> ABoardWithCardsAsync(
        this ApiFactory factory, TestAccount owner, int cardCount, string name = "Test board")
    {
        var board = await factory.ABoardAsync(owner, name);
        var columnId = await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}' ORDER BY [Position]");

        for (var position = 0; position < cardCount; position++)
        {
            await factory.ExecuteAsync(
                $"""
                INSERT INTO [dbo].[Cards]
                    ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
                VALUES ('{Guid.CreateVersion7()}', '{board.Id}', '{columnId}', {position}, N'Filler {position}', N'', 1);
                """);
        }

        if (cardCount > 0)
        {
            await factory.ExecuteAsync(
                $"""
                UPDATE [dbo].[Columns] SET [CardCount] = {cardCount}, [NextCardPosition] = {cardCount}
                WHERE [Id] = '{columnId}';
                """);
            await factory.ExecuteAsync(
                $"UPDATE [dbo].[Boards] SET [CardCount] = {cardCount} WHERE [Id] = '{board.Id}';");
        }

        return board;
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
