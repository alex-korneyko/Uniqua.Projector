using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T10 — <c>/api/v1/boards/{boardId}/columns</c> on the wire: <c>addColumn</c>, <c>renameColumn</c>,
/// <c>moveColumn</c> and <c>deleteColumn</c>, with the version counter each carries in its body
/// (ADR 0016) and the <c>current_column</c> / <c>current_layout</c> extension members a stale change
/// is answered with (AC-06b, AC-24). Every happy path runs once as a non-owner <c>Member</c> (AC-21).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ColumnEndpointTests(ApiFactory factory)
{
    private const string Boards = "/api/v1/boards";

    private static string Columns(Guid boardId) => $"{Boards}/{boardId}/columns";

    private static string Column(Guid boardId, Guid columnId) => $"{Boards}/{boardId}/columns/{columnId}";

    private static string Position(Guid boardId, Guid columnId) =>
        $"{Boards}/{boardId}/columns/{columnId}/position";

    // ---- AC-05 / AC-21: add — happy path, as a Member ---------------------------------------------

    [Fact]
    public async Task A_member_adds_a_column_and_it_lands_at_the_end_of_the_board()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Room for one more");
        await factory.AMemberOfAsync(board.Id, member);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.PostAsJsonAsync(Columns(board.Id), new { name = "Blocked" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await BodyAsync(response);
        var column = body.GetProperty("column");
        Assert.Equal("Blocked", column.GetProperty("name").GetString());
        Assert.Equal(3, column.GetProperty("position").GetInt32());
        Assert.True(column.GetProperty("name_version").GetInt32() >= 1);
        Assert.True(Guid.TryParse(column.GetProperty("id").GetString(), out _));
        Assert.True(body.GetProperty("column_layout_version").GetInt32() >= 1);

        // DoD: the response validates against AddedColumn — no extra members at either level.
        var topLevel = new HashSet<string>();
        foreach (var member2 in body.EnumerateObject())
        {
            topLevel.Add(member2.Name);
        }

        Assert.Equal(new HashSet<string> { "column", "column_layout_version" }, topLevel);
    }

    // ---- AC-08: add — an unusable name is refused ---------------------------------------------------

    [Theory]
    [InlineData("   ")]
    [InlineData("this-name-is-far-too-long-for-a-column-because-it-runs-well-past-fifty")]
    public async Task Adding_a_column_with_an_unusable_name_is_refused(string name)
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for a bad name");
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(Columns(board.Id), new { name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.column_name_invalid", await CodeOfAsync(response));
    }

    // ---- AC-11: add — the 20-column ceiling ----------------------------------------------------------

    [Fact]
    public async Task Adding_a_column_to_a_board_that_already_holds_twenty_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardWithColumnsAsync(owner, columnCount: 20);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(Columns(board.Id), new { name = "One too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.column_limit_reached", await CodeOfAsync(response));
    }

    // ---- rename — happy path, as a Member (AC-21) ----------------------------------------------------

    [Fact]
    public async Task A_member_renames_a_column_exactly_as_the_owner_could()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board to rename on");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await FirstColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "Backlog", name_version = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("Backlog", body.GetProperty("name").GetString());
        Assert.Equal(2, body.GetProperty("name_version").GetInt32());
    }

    // ---- AC-08: rename — an unusable name is refused -------------------------------------------------

    [Fact]
    public async Task Renaming_a_column_to_an_empty_name_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board to fail a rename on");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "   ", name_version = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.column_name_invalid", await CodeOfAsync(response));
    }

    // ---- AC-06b: rename against a stale name_version carries current_column -------------------------

    [Fact]
    public async Task Renaming_against_a_stale_name_version_is_refused_and_carries_the_current_column()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board renamed under them");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var winner = await client.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "Renamed already", name_version = 1 });
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        var stale = await client.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "My own new name", name_version = 1 });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("boards.column_renamed", await CodeOfAsync(stale));

        var body = await BodyAsync(stale);
        Assert.Equal("Renamed already", body.GetProperty("current_column").GetProperty("name").GetString());
    }

    // ---- AC-06b: delete against a stale name_version carries current_column -------------------------

    [Fact]
    public async Task Deleting_against_a_stale_name_version_is_refused_and_carries_the_current_column()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Another board renamed under them");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var renamed = await client.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "Renamed since", name_version = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var deletion = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Column(board.Id, columnId))
        {
            Content = JsonContent.Create(new { name_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        Assert.Equal("boards.column_renamed", await CodeOfAsync(deletion));

        var body = await BodyAsync(deletion);
        Assert.Equal("Renamed since", body.GetProperty("current_column").GetProperty("name").GetString());

        var stillThree = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}'");
        Assert.Equal(3, stillThree);
    }

    // ---- AC-09: delete — a column that still holds a card cannot be deleted -------------------------

    [Fact]
    public async Task Deleting_a_column_that_still_holds_a_card_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board with a busy column");
        var columnId = await FirstColumnIdAsync(board.Id);

        await factory.ExecuteAsync(
            $"""
            INSERT INTO [dbo].[Cards]
                ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
            VALUES ('{Guid.CreateVersion7()}', '{board.Id}', '{columnId}', 0, N'A card', N'', 1);
            """);
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Columns] SET [CardCount] = 1, [NextCardPosition] = 1 WHERE [Id] = '{columnId}';");
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Boards] SET [CardCount] = 1 WHERE [Id] = '{board.Id}';");

        var client = await factory.AWritingClientAsync(owner);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Column(board.Id, columnId))
        {
            Content = JsonContent.Create(new { name_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.column_not_empty", await CodeOfAsync(response));
    }

    // ---- AC-10: delete — the last remaining column cannot be deleted --------------------------------

    [Fact]
    public async Task Deleting_the_last_remaining_column_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board trimmed to one column");
        var columnId = await FirstColumnIdAsync(board.Id);

        // Trims the other two default columns directly, so the board holds exactly one — no cards.
        await factory.ExecuteAsync(
            $"DELETE FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}' AND [Id] <> '{columnId}';");

        var client = await factory.AWritingClientAsync(owner);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Column(board.Id, columnId))
        {
            Content = JsonContent.Create(new { name_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.last_column", await CodeOfAsync(response));

        var stillThere = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}'");
        Assert.Equal(1, stillThere);
    }

    // ---- move — happy path, as a Member (AC-21) ------------------------------------------------------

    [Fact]
    public async Task A_member_moves_a_column_to_a_new_position_exactly_as_the_owner_could()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board to reorder");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await FirstColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.PutAsJsonAsync(
            Position(board.Id, columnId), new { position = 2, column_layout_version = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(2, body.GetProperty("column_layout_version").GetInt32());
        var moved = body.GetProperty("columns").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == columnId.ToString());
        Assert.Equal(2, moved.GetProperty("position").GetInt32());
    }

    // ---- edge case: position 7 on a 3-column board with a current layout version --------------------

    [Fact]
    public async Task Moving_a_column_to_a_position_off_the_board_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A three-column board");
        var columnId = await FirstColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(owner);
        var response = await client.PutAsJsonAsync(
            Position(board.Id, columnId), new { position = 7, column_layout_version = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.column_position_invalid", await CodeOfAsync(response));
    }

    // ---- edge case: position 7 with a stale layout version answers the stale check first -------------

    [Fact]
    public async Task Moving_a_column_off_the_board_with_a_stale_layout_version_answers_columns_changed_first()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board reordered under them");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        // Bumps ColumnLayoutVersion to 2 by adding a column, so seenLayoutVersion: 1 is now stale.
        var added = await client.PostAsJsonAsync(Columns(board.Id), new { name = "Extra" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var response = await client.PutAsJsonAsync(
            Position(board.Id, columnId), new { position = 7, column_layout_version = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.columns_changed", await CodeOfAsync(response));

        var body = await BodyAsync(response);
        var layout = body.GetProperty("current_layout");
        Assert.Equal(2, layout.GetProperty("column_layout_version").GetInt32());
        Assert.Equal(4, layout.GetProperty("columns").GetArrayLength());
    }

    // ---- AC-24: move against a stale layout version carries current_layout --------------------------

    [Fact]
    public async Task Moving_a_column_against_a_stale_layout_version_is_refused_and_carries_the_current_layout()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board changed under them");
        var columnId = await FirstColumnIdAsync(board.Id);
        var client = await factory.AWritingClientAsync(owner);

        var added = await client.PostAsJsonAsync(Columns(board.Id), new { name = "Extra" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var response = await client.PutAsJsonAsync(
            Position(board.Id, columnId), new { position = 1, column_layout_version = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.columns_changed", await CodeOfAsync(response));

        var body = await BodyAsync(response);
        var layout = body.GetProperty("current_layout");
        Assert.Equal(2, layout.GetProperty("column_layout_version").GetInt32());
        Assert.Equal(4, layout.GetProperty("columns").GetArrayLength());
    }

    // ---- edge case: a columnId belonging to another board answers not_available ---------------------

    [Fact]
    public async Task A_column_id_from_another_board_answers_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "This board");
        var otherBoard = await factory.ABoardAsync(owner, "Another board of the same owner");
        var foreignColumnId = await FirstColumnIdAsync(otherBoard.Id);

        var client = await factory.AWritingClientAsync(owner);
        var response = await client.PatchAsJsonAsync(
            Column(board.Id, foreignColumnId), new { name = "Hijacked", name_version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));
    }

    // ---- edge case: a non-member's stale name_version never reaches current_column -------------------

    [Fact]
    public async Task A_non_member_renaming_with_a_stale_name_version_gets_not_available_not_column_renamed()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Not the stranger's board");
        var columnId = await FirstColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(stranger);
        var response = await client.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "Sneaky", name_version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("boards.not_available", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("current_column", out _));
    }

    // ---- edge case: DELETE with no body is request_invalid after membership, not_available before ----

    [Fact]
    public async Task Deleting_with_no_body_is_refused_after_membership_but_not_available_before_it()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for a bodiless delete");
        var columnId = await FirstColumnIdAsync(board.Id);

        var ownerClient = await factory.AWritingClientAsync(owner);
        var ownerResponse = await ownerClient.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Column(board.Id, columnId))
        {
            Content = JsonContent.Create(new { }),
        });

        Assert.Equal(HttpStatusCode.BadRequest, ownerResponse.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(ownerResponse));

        var strangerClient = await factory.AWritingClientAsync(stranger);
        var strangerResponse = await strangerClient.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Column(board.Id, columnId))
        {
            Content = JsonContent.Create(new { }),
        });

        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(strangerResponse));
    }

    // ---- delete — happy path, as a Member (AC-21) ------------------------------------------------------

    [Fact]
    public async Task A_member_deletes_an_empty_column_exactly_as_the_owner_could()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board with a spare column");
        await factory.AMemberOfAsync(board.Id, member);
        var lastColumnId = await LastColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, Column(board.Id, lastColumnId))
        {
            Content = JsonContent.Create(new { name_version = 1 }),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(2, body.GetProperty("columns").GetArrayLength());

        var remaining = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}'");
        Assert.Equal(2, remaining);
    }

    // ---- AC-18b (review B7d): a deleted column is refused exactly as a column that never existed ------

    [Theory]
    [InlineData("rename")]
    [InlineData("move")]
    [InlineData("delete")]
    public async Task A_change_naming_a_deleted_column_is_refused_exactly_as_one_naming_a_random_id(string operation)
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board that loses a column");
        await factory.AMemberOfAsync(board.Id, member);
        var goneId = await LastColumnIdAsync(board.Id);

        var ownerClient = await factory.AWritingClientAsync(owner);
        var removed = await ownerClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Column(board.Id, goneId))
        {
            Content = JsonContent.Create(new { name_version = 1 }),
        });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var layoutVersion = (await BodyAsync(removed)).GetProperty("column_layout_version").GetInt32();
        var before = await (await ownerClient.GetAsync($"{Boards}/{board.Id}")).Content.ReadAsStringAsync();

        HttpRequestMessage Request(Guid columnId) => operation switch
        {
            "rename" => new(HttpMethod.Patch, Column(board.Id, columnId))
            {
                Content = JsonContent.Create(new { name = "Kept typing", name_version = 1 }),
            },
            "move" => new(HttpMethod.Put, Position(board.Id, columnId))
            {
                Content = JsonContent.Create(new { position = 0, column_layout_version = layoutVersion }),
            },
            _ => new(HttpMethod.Delete, Column(board.Id, columnId))
            {
                Content = JsonContent.Create(new { name_version = 1 }),
            },
        };

        var memberClient = await factory.AWritingClientAsync(member);
        var deleted = await memberClient.SendAsync(Request(goneId));
        var random = await memberClient.SendAsync(Request(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(deleted));
        await AssertSameRefusalAsync(random, deleted);

        var after = await (await ownerClient.GetAsync($"{Boards}/{board.Id}")).Content.ReadAsStringAsync();
        Assert.Equal(before, after);
    }

    // ---- AC-06b (review B7d): a stale rename or delete from a genuinely separate session -------------

    [Theory]
    [InlineData("another member", "rename")]
    [InlineData("another member", "delete")]
    [InlineData("the same account in a second session", "rename")]
    [InlineData("the same account in a second session", "delete")]
    public async Task A_change_from_a_session_whose_view_predates_another_sessions_rename_is_refused(
        string other, string operation)
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board open in two places");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await LastColumnIdAsync(board.Id);

        var firstSession = await factory.AWritingClientAsync(owner);
        var secondSession = other == "another member"
            ? await factory.AWritingClientAsync(member)
            : await factory.AWritingClientAsync(owner with { SessionId = await factory.ALiveSessionAsync(owner) });

        var seen = (await BodyAsync(await firstSession.GetAsync($"{Boards}/{board.Id}")))
            .GetProperty("columns").EnumerateArray()
            .Single(column => column.GetProperty("id").GetString() == columnId.ToString())
            .GetProperty("name_version").GetInt32();

        var winner = await secondSession.PatchAsJsonAsync(
            Column(board.Id, columnId), new { name = "Renamed elsewhere", name_version = seen });
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        var stale = operation == "rename"
            ? await firstSession.PatchAsJsonAsync(
                Column(board.Id, columnId), new { name = "My own name", name_version = seen })
            : await firstSession.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Column(board.Id, columnId))
            {
                Content = JsonContent.Create(new { name_version = seen }),
            });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await BodyAsync(stale);
        Assert.Equal("boards.column_renamed", body.GetProperty("code").GetString());
        Assert.Equal("Renamed elsewhere", body.GetProperty("current_column").GetProperty("name").GetString());
        Assert.Equal(seen + 1, body.GetProperty("current_column").GetProperty("name_version").GetInt32());

        Assert.Equal(["To do", "In progress", "Renamed elsewhere"], await ColumnNamesAsync(board.Id));
    }

    // ---- T23 (review Q1): a lone-surrogate name is an unusable string, not a server error --------------

    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    public async Task A_lone_surrogate_column_name_is_request_invalid_for_a_member_and_not_available_for_anyone_else(
        string operation)
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board that gets a broken column name");
        var columnId = await FirstColumnIdAsync(board.Id);

        var (method, url, json) = operation == "add"
            ? (HttpMethod.Post, Columns(board.Id), """{"name":"\ud800"}""")
            : (HttpMethod.Patch, Column(board.Id, columnId), """{"name":"x\udfff","name_version":1}""");

        var ownerResponse = await SendJsonTextAsync(await factory.AWritingClientAsync(owner), method, url, json);
        var strangerResponse = await SendJsonTextAsync(await factory.AWritingClientAsync(stranger), method, url, json);

        Assert.Equal(HttpStatusCode.BadRequest, ownerResponse.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(ownerResponse));
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(strangerResponse));
        Assert.Equal(["To do", "In progress", "Done"], await ColumnNamesAsync(board.Id));
    }

    // ---- T23 (review B6a): a member outside the request schema is request_invalid --------------------

    [Theory]
    [InlineData("add", """{"name":"Extra","extra":1}""")]
    [InlineData("rename", """{"name":"Extra","name_version":1,"extra":1}""")]
    [InlineData("move", """{"position":2,"column_layout_version":1,"extra":1}""")]
    [InlineData("delete", """{"name_version":1,"extra":null}""")]
    public async Task An_unknown_member_in_a_column_change_is_request_invalid_after_membership_and_changes_nothing(
        string operation, string json)
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board with strict column schemas");
        var columnId = await FirstColumnIdAsync(board.Id);

        var (method, url) = operation switch
        {
            "add" => (HttpMethod.Post, Columns(board.Id)),
            "rename" => (HttpMethod.Patch, Column(board.Id, columnId)),
            "move" => (HttpMethod.Put, Position(board.Id, columnId)),
            _ => (HttpMethod.Delete, Column(board.Id, columnId)),
        };

        var ownerResponse = await SendJsonTextAsync(await factory.AWritingClientAsync(owner), method, url, json);
        var strangerResponse = await SendJsonTextAsync(await factory.AWritingClientAsync(stranger), method, url, json);

        Assert.Equal(HttpStatusCode.BadRequest, ownerResponse.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(ownerResponse));
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(strangerResponse));
        Assert.Equal(["To do", "In progress", "Done"], await ColumnNamesAsync(board.Id));
    }

    // ---- T23 (review B6c): a non-UUID column id is a column that does not exist, after the shape -------

    [Theory]
    [InlineData("rename")]
    [InlineData("move")]
    [InlineData("delete")]
    public async Task A_non_uuid_column_id_with_a_bad_body_is_answered_like_a_missing_column_id_with_a_bad_body(
        string operation)
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board asked about columns it lacks");
        var client = await factory.AWritingClientAsync(owner);

        string UrlFor(string columnId) => operation == "move"
            ? $"{Boards}/{board.Id}/columns/{columnId}/position"
            : $"{Boards}/{board.Id}/columns/{columnId}";

        var method = operation switch
        {
            "rename" => HttpMethod.Patch,
            "move" => HttpMethod.Put,
            _ => HttpMethod.Delete,
        };

        var notAUuid = await SendJsonTextAsync(client, method, UrlFor("not-a-uuid"), "{}");
        var missing = await SendJsonTextAsync(client, method, UrlFor(Guid.CreateVersion7().ToString()), "{}");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(missing));
        await AssertSameRefusalAsync(missing, notAUuid);
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    /// <summary>Sends <paramref name="json"/> exactly as written, byte for byte.</summary>
    private static Task<HttpResponseMessage> SendJsonTextAsync(
        HttpClient client, HttpMethod method, string url, string json) =>
        client.SendAsync(new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    private async Task<string[]> ColumnNamesAsync(Guid boardId)
    {
        var names = new List<string>();
        for (var position = 0; ; position++)
        {
            var name = await factory.ScalarAsync<string?>(
                $"SELECT [Name] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' AND [Position] = {position}");
            if (name is null)
            {
                return [.. names];
            }

            names.Add(name);
        }
    }

    /// <summary>
    /// Field for field: status, content type, and every body member but <c>instance</c> and
    /// <c>traceId</c> (which echo the request itself) — present in both, with the same value.
    /// </summary>
    private static async Task AssertSameRefusalAsync(HttpResponseMessage expected, HttpResponseMessage actual)
    {
        Assert.Equal(expected.StatusCode, actual.StatusCode);
        Assert.Equal(
            expected.Content.Headers.ContentType?.ToString(), actual.Content.Headers.ContentType?.ToString());

        static string[] Members(JsonElement body) =>
        [
            .. body.EnumerateObject()
                .Where(member => member.Name is not ("instance" or "traceId"))
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .Select(member => $"{member.Name}={member.Value}"),
        ];

        Assert.Equal(Members(await BodyAsync(expected)), Members(await BodyAsync(actual)));
    }

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    private async Task<Guid> LastColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position] DESC");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
