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
/// it runs.
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

    public void Reset()
    {
        Enabled = false;
        TargetAccountId = Guid.Empty;
        TargetId = Guid.Empty;
        InterceptedAttempts = 0;
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

        InterceptedAttempts++;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var bump = connection.CreateCommand();
        bump.CommandText = BumpSql(match.Groups["table"].Value, TargetId);
        await bump.ExecuteNonQueryAsync(cancellationToken);
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
