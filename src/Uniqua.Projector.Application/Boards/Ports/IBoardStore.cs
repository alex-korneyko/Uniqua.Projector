using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards.Ports;

/// <summary>
/// The board aggregate's persistence port (ADR 0014, ADR 0015). Every method that reaches a board, a
/// column or a card does so scoped to the caller's own membership; there is no method that returns
/// any of them unconditionally, so a non-member cannot even confirm a board exists (AC-25) and a
/// column or card named from another board is never reached through this one (AC-26).
/// </summary>
public interface IBoardStore
{
    /// <summary>
    /// AC-25 / AC-26 (sad.md §5, ADR 0014). The one way a board is loaded: scoped to
    /// <paramref name="accountId"/>'s own membership, or <c>null</c> — identical to the board not
    /// existing at all. Everything a request names on that board (a column) is reached only by
    /// asking the returned board itself, never by a second, unscoped lookup.
    /// </summary>
    Task<MemberBoard?> LoadForMemberAsync(
        Guid boardId, Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// AC-04. Every board the account is a member of, most recently created first, each marked with
    /// whether the account owns it. Lists no board the account is not a member of.
    /// </summary>
    Task<IReadOnlyList<BoardListEntry>> ListForAccountAsync(
        Guid accountId, CancellationToken cancellationToken);

    /// <summary>The card summaries shown when a board opens (ADR 0018), read through the board.</summary>
    Task<IReadOnlyList<CardSummary>> ReadCardSummariesAsync(
        Guid boardId, CancellationToken cancellationToken);

    /// <summary>
    /// AC-26. A single card, found only when it belongs to <paramref name="boardId"/>
    /// (<c>Id = @card AND BoardId = @board</c>) — a card id that belongs to another board answers
    /// <c>null</c>, exactly as an absent card would.
    /// </summary>
    Task<Card?> FindCardAsync(Guid boardId, Guid cardId, CancellationToken cancellationToken);

    /// <summary>Registers a newly created board for insertion on the next <see cref="SaveAsync"/>.</summary>
    void Add(Board board);

    /// <summary>
    /// Registers a board for deletion. The implementation removes it, its columns, cards and
    /// memberships in one statement (data-model.md § Notes for implement) rather than letting EF's
    /// change tracker delete columns before their cards, so a board that still holds cards deletes
    /// cleanly instead of hitting the <c>NO ACTION</c> foreign key from Cards to Columns.
    /// </summary>
    void Remove(Board board);

    /// <summary>
    /// Registers a card the Board just admitted (<see cref="Board.AdmitCard"/>) for insertion on the
    /// next <see cref="SaveAsync"/>, alongside the board's and column's changed counters (sad.md §6,
    /// flow 9).
    /// </summary>
    void AddCard(Card card);

    /// <summary>
    /// Registers a card found through <see cref="FindCardAsync"/> for deletion on the next
    /// <see cref="SaveAsync"/>, conditional on the <c>ContentVersion</c> it was read at (sad.md §6,
    /// flow 10).
    /// </summary>
    void RemoveCard(Card card);

    /// <summary>
    /// Persists every change queued since the last call. A lost race on a board's, a column's, a
    /// card's or the owned-board counter's version — or a card and a column deletion meeting at the
    /// Cards-to-Columns foreign key — surfaces as a
    /// <see cref="Uniqua.Projector.Application.Boards.BoardConcurrencyConflict"/> rather than an EF
    /// Core exception type, so the exception never crosses into Application from anywhere but this
    /// port (ADR 0015; CLAUDE.md § Persistence is EF Core).
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>A board as loaded for one specific member: the aggregate and that member's own role.</summary>
public sealed record MemberBoard(Board Board, BoardRole Role);

/// <summary>AC-04's one entry in "my boards": no columns, no cards, just enough to list and open it.</summary>
public sealed record BoardListEntry(Guid Id, string Name, DateTimeOffset CreatedAt, bool IsOwner);

/// <summary>One row of a board's card list (ADR 0018).</summary>
public sealed record CardSummary(Guid Id, Guid ColumnId, int Position, string Title, int ContentVersion);
