using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// Records the SQL the application actually sends. Some of this feature's promises are about the
/// shape of a query rather than its answer — spec §6 budgets 30 ms to recognise a session on an
/// ordinary read, which is a promise that recognition is one primary-key lookup and not a join
/// that happens to be fast on an empty table. Only the statements can testify to that.
/// </summary>
/// <remarks>
/// Every integration test class shares one collection fixture, so xUnit runs them sequentially and
/// a single recorder can safely be reset and read inside one test.
/// </remarks>
public sealed class CommandRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _statements = new();

    public IReadOnlyCollection<string> Statements => [.. _statements];

    public void Clear() => _statements.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        _statements.Enqueue(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _statements.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        _statements.Enqueue(command.CommandText);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _statements.Enqueue(command.CommandText);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
