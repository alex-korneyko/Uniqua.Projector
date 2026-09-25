namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// One row per account that has ever created a board (data-model.md § OwnedBoardCounters), owned by
/// Domain rather than by Identity because Identity's <c>ConcurrencyStamp</c> is not this
/// aggregate's to share (ADR 0015, Consequences). It is a root of its own: board creation admits
/// against it, and board deletion releases back into it.
/// </summary>
public sealed class OwnedBoardCounter
{
    /// <summary>AC-03. Each account's share of the product is bounded.</summary>
    public const int MaxOwnedBoards = 50;

    private OwnedBoardCounter(Guid accountId, int ownedBoardCount)
    {
        AccountId = accountId;
        OwnedBoardCount = ownedBoardCount;
    }

    public Guid AccountId { get; }

    public int OwnedBoardCount { get; private set; }

    /// <summary>The row as it is created lazily on an account's first board (data-model.md).</summary>
    public static OwnedBoardCounter ForNewAccount(Guid accountId) => new(accountId, 0);

    /// <summary>
    /// Reserves one slot for a new board, or refuses with <see cref="BoardErrors.OwnedBoardLimitReached"/>
    /// at the 51st — the limit that AC-03 says holds even when several boards are created at once,
    /// because this row is exactly what the optimistic-concurrency retry (ADR 0015) guards.
    /// </summary>
    public Result<bool, BoardError> Admit()
    {
        if (OwnedBoardCount >= MaxOwnedBoards)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.OwnedBoardLimitReached);
        }

        OwnedBoardCount++;
        return Result<bool, BoardError>.Success(true);
    }

    /// <summary>Frees the slot a deleted board held.</summary>
    /// <remarks>Never drops below zero, so a stray release cannot create extra headroom.</remarks>
    public void Release()
    {
        if (OwnedBoardCount > 0)
        {
            OwnedBoardCount--;
        }
    }
}
