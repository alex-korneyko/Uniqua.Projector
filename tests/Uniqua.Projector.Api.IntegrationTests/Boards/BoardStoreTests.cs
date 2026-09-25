using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T5 — <see cref="IBoardStore"/> and <see cref="IOwnedBoardCounterStore"/> over the real store
/// (ADR 0014, ADR 0015), one row of the task's Definition of Done per test. The one edge case not
/// covered here — "a board id that is not a GUID" — never reaches the store as a <see cref="Guid"/>
/// at all (routing rejects it before this layer is asked anything), so it has no store-level test.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BoardStoreTests(ApiFactory factory)
{
    // ---- AC-04: the happy path ---------------------------------------------------------------

    [Fact]
    public async Task ListForAccountAsync_lists_only_the_callers_boards_newest_first_and_marks_ownership()
    {
        var member = Guid.CreateVersion7();
        var stranger = Guid.CreateVersion7();
        await InsertAccountAsync(member);
        await InsertAccountAsync(stranger);

        // Owned, created first.
        var ownedBoardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(ownedBoardId, "Owned board", DateTimeOffset.UtcNow.AddMinutes(-10)));
        await factory.ExecuteAsync(InsertMembershipSql(ownedBoardId, member, "Owner"));

        // A member of, created second — must sort ahead of the owned one.
        var memberBoardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(memberBoardId, "Member board", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await factory.ExecuteAsync(InsertMembershipSql(memberBoardId, member, "Member"));

        // Someone else's board entirely — must never appear.
        var strangersBoardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(strangersBoardId, "Not mine", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertMembershipSql(strangersBoardId, stranger, "Owner"));

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBoardStore>();

        var entries = await store.ListForAccountAsync(member, CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Equal(memberBoardId, entries[0].Id);
        Assert.False(entries[0].IsOwner);
        Assert.Equal(ownedBoardId, entries[1].Id);
        Assert.True(entries[1].IsOwner);
        Assert.DoesNotContain(entries, entry => entry.Id == strangersBoardId);
    }

    // ---- AC-25: a non-member gets exactly what an absent board gets ---------------------------

    [Fact]
    public async Task LoadForMemberAsync_returns_null_when_the_caller_is_not_a_member()
    {
        var owner = Guid.CreateVersion7();
        var nonMember = Guid.CreateVersion7();
        await InsertAccountAsync(owner);
        await InsertAccountAsync(nonMember);

        var boardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(boardId, "A board owned by someone else", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertMembershipSql(boardId, owner, "Owner"));

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBoardStore>();

        var forNonMember = await store.LoadForMemberAsync(boardId, nonMember, CancellationToken.None);
        var forAbsentBoard = await store.LoadForMemberAsync(Guid.CreateVersion7(), nonMember, CancellationToken.None);

        Assert.Null(forNonMember);
        Assert.Null(forAbsentBoard);
    }

    [Fact]
    public async Task The_member_scoped_load_issues_one_query_joined_to_the_callers_membership()
    {
        var owner = Guid.CreateVersion7();
        await InsertAccountAsync(owner);

        var boardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(boardId, "A board", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertMembershipSql(boardId, owner, "Owner"));

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBoardStore>();

        factory.Commands.Clear();
        await store.LoadForMemberAsync(boardId, owner, CancellationToken.None);

        var reads = factory.Commands.Statements
            .Where(sql => sql.Contains("SELECT", StringComparison.Ordinal)
                && sql.Contains("[Boards]", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(reads);
        Assert.Contains("[BoardMemberships]", reads[0], StringComparison.Ordinal);
    }

    // ---- AC-26: a column or card named from another board is never reached through this one ----

    [Fact]
    public async Task FindCardAsync_returns_null_for_a_card_that_belongs_to_another_board()
    {
        var owner = Guid.CreateVersion7();
        await InsertAccountAsync(owner);

        var thisBoardId = Guid.CreateVersion7();
        var thisColumnId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(thisBoardId, "This board", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertColumnSql(thisColumnId, thisBoardId));
        await factory.ExecuteAsync(InsertMembershipSql(thisBoardId, owner, "Owner"));

        var otherBoardId = Guid.CreateVersion7();
        var otherColumnId = Guid.CreateVersion7();
        var otherCardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(otherBoardId, "Another board", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertColumnSql(otherColumnId, otherBoardId));
        await factory.ExecuteAsync(InsertCardSql(otherCardId, otherBoardId, otherColumnId));

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBoardStore>();

        var found = await store.FindCardAsync(thisBoardId, otherCardId, CancellationToken.None);

        Assert.Null(found);
    }

    // ---- ADR 0015: a forced version collision is retried, then reported as contended -----------

    [Fact]
    public async Task A_forced_version_collision_is_retried_at_most_three_times_then_reports_contended()
    {
        var owner = Guid.CreateVersion7();
        await InsertAccountAsync(owner);

        var boardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(boardId, "A board", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertMembershipSql(boardId, owner, "Owner"));
        await factory.ExecuteAsync(InsertColumnSql(Guid.CreateVersion7(), boardId, position: 0));
        await factory.ExecuteAsync(InsertColumnSql(Guid.CreateVersion7(), boardId, position: 1));
        await factory.ExecuteAsync(InsertColumnSql(Guid.CreateVersion7(), boardId, position: 2));

        factory.Contention.Reset();
        factory.Contention.TargetId = boardId;
        factory.Contention.Enabled = true;

        var outcome = await BoardChangeRetry.RunAsync(async attempt =>
        {
            using var scope = factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await store.LoadForMemberAsync(boardId, owner, CancellationToken.None);
            Assert.NotNull(loaded);

            var added = loaded.Board.AddColumn($"Column for attempt {attempt}");
            Assert.True(added.IsSuccess);

            await store.SaveAsync(CancellationToken.None);
            return true;
        });

        factory.Contention.Enabled = false;

        Assert.True(outcome.Contended);
        Assert.Equal(BoardChangeRetry.MaxAttempts, outcome.Attempts);
        Assert.Equal(BoardChangeRetry.MaxAttempts, factory.Contention.InterceptedAttempts);

        // Nothing written: the fourth column never lands, because every attempt lost its race.
        Assert.Equal(3, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
    }

    // ---- data-model.md § Notes for implement: two first creations race on one account -----------

    [Fact]
    public async Task Two_first_ever_creations_for_one_account_are_reconciled_without_a_raw_database_error()
    {
        var accountId = Guid.CreateVersion7();
        await InsertAccountAsync(accountId);

        async Task<OwnedBoardCounter> LoadOrCreateAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<IOwnedBoardCounterStore>()
                .LoadOrCreateAsync(accountId, CancellationToken.None);
        }

        var results = await Task.WhenAll(LoadOrCreateAsync(), LoadOrCreateAsync());

        Assert.All(results, counter => Assert.Equal(0, counter.OwnedBoardCount));
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{accountId}'"));
    }

    // ---- Edge case: a board holding cards deletes in one statement -------------------------------

    [Fact]
    public async Task Deleting_a_board_that_holds_cards_removes_it_in_one_statement_with_no_orphans()
    {
        var owner = Guid.CreateVersion7();
        await InsertAccountAsync(owner);

        var boardId = Guid.CreateVersion7();
        var columnId = Guid.CreateVersion7();
        var cardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(boardId, "A board with cards", DateTimeOffset.UtcNow));
        await factory.ExecuteAsync(InsertColumnSql(columnId, boardId));
        await factory.ExecuteAsync(InsertCardSql(cardId, boardId, columnId));
        await factory.ExecuteAsync(InsertMembershipSql(boardId, owner, "Owner"));

        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IBoardStore>();
            var loaded = await store.LoadForMemberAsync(boardId, owner, CancellationToken.None);
            Assert.NotNull(loaded);

            factory.Commands.Clear();
            store.Remove(loaded.Board);
            await store.SaveAsync(CancellationToken.None);
        }

        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Boards] WHERE [Id] = '{boardId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [BoardId] = '{boardId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[BoardMemberships] WHERE [BoardId] = '{boardId}'"));

        // One statement against Boards, and never a direct DELETE against Columns or Cards — the
        // cascade does that work, so EF's change tracker never gets a chance to delete a column
        // before its cards (data-model.md § Notes for implement).
        var boardDeletes = factory.Commands.Statements
            .Where(sql => sql.Contains("DELETE", StringComparison.Ordinal)
                && sql.Contains("[Boards]", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(boardDeletes);
        Assert.DoesNotContain(
            factory.Commands.Statements,
            sql => sql.Contains("DELETE", StringComparison.Ordinal)
                && sql.Contains("[Columns]", StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.Commands.Statements,
            sql => sql.Contains("DELETE", StringComparison.Ordinal)
                && sql.Contains("[Cards]", StringComparison.Ordinal));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private Task InsertAccountAsync(Guid accountId) =>
        factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"board-{Guid.NewGuid():N}"));

    private static string InsertBoardSql(Guid id, string name, DateTimeOffset createdAt) =>
        $"""
        INSERT INTO [dbo].[Boards] ([Id], [Name], [CreatedAt], [CardCount], [ColumnLayoutVersion])
        VALUES ('{id}', N'{name}', '{createdAt:O}', 0, 1);
        """;

    private static string InsertColumnSql(Guid id, Guid boardId, int position = 0) =>
        $"""
        INSERT INTO [dbo].[Columns]
            ([Id], [BoardId], [Name], [Position], [CardCount], [NextCardPosition], [NameVersion])
        VALUES ('{id}', '{boardId}', N'To do', {position}, 0, 0, 1);
        """;

    private static string InsertCardSql(Guid id, Guid boardId, Guid columnId) =>
        $"""
        INSERT INTO [dbo].[Cards]
            ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
        VALUES ('{id}', '{boardId}', '{columnId}', 0, N'A card', N'', 1);
        """;

    private static string InsertMembershipSql(Guid boardId, Guid accountId, string role) =>
        $"""
        INSERT INTO [dbo].[BoardMemberships] ([Id], [BoardId], [AccountId], [Role])
        VALUES ('{Guid.CreateVersion7()}', '{boardId}', '{accountId}', N'{role}');
        """;
}
