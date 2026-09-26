using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Application.Boards;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain.Boards;
using Uniqua.Projector.Infrastructure.Boards.Configurations;

namespace Uniqua.Projector.Infrastructure.Boards;

/// <summary>
/// <see cref="IBoardStore"/> over <see cref="AppDbContext"/> (ADR 0014, ADR 0015). Every query that
/// reaches a board is filtered on the caller's membership or on the board a card must belong to;
/// none is unconditional. It logs nothing, so no board name, column name, card title or description
/// can reach a log line from here (sad.md §8).
/// </summary>
internal sealed class BoardStore(AppDbContext context) : IBoardStore
{
    private readonly List<Board> _removed = [];

    /// <summary>
    /// One statement: the board, its columns in position order and its memberships, joined to the
    /// caller's own membership (<c>IX_BoardMemberships_BoardId_AccountId</c>). A board the caller is
    /// not a member of and a board that does not exist both answer <c>null</c>.
    /// </summary>
    public async Task<MemberBoard?> LoadForMemberAsync(
        Guid boardId, Guid accountId, CancellationToken cancellationToken)
    {
        var board = await context.Boards
            .Where(candidate => candidate.Id == boardId
                && candidate.Memberships.Any(membership => membership.AccountId == accountId))
            .Include(candidate => candidate.Columns.OrderBy(column => column.Position))
            .Include(candidate => candidate.Memberships)
            .AsSingleQuery()
            .SingleOrDefaultAsync(cancellationToken);

        return board is null
            ? null
            : new MemberBoard(board, board.Memberships.Single(m => m.AccountId == accountId).Role);
    }

    /// <summary>
    /// Starts from the caller's memberships (<c>IX_BoardMemberships_AccountId_BoardId</c>, which
    /// covers the role) and reaches only those boards, newest first.
    /// </summary>
    public async Task<IReadOnlyList<BoardListEntry>> ListForAccountAsync(
        Guid accountId, CancellationToken cancellationToken) =>
        await context.Set<BoardMembership>()
            .Where(membership => membership.AccountId == accountId)
            .Join(
                context.Boards,
                membership => membership.BoardId,
                board => board.Id,
                (membership, board) => new { membership.Role, board.Id, board.Name, board.CreatedAt })
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new BoardListEntry(
                entry.Id, entry.Name, entry.CreatedAt, entry.Role == BoardRole.Owner))
            .ToListAsync(cancellationToken);

    /// <summary>Covered by <c>IX_Cards_BoardId_ColumnId_Position</c>: no description is read.</summary>
    public async Task<IReadOnlyList<CardSummary>> ReadCardSummariesAsync(
        Guid boardId, CancellationToken cancellationToken) =>
        await context.Cards
            .Where(card => card.BoardId == boardId)
            .OrderBy(card => card.ColumnId)
            .ThenBy(card => card.Position)
            .Select(card => new CardSummary(
                card.Id, card.ColumnId, card.Position, card.Title, card.ContentVersion))
            .ToListAsync(cancellationToken);

    public Task<Card?> FindCardAsync(Guid boardId, Guid cardId, CancellationToken cancellationToken) =>
        context.Cards.SingleOrDefaultAsync(
            card => card.Id == cardId && card.BoardId == boardId, cancellationToken);

    public void Add(Board board) => context.Boards.Add(board);

    public void Remove(Board board) => _removed.Add(board);

    public void AddCard(Card card) => context.Cards.Add(card);

    /// <summary>The delete carries <c>ContentVersion</c> as its concurrency token (ADR 0016).</summary>
    public void RemoveCard(Card card) => context.Cards.Remove(card);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);

            foreach (var board in _removed)
            {
                await DeleteAsync(board, cancellationToken);
            }

            _removed.Clear();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BoardConcurrencyConflict();
        }
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            // A card committed into a column this save deletes (or a column deleted under a card
            // this save inserts): the NO ACTION foreign key from Cards to Columns refuses the
            // statement. That is the same lost race a version miss is, so the bounded retry reloads
            // and the Board re-decides — column_not_empty, or the column is gone (AC-09, AC-18b;
            // review Q2b) — instead of the member getting a 500.
            throw new BoardConcurrencyConflict();
        }
    }

    /// <summary>SQL Server error 547: a statement conflicted with a FOREIGN KEY (or CHECK) constraint.</summary>
    private const int ConstraintViolation = 547;

    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: ConstraintViolation };

    /// <summary>
    /// data-model.md § Notes for implement: one <c>DELETE</c> against Boards, conditioned on the
    /// row version the board was loaded with, and the store's cascade removes its columns, cards
    /// and memberships — EF's change tracker never gets to delete a column before its cards.
    /// </summary>
    private async Task DeleteAsync(Board board, CancellationToken cancellationToken)
    {
        var entry = context.Entry(board);
        var loadedVersion = entry.Property<byte[]>(BoardConfiguration.RowVersion).OriginalValue;

        var deleted = await context.Boards
            .Where(candidate => candidate.Id == board.Id
                && EF.Property<byte[]>(candidate, BoardConfiguration.RowVersion) == loadedVersion)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            throw new BoardConcurrencyConflict();
        }

        // The rows are gone; stop tracking the graph so a later save cannot try to write it back.
        foreach (var column in board.Columns)
        {
            context.Entry(column).State = EntityState.Detached;
        }

        foreach (var membership in board.Memberships)
        {
            context.Entry(membership).State = EntityState.Detached;
        }

        entry.State = EntityState.Detached;
    }
}
