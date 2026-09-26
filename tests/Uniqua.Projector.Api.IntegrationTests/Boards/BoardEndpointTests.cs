using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T9 — <c>/api/v1/boards</c> on the wire: <c>listMyBoards</c>, <c>createBoard</c>,
/// <c>openBoard</c>, <c>renameBoard</c> and <c>deleteBoard</c>, and the one refusal (AC-25) that has
/// to look identical whether the board never existed or the caller simply is not a member of it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BoardEndpointTests(ApiFactory factory)
{
    private const string Boards = "/api/v1/boards";

    // ---- AC-01: create — happy path -----------------------------------------------------------

    [Fact]
    public async Task Creating_a_board_with_a_valid_name_makes_the_caller_its_sole_owner_with_three_columns()
    {
        var owner = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(Boards, new { name = "A brand new board" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Equal("A brand new board", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("is_owner").GetBoolean());
        Assert.True(Guid.TryParse(body.GetProperty("id").GetString(), out _));
        Assert.Empty(body.GetProperty("cards").EnumerateArray());

        var columnNames = body.GetProperty("columns").EnumerateArray()
            .OrderBy(column => column.GetProperty("position").GetInt32())
            .Select(column => column.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.Equal(["To do", "In progress", "Done"], columnNames);
    }

    // ---- AC-02: create — the name is refused --------------------------------------------------

    [Fact]
    public async Task Creating_a_board_with_a_name_that_is_empty_after_trimming_is_refused()
    {
        var owner = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(Boards, new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.board_name_invalid", await CodeOfAsync(response));
    }

    [Fact]
    public async Task Creating_a_board_with_a_name_over_a_hundred_characters_is_refused_and_writes_nothing()
    {
        var owner = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(owner);

        // 101 code points once trimmed — one past the ceiling; the surrounding spaces do not count.
        var response = await client.PostAsJsonAsync(Boards, new { name = $"  {new string('n', 101)}  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.board_name_invalid", await CodeOfAsync(response));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[BoardMemberships] WHERE [AccountId] = '{owner.Id}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT ISNULL(MAX([OwnedBoardCount]), 0) FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner.Id}'"));
    }

    [Theory]
    [InlineData("  \t ")]
    [InlineData("101")]
    public async Task The_owner_renaming_to_an_unusable_name_is_refused_and_the_name_is_unchanged(string name)
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A name worth keeping");
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PatchAsJsonAsync(
            $"{Boards}/{board.Id}", new { name = name == "101" ? new string('r', 101) : name });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.board_name_invalid", await CodeOfAsync(response));
        Assert.Equal("A name worth keeping", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Boards] WHERE [Id] = '{board.Id}'"));
    }

    // ---- AC-03: the 50-owned-board ceiling -----------------------------------------------------

    [Fact]
    public async Task An_account_that_already_owns_fifty_boards_is_refused_a_fifty_first()
    {
        var owner = await factory.AnAccountOwningBoardsAsync(50);
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PostAsJsonAsync(Boards, new { name = "One too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.owned_board_limit_reached", await CodeOfAsync(response));
    }

    // ---- AC-04: list only the caller's boards, marking ownership -------------------------------

    [Fact]
    public async Task Listing_lists_every_board_the_caller_is_a_member_of_and_no_other_marking_ownership()
    {
        var member = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();

        var owned = await factory.ABoardAsync(member, "Owned by the caller");
        factory.Clock.Advance(TimeSpan.FromSeconds(1));

        var joined = await factory.ABoardAsync(stranger, "Joined, not owned");
        await factory.AMemberOfAsync(joined.Id, member);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));

        var notMine = await factory.ABoardAsync(stranger, "Never joined");

        var client = await factory.AWritingClientAsync(member);
        var response = await client.GetAsync(Boards);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);

        var items = body.GetProperty("items").EnumerateArray().ToArray();
        var ids = items.Select(item => item.GetProperty("id").GetString()).ToArray();

        Assert.Contains(owned.Id.ToString(), ids);
        Assert.Contains(joined.Id.ToString(), ids);
        Assert.DoesNotContain(notMine.Id.ToString(), ids);

        Assert.True(items.Single(item => item.GetProperty("id").GetString() == owned.Id.ToString())
            .GetProperty("is_owner").GetBoolean());
        Assert.False(items.Single(item => item.GetProperty("id").GetString() == joined.Id.ToString())
            .GetProperty("is_owner").GetBoolean());
    }

    // ---- AC-19: rename — happy path -------------------------------------------------------------

    [Fact]
    public async Task Renaming_by_the_owner_records_the_new_name()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Before the rename");
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.PatchAsJsonAsync(
            $"{Boards}/{board.Id}", new { name = "After the rename" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(board.Id.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("After the rename", body.GetProperty("name").GetString());
    }

    // ---- AC-20 / AC-20b: delete — happy path and the mismatch --------------------------------------

    [Fact]
    public async Task Deleting_with_the_matching_name_removes_the_board_and_it_answers_not_available_after()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "To be deleted");
        var client = await factory.AWritingClientAsync(owner);

        var deletion = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}")
        {
            Content = JsonContent.Create(new { confirm_name = board.Name }),
        });

        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        var reopened = await client.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.NotFound, reopened.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(reopened));
    }

    [Fact]
    public async Task After_a_board_is_deleted_its_former_columns_and_cards_answer_as_never_existed()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Going with everything on it");
        await factory.AMemberOfAsync(board.Id, member);
        var client = await factory.AWritingClientAsync(owner);

        var columnId = await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}' ORDER BY [Position]");
        var added = await client.PostAsJsonAsync(
            $"{Boards}/{board.Id}/cards", new { column_id = columnId, title = "Goes with the board" });
        var cardId = Guid.Parse((await BodyAsync(added)).GetProperty("id").GetString()!);

        var deletion = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Boards}/{board.Id}")
        {
            Content = JsonContent.Create(new { confirm_name = board.Name }),
        });
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);

        // Probed as the owner and as the former member, against the ids they last saw.
        var neverExisted = Guid.CreateVersion7();
        foreach (var prober in new[] { client, await factory.AWritingClientAsync(member) })
        {
            var probes = new (HttpRequestMessage Former, HttpRequestMessage Never)[]
            {
                (new(HttpMethod.Get, $"{Boards}/{board.Id}/cards/{cardId}"),
                 new(HttpMethod.Get, $"{Boards}/{neverExisted}/cards/{cardId}")),
                (new(HttpMethod.Patch, $"{Boards}/{board.Id}/cards/{cardId}")
                    { Content = JsonContent.Create(new { title = "Too late", content_version = 1 }) },
                 new(HttpMethod.Patch, $"{Boards}/{neverExisted}/cards/{cardId}")
                    { Content = JsonContent.Create(new { title = "Too late", content_version = 1 }) }),
                (new(HttpMethod.Patch, $"{Boards}/{board.Id}/columns/{columnId}")
                    { Content = JsonContent.Create(new { name = "Too late", name_version = 1 }) },
                 new(HttpMethod.Patch, $"{Boards}/{neverExisted}/columns/{columnId}")
                    { Content = JsonContent.Create(new { name = "Too late", name_version = 1 }) }),
                (new(HttpMethod.Post, $"{Boards}/{board.Id}/cards")
                    { Content = JsonContent.Create(new { column_id = columnId, title = "Too late" }) },
                 new(HttpMethod.Post, $"{Boards}/{neverExisted}/cards")
                    { Content = JsonContent.Create(new { column_id = columnId, title = "Too late" }) }),
            };

            foreach (var (former, never) in probes)
            {
                var formerResponse = await prober.SendAsync(former);
                var neverResponse = await prober.SendAsync(never);

                Assert.Equal(HttpStatusCode.NotFound, formerResponse.StatusCode);
                Assert.Equal("boards.not_available", await CodeOfAsync(formerResponse));
                await AssertIndistinguishableAsync(formerResponse, neverResponse);
            }
        }

        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [Id] = '{columnId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [Id] = '{cardId}'"));
    }

    [Fact]
    public async Task Deleting_with_a_name_that_does_not_match_is_refused_and_carries_the_current_name()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Renamed since opened");
        var client = await factory.AWritingClientAsync(owner);

        // The dialog opened before this rename, and still confirms with the old name.
        await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = "The actual current name" });

        var deletion = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}")
        {
            Content = JsonContent.Create(new { confirm_name = board.Name }),
        });

        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        var body = await BodyAsync(deletion);
        Assert.Equal("boards.confirmation_mismatch", body.GetProperty("code").GetString());
        Assert.Equal("The actual current name", body.GetProperty("current_name").GetString());

        // Nothing was deleted.
        var stillThere = await client.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    // ---- AC-22: a non-owner member cannot rename or delete --------------------------------------

    [Fact]
    public async Task A_member_who_is_not_the_owner_cannot_rename_the_board()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Owned by someone else");
        await factory.AMemberOfAsync(board.Id, member);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = "Hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("boards.owner_only", await CodeOfAsync(response));

        var stillNamed = await client.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal("Owned by someone else", (await BodyAsync(stillNamed)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_member_who_is_not_the_owner_cannot_delete_the_board()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Owned by someone else");
        await factory.AMemberOfAsync(board.Id, member);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}")
        {
            Content = JsonContent.Create(new { confirm_name = board.Name }),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("boards.owner_only", await CodeOfAsync(response));

        var stillThere = await client.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    // ---- Order of checks: request shape before the owner check (edge case table) -----------------

    [Fact]
    public async Task A_member_sending_a_malshaped_name_is_refused_before_the_owner_check_even_runs()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Owned by someone else");
        await factory.AMemberOfAsync(board.Id, member);

        var client = await factory.AWritingClientAsync(member);
        var response = await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(response));
    }

    // ---- AC-25 / edge cases: non-member and never-existed are answered byte-identically ------------

    [Fact]
    public async Task A_non_member_opening_a_board_gets_exactly_the_never_existed_refusal()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Not the stranger's board");

        var client = await factory.AWritingClientAsync(stranger);

        var nonMember = await client.GetAsync($"{Boards}/{board.Id}");
        var neverExisted = await client.GetAsync($"{Boards}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, nonMember.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, neverExisted.StatusCode);
        await AssertIndistinguishableAsync(nonMember, neverExisted);
    }

    [Fact]
    public async Task A_non_member_renaming_a_board_gets_not_available_even_with_an_invalid_body()
    {
        // Edge case table: "Non-member sends {"name": ""} to rename" -> 404 not_available, not 400,
        // not 403. The membership check runs before the request shape is even looked at.
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Not the stranger's board");

        var client = await factory.AWritingClientAsync(stranger);
        var response = await client.PatchAsJsonAsync($"{Boards}/{board.Id}", new { name = "" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));
    }

    [Fact]
    public async Task A_non_member_deleting_a_board_changes_nothing_and_gets_not_available()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Not the stranger's board");

        var client = await factory.AWritingClientAsync(stranger);
        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}")
        {
            Content = JsonContent.Create(new { confirm_name = board.Name }),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));

        var stillThere = await factory.AWritingClientAsync(owner);
        Assert.Equal(HttpStatusCode.OK, (await stillThere.GetAsync($"{Boards}/{board.Id}")).StatusCode);
    }

    // ---- AC-27: no session reveals nothing, real board or not -------------------------------------

    [Fact]
    public async Task With_no_session_a_real_board_and_one_that_never_existed_are_answered_identically()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Whatever it is, a visitor cannot tell");

        var anonymous = factory.CreateClient();

        var realBoard = await anonymous.GetAsync($"{Boards}/{board.Id}");
        var neverExisted = await anonymous.GetAsync($"{Boards}/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Unauthorized, realBoard.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, neverExisted.StatusCode);
        Assert.Equal("accounts.session_not_recognised", await CodeOfAsync(realBoard));
        Assert.Equal("accounts.session_not_recognised", await CodeOfAsync(neverExisted));
    }

    // ---- Edge cases ------------------------------------------------------------------------------

    [Fact]
    public async Task A_path_that_is_not_a_guid_is_answered_exactly_like_an_unknown_board()
    {
        var owner = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.GetAsync($"{Boards}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(response));
    }

    [Fact]
    public async Task A_body_that_is_not_json_at_all_is_refused_identically_for_every_board()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Whoever asks, the body wins first");

        var ownerClient = await factory.AWritingClientAsync(owner);
        var strangerClient = await factory.AWritingClientAsync(stranger);

        var toOwnBoard = await PatchNotJsonAsync(ownerClient, board.Id);
        var toSomeoneElsesBoard = await PatchNotJsonAsync(strangerClient, board.Id);
        var toAnAbsentBoard = await PatchNotJsonAsync(strangerClient, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.BadRequest, toOwnBoard.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toSomeoneElsesBoard.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toAnAbsentBoard.StatusCode);

        Assert.Equal("boards.request_malformed", await CodeOfAsync(toOwnBoard));
        Assert.Equal("boards.request_malformed", await CodeOfAsync(toSomeoneElsesBoard));
        Assert.Equal("boards.request_malformed", await CodeOfAsync(toAnAbsentBoard));
    }

    [Fact]
    public async Task Deleting_ignores_a_confirmation_name_in_the_query_string()
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "Query strings are not read");
        var client = await factory.AWritingClientAsync(owner);

        var response = await client.SendAsync(new HttpRequestMessage(
            HttpMethod.Delete, $"{Boards}/{board.Id}?confirm_name={Uri.EscapeDataString(board.Name)}")
        {
            // The body names the wrong board, so the request must fail — proving the query string's
            // correct name was never read.
            Content = JsonContent.Create(new { confirm_name = "not the board's name" }),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("boards.confirmation_mismatch", await CodeOfAsync(response));

        var stillThere = await client.GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    // ---- T23 (review Q1): a lone-surrogate string is an unusable string, not a server error ----------

    [Fact]
    public async Task Creating_a_board_whose_name_is_a_lone_surrogate_is_refused_as_request_invalid()
    {
        var owner = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(owner);

        var response = await SendJsonTextAsync(client, HttpMethod.Post, Boards, """{"name":"\ud800"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(response));
    }

    [Theory]
    [InlineData("PATCH", """{"name":"\ud800"}""")]
    [InlineData("DELETE", """{"confirm_name":"\udc00"}""")]
    public async Task A_lone_surrogate_in_a_board_change_is_request_invalid_for_a_member_and_not_available_for_anyone_else(
        string method, string json)
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board that gets a broken string");

        var ownerResponse = await SendJsonTextAsync(
            await factory.AWritingClientAsync(owner), new HttpMethod(method), $"{Boards}/{board.Id}", json);
        var strangerResponse = await SendJsonTextAsync(
            await factory.AWritingClientAsync(stranger), new HttpMethod(method), $"{Boards}/{board.Id}", json);

        Assert.Equal(HttpStatusCode.BadRequest, ownerResponse.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(ownerResponse));
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(strangerResponse));

        var stillNamed = await (await factory.AWritingClientAsync(owner)).GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(board.Name, (await BodyAsync(stillNamed)).GetProperty("name").GetString());
    }

    // ---- T23 (review B6a): a member outside the request schema is request_invalid --------------------

    [Fact]
    public async Task Creating_a_board_with_an_unknown_member_is_refused_and_creates_nothing()
    {
        var owner = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(owner);

        var response = await SendJsonTextAsync(
            client, HttpMethod.Post, Boards, """{"name":"A board with extras","extra":1}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(response));

        var owned = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[BoardMemberships] WHERE [AccountId] = '{owner.Id}'");
        Assert.Equal(0, owned);
    }

    [Theory]
    [InlineData("PATCH", """{"name":"Renamed with extras","extra":1}""")]
    [InlineData("DELETE", """{"confirm_name":"A board with a strict schema","extra":true}""")]
    public async Task An_unknown_member_in_a_board_change_is_request_invalid_after_membership_and_changes_nothing(
        string method, string json)
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board with a strict schema");

        var ownerResponse = await SendJsonTextAsync(
            await factory.AWritingClientAsync(owner), new HttpMethod(method), $"{Boards}/{board.Id}", json);
        var strangerResponse = await SendJsonTextAsync(
            await factory.AWritingClientAsync(stranger), new HttpMethod(method), $"{Boards}/{board.Id}", json);

        Assert.Equal(HttpStatusCode.BadRequest, ownerResponse.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(ownerResponse));
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(strangerResponse));

        var stillThere = await (await factory.AWritingClientAsync(owner)).GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        Assert.Equal(board.Name, (await BodyAsync(stillThere)).GetProperty("name").GetString());
    }

    // ---- T23 (review Q4d): deleteBoard with no body is judged after membership, like the others ------

    [Fact]
    public async Task Deleting_a_board_with_no_body_is_request_invalid_after_membership_but_not_available_before_it()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();
        var board = await factory.ABoardAsync(owner, "A board for a bodiless delete");

        var ownerResponse = await SendJsonTextAsync(
            await factory.AWritingClientAsync(owner), HttpMethod.Delete, $"{Boards}/{board.Id}", json: null);
        var strangerResponse = await SendJsonTextAsync(
            await factory.AWritingClientAsync(stranger), HttpMethod.Delete, $"{Boards}/{board.Id}", json: null);

        Assert.Equal(HttpStatusCode.BadRequest, ownerResponse.StatusCode);
        Assert.Equal("boards.request_invalid", await CodeOfAsync(ownerResponse));
        Assert.Equal(HttpStatusCode.NotFound, strangerResponse.StatusCode);
        Assert.Equal("boards.not_available", await CodeOfAsync(strangerResponse));

        var stillThere = await (await factory.AWritingClientAsync(owner)).GetAsync($"{Boards}/{board.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    // ---- T25 (review Q4f): a refusal reads only what it answers with, never the card list -----------

    [Theory]
    [InlineData("renameBoard, wrong shape", "boards.request_invalid")]
    [InlineData("addCard, wrong shape", "boards.request_invalid")]
    [InlineData("deleteBoard, confirmation mismatch", "boards.confirmation_mismatch")]
    [InlineData("renameColumn, stale name_version", "boards.column_renamed")]
    [InlineData("moveColumn, stale layout version", "boards.columns_changed")]
    public async Task A_refusal_reads_no_card_summaries_on_its_way_out(string refusal, string expectedCode)
    {
        var owner = await factory.AnAccountAsync();
        var board = await factory.ABoardWithCardsAsync(owner, cardCount: 5, name: "A board holding cards");
        var client = await factory.AWritingClientAsync(owner);
        var columnId = await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}' ORDER BY [Position]");

        // Make the column's name and the board's layout move on, so the stale requests below are stale.
        var renamed = await client.PatchAsJsonAsync(
            $"{Boards}/{board.Id}/columns/{columnId}", new { name = "Renamed first", name_version = 1 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var added = await client.PostAsJsonAsync($"{Boards}/{board.Id}/columns", new { name = "Added first" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var request = refusal switch
        {
            "renameBoard, wrong shape" => new HttpRequestMessage(HttpMethod.Patch, $"{Boards}/{board.Id}")
            {
                Content = JsonContent.Create(new { name = 5 }),
            },
            "addCard, wrong shape" => new HttpRequestMessage(HttpMethod.Post, $"{Boards}/{board.Id}/cards")
            {
                Content = JsonContent.Create(new { title = "No column named" }),
            },
            "deleteBoard, confirmation mismatch" => new HttpRequestMessage(HttpMethod.Delete, $"{Boards}/{board.Id}")
            {
                Content = JsonContent.Create(new { confirm_name = "Not its name" }),
            },
            "renameColumn, stale name_version" => new HttpRequestMessage(
                HttpMethod.Patch, $"{Boards}/{board.Id}/columns/{columnId}")
            {
                Content = JsonContent.Create(new { name = "Mine", name_version = 1 }),
            },
            _ => new HttpRequestMessage(HttpMethod.Put, $"{Boards}/{board.Id}/columns/{columnId}/position")
            {
                Content = JsonContent.Create(new { position = 1, column_layout_version = 1 }),
            },
        };

        factory.Commands.Clear();
        var response = await client.SendAsync(request);
        var statements = factory.Commands.Statements;

        Assert.Equal(expectedCode, await CodeOfAsync(response));
        Assert.DoesNotContain(statements, sql => sql.Contains("FROM [Cards]", StringComparison.Ordinal));

        // What the refusal does carry is still the board as it now stands.
        var body = await BodyAsync(response);
        switch (expectedCode)
        {
            case "boards.confirmation_mismatch":
                Assert.Equal(board.Name, body.GetProperty("current_name").GetString());
                break;
            case "boards.column_renamed":
                Assert.Equal("Renamed first", body.GetProperty("current_column").GetProperty("name").GetString());
                Assert.Equal(2, body.GetProperty("current_column").GetProperty("name_version").GetInt32());
                break;
            case "boards.columns_changed":
                var layout = body.GetProperty("current_layout");
                Assert.Equal(2, layout.GetProperty("column_layout_version").GetInt32());
                Assert.Equal(4, layout.GetProperty("columns").GetArrayLength());
                break;
        }
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// Sends <paramref name="json"/> exactly as written — a lone-surrogate escape or an extra member
    /// cannot be produced by serialising an anonymous object — or no body at all when it is null.
    /// </summary>
    private static Task<HttpResponseMessage> SendJsonTextAsync(
        HttpClient client, HttpMethod method, string url, string? json) =>
        client.SendAsync(new HttpRequestMessage(method, url)
        {
            Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private static Task<HttpResponseMessage> PatchNotJsonAsync(HttpClient client, Guid boardId) =>
        client.PatchAsync(
            $"{Boards}/{boardId}",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await BodyAsync(response);
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// DoD: "A test compares the non-member and never-existed not_available responses status,
    /// headers and body, excluding instance and traceId." Those two members echo the request itself
    /// (contracts/openapi.yaml, components.responses.BoardNotAvailable) and so are the only ones
    /// allowed to differ.
    /// </summary>
    private static async Task AssertIndistinguishableAsync(
        HttpResponseMessage first, HttpResponseMessage second)
    {
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(
            first.Content.Headers.ContentType?.ToString(), second.Content.Headers.ContentType?.ToString());

        var firstBody = await BodyAsync(first);
        var secondBody = await BodyAsync(second);

        foreach (var name in new[] { "type", "title", "status", "detail", "code" })
        {
            Assert.Equal(
                firstBody.TryGetProperty(name, out var a) ? a.ToString() : null,
                secondBody.TryGetProperty(name, out var b) ? b.ToString() : null);
        }
    }
}
