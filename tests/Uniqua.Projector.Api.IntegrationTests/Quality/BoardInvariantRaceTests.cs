using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// T14 — spec.md §6 NFR "Board invariants under simultaneous changes" (sad.md §10, QG-2): across
/// 1,000 randomised pairs of simultaneous changes — column deletes, column adds, card adds and
/// board creations, including pairs made one short of each ceiling — 0 boards are left with no
/// column, 0 non-empty columns are deleted, 0 boards exceed 20 columns or 1,000 cards, and 0
/// accounts own more than 50 boards (AC-03, AC-10b, AC-11, AC-15).
/// </summary>
/// <remarks>
/// Every one-short-of-the-ceiling pair is forced to collide with <see cref="ContentionForcer"/>,
/// generalised from its original <c>AspNetUsers</c>-only match to the board, column, card and
/// owned-board-counter rows (data-model.md § Test fixtures) — otherwise a pair that happens to run
/// one attempt after the other would never actually exercise the race it is named for. Each pair
/// runs on a fresh board and fresh accounts, inserted directly rather than registered, so the
/// 120-per-minute change limit and the cost of password hashing never interfere
/// (data-model.md § Test fixtures).
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class BoardInvariantRaceTests(ApiFactory factory)
{
    private const int PairCount = 1_000;

    private enum PairKind
    {
        DeleteDelete,
        AddColumnAddColumn,
        AddCardAddCard,
        AddCardDeleteColumn,
        CreateCreate,
    }

    private static readonly PairKind[] Kinds =
    [
        PairKind.DeleteDelete,
        PairKind.AddColumnAddColumn,
        PairKind.AddCardAddCard,
        PairKind.AddCardDeleteColumn,
        PairKind.CreateCreate,
    ];

    [Fact]
    public async Task A_thousand_randomised_simultaneous_pairs_leave_every_board_invariant_intact()
    {
        var seed = Environment.TickCount;
        var random = new Random(seed);
        var violations = new List<string>();
        var forcedCollisionPairs = 0;

        for (var iteration = 0; iteration < PairCount; iteration++)
        {
            var kind = Kinds[random.Next(Kinds.Length)];

            try
            {
                var collided = kind switch
                {
                    PairKind.DeleteDelete => await RunDeleteDeleteAsync(iteration, violations),
                    PairKind.AddColumnAddColumn => await RunAddColumnAddColumnAsync(iteration, violations),
                    PairKind.AddCardAddCard => await RunAddCardAddCardAsync(iteration, violations),
                    PairKind.AddCardDeleteColumn => await RunAddCardDeleteColumnAsync(iteration, violations),
                    PairKind.CreateCreate => await RunCreateCreateAsync(iteration, violations),
                    _ => throw new InvalidOperationException($"Unhandled pair kind {kind}."),
                };

                if (collided)
                {
                    forcedCollisionPairs++;
                }
            }
            finally
            {
                factory.Contention.Reset();
            }
        }

        Assert.True(
            forcedCollisionPairs > 0,
            $"seed {seed}: not one of the {PairCount} pairs recorded a forced collision — "
            + "ContentionForcer never intercepted a matching UPDATE, so the one-short-of-the-ceiling "
            + "pairs never actually collided.");

        Assert.True(
            violations.Count == 0,
            $"seed {seed}: {violations.Count} invariant violation(s) across {PairCount} pairs "
            + $"(first {Math.Min(20, violations.Count)} shown):\n"
            + string.Join("\n", violations.Take(20)));
    }

    // ---- AC-10b: a board trimmed to exactly two empty columns, both deleted at once ----------------

    private async Task<bool> RunDeleteDeleteAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        var (firstId, secondId, lastId) = await ThreeDefaultColumnIdsAsync(boardId);

        using (var trimScope = factory.Services.CreateScope())
        {
            var trimmed = await trimScope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner, secondId, seenNameVersion: 1, CancellationToken.None);
            if (!trimmed.IsSuccess)
            {
                violations.Add($"pair {iteration} (delete-delete): trimming the middle column failed unexpectedly.");
                return false;
            }
        }

        factory.Contention.TargetId = boardId;
        factory.Contention.Enabled = true;

        async Task<Domain.Result<ColumnLayout, BoardError>> DeleteAsync(Guid columnId)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner, columnId, seenNameVersion: 1, CancellationToken.None);
        }

        var results = await Task.WhenAll(DeleteAsync(firstId), DeleteAsync(lastId));
        var collided = factory.Contention.InterceptedAttempts > 0;
        factory.Contention.Enabled = false;

        // Both landing would break the last-column rule (never asserted here — the ceiling check
        // below catches it); both losing to a spent retry budget is a legal `contended` outcome
        // (API contract, edge cases table) and is not itself a violation.
        var successes = results.Count(r => r.IsSuccess);
        if (successes > 1)
        {
            violations.Add(
                $"pair {iteration} (delete-delete): {successes} of 2 deletes succeeded, at most 1 may.");
        }

        var remainingColumns = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'");
        if (remainingColumns < 1)
        {
            violations.Add($"pair {iteration} (delete-delete): board {boardId} left with no column.");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
        return collided;
    }

    // ---- AC-11: a board one short of the 20-column ceiling, two adds at once -----------------------

    private async Task<bool> RunAddColumnAddColumnAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        await FillColumnsUpToAsync(boardId, Board.MaxColumns - 1);

        factory.Contention.TargetId = boardId;
        factory.Contention.Enabled = true;

        async Task<Domain.Result<AddedColumn, BoardError>> AddAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddColumn>()
                .ExecuteAsync(boardId, owner, name, CancellationToken.None);
        }

        var results = await Task.WhenAll(AddAsync($"Racer A {iteration}"), AddAsync($"Racer B {iteration}"));
        var collided = factory.Contention.InterceptedAttempts > 0;
        factory.Contention.Enabled = false;

        // Both landing would push the board past the 20-column ceiling (caught below); both
        // losing to a spent retry budget is a legal `contended` outcome and not itself a violation.
        var successes = results.Count(r => r.IsSuccess);
        if (successes > 1)
        {
            violations.Add(
                $"pair {iteration} (add-column): {successes} of 2 adds succeeded, at most 1 may.");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
        return collided;
    }

    // ---- AC-15: a board one short of the 1,000-card ceiling, two adds at once -----------------------

    private async Task<bool> RunAddCardAddCardAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        var columnId = await FirstColumnIdAsync(boardId);
        await FillCardsUpToAsync(boardId, columnId, Board.MaxCards - 1);

        factory.Contention.TargetId = boardId;
        factory.Contention.Enabled = true;

        async Task<Domain.Result<Application.Boards.Ports.CardSummary, BoardError>> AddAsync(string title)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddCard>()
                .ExecuteAsync(boardId, owner, columnId, title, null, CancellationToken.None);
        }

        var results = await Task.WhenAll(AddAsync($"Racer A {iteration}"), AddAsync($"Racer B {iteration}"));
        var collided = factory.Contention.InterceptedAttempts > 0;
        factory.Contention.Enabled = false;

        // Both landing would push the board past the 1,000-card ceiling (caught below); both
        // losing to a spent retry budget is a legal `contended` outcome and not itself a violation.
        var successes = results.Count(r => r.IsSuccess);
        if (successes > 1)
        {
            violations.Add(
                $"pair {iteration} (add-card): {successes} of 2 adds succeeded, at most 1 may.");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
        return collided;
    }

    // ---- an add-card and a delete-column on a different, empty column, both legal -------------------

    private async Task<bool> RunAddCardDeleteColumnAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        var (firstId, _, lastId) = await ThreeDefaultColumnIdsAsync(boardId);

        var addTask = Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddCard>()
                .ExecuteAsync(boardId, owner, firstId, $"Card {iteration}", null, CancellationToken.None);
        });
        var deleteTask = Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner, lastId, seenNameVersion: 1, CancellationToken.None);
        });

        await Task.WhenAll(addTask, deleteTask);
        var addResult = await addTask;
        var deleteResult = await deleteTask;

        if (!addResult.IsSuccess)
        {
            violations.Add(
                $"pair {iteration} (add-card/delete-column): the card add was refused ({addResult.Error!.Code}) "
                + "although nothing about it conflicts with deleting a different, empty column.");
        }

        if (!deleteResult.IsSuccess)
        {
            violations.Add(
                $"pair {iteration} (add-card/delete-column): the column delete was refused "
                + $"({deleteResult.Error!.Code}) although the column was empty and not the last one.");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);

        var remainingColumns = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'");
        if (remainingColumns < 1)
        {
            violations.Add($"pair {iteration} (add-card/delete-column): board {boardId} left with no column.");
        }

        return false;
    }

    // ---- AC-03: one account one short of the 50-owned-board ceiling, two creates at once -------------

    private async Task<bool> RunCreateCreateAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        await SetOwnedBoardCountAsync(owner, OwnedBoardCounter.MaxOwnedBoards - 1);

        factory.Contention.TargetId = owner;
        factory.Contention.Enabled = true;

        async Task<Domain.Result<BoardView, BoardError>> CreateAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<CreateBoard>()
                .ExecuteAsync(owner, name, CancellationToken.None);
        }

        var results = await Task.WhenAll(CreateAsync($"Racer A {iteration}"), CreateAsync($"Racer B {iteration}"));
        var collided = factory.Contention.InterceptedAttempts > 0;
        factory.Contention.Enabled = false;

        // Both landing would push the account past the 50-owned-board ceiling (caught below); both
        // losing to a spent retry budget is a legal `contended` outcome and not itself a violation.
        var successes = results.Count(r => r.IsSuccess);
        if (successes > 1)
        {
            violations.Add(
                $"pair {iteration} (create-create): {successes} of 2 creates succeeded, at most 1 may.");
        }

        var ownedCount = await factory.ScalarAsync<int>(
            $"SELECT [OwnedBoardCount] FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner}'");
        if (ownedCount > OwnedBoardCounter.MaxOwnedBoards)
        {
            violations.Add(
                $"pair {iteration} (create-create): account {owner} ended owning {ownedCount} boards, "
                + $"over the {OwnedBoardCounter.MaxOwnedBoards} ceiling.");
        }

        return collided;
    }

    // ---- Invariant helper -----------------------------------------------------------------------------

    private async Task AssertBoardCountersConsistentAsync(Guid boardId, int iteration, List<string> violations)
    {
        var columnCount = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'");
        if (columnCount is < 1 or > Board.MaxColumns)
        {
            violations.Add(
                $"pair {iteration}: board {boardId} holds {columnCount} columns, outside 1..{Board.MaxColumns}.");
        }

        var boardCardCount = await factory.ScalarAsync<int>(
            $"SELECT [CardCount] FROM [dbo].[Boards] WHERE [Id] = '{boardId}'");
        var actualCardCount = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [BoardId] = '{boardId}'");
        if (boardCardCount != actualCardCount)
        {
            violations.Add(
                $"pair {iteration}: board {boardId} CardCount={boardCardCount} but holds "
                + $"{actualCardCount} card row(s) — a column deletion or card add may have lost track "
                + "of a card.");
        }

        if (boardCardCount > Board.MaxCards)
        {
            violations.Add(
                $"pair {iteration}: board {boardId} holds {boardCardCount} cards, "
                + $"over the {Board.MaxCards} ceiling.");
        }
    }

    // ---- Fixtures, inserted directly so 1,000 pairs stay inside the CI budget (edge cases table) ------

    /// <summary>An account row, inserted directly rather than registered — no password to hash.</summary>
    private async Task<Guid> AFastAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"race-{Guid.NewGuid():N}"));
        return accountId;
    }

    private async Task<Guid> ACreatedBoardAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(ownerId, "A race board", CancellationToken.None);
        Assert.True(created.IsSuccess);
        return created.Value.Id;
    }

    private Task SetOwnedBoardCountAsync(Guid accountId, int count) =>
        factory.ExecuteAsync(
            $"""
            MERGE [dbo].[OwnedBoardCounters] AS target
            USING (SELECT '{accountId}' AS AccountId) AS source
            ON target.[AccountId] = source.[AccountId]
            WHEN MATCHED THEN UPDATE SET [OwnedBoardCount] = {count}
            WHEN NOT MATCHED THEN INSERT ([AccountId], [OwnedBoardCount]) VALUES (source.[AccountId], {count});
            """);

    private async Task<Guid> FirstColumnIdAsync(Guid boardId) =>
        await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

    private async Task<(Guid First, Guid Second, Guid Last)> ThreeDefaultColumnIdsAsync(Guid boardId)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await factory.ScalarAsync<Guid>(
                $"""
                SELECT [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]
                OFFSET {i} ROWS FETCH NEXT 1 ROWS ONLY
                """));
        }

        return (ids[0], ids[1], ids[2]);
    }

    /// <summary>Fills a freshly created board (three columns) up to <paramref name="total"/> columns.</summary>
    private async Task FillColumnsUpToAsync(Guid boardId, int total)
    {
        for (var position = 3; position < total; position++)
        {
            await factory.ExecuteAsync(
                $"""
                INSERT INTO [dbo].[Columns]
                    ([Id], [BoardId], [Name], [Position], [CardCount], [NextCardPosition], [NameVersion])
                VALUES ('{Guid.CreateVersion7()}', '{boardId}', N'Filler {position}', {position}, 0, 0, 1);
                """);
        }
    }

    private async Task FillCardsUpToAsync(Guid boardId, Guid columnId, int total)
    {
        for (var position = 0; position < total; position++)
        {
            await factory.ExecuteAsync(
                $"""
                INSERT INTO [dbo].[Cards]
                    ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
                VALUES ('{Guid.CreateVersion7()}', '{boardId}', '{columnId}', {position}, N'Filler {position}', N'', 1);
                """);
        }

        if (total > 0)
        {
            await factory.ExecuteAsync(
                $"""
                UPDATE [dbo].[Columns] SET [CardCount] = {total}, [NextCardPosition] = {total}
                WHERE [Id] = '{columnId}';
                """);
            await factory.ExecuteAsync(
                $"UPDATE [dbo].[Boards] SET [CardCount] = {total} WHERE [Id] = '{boardId}';");
        }
    }
}
