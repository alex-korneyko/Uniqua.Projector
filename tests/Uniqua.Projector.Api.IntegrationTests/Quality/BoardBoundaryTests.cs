using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// T13 — sad.md §10 QG-1: across every read and change kind, a board the caller is not a member of
/// is answered exactly as a board that never existed — status, every header and every body member
/// except <c>instance</c> and <c>traceId</c> (contracts/openapi.yaml,
/// components.responses.BoardNotAvailable) — with valid, invalid, stale and cross-board inputs
/// among the requests compared (AC-25, AC-26). A fixture-inserted <c>Member</c> (ADR 0013) proves
/// the other half of the boundary: every column and card change succeeds exactly as the owner's
/// would (AC-21), and only rename and delete are refused, with <c>boards.owner_only</c> (AC-22).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BoardBoundaryTests(ApiFactory factory)
{
    private const string Boards = "/api/v1/boards";

    // ---- The eleven board-naming operations, compared non-member vs never-existed -----------------

    [Fact]
    public async Task Every_board_naming_operation_answers_a_non_member_exactly_as_it_answers_a_never_existed_board()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Not the stranger's board");
        var columnId = await FirstColumnIdAsync(board.Id);
        var cardId = await ACardIdAsync(owner, board.Id, columnId);

        var otherBoard = await factory.ABoardAsync(owner, "A different board of the same owner");
        var foreignColumnId = await FirstColumnIdAsync(otherBoard.Id);
        var foreignCardId = await ACardIdAsync(owner, otherBoard.Id, foreignColumnId);

        var client = await factory.AWritingClientAsync(stranger);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var violations = new List<string>();

        foreach (var scenario in Scenarios(board.Id, columnId, cardId, foreignColumnId, foreignCardId))
        {
            var before = await BoardSnapshotAsync(ownerClient, board.Id);

            var nonMember = await client.SendAsync(scenario.Build(board.Id));
            var neverExisted = await client.SendAsync(scenario.Build(Guid.CreateVersion7()));

            var after = await BoardSnapshotAsync(ownerClient, board.Id);

            if (before != after)
            {
                violations.Add($"{scenario.Name}: the board changed after a non-member's request.");
            }

            await AssertIndistinguishableAsync(nonMember, neverExisted, scenario.Name, violations);
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} violation(s) of the indistinguishable-refusal rule "
            + $"(spec.md §5 AC-25, §6 NFR):\n" + string.Join("\n", violations));
    }

    private static IEnumerable<Scenario> Scenarios(
        Guid boardId, Guid columnId, Guid cardId, Guid foreignColumnId, Guid foreignCardId)
    {
        // ---- openBoard ------------------------------------------------------------------------------
        yield return new Scenario("openBoard, valid", id => new HttpRequestMessage(
            HttpMethod.Get, $"{Boards}/{id}"));

        // Review Q4h: where the non-member would name the real board, this sends a board id that is
        // not a UUID at all, compared with the never-existed board — the same one refusal either way.
        yield return new Scenario("openBoard, non-UUID board id", id => new HttpRequestMessage(
            HttpMethod.Get, id == boardId ? $"{Boards}/not-a-uuid" : $"{Boards}/{id}"));
        yield return new Scenario("renameBoard, non-UUID board id", id => JsonRequest(
            HttpMethod.Patch, id == boardId ? $"{Boards}/not-a-uuid" : $"{Boards}/{id}", new { name = "New name" }));

        // ---- renameBoard ----------------------------------------------------------------------------
        yield return new Scenario("renameBoard, valid body", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}", new { name = "New name" }));
        yield return new Scenario("renameBoard, invalid body", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}", new { name = "" }));
        yield return new Scenario("renameBoard, not-json body", id => NotJsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}"));

        // ---- deleteBoard ----------------------------------------------------------------------------
        yield return new Scenario("deleteBoard, valid-shaped confirmation", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}", new { confirm_name = "Not the stranger's board" }));
        yield return new Scenario("deleteBoard, invalid-shaped confirmation", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}", new { confirm_name = 5 }));

        // ---- addColumn ------------------------------------------------------------------------------
        yield return new Scenario("addColumn, valid body", id => JsonRequest(
            HttpMethod.Post, $"{Boards}/{id}/columns", new { name = "Sneaky column" }));
        yield return new Scenario("addColumn, invalid body", id => JsonRequest(
            HttpMethod.Post, $"{Boards}/{id}/columns", new { name = "   " }));

        // ---- renameColumn ---------------------------------------------------------------------------
        yield return new Scenario("renameColumn, valid body", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/columns/{columnId}", new { name = "Sneaky", name_version = 1 }));
        yield return new Scenario("renameColumn, invalid body", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/columns/{columnId}", new { name = "", name_version = 1 }));
        yield return new Scenario("renameColumn, stale name_version", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/columns/{columnId}", new { name = "Sneaky", name_version = 999 }));
        yield return new Scenario("renameColumn, non-UUID column id", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/columns/not-a-guid", new { name = "Sneaky", name_version = 1 }));
        yield return new Scenario("renameColumn, cross-board column id", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/columns/{foreignColumnId}", new { name = "Sneaky", name_version = 1 }));

        // ---- moveColumn -----------------------------------------------------------------------------
        yield return new Scenario("moveColumn, valid body", id => JsonRequest(
            HttpMethod.Put, $"{Boards}/{id}/columns/{columnId}/position", new { position = 1, column_layout_version = 1 }));
        yield return new Scenario("moveColumn, invalid position", id => JsonRequest(
            HttpMethod.Put, $"{Boards}/{id}/columns/{columnId}/position", new { position = 99, column_layout_version = 1 }));
        yield return new Scenario("moveColumn, stale layout version", id => JsonRequest(
            HttpMethod.Put, $"{Boards}/{id}/columns/{columnId}/position", new { position = 1, column_layout_version = 999 }));
        yield return new Scenario("moveColumn, cross-board column id", id => JsonRequest(
            HttpMethod.Put, $"{Boards}/{id}/columns/{foreignColumnId}/position", new { position = 1, column_layout_version = 1 }));

        // ---- deleteColumn ---------------------------------------------------------------------------
        yield return new Scenario("deleteColumn, valid body", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}/columns/{columnId}", new { name_version = 1 }));
        yield return new Scenario("deleteColumn, no body", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}/columns/{columnId}", new { }));
        yield return new Scenario("deleteColumn, cross-board column id", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}/columns/{foreignColumnId}", new { name_version = 1 }));

        // ---- addCard --------------------------------------------------------------------------------
        yield return new Scenario("addCard, valid body", id => JsonRequest(
            HttpMethod.Post, $"{Boards}/{id}/cards", new { column_id = columnId, title = "Sneaky card" }));
        yield return new Scenario("addCard, invalid body", id => JsonRequest(
            HttpMethod.Post, $"{Boards}/{id}/cards", new { column_id = columnId, title = "" }));
        yield return new Scenario("addCard, cross-board column id", id => JsonRequest(
            HttpMethod.Post, $"{Boards}/{id}/cards", new { column_id = foreignColumnId, title = "Sneaky card" }));

        // ---- openCard -------------------------------------------------------------------------------
        yield return new Scenario("openCard, valid path", id => new HttpRequestMessage(
            HttpMethod.Get, $"{Boards}/{id}/cards/{cardId}"));
        yield return new Scenario("openCard, non-UUID card id", id => new HttpRequestMessage(
            HttpMethod.Get, $"{Boards}/{id}/cards/not-a-guid"));
        yield return new Scenario("openCard, cross-board card id", id => new HttpRequestMessage(
            HttpMethod.Get, $"{Boards}/{id}/cards/{foreignCardId}"));

        // ---- editCard -------------------------------------------------------------------------------
        yield return new Scenario("editCard, valid body", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/cards/{cardId}", new { title = "Sneaky", content_version = 1 }));
        yield return new Scenario("editCard, invalid body (version only)", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/cards/{cardId}", new { content_version = 1 }));
        yield return new Scenario("editCard, stale content_version", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/cards/{cardId}", new { title = "Sneaky", content_version = 999 }));
        yield return new Scenario("editCard, cross-board card id", id => JsonRequest(
            HttpMethod.Patch, $"{Boards}/{id}/cards/{foreignCardId}", new { title = "Sneaky", content_version = 1 }));

        // ---- deleteCard -----------------------------------------------------------------------------
        yield return new Scenario("deleteCard, valid body", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}/cards/{cardId}", new { content_version = 1 }));
        yield return new Scenario("deleteCard, no body", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}/cards/{cardId}", new { }));
        yield return new Scenario("deleteCard, cross-board card id", id => JsonRequest(
            HttpMethod.Delete, $"{Boards}/{id}/cards/{foreignCardId}", new { content_version = 1 }));
    }

    // ---- AC-26: a member of two boards names the other board's column/card through the first ------

    [Fact]
    public async Task A_member_of_two_boards_naming_the_others_column_through_the_first_gets_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var boardA = await factory.ABoardAsync(owner, "Board A");
        var boardB = await factory.ABoardAsync(owner, "Board B");
        await factory.AMemberOfAsync(boardA.Id, member);
        await factory.AMemberOfAsync(boardB.Id, member);
        var columnOfB = await FirstColumnIdAsync(boardB.Id);

        var client = await factory.AWritingClientAsync(member);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var beforeA = await BoardSnapshotAsync(ownerClient, boardA.Id);
        var beforeB = await BoardSnapshotAsync(ownerClient, boardB.Id);

        var response = await client.PatchAsJsonAsync(
            $"{Boards}/{boardA.Id}/columns/{columnOfB}", new { name = "Hijacked", name_version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));

        Assert.Equal(beforeA, await BoardSnapshotAsync(ownerClient, boardA.Id));
        Assert.Equal(beforeB, await BoardSnapshotAsync(ownerClient, boardB.Id));
    }

    [Fact]
    public async Task A_member_of_two_boards_naming_the_others_card_through_the_first_gets_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var boardA = await factory.ABoardAsync(owner, "Board A, cards");
        var boardB = await factory.ABoardAsync(owner, "Board B, cards");
        await factory.AMemberOfAsync(boardA.Id, member);
        await factory.AMemberOfAsync(boardB.Id, member);
        var cardOfB = await ACardIdAsync(owner, boardB.Id, await FirstColumnIdAsync(boardB.Id));

        var client = await factory.AWritingClientAsync(member);
        var ownerClient = await factory.AWritingClientAsync(owner);

        var beforeA = await BoardSnapshotAsync(ownerClient, boardA.Id);
        var beforeB = await BoardSnapshotAsync(ownerClient, boardB.Id);

        var response = await client.PatchAsJsonAsync(
            $"{Boards}/{boardA.Id}/cards/{cardOfB}", new { title = "Hijacked", content_version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));

        Assert.Equal(beforeA, await BoardSnapshotAsync(ownerClient, boardA.Id));
        Assert.Equal(beforeB, await BoardSnapshotAsync(ownerClient, boardB.Id));
    }

    // ---- AC-21 / AC-22: a non-owner Member across every column and card change ----------------------

    [Fact]
    public async Task A_non_owner_member_succeeds_at_every_column_and_card_change_and_is_refused_owner_only_on_rename_and_delete()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board a member changes everywhere");
        await factory.AMemberOfAsync(board.Id, member);
        var columnId = await FirstColumnIdAsync(board.Id);

        var client = await factory.AWritingClientAsync(member);

        var addedColumn = await client.PostAsJsonAsync(
            $"{Boards}/{board.Id}/columns", new { name = "Member's column" });
        Assert.Equal(HttpStatusCode.Created, addedColumn.StatusCode);
        var newColumnId = Guid.Parse((await BodyAsync(addedColumn)).GetProperty("column").GetProperty("id").GetString()!);

        var renamedColumn = await client.PatchAsJsonAsync(
            $"{Boards}/{board.Id}/columns/{newColumnId}", new { name = "Renamed by member", name_version = 1 });
        Assert.Equal(HttpStatusCode.OK, renamedColumn.StatusCode);

        var movedColumn = await client.PutAsJsonAsync(
            $"{Boards}/{board.Id}/columns/{newColumnId}/position", new { position = 0, column_layout_version = 2 });
        Assert.Equal(HttpStatusCode.OK, movedColumn.StatusCode);

        var addedCard = await client.PostAsJsonAsync(
            $"{Boards}/{board.Id}/cards", new { column_id = columnId, title = "Member's card" });
        Assert.Equal(HttpStatusCode.Created, addedCard.StatusCode);
        var cardId = Guid.Parse((await BodyAsync(addedCard)).GetProperty("id").GetString()!);

        var editedCard = await client.PatchAsJsonAsync(
            $"{Boards}/{board.Id}/cards/{cardId}", new { title = "Edited by member", content_version = 1 });
        Assert.Equal(HttpStatusCode.OK, editedCard.StatusCode);

        var deletedCard = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}/cards/{cardId}")
        {
            Content = JsonContent.Create(new { content_version = 2 }),
        });
        Assert.Equal(HttpStatusCode.NoContent, deletedCard.StatusCode);

        var deletedColumn = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}/columns/{newColumnId}")
        {
            Content = JsonContent.Create(new { name_version = 2 }),
        });
        Assert.Equal(HttpStatusCode.OK, deletedColumn.StatusCode);

        // Rename and delete of the board itself: refused, board unchanged.
        var renameBoard = await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = "Hijacked board" });
        Assert.Equal(HttpStatusCode.Forbidden, renameBoard.StatusCode);
        Assert.Equal("boards.owner_only", await CodeOfAsync(renameBoard));

        var deleteBoard = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}")
        {
            Content = JsonContent.Create(new { confirm_name = board.Name }),
        });
        Assert.Equal(HttpStatusCode.Forbidden, deleteBoard.StatusCode);
        Assert.Equal("boards.owner_only", await CodeOfAsync(deleteBoard));

        var stillThere = await client.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        Assert.Equal(board.Name, (await BodyAsync(stillThere)).GetProperty("name").GetString());
    }

    // ---- AC-21 (review B7d): the board rules refuse a non-owner member exactly as they refuse the owner

    [Theory]
    [InlineData("column not empty", "boards.column_not_empty")]
    [InlineData("last column", "boards.last_column")]
    [InlineData("column limit", "boards.column_limit_reached")]
    [InlineData("card limit", "boards.card_limit_reached")]
    [InlineData("stale column rename", "boards.column_renamed")]
    [InlineData("stale column move", "boards.columns_changed")]
    [InlineData("stale card edit", "boards.card_changed")]
    public async Task A_non_owner_member_is_refused_by_each_board_rule_exactly_as_the_owner_is(
        string rule, string code)
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = rule switch
        {
            "column limit" => await factory.ABoardWithColumnsAsync(owner, columnCount: 20, "A full board"),
            "card limit" => await factory.ABoardWithCardsAsync(owner, cardCount: 1_000, "A board of cards"),
            _ => await factory.ABoardAsync(owner, "A board with rules"),
        };
        await factory.AMemberOfAsync(board.Id, member);
        var ownerClient = await factory.AWritingClientAsync(owner);
        var client = await factory.AWritingClientAsync(member);
        var columnId = await FirstColumnIdAsync(board.Id);
        var itemId = columnId; // the column, or for the stale card edit the card, the member names

        switch (rule)
        {
            case "column not empty":
                await ACardIdAsync(owner, board.Id, columnId);
                break;
            case "last column":
                foreach (var spare in await OtherColumnIdsAsync(board.Id, columnId))
                {
                    var trimmed = await ownerClient.SendAsync(new HttpRequestMessage(
                        HttpMethod.Delete, $"{Boards}/{board.Id}/columns/{spare}")
                    {
                        Content = JsonContent.Create(new { name_version = 1 }),
                    });
                    Assert.Equal(HttpStatusCode.OK, trimmed.StatusCode);
                }

                break;
            case "stale column rename":
                Assert.Equal(HttpStatusCode.OK, (await ownerClient.PatchAsJsonAsync(
                    $"{Boards}/{board.Id}/columns/{columnId}", new { name = "Owner's name", name_version = 1 })).StatusCode);
                break;
            case "stale column move":
                Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync(
                    $"{Boards}/{board.Id}/columns", new { name = "Owner's column" })).StatusCode);
                break;
            case "stale card edit":
                var staleCard = await ACardIdAsync(owner, board.Id, columnId);
                Assert.Equal(HttpStatusCode.OK, (await ownerClient.PatchAsJsonAsync(
                    $"{Boards}/{board.Id}/cards/{staleCard}", new { title = "Owner's title", content_version = 1 })).StatusCode);
                itemId = staleCard;
                break;
        }

        var before = await BoardSnapshotAsync(ownerClient, board.Id);

        var response = rule switch
        {
            "column not empty" or "last column" => await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, $"{Boards}/{board.Id}/columns/{itemId}")
            {
                Content = JsonContent.Create(new { name_version = 1 }),
            }),
            "column limit" => await client.PostAsJsonAsync(
                $"{Boards}/{board.Id}/columns", new { name = "One too many" }),
            "card limit" => await client.PostAsJsonAsync(
                $"{Boards}/{board.Id}/cards", new { column_id = columnId, title = "One too many" }),
            "stale column rename" => await client.PatchAsJsonAsync(
                $"{Boards}/{board.Id}/columns/{columnId}", new { name = "Member's name", name_version = 1 }),
            "stale column move" => await client.PutAsJsonAsync(
                $"{Boards}/{board.Id}/columns/{columnId}/position", new { position = 1, column_layout_version = 1 }),
            _ => await client.PatchAsJsonAsync(
                $"{Boards}/{board.Id}/cards/{itemId}", new { title = "Member's title", content_version = 1 }),
        };

        Assert.Equal(code, await CodeOfAsync(response));
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await BoardSnapshotAsync(ownerClient, board.Id));
    }

    // ---- AC-25 (review B7d; sad.md §8 Logging): no board content reaches a log line ----------------

    [Fact]
    public async Task A_sweep_of_refusals_logs_no_board_name_column_name_card_title_or_description()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();

        const string BoardName = "Quokkaboard secret plans";
        const string ColumnName = "Quokkacolumn hidden";
        const string CardTitle = "Quokkatitle private";
        const string CardDescription = "Quokkadescription confidential";
        const string Typed = "Quokkatyped attempt";
        string[] content = ["Quokka"];

        var board = await factory.ABoardAsync(owner, BoardName);
        await factory.AMemberOfAsync(board.Id, member);
        var ownerClient = await factory.AWritingClientAsync(owner);
        var memberClient = await factory.AWritingClientAsync(member);
        var strangerClient = await factory.AWritingClientAsync(stranger);
        var columnId = await FirstColumnIdAsync(board.Id);

        factory.Logs.Clear();

        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PatchAsJsonAsync(
            $"{Boards}/{board.Id}/columns/{columnId}", new { name = ColumnName, name_version = 1 })).StatusCode);
        var added = await ownerClient.PostAsJsonAsync(
            $"{Boards}/{board.Id}/cards", new { column_id = columnId, title = CardTitle, description = CardDescription });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        HttpRequestMessage[] Sweep(Guid boardId) =>
        [
            JsonRequest(HttpMethod.Delete, $"{Boards}/{boardId}", new { confirm_name = Typed }),
            JsonRequest(HttpMethod.Patch, $"{Boards}/{boardId}", new { name = Typed + new string('x', 100) }),
            JsonRequest(HttpMethod.Patch, $"{Boards}/{boardId}/columns/{columnId}", new { name = Typed, name_version = 1 }),
            JsonRequest(HttpMethod.Put, $"{Boards}/{boardId}/columns/{columnId}/position", new { position = 1, column_layout_version = 0 }),
            JsonRequest(HttpMethod.Delete, $"{Boards}/{boardId}/columns/{columnId}", new { name_version = 2 }),
            JsonRequest(HttpMethod.Patch, $"{Boards}/{boardId}/cards/{cardId}", new { title = Typed, content_version = 0 }),
            JsonRequest(HttpMethod.Patch, $"{Boards}/{boardId}/cards/{cardId}", new { title = new string('q', 151), content_version = 1 }),
            JsonRequest(HttpMethod.Post, $"{Boards}/{boardId}/cards", new { column_id = columnId, title = Typed, extra = Typed }),
            new HttpRequestMessage(HttpMethod.Post, $"{Boards}/{boardId}/columns")
            {
                Content = new StringContent("{\"name\":\"Quokka \\ud800\"}", Encoding.UTF8, "application/json"),
            },
            NotJsonRequest(HttpMethod.Patch, $"{Boards}/{boardId}"),
        ];

        foreach (var client in new[] { memberClient, strangerClient, ownerClient })
        {
            foreach (var request in Sweep(board.Id))
            {
                var response = await client.SendAsync(request);
                Assert.True((int)response.StatusCode is >= 400 and < 500, $"{request.Method} {request.RequestUri} answered {(int)response.StatusCode}");
            }
        }

        var leaked = factory.Logs.Entries
            .Where(entry => content.Any(text => entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => $"{entry.Category} [{entry.Level}]: {entry.Message}")
            .ToArray();

        Assert.True(leaked.Length == 0, "board content reached the log:\n" + string.Join("\n", leaked));
        Assert.NotEmpty(factory.Logs.Entries);
    }

    // ---- AC-27: no session — the real board and one that never existed answer identically ----------

    [Theory]
    [InlineData("")]
    [InlineData("/cards/00000000-0000-0000-0000-000000000000")]
    public async Task With_no_session_every_board_naming_operation_answers_a_real_and_a_never_existed_board_identically(
        string suffix)
    {
        // AC-27: "a visitor with no active session ... opens a link to a board" — a GET. A write
        // (PATCH/DELETE) from a visitor with no session is refused by the antiforgery check before
        // the session is even looked at (no token to send), which is a different refusal entirely
        // and not what AC-27 is about.
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A visitor cannot tell either way");

        var anonymous = factory.CreateClient();

        var real = await anonymous.GetAsync($"{Boards}/{board.Id}{suffix}");
        var neverExisted = await anonymous.GetAsync($"{Boards}/{Guid.CreateVersion7()}{suffix}");

        Assert.Equal(HttpStatusCode.Unauthorized, real.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, neverExisted.StatusCode);
        Assert.Equal("accounts.session_not_recognised", await CodeOfAsync(real));
        Assert.Equal("accounts.session_not_recognised", await CodeOfAsync(neverExisted));
    }

    // ---- Helpers --------------------------------------------------------------------------------------

    private sealed record Scenario(string Name, Func<Guid, HttpRequestMessage> Build);

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body) };

    private static HttpRequestMessage NotJsonRequest(HttpMethod method, string path) =>
        new(method, path) { Content = new StringContent("{ not json", Encoding.UTF8, "application/json") };

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    private async Task<Guid[]> OtherColumnIdsAsync(Guid boardId, Guid keep)
    {
        var ids = new List<Guid>();
        for (var position = 0; position < 3; position++)
        {
            var id = await factory.ScalarAsync<Guid>(
                $"""
                SELECT [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]
                OFFSET {position} ROWS FETCH NEXT 1 ROWS ONLY
                """);
            if (id != keep)
            {
                ids.Add(id);
            }
        }

        return [.. ids];
    }

    private async Task<Guid> ACardIdAsync(TestAccount owner, Guid boardId, Guid columnId)
    {
        var client = await factory.AWritingClientAsync(owner);
        var response = await client.PostAsJsonAsync(
            $"{Boards}/{boardId}/cards", new { column_id = columnId, title = "A card to name" });
        return Guid.Parse((await BodyAsync(response)).GetProperty("id").GetString()!);
    }

    /// <summary>
    /// The owner's own view of the board, as raw JSON text — a full board reload rather than a
    /// handful of chosen fields, so a non-member request that changed anything at all is caught,
    /// not only the field a particular scenario happened to touch.
    /// </summary>
    private static async Task<string> BoardSnapshotAsync(HttpClient ownerClient, Guid boardId)
    {
        var response = await ownerClient.GetAsync($"{Boards}/{boardId}");
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// DoD: "for each of the 13 operations, the non-member response equals the never-existed
    /// response in status, headers and body (excluding instance and traceId)". Header names are
    /// compared as a set, then value for value, skipping <c>Date</c> — never set by TestServer for
    /// these responses, but excluded defensively rather than assumed away.
    /// </summary>
    private static async Task AssertIndistinguishableAsync(
        HttpResponseMessage first, HttpResponseMessage second, string scenario, List<string> violations)
    {
        if (first.StatusCode != second.StatusCode)
        {
            violations.Add(
                $"{scenario}: status {(int)first.StatusCode} ({first.StatusCode}) vs "
                + $"{(int)second.StatusCode} ({second.StatusCode}).");
            return;
        }

        var firstHeaders = HeaderMap(first);
        var secondHeaders = HeaderMap(second);

        if (!firstHeaders.Keys.ToHashSet().SetEquals(secondHeaders.Keys))
        {
            violations.Add(
                $"{scenario}: header names differ — "
                + $"[{string.Join(",", firstHeaders.Keys)}] vs [{string.Join(",", secondHeaders.Keys)}].");
        }
        else
        {
            foreach (var (name, value) in firstHeaders)
            {
                if (secondHeaders.TryGetValue(name, out var otherValue) && value != otherValue)
                {
                    violations.Add(
                        $"{scenario}: header '{name}' differs — '{value}' vs '{otherValue}'.");
                }
            }
        }

        var firstBody = await BodyAsync(first);
        var secondBody = await BodyAsync(second);

        var firstMembers = firstBody.EnumerateObject()
            .Where(m => m.Name is not ("instance" or "traceId"))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => $"{m.Name}={m.Value}")
            .ToArray();
        var secondMembers = secondBody.EnumerateObject()
            .Where(m => m.Name is not ("instance" or "traceId"))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => $"{m.Name}={m.Value}")
            .ToArray();

        if (!firstMembers.SequenceEqual(secondMembers))
        {
            violations.Add(
                $"{scenario}: body differs — [{string.Join(",", firstMembers)}] vs "
                + $"[{string.Join(",", secondMembers)}].");
        }
    }

    private static Dictionary<string, string> HeaderMap(HttpResponseMessage response)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            // "Date" is never set by TestServer for these responses, but excluded defensively
            // rather than assumed away. "Set-Cookie" carries the antiforgery XSRF-TOKEN, which the
            // framework regenerates at random on every response regardless of what board (if any)
            // is behind the request — comparing it would fail two calls to the very same board.
            if (header.Key is "Date" or "Set-Cookie")
            {
                continue;
            }

            map[header.Key] = string.Join(",", header.Value);
        }

        return map;
    }
}
