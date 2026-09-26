using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// T15 — QG-3: measures opening a full board, a single change and change throughput against the
/// spec §6 budgets, over the sad.md §10 workload (at least 25 accounts, each on its own board, an
/// even mix of the eight change kinds, 60 s per run).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a regression check, not the measurement</strong>, exactly as
/// <see cref="LatencyBudgetTests"/> already is for the accounts-and-sessions figures: the real
/// figures are the reference-machine smoke run's (sad.md §10). It also times from the client, round
/// trip through the in-process test server, whereas spec §6 budgets server-side time (review Q6):
/// the figures are comparable run to run, not with the §6 budgets. CI compares this run's
/// p95/p95/rate against the last recorded run (spec §8, fourth question) and fails on a p95 more
/// than <see cref="RegressionTolerance"/> slower (or, for throughput, that much lower) — set
/// <see cref="ReferenceMachineFlag"/> to assert the absolute spec §6 figures instead, on the
/// machine those figures are actually measured on.
/// </para>
/// <para>
/// The last recorded run is <c>Quality/.baselines/board-latency-budget.json</c>, a deliberately
/// committed artefact (review Q3). An ordinary run only reads it. To refresh it, run this test with
/// <see cref="RecordFlag"/> set (<c>BOARD_LATENCY_RECORD=1</c>) on the machine CI's figures should be
/// held to, and commit the file: the new figures are written only once every assertion above has
/// passed, so a failing run can never lower its own bar, and they replace the old ones outright
/// rather than ratcheting. No recorded baseline yet is not a failure (edge case table): the run
/// passes, and records one only when asked to.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Smoke")]
public sealed class BoardLatencyBudgetTests(ApiFactory factory)
{
    // ---- §8 workload (sad.md §10 QG-3, spec.md §8 fourth open question) ---------------------------

    /// <summary>
    /// sad.md §10 sets the workload floor at "at least 25 accounts". 25 was tried first and cannot
    /// reach the §6 throughput target: at <see cref="MinCyclePeriod"/>'s pace (1.8 changes/s/account,
    /// itself already under the 2/s per-account ceiling) 25 accounts cap out at 45 changes/s, so the
    /// target of ≥ 50/s can never be hit no matter how fast the server answers — every run measured
    /// ~44.8/s, which is the pacing, not the server (test-author review, defect report). 30 accounts
    /// at the same safe pace cap out at 54/s, clearing the target with headroom while every account
    /// still stays under its rate limit.
    /// </summary>
    private const int Accounts = 30;

    private const int WorkloadSeconds = 60;

    /// <summary>
    /// Each cycle below spends 8 changes and is paced to take at least this long: 1.8 changes/s per
    /// account, a real-time rate under the 2/s the per-account limit (120/minute) allows.
    /// </summary>
    /// <remarks>
    /// The pacing is not what keeps an account under the limit during this test: the limiter reads
    /// the frozen <see cref="TestClock"/>, so no attempt ever leaves its minute and every attempt of
    /// the whole run counts against one window (review Q3). What keeps the run clear is the total —
    /// at most ⌈60 s / period⌉ = 14 cycles × 8 changes, plus <see cref="SetupChangesPerAccount"/>,
    /// = 114 attempts — and the test asserts that bound rather than assuming it. An account tripping
    /// <c>boards.change_rate_limited</c> mid-run means the workload itself is miscalibrated (edge
    /// case table), not a real measurement.
    /// </remarks>
    private static readonly TimeSpan MinCyclePeriod = TimeSpan.FromSeconds(8 / 1.8);

    /// <summary>The two board creations <see cref="SeedAccountAsync"/> makes for each account.</summary>
    private const int SetupChangesPerAccount = 2;

    // ---- §6 budgets ----------------------------------------------------------------------------

    private const double OpenBudgetMs = 300;
    private const double ChangeBudgetMs = 200;
    private const double ThroughputFloorPerSecond = 50;

    /// <summary>How much slower than the last recorded run counts as a regression (sad.md §10).</summary>
    private const double RegressionTolerance = 1.25;

    /// <summary>Set to run the absolute §6 assertions, on the machine those figures are measured on.</summary>
    private const string ReferenceMachineFlag = "UNIQUA_REFERENCE_MACHINE";

    /// <summary>Set (to <c>1</c>) to write a passing run's figures as the new recorded baseline.</summary>
    private const string RecordFlag = "BOARD_LATENCY_RECORD";

    private static readonly string BaselinePath = BaselineFilePath();

    [Fact]
    public async Task Opening_a_full_board_a_single_change_and_throughput_stay_within_the_budgets()
    {
        factory.Clock.Reset();

        var accounts = await Task.WhenAll(
            Enumerable.Range(0, Accounts).Select(index => SeedAccountAsync(index)));

        // ---- opening a full board: a handful of timed reads per account, after an untimed warm-up.
        var openSamples = new ConcurrentBag<double>();
        await Task.WhenAll(accounts.Select(async seeded =>
        {
            await seeded.Client.GetAsync($"{Boards}/{seeded.HeavyBoard.Id}");

            for (var sample = 0; sample < 3; sample++)
            {
                var elapsed = Stopwatch.StartNew();
                var response = await seeded.Client.GetAsync($"{Boards}/{seeded.HeavyBoard.Id}");
                elapsed.Stop();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                openSamples.Add(elapsed.Elapsed.TotalMilliseconds);
            }
        }));

        // ---- the mixed-change workload: 60 s of an even mix of the eight §6 change kinds, on a
        // board of its own, each account paced under the per-account rate limit.
        var changeSamples = new ConcurrentBag<double>();
        var workload = Stopwatch.StartNew();

        var perAccountCounts = await Task.WhenAll(accounts.Select(seeded =>
            RunMixedChangeWorkloadAsync(seeded, workload, changeSamples)));

        workload.Stop();

        var changeCount = perAccountCounts.Sum();
        var changesPerSecond = changeCount / workload.Elapsed.TotalSeconds;

        // Under the frozen clock every attempt of the run shares one rate-limit window: the bound
        // the workload relies on, asserted (see MinCyclePeriod).
        var mostAttempts = perAccountCounts.Max() + SetupChangesPerAccount;
        Assert.True(
            mostAttempts <= Api.Boards.BoardChangeRateLimit.PermittedPerWindow,
            $"an account made {mostAttempts} change attempts in one frozen-clock window, over the "
            + $"{Api.Boards.BoardChangeRateLimit.PermittedPerWindow} the limit admits — the workload is miscalibrated.");

        // ---- assert -------------------------------------------------------------------------------

        var p95Open = Percentile95(openSamples);
        var p95Change = Percentile95(changeSamples);

        var onReferenceMachine =
            Environment.GetEnvironmentVariable(ReferenceMachineFlag) is "1" or "true";

        if (onReferenceMachine)
        {
            Assert.True(
                p95Open <= OpenBudgetMs,
                $"p95 to open a 20-column, 1,000-card board was {p95Open:F0} ms, against the spec "
                + $"§6 budget of {OpenBudgetMs:F0} ms on the reference machine.");
            Assert.True(
                p95Change <= ChangeBudgetMs,
                $"p95 for a single change was {p95Change:F0} ms, against the spec §6 budget of "
                + $"{ChangeBudgetMs:F0} ms on the reference machine.");
            Assert.True(
                changesPerSecond >= ThroughputFloorPerSecond,
                $"throughput was {changesPerSecond:F1} changes/s, against the spec §6 target of "
                + $"{ThroughputFloorPerSecond:F0}/s on the reference machine.");
        }

        AssertAgainstBaselineOrRecord(
            BaselinePath,
            new BoardLatencyBaseline(p95Open, p95Change, changesPerSecond),
            record: Environment.GetEnvironmentVariable(RecordFlag) is "1" or "true");
    }

    // ---- T27 (review Q3): the baseline is the last recorded run — compared with, never loosened -----

    private static readonly BoardLatencyBaseline Recorded = new(OpenP95Ms: 100, ChangeP95Ms: 100, ChangesPerSecond: 50);

    [Theory]
    [InlineData(126, 90, 55)]
    [InlineData(90, 126, 55)]
    [InlineData(90, 90, 39)]
    public void A_regressed_run_fails_and_leaves_the_recorded_baseline_unchanged(
        double openP95, double changeP95, double changesPerSecond)
    {
        var path = AScratchBaseline(Recorded);
        try
        {
            var before = File.ReadAllText(path);

            // Even a run asked to record must not lower its own bar by failing.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertAgainstBaselineOrRecord(
                path, new BoardLatencyBaseline(openP95, changeP95, changesPerSecond), record: true));

            Assert.Equal(before, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_passing_ordinary_run_does_not_rewrite_the_committed_baseline()
    {
        var path = AScratchBaseline(Recorded);
        try
        {
            var before = File.ReadAllText(path);

            // Slower than recorded, but inside the 25% tolerance: a pass, and nothing written.
            AssertAgainstBaselineOrRecord(path, new BoardLatencyBaseline(120, 120, 42), record: false);

            Assert.Equal(before, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_passing_run_asked_to_record_writes_its_own_figures_not_a_ratchet_of_old_and_new()
    {
        var path = AScratchBaseline(Recorded);
        try
        {
            var current = new BoardLatencyBaseline(OpenP95Ms: 110, ChangeP95Ms: 80, ChangesPerSecond: 60);

            AssertAgainstBaselineOrRecord(path, current, record: true);

            Assert.Equal(current, ReadBaseline(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void With_no_baseline_yet_a_run_passes_and_only_a_recording_run_writes_one(bool record)
    {
        var path = Path.Combine(Path.GetTempPath(), $"board-latency-{Guid.NewGuid():N}.json");
        try
        {
            var current = new BoardLatencyBaseline(OpenP95Ms: 110, ChangeP95Ms: 80, ChangesPerSecond: 60);

            AssertAgainstBaselineOrRecord(path, current, record);

            Assert.Equal(record, File.Exists(path));
            if (record)
            {
                Assert.Equal(current, ReadBaseline(path));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_committed_baseline_is_there_for_ci_to_compare_against()
    {
        Assert.NotNull(ReadBaseline(BaselinePath));
    }

    private static string AScratchBaseline(BoardLatencyBaseline baseline)
    {
        var path = Path.Combine(Path.GetTempPath(), $"board-latency-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(baseline));
        return path;
    }

    // ---- workload: one account's 60 s of the eight change kinds, in a fixed round-robin order that
    // keeps the work board's column and card counts stable across every full cycle -----------------

    private static async Task<int> RunMixedChangeWorkloadAsync(
        SeededAccount seeded,
        Stopwatch workload,
        ConcurrentBag<double> changeSamples)
    {
        var state = await FetchWorkBoardStateAsync(seeded);
        var cycle = 0;
        var localChangeCount = 0;

        while (workload.Elapsed < TimeSpan.FromSeconds(WorkloadSeconds))
        {
            var cycleElapsed = Stopwatch.StartNew();

            // 1: addColumn
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.PostAsJsonAsync(
                    Columns(seeded.WorkBoard.Id), new { name = $"Smoke {cycle}" });
                AssertSucceeded(response, "addColumn");

                var body = await BodyAsync(response);
                state.NewColumnId = Guid.Parse(body.GetProperty("column").GetProperty("id").GetString()!);
                state.ColumnLayoutVersion = body.GetProperty("column_layout_version").GetInt32();
                return response;
            });

            // 2: renameColumn
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.PatchAsJsonAsync(
                    Column(seeded.WorkBoard.Id, state.RenameColumnId),
                    new { name = $"Renamed {cycle}", name_version = state.RenameColumnNameVersion });
                AssertSucceeded(response, "renameColumn");

                var body = await BodyAsync(response);
                state.RenameColumnNameVersion = body.GetProperty("name_version").GetInt32();
                return response;
            });

            // 3: moveColumn
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                state.MoveTargetPosition = state.MoveTargetPosition == 2 ? 5 : 2;
                var response = await seeded.Client.PutAsJsonAsync(
                    Position(seeded.WorkBoard.Id, state.MoveColumnId),
                    new { position = state.MoveTargetPosition, column_layout_version = state.ColumnLayoutVersion });
                AssertSucceeded(response, "moveColumn");

                var body = await BodyAsync(response);
                state.ColumnLayoutVersion = body.GetProperty("column_layout_version").GetInt32();
                return response;
            });

            // 4: deleteColumn — removes the column added in step 1, so the board's column count
            // returns to its seeded 19 by the end of the cycle.
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.SendAsync(new HttpRequestMessage(
                    HttpMethod.Delete, Column(seeded.WorkBoard.Id, state.NewColumnId))
                {
                    Content = JsonContent.Create(new { name_version = 1 }),
                });
                AssertSucceeded(response, "deleteColumn");
                return response;
            });

            // 5: addCard
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.PostAsJsonAsync(
                    Cards(seeded.WorkBoard.Id),
                    new { column_id = state.CardsColumnId, title = $"Smoke card {cycle}" });
                AssertSucceeded(response, "addCard");

                var body = await BodyAsync(response);
                state.NewCardId = Guid.Parse(body.GetProperty("id").GetString()!);
                return response;
            });

            // 6: editCard
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.PatchAsJsonAsync(
                    Card(seeded.WorkBoard.Id, state.StableCardId),
                    new
                    {
                        title = $"Edited {cycle}",
                        description = "Touched by the smoke run",
                        content_version = state.StableCardContentVersion,
                    });
                AssertSucceeded(response, "editCard");

                var body = await BodyAsync(response);
                state.StableCardContentVersion = body.GetProperty("content_version").GetInt32();
                return response;
            });

            // 7: deleteCard — removes the card added in step 5, so the board's card count returns
            // to its seeded 999 by the end of the cycle.
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.SendAsync(new HttpRequestMessage(
                    HttpMethod.Delete, Card(seeded.WorkBoard.Id, state.NewCardId))
                {
                    Content = JsonContent.Create(new { content_version = 1 }),
                });
                AssertSucceeded(response, "deleteCard");
                return response;
            });

            // 8: renameBoard
            localChangeCount += await TimedChangeAsync(changeSamples, async () =>
            {
                var response = await seeded.Client.PatchAsJsonAsync(
                    $"{Boards}/{seeded.WorkBoard.Id}", new { name = $"Board {cycle}" });
                AssertSucceeded(response, "renameBoard");
                return response;
            });

            cycle++;
            cycleElapsed.Stop();

            var remaining = MinCyclePeriod - cycleElapsed.Elapsed;
            if (remaining > TimeSpan.Zero && workload.Elapsed < TimeSpan.FromSeconds(WorkloadSeconds))
            {
                await Task.Delay(remaining);
            }
        }

        return localChangeCount;
    }

    private static async Task<int> TimedChangeAsync(
        ConcurrentBag<double> changeSamples, Func<Task<HttpResponseMessage>> change)
    {
        var elapsed = Stopwatch.StartNew();
        await change();
        elapsed.Stop();

        changeSamples.Add(elapsed.Elapsed.TotalMilliseconds);
        return 1;
    }

    private static void AssertSucceeded(HttpResponseMessage response, string changeKind)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Assert.Fail(
                $"{changeKind} was refused boards.change_rate_limited during the smoke workload — "
                + "the workload paces every account under 120 changes/minute (2/s); a run that trips "
                + "the limit is miscalibrated, not a real measurement (edge case table).");
        }

        Assert.True(
            response.IsSuccessStatusCode,
            $"{changeKind} returned {(int)response.StatusCode} during the smoke workload, which the "
            + "workload does not expect from a board it seeded and paces itself.");
    }

    // ---- setup: one account, its own heavy (20-column, 1,000-card) board for open-latency sampling,
    // and its own work (19-column, 999-card) board for the mixed-change workload ---------------------

    private async Task<SeededAccount> SeedAccountAsync(int index)
    {
        var account = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync(account);

        var heavyBoard = await factory.ABoardWithColumnsAsync(
            account, Board.MaxColumns, name: $"Heavy {index}");
        await InsertCardsAsync(heavyBoard.Id, Board.MaxCards);

        var workBoard = await factory.ABoardWithColumnsAsync(
            account, Board.MaxColumns - 1, name: $"Work {index}");
        await InsertCardsAsync(workBoard.Id, Board.MaxCards - 1);

        return new SeededAccount(client, heavyBoard, workBoard);
    }

    /// <summary>
    /// Inserts <paramref name="cardCount"/> filler cards into <paramref name="boardId"/>'s first
    /// column with a single batched statement — a per-row round trip for the 1,000-card heavy board
    /// would dominate the setup time of a run this workload already spends 60 s on.
    /// </summary>
    private async Task InsertCardsAsync(Guid boardId, int cardCount)
    {
        if (cardCount == 0)
        {
            return;
        }

        var columnId = await factory.ScalarAsync<Guid>(
            $"SELECT TOP 1 [Id] FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' ORDER BY [Position]");

        var rows = string.Join(
            ",\n",
            Enumerable.Range(0, cardCount).Select(position =>
                $"('{Guid.CreateVersion7()}', '{boardId}', '{columnId}', {position}, "
                + $"N'Filler {position}', N'', 1)"));

        await factory.ExecuteAsync(
            $"""
            INSERT INTO [dbo].[Cards]
                ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
            VALUES
            {rows};
            """);

        await factory.ExecuteAsync(
            $"""
            UPDATE [dbo].[Columns] SET [CardCount] = {cardCount}, [NextCardPosition] = {cardCount}
            WHERE [Id] = '{columnId}';
            """);
        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Boards] SET [CardCount] = {cardCount} WHERE [Id] = '{boardId}';");
    }

    /// <summary>
    /// Reads the work board's current state through the real API (not the store directly), so the
    /// workload starts from exactly the versions and ids a member's client would see.
    /// </summary>
    private static async Task<WorkBoardState> FetchWorkBoardStateAsync(SeededAccount seeded)
    {
        var response = await seeded.Client.GetAsync($"{Boards}/{seeded.WorkBoard.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);

        var columns = body.GetProperty("columns").EnumerateArray()
            .OrderBy(column => column.GetProperty("position").GetInt32())
            .ToArray();
        var cards = body.GetProperty("cards").EnumerateArray().ToArray();

        return new WorkBoardState
        {
            ColumnLayoutVersion = body.GetProperty("column_layout_version").GetInt32(),
            RenameColumnId = Guid.Parse(columns[1].GetProperty("id").GetString()!),
            RenameColumnNameVersion = columns[1].GetProperty("name_version").GetInt32(),
            MoveColumnId = Guid.Parse(columns[2].GetProperty("id").GetString()!),
            MoveTargetPosition = columns[2].GetProperty("position").GetInt32(),
            CardsColumnId = Guid.Parse(columns[0].GetProperty("id").GetString()!),
            StableCardId = Guid.Parse(cards[0].GetProperty("id").GetString()!),
            StableCardContentVersion = cards[0].GetProperty("content_version").GetInt32(),
            NewColumnId = Guid.Empty,
            NewCardId = Guid.Empty,
        };
    }

    // ---- CI regression check: 25% tolerance against the last recorded run (sad.md §10 QG-3) --------

    /// <summary>
    /// Compares <paramref name="current"/> with the last recorded run at
    /// <paramref name="baselinePath"/>, if there is one, and only then — every assertion having
    /// passed — writes <paramref name="current"/> as the new record, and only when
    /// <paramref name="record"/> asks for it (review Q3).
    /// </summary>
    private static void AssertAgainstBaselineOrRecord(string baselinePath, BoardLatencyBaseline current, bool record)
    {
        var (p95Open, p95Change, changesPerSecond) = current;

        if (ReadBaseline(baselinePath) is { } previous)
        {
            Assert.False(
                p95Open > previous.OpenP95Ms * RegressionTolerance,
                $"p95 to open a full board was {p95Open:F0} ms, more than {RegressionTolerance}x the last "
                + $"recorded {previous.OpenP95Ms:F0} ms.");
            Assert.False(
                p95Change > previous.ChangeP95Ms * RegressionTolerance,
                $"p95 for a single change was {p95Change:F0} ms, more than {RegressionTolerance}x the last "
                + $"recorded {previous.ChangeP95Ms:F0} ms.");
            Assert.False(
                changesPerSecond < previous.ChangesPerSecond / RegressionTolerance,
                $"throughput was {changesPerSecond:F1} changes/s, more than {RegressionTolerance}x slower "
                + $"than the last recorded {previous.ChangesPerSecond:F1} changes/s.");
        }

        if (record)
        {
            WriteBaseline(baselinePath, current);
        }
    }

    private static BoardLatencyBaseline? ReadBaseline(string baselinePath)
    {
        if (!File.Exists(baselinePath))
        {
            return null;
        }

        var json = File.ReadAllText(baselinePath);
        return JsonSerializer.Deserialize<BoardLatencyBaseline>(json);
    }

    private static void WriteBaseline(string baselinePath, BoardLatencyBaseline baseline)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
        File.WriteAllText(baselinePath, JsonSerializer.Serialize(baseline));
    }

    private static string BaselineFilePath([CallerFilePath] string sourcePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourcePath)!, ".baselines", "board-latency-budget.json");

    private static double Percentile95(ConcurrentBag<double> samples)
    {
        var ordered = samples.OrderBy(sample => sample).ToArray();
        var index = (int)Math.Ceiling(ordered.Length * 0.95) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    // ---- routes --------------------------------------------------------------------------------

    private const string Boards = "/api/v1/boards";

    private static string Columns(Guid boardId) => $"{Boards}/{boardId}/columns";

    private static string Column(Guid boardId, Guid columnId) => $"{Boards}/{boardId}/columns/{columnId}";

    private static string Position(Guid boardId, Guid columnId) =>
        $"{Boards}/{boardId}/columns/{columnId}/position";

    private static string Cards(Guid boardId) => $"{Boards}/{boardId}/cards";

    private static string Card(Guid boardId, Guid cardId) => $"{Boards}/{boardId}/cards/{cardId}";

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ---- types ----------------------------------------------------------------------------------

    private sealed record SeededAccount(HttpClient Client, TestBoard HeavyBoard, TestBoard WorkBoard);

    private sealed class WorkBoardState
    {
        public int ColumnLayoutVersion { get; set; }
        public Guid RenameColumnId { get; set; }
        public int RenameColumnNameVersion { get; set; }
        public Guid MoveColumnId { get; set; }
        public int MoveTargetPosition { get; set; }
        public Guid CardsColumnId { get; set; }
        public Guid StableCardId { get; set; }
        public int StableCardContentVersion { get; set; }
        public Guid NewColumnId { get; set; }
        public Guid NewCardId { get; set; }
    }

    private sealed record BoardLatencyBaseline(double OpenP95Ms, double ChangeP95Ms, double ChangesPerSecond);
}
