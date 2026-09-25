using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T11 — <c>/api/v1/boards/{boardId}/cards</c> on the wire: <c>addCard</c>, <c>openCard</c>,
/// <c>editCard</c> and <c>deleteCard</c>, with the title-before-description order the contract fixes
/// for AC-14, the 1,000-card ceiling (AC-15), the <c>current_card</c> a stale change is answered with
/// (AC-23), and text returned exactly as stored, markup included (AC-16). Every happy path runs once
/// as a non-owner <c>Member</c> (AC-21).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CardEndpointTests(ApiFactory factory)
{
    private const string Boards = "/api/v1/boards";

    private static string Cards(Guid boardId) => $"{Boards}/{boardId}/cards";

    private static string Card(Guid boardId, Guid cardId) => $"{Boards}/{boardId}/cards/{cardId}";

    // ---- AC-12 / AC-21: add — happy path, as a Member ------------------------------------------------

    [Fact]
    public async Task A_member_adds_a_card_and_it_lands_at_the_end_of_the_column()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Room for a card");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await FirstColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.PostAsJsonAsync(
            Cards(board.Id), new { column_id = columnId, title = "Test card", description = "Some detail" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(columnId.ToString(), body.GetProperty("column_id").GetString());
        Assert.Equal("Test card", body.GetProperty("title").GetString());
        Assert.Equal(0, body.GetProperty("position").GetInt32());
        Assert.Equal(1, body.GetProperty("content_version").GetInt32());
        Assert.True(Guid.TryParse(body.GetProperty("id").GetString(), out _));

        // DoD: the response validates against CardSummary — no description, no extra members.
        var topLevel = new HashSet<string>();
        foreach (var member2 in body.EnumerateObject())
        {
            topLevel.Add(member2.Name);
        }

        Assert.Equal(
            new HashSet<string> { "id", "column_id", "position", "title", "content_version" }, topLevel);
    }

    // ---- AC-12: an absent description defaults to empty ----------------------------------------------

    [Fact]
    public async Task Adding_a_card_with_no_description_stores_it_as_empty()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for a bare card");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var added = await client.PostAsJsonAsync(Cards(board.Id), new { column_id = columnId, title = "Bare card" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var opened = await client.GetAsync(Card(board.Id, cardId));
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal(string.Empty, (await BodyAsync(opened)).GetProperty("description").GetString());
    }

    // ---- AC-16: markup, line breaks, repeated spaces and a URL round-trip byte for byte ---------------

    [Fact]
    public async Task A_card_with_markup_and_a_url_round_trips_exactly_as_typed()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for markup");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        const string title = "<script>alert(1)</script>";
        const string description = "Line one\nLine two  with  double  spaces and https://example.com/path";

        var added = await client.PostAsJsonAsync(
            Cards(board.Id), new { column_id = columnId, title, description });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal("application/json; charset=utf-8", added.Content.Headers.ContentType?.ToString());
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var opened = await client.GetAsync(Card(board.Id, cardId));
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("application/json; charset=utf-8", opened.Content.Headers.ContentType?.ToString());

        var body = await BodyAsync(opened);
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(description, body.GetProperty("description").GetString());
    }

    // ---- AC-14: title checked before description on add -----------------------------------------------

    [Fact]
    public async Task Adding_a_card_with_both_an_invalid_title_and_an_invalid_description_reports_the_title_first()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for a bad add");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(
            Cards(board.Id),
            new { column_id = columnId, title = "   ", description = new string('x', 10_001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.card_title_invalid", await CodeOfAsync(response));
    }

    // ---- AC-14: a too-long description alone is refused on add ---------------------------------------

    [Fact]
    public async Task Adding_a_card_with_only_an_invalid_description_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Another board for a bad add");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(
            Cards(board.Id),
            new { column_id = columnId, title = "Fine title", description = new string('x', 10_001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.card_description_invalid", await CodeOfAsync(response));
    }

    // ---- AC-15: the 1,000-card ceiling, seeded by ABoardWithCardsAsync --------------------------------

    [Fact]
    public async Task Adding_a_card_to_a_board_that_already_holds_a_thousand_cards_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardWithCardsAsync(owner, cardCount: Board.MaxCards);
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(
            Cards(board.Id), new { column_id = columnId, title = "One too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.card_limit_reached", await CodeOfAsync(response));
    }

    // ---- edge case: a column_id from another board answers not_available -----------------------------

    [Fact]
    public async Task A_column_id_from_another_board_answers_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "This board");
        var otherBoard = await factory.ABoardAsync(owner, "Another board of the same owner");
        var foreignColumnId = await FirstColumnIdAsync(otherBoard.Id);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(
            Cards(board.Id), new { column_id = foreignColumnId, title = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));
    }

    // ---- AC-13 / AC-21: edit — happy path, as a Member ------------------------------------------------

    [Fact]
    public async Task A_member_edits_a_card_and_the_new_text_is_what_the_next_open_shows()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board to edit a card on");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await FirstColumnIdAsync(board.Id);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var added = await ownerClient.PostAsJsonAsync(
            Cards(board.Id), new { column_id = columnId, title = "Original", description = "Original detail" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var memberClient = await factory.AWritingClientAsync(member);
        var edited = await memberClient.PatchAsJsonAsync(
            Card(board.Id, cardId), new { title = "Renamed", description = "New detail", content_version = 1 });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedBody = await BodyAsync(edited);
        Assert.Equal("Renamed", editedBody.GetProperty("title").GetString());
        Assert.Equal("New detail", editedBody.GetProperty("description").GetString());
        Assert.Equal(2, editedBody.GetProperty("content_version").GetInt32());

        var opened = await ownerClient.GetAsync(Card(board.Id, cardId));
        var openedBody = await BodyAsync(opened);
        Assert.Equal("Renamed", openedBody.GetProperty("title").GetString());
        Assert.Equal("New detail", openedBody.GetProperty("description").GetString());
    }

    // ---- edge case: an edit with only content_version is request_invalid ------------------------------

    [Fact]
    public async Task Editing_with_only_a_content_version_is_refused_as_request_invalid()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for a shapeless edit");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var added = await client.PostAsJsonAsync(Cards(board.Id), new { column_id = columnId, title = "A card" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var response = await client.PatchAsJsonAsync(Card(board.Id, cardId), new { content_version = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(response));
    }

    // ---- AC-23: an edit against a stale content_version is refused and carries current_card ----------

    [Fact]
    public async Task Editing_against_a_stale_content_version_is_refused_and_carries_the_current_card()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board changed under them");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var added = await client.PostAsJsonAsync(Cards(board.Id), new { column_id = columnId, title = "Original" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var winner = await client.PatchAsJsonAsync(
            Card(board.Id, cardId), new { title = "Changed already", content_version = 1 });
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        var stale = await client.PatchAsJsonAsync(
            Card(board.Id, cardId), new { title = "My own new title", content_version = 1 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("boards.card_changed", await CodeOfAsync(stale));

        var body = await BodyAsync(stale);
        Assert.Equal("Changed already", body.GetProperty("current_card").GetProperty("title").GetString());
    }

    // ---- edge case: a non-member's stale content_version never reaches current_card ------------------

    [Fact]
    public async Task A_non_member_editing_with_a_stale_content_version_gets_not_available_not_current_card()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Not the stranger's board");
        var columnId = await FirstColumnIdAsync(board.Id);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var added = await ownerClient.PostAsJsonAsync(Cards(board.Id), new { column_id = columnId, title = "A card" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var strangerClient = await factory.AWritingClientAsync(stranger);
        var response = await strangerClient.PatchAsJsonAsync(
            Card(board.Id, cardId), new { title = "Sneaky", content_version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("boards.not_available", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("current_card", out _));
    }

    // ---- AC-18 / AC-21: delete — happy path, as a Member ----------------------------------------------

    [Fact]
    public async Task A_member_deletes_a_card_and_a_later_change_is_refused_as_never_existed()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board to delete a card on");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await FirstColumnIdAsync(board.Id);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var added = await ownerClient.PostAsJsonAsync(Cards(board.Id), new { column_id = columnId, title = "Doomed" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var memberClient = await factory.AWritingClientAsync(member);
        var deletion = await memberClient.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Card(board.Id, cardId))
        {
            Content = JsonContent.Create(new { content_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        var remaining = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [Id] = '{cardId}'");
        Assert.Equal(0, remaining);

        // ---- edge case: delete of an already deleted card is 404 not_available, never card_changed ----
        var again = await memberClient.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Card(board.Id, cardId))
        {
            Content = JsonContent.Create(new { content_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(again));
    }

    // ---- AC-23: a delete against a stale content_version is refused and carries current_card ---------

    [Fact]
    public async Task Deleting_against_a_stale_content_version_is_refused_and_carries_the_current_card()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Another board changed under them");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var added = await client.PostAsJsonAsync(Cards(board.Id), new { column_id = columnId, title = "Original" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var renamed = await client.PatchAsJsonAsync(
            Card(board.Id, cardId), new { title = "Renamed since", content_version = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var deletion = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Card(board.Id, cardId))
        {
            Content = JsonContent.Create(new { content_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        Assert.Equal("boards.card_changed", await CodeOfAsync(deletion));

        var body = await BodyAsync(deletion);
        Assert.Equal("Renamed since", body.GetProperty("current_card").GetProperty("title").GetString());

        var stillThere = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [Id] = '{cardId}'");
        Assert.Equal(1, stillThere);
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
