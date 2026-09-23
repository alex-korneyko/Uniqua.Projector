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

    public string? ConnectionString { get; set; }

    public bool Enabled { get; set; }

    public Guid TargetAccountId { get; set; }

    /// <summary>
    /// How many times the conditional UPDATE was seen and bumped out from under itself. The
    /// contention test compares this against the retry budget the store is known to use, so the
    /// test fails loudly if the interceptor ever stops matching the SQL shape it depends on.
    /// </summary>
    public int InterceptedAttempts { get; private set; }

    public void Reset()
    {
        Enabled = false;
        TargetAccountId = Guid.Empty;
        InterceptedAttempts = 0;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
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

        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
