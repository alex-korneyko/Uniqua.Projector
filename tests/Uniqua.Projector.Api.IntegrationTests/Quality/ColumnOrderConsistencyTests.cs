using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// T14 — spec.md §6 NFR "Column order consistency" (sad.md §10, QG-2): across 1,000 randomised
/// sequences of accepted adds, moves and deletes, every column of a board holds exactly one
/// distinct position — 0 duplicated and 0 missing positions after every step, checked against the
/// store directly rather than against what a use case merely claims to have done (ADR 0017).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ColumnOrderConsistencyTests(ApiFactory factory)
{
    private const int SequenceCount = 1_000;

    private const int MinSteps = 5;

    private const int MaxSteps = 15;

    private enum StepKind
    {
        Add,
        Move,
        Delete,
    }

    [Fact]
    public async Task A_thousand_randomised_column_sequences_keep_positions_exactly_dense_after_every_step()
    {
        var seed = Environment.TickCount;
        var random = new Random(seed);
        var violations = new List<string>();

        for (var sequence = 0; sequence < SequenceCount; sequence++)
        {
            var owner = await AFastAccountAsync();
            var boardId = await ACreatedBoardAsync(owner);
            var columnIds = (await OrderedColumnIdsAsync(boardId)).ToList();

            var steps = MinSteps + random.Next(MaxSteps - MinSteps + 1);
            for (var step = 0; step < steps; step++)
            {
                var choices = new List<StepKind> { StepKind.Add };
                if (columnIds.Count > 0)
                {
                    choices.Add(StepKind.Move);
                }

                if (columnIds.Count > 1)
                {
                    choices.Add(StepKind.Delete);
                }

                var kind = choices[random.Next(choices.Count)];
                if (kind is StepKind.Add && columnIds.Count >= Board.MaxColumns)
                {
                    kind = StepKind.Move;
                }

                using var scope = factory.Services.CreateScope();
                var accepted = true;

                switch (kind)
                {
                    case StepKind.Add:
                        var added = await scope.ServiceProvider.GetRequiredService<AddColumn>()
                            .ExecuteAsync(boardId, owner, $"Seq {sequence} step {step}", CancellationToken.None);
                        if (added.IsSuccess)
                        {
                            columnIds.Add(added.Value.Column.Id);
                        }
                        else
                        {
                            accepted = false;
                        }

                        break;

                    case StepKind.Move:
                        var columnToMove = columnIds[random.Next(columnIds.Count)];
                        var newPosition = random.Next(columnIds.Count);
                        var layoutVersion = await CurrentLayoutVersionAsync(boardId);
                        var moved = await scope.ServiceProvider.GetRequiredService<MoveColumn>()
                            .ExecuteAsync(
                                boardId, owner, columnToMove, newPosition, layoutVersion, CancellationToken.None);
                        accepted = moved.IsSuccess;
                        break;

                    case StepKind.Delete:
                        var columnToDelete = columnIds[random.Next(columnIds.Count)];
                        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteColumn>()
                            .ExecuteAsync(boardId, owner, columnToDelete, seenNameVersion: 1, CancellationToken.None);
                        if (deleted.IsSuccess)
                        {
                            columnIds.Remove(columnToDelete);
                        }
                        else
                        {
                            accepted = false;
                        }

                        break;
                }

                if (!accepted)
                {
                    continue;
                }

                var positions = (await ColumnPositionsAsync(boardId)).OrderBy(p => p).ToArray();
                var expected = Enumerable.Range(0, positions.Length).ToArray();
                if (!positions.SequenceEqual(expected))
                {
                    violations.Add(
                        $"seq {sequence} step {step} ({kind}): board {boardId} positions were "
                        + $"[{string.Join(",", positions)}], expected [{string.Join(",", expected)}].");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"seed {seed}: {violations.Count} violation(s) across {SequenceCount} sequences "
            + $"(first {Math.Min(20, violations.Count)} shown):\n"
            + string.Join("\n", violations.Take(20)));
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    private async Task<Guid> AFastAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"colorder-{Guid.NewGuid():N}"));
        return accountId;
    }

    private async Task<Guid> ACreatedBoardAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<CreateBoard>()
            .ExecuteAsync(ownerId, "A column-order board", CancellationToken.None);
        Assert.True(created.IsSuccess);
        return created.Value.Id;
    }

    private async Task<IReadOnlyList<Guid>> OrderedColumnIdsAsync(Guid boardId)
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

        return ids;
    }

    private Task<int> CurrentLayoutVersionAsync(Guid boardId) =>
        factory.ScalarAsync<int>(
            $"SELECT [ColumnLayoutVersion] FROM [dbo].[Boards] WHERE [Id] = '{boardId}'");

    /// <summary>
    /// Every column's position for the board, read back from the store — not from what a use
    /// case's return value claims — as a single scalar so one round trip is enough.
    /// </summary>
    private async Task<int[]> ColumnPositionsAsync(Guid boardId)
    {
        var csv = await factory.ScalarAsync<string>(
            $"""
            SELECT STRING_AGG(CAST([Position] AS NVARCHAR(10)), ',')
            FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'
            """);

        return string.IsNullOrEmpty(csv) ? [] : csv.Split(',').Select(int.Parse).ToArray();
    }
}
