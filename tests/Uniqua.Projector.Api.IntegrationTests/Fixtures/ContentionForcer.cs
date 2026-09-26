using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// Forces <c>IdentityAccountStore.RecordFailureAsync</c>'s compare-and-set to lose on every
/// attempt, so the contention test can drive it into the blind-increment fallback deterministically
/// instead of by racing real concurrent workers against a clock (Q-13a: the old test depended on
/// real timing, could fail with correct code — it did once in the gate run that raised the finding
/// — and never actually confirmed the fallback had run at all).
/// </summary>
/// <remarks>
/// Registered as an EF Core <see cref="IInterceptor"/> the same way <see cref="CommandRecorder"/>
/// is. When <see cref="Enabled"/>, every conditional compare-and-set UPDATE the retry loop sends is
/// let through only after this bumps the row itself, on a separate connection, so the predicate the
/// application's own UPDATE carries can never still match — the row moved out from under it before
/// it runs. <see cref="BumpsRemaining"/> bounds how many times it does so, and
/// <see cref="RaceAsync{T}"/> forces two racing board changes to collide by holding the first one's
/// save until the other has committed.
/// </remarks>
public sealed class ContentionForcer : DbCommandInterceptor
{
    // IdentityAccountStore.RecordFailureAsync's retry loop issues one UPDATE per attempt, filtered
    // on [Id] AND [AccessFailedCount] AND [LastFailedAttemptAt] — the compare-and-set. The
    // fallback's blind increment, reached only once the retry budget is spent, filters on [Id]
    // alone. That difference in shape is what lets this tell the two apart without depending on
    // which attempt number it is.
    private static readonly Regex ConditionalUpdate = new(
        @"UPDATE.*\[AspNetUsers\].*WHERE.*\[Id\].*AND.*\[AccessFailedCount\].*AND.*\[LastFailedAttemptAt\]",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The four board-feature tables data-model.md § Notes for implement names: each carries its own
    /// concurrency token (<c>RowVersion</c> on Boards and OwnedBoardCounters, <c>NameVersion</c> on
    /// Columns, <c>ContentVersion</c> on Cards), and EF Core's own conditional UPDATE against any of
    /// them is what this bumps out from under the caller. The model sets no default schema, so EF
    /// Core writes the bare table name; the schema prefix is accepted but not required.
    /// </summary>
    private static readonly Regex BoardRowUpdate = new(
        @"UPDATE\s+(?:\[dbo\]\.)?\[(?<table>Boards|Columns|Cards|OwnedBoardCounters)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string? ConnectionString { get; set; }

    public bool Enabled { get; set; }

    public Guid TargetAccountId { get; set; }

    /// <summary>
    /// The row a board, column, card or owned-board-counter UPDATE targets, for
    /// <see cref="BoardRowUpdate"/>. A board, column or card id for the first three tables; an
    /// account id for <c>OwnedBoardCounters</c>, which is keyed by <c>AccountId</c> rather than
    /// <c>Id</c>.
    /// </summary>
    public Guid TargetId { get; set; }

    /// <summary>
    /// How many times a matched UPDATE was seen and bumped out from under itself. The contention
    /// test compares this against the retry budget the store or the retry helper is known to use, so
    /// the test fails loudly if the interceptor ever stops matching the SQL shape it depends on.
    /// </summary>
    public int InterceptedAttempts { get; private set; }

    private Regex? _oncePattern;
    private string? _onceSql;

    /// <summary>Whether the statement armed by <see cref="RunOnceBefore"/> has run.</summary>
    public bool RanOnce { get; private set; }

    /// <summary>
    /// Arms a one-shot: just before the first command whose text matches <paramref name="pattern"/>,
    /// runs <paramref name="sql"/> on a separate connection, committed — someone else's change landing
    /// between a use case's load and its save — then disarms. Works whether or not
    /// <see cref="Enabled"/> is set.
    /// </summary>
    public void RunOnceBefore(string pattern, string sql)
    {
        _oncePattern = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        _onceSql = sql;
        RanOnce = false;
    }

    /// <summary>
    /// How many more effective bumps <see cref="Enabled"/> may make — one that actually moved the
    /// targeted row — before it lets every write through untouched; <see langword="null"/> (the
    /// default) is unlimited, which is what a spent-retry-budget test needs. A budget of 1 forces a
    /// single lost race and then lets the retry through, so the retry has to re-decide rather than be
    /// bumped into <c>boards.contended</c> like every attempt of both racers was (review Q2a).
    /// </summary>
    public int? BumpsRemaining { get; set; }

    /// <summary>Both racers' results, in the order given, and whether they were forced to collide.</summary>
    public sealed record Race<T>(T First, T Second, bool Collided);

    private TaskCompletionSource? _gate;
    private int _holdTaken;
    private int _gatedWrites;

    /// <summary>How long a held write may wait for the other racer before it is let go regardless.</summary>
    public static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs two racers at once and forces them to collide deterministically (review Q2a, Q6): the
    /// first board-row write either sends — the conditional UPDATE against Boards, Columns, Cards or
    /// OwnedBoardCounters — is held back until the other racer has finished, so that other racer's
    /// change commits between the held racer's load and its save. The held save then always meets a
    /// row that moved under it and must lose, and its retry must reload and re-decide.
    /// </summary>
    /// <returns>
    /// <see cref="Race{T}.Collided"/> is <see langword="true"/> only when a write was held and the other
    /// racer wrote and committed while it was held — the collision the race is named for, rather than
    /// two changes that merely ran side by side.
    /// </returns>
    /// <remarks>
    /// The held write is the first statement its transaction takes a lock on any row the other
    /// racer touches (an UPDATE is only preceded by inserts of rows of its own), so holding it cannot
    /// deadlock the other racer; <see cref="HoldTimeout"/> guards against that anyway.
    /// </remarks>
    public async Task<Race<T>> RaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _holdTaken = 0;
        _gatedWrites = 0;
        _gate = gate;

        var a = Task.Run(first);
        var b = Task.Run(second);

        // The held racer cannot finish while held, so the first to finish is the other one.
        await Task.WhenAny(Task.WhenAny(a, b), Task.Delay(HoldTimeout));
        var collided = Volatile.Read(ref _holdTaken) == 1
            && Volatile.Read(ref _gatedWrites) >= 2
            && (a.IsCompleted || b.IsCompleted);

        _gate = null;
        gate.TrySetResult();

        await Task.WhenAll(a, b);
        return new Race<T>(await a, await b, collided);
    }

    private async Task HoldIfFirstGatedWriteAsync(DbCommand command)
    {
        if (_gate is not { } gate || !BoardRowUpdate.IsMatch(command.CommandText))
        {
            return;
        }

        Interlocked.Increment(ref _gatedWrites);
        if (Interlocked.CompareExchange(ref _holdTaken, 1, 0) == 0)
        {
            await gate.Task.WaitAsync(HoldTimeout);
        }
    }

    public void Reset()
    {
        Enabled = false;
        TargetAccountId = Guid.Empty;
        TargetId = Guid.Empty;
        InterceptedAttempts = 0;
        BumpsRemaining = null;
        _gate?.TrySetResult();
        _gate = null;
        _oncePattern = null;
        _onceSql = null;
        RanOnce = false;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await RunOnceIfMatchedAsync(command, cancellationToken);
        await HoldIfFirstGatedWriteAsync(command);

        if (Enabled && ConditionalUpdate.IsMatch(command.CommandText))
        {
            InterceptedAttempts++;

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var bump = connection.CreateCommand();
            bump.CommandText = $"""
                UPDATE [dbo].[AspNetUsers]
                SET [AccessFailedCount] = [AccessFailedCount] + 1,
                    [LastFailedAttemptAt] = SYSDATETIMEOFFSET()
                WHERE [Id] = '{TargetAccountId}';
                """;
            await bump.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await BumpBoardRowIfMatchedAsync(command, cancellationToken);
        }

        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <summary>
    /// A board or owned-board-counter UPDATE reads its new <c>rowversion</c> back
    /// (<c>OUTPUT INSERTED.[RowVersion]</c>), so EF Core sends it as a reader, not a non-query.
    /// </summary>
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await RunOnceIfMatchedAsync(command, cancellationToken);
        await HoldIfFirstGatedWriteAsync(command);
        await BumpBoardRowIfMatchedAsync(command, cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private async Task RunOnceIfMatchedAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (_oncePattern is not { } pattern || !pattern.IsMatch(command.CommandText))
        {
            return;
        }

        // Disarm before running, so two racing commands cannot both take the one shot.
        if (Interlocked.Exchange(ref _onceSql, null) is not { } sql)
        {
            return;
        }

        _oncePattern = null;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var once = connection.CreateCommand();
        once.CommandText = sql;
        await once.ExecuteNonQueryAsync(cancellationToken);
        RanOnce = true;
    }

    private async Task BumpBoardRowIfMatchedAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (!Enabled || BoardRowUpdate.Match(command.CommandText) is not { Success: true } match)
        {
            return;
        }

        if (BumpsRemaining is <= 0)
        {
            return;
        }

        InterceptedAttempts++;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var bump = connection.CreateCommand();
        bump.CommandText = BumpSql(match.Groups["table"].Value, TargetId);
        var moved = await bump.ExecuteNonQueryAsync(cancellationToken);

        if (moved > 0 && BumpsRemaining is { } remaining)
        {
            BumpsRemaining = remaining - 1;
        }
    }

    /// <summary>
    /// A harmless self-referencing UPDATE against the targeted row: SQL Server bumps a
    /// <c>rowversion</c> on any UPDATE that touches the row regardless of whether a value actually
    /// changed, and the two application-managed int tokens are bumped explicitly for the same
    /// reason — so whichever value EF Core's own UPDATE still expects can no longer match.
    /// </summary>
    private static string BumpSql(string table, Guid targetId) => table switch
    {
        "Boards" =>
            $"UPDATE [dbo].[Boards] SET [CardCount] = [CardCount] WHERE [Id] = '{targetId}';",
        "OwnedBoardCounters" =>
            $"UPDATE [dbo].[OwnedBoardCounters] SET [OwnedBoardCount] = [OwnedBoardCount] "
            + $"WHERE [AccountId] = '{targetId}';",
        "Columns" =>
            $"UPDATE [dbo].[Columns] SET [NameVersion] = [NameVersion] + 1 WHERE [Id] = '{targetId}';",
        "Cards" =>
            $"UPDATE [dbo].[Cards] SET [ContentVersion] = [ContentVersion] + 1 WHERE [Id] = '{targetId}';",
        _ => throw new InvalidOperationException($"No contention bump is defined for '{table}'."),
    };
}
