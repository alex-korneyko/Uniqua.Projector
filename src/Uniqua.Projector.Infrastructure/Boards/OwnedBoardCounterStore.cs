using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Infrastructure.Boards;

/// <summary>
/// <see cref="IOwnedBoardCounterStore"/> over <see cref="AppDbContext"/> (ADR 0015; data-model.md §
/// OwnedBoardCounters).
/// </summary>
internal sealed class OwnedBoardCounterStore(AppDbContext context) : IOwnedBoardCounterStore
{
    /// <summary>
    /// The row is inserted lazily on the account's first board. When two first creations race, the
    /// loser's primary-key violation is treated like any lost race: it re-reads the row the winner
    /// just inserted rather than surfacing a database failure. Should the re-read still find
    /// nothing, the race is reported as a <see cref="BoardConcurrencyConflict"/> for the caller's
    /// retry to re-decide.
    /// </summary>
    public async Task<OwnedBoardCounter> LoadOrCreateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(accountId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = OwnedBoardCounter.ForNewAccount(accountId);
        context.OwnedBoardCounters.Add(created);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException failure) when (IsDuplicateKey(failure))
        {
            context.Entry(created).State = EntityState.Detached;
        }

        return await FindAsync(accountId, cancellationToken) ?? throw new BoardConcurrencyConflict();
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BoardConcurrencyConflict();
        }
    }

    private Task<OwnedBoardCounter?> FindAsync(Guid accountId, CancellationToken cancellationToken) =>
        context.OwnedBoardCounters.SingleOrDefaultAsync(
            counter => counter.AccountId == accountId, cancellationToken);

    // 2601 duplicate key in a unique index, 2627 primary-key / unique constraint violation.
    private static bool IsDuplicateKey(DbUpdateException failure) =>
        failure.InnerException is SqlException { Number: 2601 or 2627 };
}
