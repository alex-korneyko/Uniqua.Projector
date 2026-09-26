using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// T14 / T26 — spec.md §6 NFR "Board invariants under simultaneous changes" (sad.md §10, QG-2):
/// across 1,000 randomised pairs of simultaneous changes — column deletes, column adds, card adds,
/// board creations, and a card add racing the deletion of its own column — 0 boards are left with
/// no column, 0 non-empty columns are deleted, 0 boards exceed 20 columns or 1,000 cards, and 0
/// accounts own more than 50 boards (AC-03, AC-09, AC-10b, AC-11, AC-15).
/// </summary>
/// <remarks>
/// <para>
/// Every pair is forced to collide (review Q2a): <see cref="ContentionForcer.RaceAsync{T}"/> holds
/// whichever racer reaches its save first until the other racer has committed, so the held one's
/// save always meets a board that moved under it and must reload and re-decide. Each ceiling pair
/// therefore has exactly one right answer — one change lands, the other is refused with its own
/// domain refusal — and a pair that ends <c>boards.contended</c>, lands both, or refuses for any other
/// reason fails the run. A retry that replayed its stale decision instead of reloading would land
/// both (or end contended) and so cannot pass.
/// </para>
/// <para>
/// Each pair runs on a fresh board and fresh accounts, inserted directly rather than registered, so
/// the 120-per-minute change limit and the cost of password hashing never interfere
/// (data-model.md § Test fixtures).
/// </para>
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
        AddCardDeleteOtherColumn,
        AddCardDeleteSameColumn,
        CreateCreate,
    }

    private static readonly PairKind[] Kinds =
    [
        PairKind.DeleteDelete,
        PairKind.AddColumnAddColumn,
        PairKind.AddCardAddCard,
        PairKind.AddCardDeleteOtherColumn,
        PairKind.AddCardDeleteSameColumn,
        PairKind.CreateCreate,
    ];

    [Fact]
    public async Task A_thousand_randomised_simultaneous_pairs_leave_every_board_invariant_intact()
    {
        var seed = Environment.TickCount;
        var random = new Random(seed);
        var violations = new List<string>();
        var seen = Kinds.ToDictionary(kind => kind, _ => 0);

        for (var iteration = 0; iteration < PairCount; iteration++)
        {
            var kind = Kinds[random.Next(Kinds.Length)];
            seen[kind]++;

            try
            {
                await (kind switch
                {
                    PairKind.DeleteDelete => RunDeleteDeleteAsync(iteration, violations),
                    PairKind.AddColumnAddColumn => RunAddColumnAddColumnAsync(iteration, violations),
                    PairKind.AddCardAddCard => RunAddCardAddCardAsync(iteration, violations),
                    PairKind.AddCardDeleteOtherColumn => RunAddCardDeleteOtherColumnAsync(iteration, violations),
                    PairKind.AddCardDeleteSameColumn => RunAddCardDeleteSameColumnAsync(iteration, violations),
                    PairKind.CreateCreate => RunCreateCreateAsync(iteration, violations),
                    _ => throw new InvalidOperationException($"Unhandled pair kind {kind}."),
                });
            }
            catch (Exception exception)
            {
                // A racer that throws is the 500 a member would have been shown.
                violations.Add($"pair {iteration} ({kind}): threw {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                factory.Contention.Reset();
            }
        }

        Assert.True(
            violations.Count == 0,
            $"seed {seed}: {violations.Count} violation(s) across {PairCount} pairs "
            + $"({string.Join(", ", seen.Select(pair => $"{pair.Key}={pair.Value}"))}; "
            + $"first {Math.Min(20, violations.Count)} shown):\n"
            + string.Join("\n", violations.Take(20)));
    }

    // ---- AC-10b: a board trimmed to exactly two empty columns, both deleted at once ----------------

    private async Task RunDeleteDeleteAsync(int iteration, List<string> violations)
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
                return;
            }
        }

        async Task<Domain.Result<ColumnLayout, BoardError>> DeleteAsync(Guid columnId)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner, columnId, seenNameVersion: 1, CancellationToken.None);
        }

        var race = await RaceAsync(() => DeleteAsync(firstId), () => DeleteAsync(lastId));

        AssertOneLandsAndTheOtherIsRefused(race, BoardErrors.LastColumn, iteration, "delete-delete", violations);

        var remainingColumns = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'");
        if (remainingColumns != 1)
        {
            violations.Add($"pair {iteration} (delete-delete): board {boardId} holds {remainingColumns} columns, not 1.");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
    }

    // ---- AC-11: a board one short of the 20-column ceiling, two adds at once -----------------------

    private async Task RunAddColumnAddColumnAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        await FillColumnsUpToAsync(boardId, Board.MaxColumns - 1);

        async Task<Domain.Result<AddedColumn, BoardError>> AddAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddColumn>()
                .ExecuteAsync(boardId, owner, name, CancellationToken.None);
        }

        var race = await RaceAsync(() => AddAsync($"Racer A {iteration}"), () => AddAsync($"Racer B {iteration}"));

        AssertOneLandsAndTheOtherIsRefused(
            race, BoardErrors.ColumnLimitReached, iteration, "add-column", violations);
        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
    }

    // ---- AC-15: a board one short of the 1,000-card ceiling, two adds at once -----------------------

    private async Task RunAddCardAddCardAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        var columnId = await FirstColumnIdAsync(boardId);
        await FillCardsUpToAsync(boardId, columnId, Board.MaxCards - 1);

        async Task<Domain.Result<Application.Boards.Ports.CardSummary, BoardError>> AddAsync(string title)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddCard>()
                .ExecuteAsync(boardId, owner, columnId, title, null, CancellationToken.None);
        }

        var race = await RaceAsync(() => AddAsync($"Racer A {iteration}"), () => AddAsync($"Racer B {iteration}"));

        AssertOneLandsAndTheOtherIsRefused(race, BoardErrors.CardLimitReached, iteration, "add-card", violations);
        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
    }

    // ---- an add-card and a delete-column on a different, empty column: both legal, both land ---------

    private async Task RunAddCardDeleteOtherColumnAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        var (firstId, _, lastId) = await ThreeDefaultColumnIdsAsync(boardId);

        async Task<object> AddAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddCard>()
                .ExecuteAsync(boardId, owner, firstId, $"Card {iteration}", null, CancellationToken.None);
        }

        async Task<object> DeleteAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner, lastId, seenNameVersion: 1, CancellationToken.None);
        }

        var race = await RaceAsync(AddAsync, DeleteAsync);
        AssertForced(race, iteration, "add-card/delete-other-column", violations);

        var add = (Domain.Result<Application.Boards.Ports.CardSummary, BoardError>)race.First;
        var delete = (Domain.Result<ColumnLayout, BoardError>)race.Second;

        if (!add.IsSuccess)
        {
            violations.Add(
                $"pair {iteration} (add-card/delete-other-column): the card add was refused ({add.Error!.Code}) "
                + "although nothing about it conflicts with deleting a different, empty column.");
        }

        if (!delete.IsSuccess)
        {
            violations.Add(
                $"pair {iteration} (add-card/delete-other-column): the column delete was refused "
                + $"({delete.Error!.Code}) although the column was empty and not the last one.");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
    }

    // ---- AC-09 / AC-18b: an add-card to column X racing the deletion of X itself ---------------------

    private async Task RunAddCardDeleteSameColumnAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        var boardId = await ACreatedBoardAsync(owner);
        var (_, _, contestedId) = await ThreeDefaultColumnIdsAsync(boardId);

        async Task<object> AddAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AddCard>()
                .ExecuteAsync(boardId, owner, contestedId, $"Card {iteration}", null, CancellationToken.None);
        }

        async Task<object> DeleteAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                .ExecuteAsync(boardId, owner, contestedId, seenNameVersion: 1, CancellationToken.None);
        }

        var race = await RaceAsync(AddAsync, DeleteAsync);
        AssertForced(race, iteration, "add-card/delete-same-column", violations);

        var add = (Domain.Result<Application.Boards.Ports.CardSummary, BoardError>)race.First;
        var delete = (Domain.Result<ColumnLayout, BoardError>)race.Second;

        // Exactly one of the two may land, and the other must be refused for the reason the one that
        // landed gives it: a card in X keeps X (column_not_empty), and X gone takes the add with it
        // (not_available, as any absent column is).
        var outcome = (add.IsSuccess, delete.IsSuccess) switch
        {
            (true, false) when delete.Error == BoardErrors.ColumnNotEmpty => null,
            (false, true) when add.Error == BoardErrors.NotAvailable => null,
            _ => $"add {Describe(add.IsSuccess, add.Error)}, delete {Describe(delete.IsSuccess, delete.Error)}",
        };
        if (outcome is not null)
        {
            violations.Add(
                $"pair {iteration} (add-card/delete-same-column): {outcome}; expected exactly one to land "
                + "and the other refused column_not_empty or not_available.");
        }

        var columnLeft = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [Id] = '{contestedId}'");
        var cardsInIt = await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [ColumnId] = '{contestedId}'");
        if (columnLeft == 0 && cardsInIt > 0)
        {
            violations.Add($"pair {iteration} (add-card/delete-same-column): column deleted holding {cardsInIt} card(s).");
        }

        await AssertBoardCountersConsistentAsync(boardId, iteration, violations);
    }

    // ---- AC-03: one account one short of the 50-owned-board ceiling, two creates at once -------------

    private async Task RunCreateCreateAsync(int iteration, List<string> violations)
    {
        var owner = await AFastAccountAsync();
        await SetOwnedBoardCountAsync(owner, OwnedBoardCounter.MaxOwnedBoards - 1);

        async Task<Domain.Result<BoardView, BoardError>> CreateAsync(string name)
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<CreateBoard>()
                .ExecuteAsync(owner, name, CancellationToken.None);
        }

        var race = await RaceAsync(
            () => CreateAsync($"Racer A {iteration}"), () => CreateAsync($"Racer B {iteration}"));

        AssertOneLandsAndTheOtherIsRefused(
            race, BoardErrors.OwnedBoardLimitReached, iteration, "create-create", violations);

        var ownedCount = await factory.ScalarAsync<int>(
            $"SELECT [OwnedBoardCount] FROM [dbo].[OwnedBoardCounters] WHERE [AccountId] = '{owner}'");
        if (ownedCount != OwnedBoardCounter.MaxOwnedBoards)
        {
            violations.Add(
                $"pair {iteration} (create-create): account {owner} ended owning {ownedCount} boards, "
                + $"not exactly the {OwnedBoardCounter.MaxOwnedBoards} ceiling.");
        }
    }

    // ---- Racing and the outcome rules ----------------------------------------------------------------

    /// <summary>Runs both racers at once, forced to collide (see the class remarks).</summary>
    private Task<ContentionForcer.Race<T>> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second) =>
        factory.Contention.RaceAsync(first, second);

    /// <summary>Q2a / Q6: every pair must actually have collided, not merely run side by side.</summary>
    private static void AssertForced<T>(
        ContentionForcer.Race<T> race, int iteration, string pair, List<string> violations)
    {
        if (!race.Collided)
        {
            violations.Add(
                $"pair {iteration} ({pair}): no forced collision — neither racer's save was held while "
                + "the other committed, so the pair never exercised the race it is named for.");
        }
    }

    /// <summary>
    /// The ceiling rule: exactly one of the two lands and the other is refused with
    /// <paramref name="loserRefusal"/> itself. Both landing breaks the invariant; either ending
    /// <c>boards.contended</c>, or refused for any other reason, means the retry did not re-decide.
    /// </summary>
    private static void AssertOneLandsAndTheOtherIsRefused<TValue>(
        ContentionForcer.Race<Domain.Result<TValue, BoardError>> race,
        BoardError loserRefusal,
        int iteration,
        string pair,
        List<string> violations)
    {
        AssertForced(race, iteration, pair, violations);

        var results = new[] { race.First, race.Second };
        var successes = results.Count(result => result.IsSuccess);
        var refusals = results.Where(result => !result.IsSuccess).Select(result => result.Error!).ToArray();

        if (successes != 1 || refusals.Length != 1 || !ReferenceEquals(refusals[0], loserRefusal))
        {
            violations.Add(
                $"pair {iteration} ({pair}): {successes} of 2 landed, refusals "
                + $"[{string.Join(", ", refusals.Select(error => error.Code))}]; expected exactly one to land "
                + $"and the other refused {loserRefusal.Code}.");
        }
    }

    private static string Describe(bool isSuccess, BoardError? error) =>
        isSuccess ? "landed" : $"refused {error!.Code}";

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

    /// <summary>
    /// Fills <paramref name="columnId"/> with <paramref name="total"/> filler cards in one batched
    /// statement (at most 1,000 rows, SQL Server's VALUES limit) — a round trip per card made every
    /// add-card pair cost a thousand statements.
    /// </summary>
    private async Task FillCardsUpToAsync(Guid boardId, Guid columnId, int total)
    {
        if (total == 0)
        {
            return;
        }

        var rows = string.Join(
            ",\n",
            Enumerable.Range(0, total).Select(position =>
                $"('{Guid.CreateVersion7()}', '{boardId}', '{columnId}', {position}, N'Filler {position}', N'', 1)"));

        await factory.ExecuteAsync(
            $"""
            INSERT INTO [dbo].[Cards]
                ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
            VALUES
            {rows};
            UPDATE [dbo].[Columns] SET [CardCount] = {total}, [NextCardPosition] = {total}
            WHERE [Id] = '{columnId}';
            UPDATE [dbo].[Boards] SET [CardCount] = {total} WHERE [Id] = '{boardId}';
            """);
    }
}
