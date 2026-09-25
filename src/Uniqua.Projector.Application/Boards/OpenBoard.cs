using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-04 / AC-25 / ADR 0014: the member-scoped load, or <see cref="BoardErrors.NotAvailable"/> for
/// an absent board and a board the caller is not a member of alike — the store's
/// <c>LoadForMemberAsync</c> already collapses those into the same <c>null</c>, so this use case
/// never has to tell them apart. A found board's columns and card summaries are read through it,
/// never by a second unscoped lookup (sad.md §6, flow 2).
/// </summary>
public sealed class OpenBoard(IBoardStore boards)
{
    public async Task<Result<BoardView, BoardError>> ExecuteAsync(
        Guid boardId, Guid accountId, CancellationToken cancellationToken)
    {
        var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
        if (loaded is null)
        {
            return Result<BoardView, BoardError>.Failure(BoardErrors.NotAvailable);
        }

        var cards = await boards.ReadCardSummariesAsync(loaded.Board.Id, cancellationToken);

        return Result<BoardView, BoardError>.Success(
            ToView(loaded.Board, loaded.Role == BoardRole.Owner, cards));
    }

    /// <summary>
    /// The board as the contract shows it: columns in position order, and cards ordered by their
    /// column's position then their own (contracts/openapi.yaml, components.schemas.Board).
    /// </summary>
    internal static BoardView ToView(Board board, bool isOwner, IReadOnlyList<CardSummary> cards)
    {
        var columns = board.Columns
            .OrderBy(column => column.Position)
            .Select(column => new BoardColumnView(column.Id, column.Name, column.Position, column.NameVersion))
            .ToList();

        var columnPositions = columns.ToDictionary(column => column.Id, column => column.Position);
        var orderedCards = cards
            .OrderBy(card => columnPositions.GetValueOrDefault(card.ColumnId))
            .ThenBy(card => card.Position)
            .ToList();

        return new BoardView(board.Id, board.Name, isOwner, board.ColumnLayoutVersion, columns, orderedCards);
    }
}
