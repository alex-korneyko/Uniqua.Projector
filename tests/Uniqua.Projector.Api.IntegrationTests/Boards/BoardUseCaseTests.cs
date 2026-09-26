using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T6 — the board use cases resolved from <see cref="ApiFactory"/> services, the way
/// <c>Accounts/RegisterAccountTests.cs</c> does, one row of the task's Definition of Done per test.
/// Every use case except <see cref="CreateBoard"/> and <see cref="ListMyBoards"/> is expected to
/// begin with the member-scoped load (ADR 0014): the non-member and absent-board cases below are
/// what proves that from outside.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BoardUseCaseTests(ApiFactory factory)
{
    // ---- AC-01: create makes the creator sole owner, three columns, and opens the board ---------

    [Fact]
    public async Task Creating_a_board_makes_the_creator_its_sole_owner_with_three_columns_and_opens_it()
    {
        var owner = await factory.AnAccountAsync();

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "A brand new board", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var board = result.Value;

        Assert.Equal("A brand new board", board.Name);
        Assert.True(board.IsOwner);
        Assert.Equal(
            ["To do", "In progress", "Done"],
            board.Columns.OrderBy(c => c.Position).Select(c => c.Name));
        Assert.Empty(board.Cards);

        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Id] = '{board.Id}'"));
        Assert.Equal(3, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{board.Id}'"));
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM [dbo].[BoardMemberships]
            WHERE [BoardId] = '{board.Id}' AND [AccountId] = '{owner.Id}' AND [Role] = N'Owner'
            """));
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT [OwnedBoardCount] FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner.Id}'"));
    }

    // ---- AC-03: the 50-board ceiling, including the forced-collision edge case -------------------

    [Fact]
    public async Task An_account_that_already_owns_fifty_boards_is_refused_a_fifty_first()
    {
        var owner = await factory.AnAccountAsync();
        await SetOwnedBoardCountAsync(owner.Id, OwnedBoardCounter.MaxOwnedBoards);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "One too many", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.OwnedBoardLimitReached, result.Error);
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Name] = N'One too many'"));
    }

    [Fact]
    public async Task Two_creations_at_forty_nine_owned_boards_forced_to_collide_let_exactly_one_succeed()
    {
        // Edge case table: "Two creations at 49 owned boards, forced to collide" -> exactly one
        // succeeds, the other OwnedBoardLimitReached after the retry re-decides. The collision is
        // forced by ContentionForcer.RaceAsync rather than hoped for from timing.
        var owner = await factory.AnAccountAsync();
        await SetOwnedBoardCountAsync(owner.Id, OwnedBoardCounter.MaxOwnedBoards - 1);

        async Task<Domain.Result<BoardView, BoardError>> CreateAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<CreateBoard>()
                .ExecuteAsync(owner.Id, name, CancellationToken.None);
        }

        // Forced to collide: the first save is held until the other racer has committed, so the
        // held one must lose its first attempt and re-decide on reload (review Q2a).
        var race = await factory.Contention.RaceAsync(() => CreateAsync("Racer A"), () => CreateAsync("Racer B"));
        Assert.True(race.Collided, "the two creations never collided");
        var results = new[] { race.First, race.Second };

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        var refusal = results.Single(r => !r.IsSuccess);
        Assert.Same(BoardErrors.OwnedBoardLimitReached, refusal.Error);
        Assert.Equal(OwnedBoardCounter.MaxOwnedBoards, await factory.ScalarAsync<int>(
            $"SELECT [OwnedBoardCount] FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner.Id}'"));
    }

    // ---- AC-04: list only the caller's boards, newest first, with ownership marked ----------------

    [Fact]
    public async Task Listing_returns_only_the_callers_boards_newest_first_marking_ownership()
    {
        var member = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();

        Guid strangersBoardId;
        using (var scope = factory.Services.CreateScope())
        {
            var create = scope.ServiceProvider.GetRequiredService<CreateBoard>();

            var owned = await create.ExecuteAsync(member.Id, "Owned first", CancellationToken.None);
            Assert.True(owned.IsSuccess);

            // Both boards must land in different instants: CreatedAt is the sort key AC-04 names
            // ("newest first"), and without a gap two boards created at the same frozen instant
            // have no defined order between them.
            factory.Clock.Advance(TimeSpan.FromSeconds(1));

            var strangers = await create.ExecuteAsync(stranger.Id, "Not mine", CancellationToken.None);
            Assert.True(strangers.IsSuccess);
            strangersBoardId = strangers.Value.Id;

            await factory.ExecuteAsync(InsertMembershipSql(strangersBoardId, member.Id, "Member"));
        }

        using var listScope = factory.Services.CreateScope();
        var entries = await listScope.ServiceProvider.GetRequiredService<ListMyBoards>()
            .ExecuteAsync(member.Id, CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Not mine", entries[0].Name);
        Assert.False(entries[0].IsOwner);
        Assert.Equal("Owned first", entries[1].Name);
        Assert.True(entries[1].IsOwner);
    }

    [Fact]
    public async Task Listing_for_an_account_with_no_memberships_returns_empty_and_writes_nothing()
    {
        var account = await factory.AnAccountAsync();

        using var scope = factory.Services.CreateScope();
        var entries = await scope.ServiceProvider.GetRequiredService<ListMyBoards>()
            .ExecuteAsync(account.Id, CancellationToken.None);

        Assert.Empty(entries);
    }

    // ---- AC-25: opening an absent board and a non-member board answer identically -----------------

    [Fact]
    public async Task Opening_an_absent_board_and_a_non_member_board_answer_identically()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Someone else's board", CancellationToken.None);
        Assert.True(created.IsSuccess);

        using var scope = factory.Services.CreateScope();
        var openBoard = scope.ServiceProvider.GetRequiredService<OpenBoard>();

        var forNonMember = await openBoard.ExecuteAsync(created.Value.Id, stranger.Id, CancellationToken.None);
        var forAbsentBoard = await openBoard.ExecuteAsync(Guid.CreateVersion7(), stranger.Id, CancellationToken.None);

        Assert.False(forNonMember.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, forNonMember.Error);
        Assert.False(forAbsentBoard.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, forAbsentBoard.Error);
    }

    // ---- Edge case: opening a deleted board answers exactly as an absent one would -----------------

    [Fact]
    public async Task Opening_a_deleted_board_answers_not_available_exactly_as_never_existed()
    {
        var owner = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "About to vanish", CancellationToken.None);
        Assert.True(created.IsSuccess);

        using (var deleteScope = factory.Services.CreateScope())
        {
            var deleted = await deleteScope.ServiceProvider.GetRequiredService<DeleteBoard>()
                .ExecuteAsync(created.Value.Id, owner.Id, "About to vanish", CancellationToken.None);
            Assert.True(deleted.IsSuccess);
        }

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<OpenBoard>()
            .ExecuteAsync(created.Value.Id, owner.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
    }

    // ---- AC-19: the owner renames -------------------------------------------------------------

    [Fact]
    public async Task Owner_renaming_records_the_new_name_and_every_member_sees_it_in_their_list()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Old name", CancellationToken.None);
        Assert.True(created.IsSuccess);
        await factory.AMemberOfAsync(created.Value.Id, member);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameBoard>()
            .ExecuteAsync(created.Value.Id, owner.Id, "New name", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));

        // AC-19 "every member sees it in their list of boards": the non-owner member's own list.
        using var listScope = factory.Services.CreateScope();
        var memberList = await listScope.ServiceProvider.GetRequiredService<ListMyBoards>()
            .ExecuteAsync(member.Id, CancellationToken.None);
        var entry = Assert.Single(memberList, board => board.Id == created.Value.Id);
        Assert.Equal("New name", entry.Name);
        Assert.False(entry.IsOwner);
    }

    // ---- T25 (review Q4g; AC-19): the rename answers with the name the domain stored ----------------

    [Fact]
    public async Task Renaming_returns_the_name_the_board_now_has_exactly_as_stored()
    {
        var owner = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Old name", CancellationToken.None);
        Assert.True(created.IsSuccess);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameBoard>()
            .ExecuteAsync(created.Value.Id, owner.Id, " \t Padded  new name  ", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'");
        Assert.Equal("Padded  new name", stored);
        Assert.Equal(stored, result.Value);
    }

    // ---- AC-22: a Member is refused OwnerOnly before the name is even looked at --------------------

    [Fact]
    public async Task A_member_who_is_not_owner_is_refused_owner_only_even_with_an_invalid_name()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Owner's board", CancellationToken.None);
        Assert.True(created.IsSuccess);
        await factory.ExecuteAsync(InsertMembershipSql(created.Value.Id, member.Id, "Member"));

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameBoard>()
            .ExecuteAsync(created.Value.Id, member.Id, string.Empty, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.OwnerOnly, result.Error);
        Assert.Equal("Owner's board", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));
    }

    // ---- AC-25: a non-member renaming gets the same refusal an absent board would -----------------

    [Fact]
    public async Task A_non_member_renaming_gets_the_same_refusal_as_an_absent_board_and_changes_nothing()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Not the stranger's board", CancellationToken.None);
        Assert.True(created.IsSuccess);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RenameBoard>()
            .ExecuteAsync(created.Value.Id, stranger.Id, "Hijacked name", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
        Assert.Equal("Not the stranger's board", await factory.ScalarAsync<string>(
            $"SELECT [Name] FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));
    }

    // ---- AC-20 / edge case: deleting a board holding cards removes everything and frees the slot ---

    [Fact]
    public async Task Owner_deleting_with_a_matching_confirmation_removes_the_board_its_cards_and_frees_the_slot()
    {
        var owner = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Doomed board", CancellationToken.None);
        Assert.True(created.IsSuccess);

        var columnId = created.Value.Columns[0].Id;
        for (var i = 0; i < 3; i++)
        {
            await factory.ExecuteAsync(InsertCardSql(Guid.CreateVersion7(), created.Value.Id, columnId, i));
        }

        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT [OwnedBoardCount] FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner.Id}'"));

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteBoard>()
            .ExecuteAsync(created.Value.Id, owner.Id, "Doomed board", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{created.Value.Id}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [BoardId] = '{created.Value.Id}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[BoardMemberships] WHERE [BoardId] = '{created.Value.Id}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT [OwnedBoardCount] FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner.Id}'"));
    }

    // ---- AC-20b: a mismatched confirmation refuses, deletes nothing, and names the current name ----

    [Fact]
    public async Task A_confirmation_that_does_not_match_the_current_name_refuses_and_deletes_nothing()
    {
        var owner = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Original name", CancellationToken.None);
        Assert.True(created.IsSuccess);

        using (var renameScope = factory.Services.CreateScope())
        {
            var renamed = await renameScope.ServiceProvider.GetRequiredService<RenameBoard>()
                .ExecuteAsync(created.Value.Id, owner.Id, "Renamed since", CancellationToken.None);
            Assert.True(renamed.IsSuccess);
        }

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteBoard>()
            .ExecuteAsync(created.Value.Id, owner.Id, "Original name", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.ConfirmationMismatch("Renamed since").Code, result.Error!.Code);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));
    }

    // ---- AC-22: a Member cannot delete the board ---------------------------------------------------

    [Fact]
    public async Task A_member_who_is_not_owner_cannot_delete_the_board()
    {
        var owner = await factory.AnAccountAsync();
        var member = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Protected board", CancellationToken.None);
        Assert.True(created.IsSuccess);
        await factory.ExecuteAsync(InsertMembershipSql(created.Value.Id, member.Id, "Member"));

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteBoard>()
            .ExecuteAsync(created.Value.Id, member.Id, "Protected board", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.OwnerOnly, result.Error);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));
    }

    // ---- AC-25: a non-member deleting gets the same refusal an absent board would ------------------

    [Fact]
    public async Task A_non_member_deleting_gets_the_same_refusal_as_an_absent_board_and_deletes_nothing()
    {
        var owner = await factory.AnAccountAsync();
        var stranger = await factory.AnAccountAsync();

        using var createScope = factory.Services.CreateScope();
        var created = await createScope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(owner.Id, "Guarded board", CancellationToken.None);
        Assert.True(created.IsSuccess);

        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DeleteBoard>()
            .ExecuteAsync(created.Value.Id, stranger.Id, "Guarded board", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Id] = '{created.Value.Id}'"));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private Task SetOwnedBoardCountAsync(Guid accountId, int count) =>
        factory.ExecuteAsync(
            $"""
            MERGE [dbo].[OwnedBoardCounters] AS target
            USING (SELECT '{accountId}' AS AccountId) AS source
            ON target.[AccountId] = source.[AccountId]
            WHEN MATCHED THEN UPDATE SET [OwnedBoardCount] = {count}
            WHEN NOT MATCHED THEN INSERT ([AccountId], [OwnedBoardCount]) VALUES (source.[AccountId], {count});
            """);

    private static string InsertMembershipSql(Guid boardId, Guid accountId, string role) =>
        $"""
        INSERT INTO [dbo].[BoardMemberships] ([Id], [BoardId], [AccountId], [Role])
        VALUES ('{Guid.CreateVersion7()}', '{boardId}', '{accountId}', N'{role}');
        """;

    private static string InsertCardSql(Guid id, Guid boardId, Guid columnId, int position) =>
        $"""
        INSERT INTO [dbo].[Cards]
            ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
        VALUES ('{id}', '{boardId}', '{columnId}', {position}, N'A card', N'', 1);
        """;
}
