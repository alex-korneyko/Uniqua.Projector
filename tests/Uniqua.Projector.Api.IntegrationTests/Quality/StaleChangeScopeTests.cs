using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// T14 — AC-24b: a change to a different card or column is accepted without reopening the board.
/// The stale-change rule (spec.md §5, ADR 0016) is scoped to the one thing a version counter
/// belongs to — a card's own <c>content_version</c>, a column's own <c>name_version</c>, or the
/// board's <c>column_layout_version</c> — never to the board as a whole, so a member who changed
/// one card does not make every other member's view of everything else stale.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StaleChangeScopeTests(ApiFactory factory)
{
    private const string Boards = "/api/v1/boards";

    [Fact]
    public async Task Editing_a_different_card_and_moving_a_column_are_both_accepted_without_reopening()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board two members watch together");
        await factory.AMemberOfAsync(board.Id, member);

        var columnId = await FirstColumnIdAsync(board.Id);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var cardXResponse = await ownerClient.PostAsJsonAsync(
            $"{Boards}/{board.Id}/cards", new { column_id = columnId, title = "Card X" });
        Assert.Equal(HttpStatusCode.Created, cardXResponse.StatusCode);
        var cardXId = Guid.Parse((await BodyAsync(cardXResponse)).GetProperty("id").GetString()!);

        var cardYResponse = await ownerClient.PostAsJsonAsync(
            $"{Boards}/{board.Id}/cards", new { column_id = columnId, title = "Card Y" });
        Assert.Equal(HttpStatusCode.Created, cardYResponse.StatusCode);
        var cardYId = Guid.Parse((await BodyAsync(cardYResponse)).GetProperty("id").GetString()!);

        // Both members "opened" the board here: card X and card Y each at content_version 1, the
        // column at column_layout_version 1 — the versions every request below still carries.
        var memberClient = await factory.AWritingClientAsync(member);

        // One member edits card X. Neither card Y's content_version nor the board's
        // column_layout_version moves because of this (ADR 0016: per-concern version counters).
        var editX = await memberClient.PatchAsJsonAsync(
            $"{Boards}/{board.Id}/cards/{cardXId}", new { title = "Card X, changed", content_version = 1 });
        Assert.Equal(HttpStatusCode.OK, editX.StatusCode);

        // The other member — without reopening the board — edits card Y at the content_version
        // they still hold from before card X was ever touched. Accepted: card Y was never stale.
        var editY = await ownerClient.PatchAsJsonAsync(
            $"{Boards}/{board.Id}/cards/{cardYId}", new { title = "Card Y, changed", content_version = 1 });
        Assert.Equal(HttpStatusCode.OK, editY.StatusCode);

        // ...and moves a column at the column_layout_version they still hold, same reason: a card
        // edit never bumps it.
        var move = await ownerClient.PutAsJsonAsync(
            $"{Boards}/{board.Id}/columns/{columnId}/position",
            new { position = 1, column_layout_version = 1 });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
    }

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
