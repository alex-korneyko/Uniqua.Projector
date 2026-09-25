using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards.Ports;

/// <summary>
/// The per-account owned-board counter's persistence port (ADR 0015; data-model.md §
/// OwnedBoardCounters). The row is created lazily on an account's first board, so the load side
/// doubles as the create side.
/// </summary>
public interface IOwnedBoardCounterStore
{
    /// <summary>
    /// The account's counter, inserted at zero if this is its first board. Two callers racing this
    /// first insert both attempt it; the loser's primary-key violation is treated exactly like any
    /// other lost race — it re-reads the row the winner just created rather than surfacing a
    /// database failure, and only if that row still cannot be read does it raise a
    /// <see cref="Uniqua.Projector.Application.Boards.BoardConcurrencyConflict"/> for the retry.
    /// </summary>
    Task<OwnedBoardCounter> LoadOrCreateAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the counter change queued since the last call. A lost race on its version surfaces
    /// as a <see cref="Uniqua.Projector.Application.Boards.BoardConcurrencyConflict"/>.
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
