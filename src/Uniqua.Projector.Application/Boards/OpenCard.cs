using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-18b / AC-23 / AC-26 / ADR 0018: the member-scoped load first, then
/// <see cref="IBoardStore.FindCardAsync"/> scoped to that board — a card id from another board, or a
/// deleted card's id, answers <see cref="BoardErrors.NotAvailable"/> exactly alike — and the card's
/// current full text (never a stale summary) is what AC-23's comparison is made against.
/// </summary>
public sealed class OpenCard(IBoardStore boards)
{
    public async Task<Result<CardView, BoardError>> ExecuteAsync(
        Guid boardId, Guid accountId, Guid cardId, CancellationToken cancellationToken)
    {
        var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
        if (loaded is null)
        {
            return Result<CardView, BoardError>.Failure(BoardErrors.NotAvailable);
        }

        var card = await boards.FindCardAsync(loaded.Board.Id, cardId, cancellationToken);
        return card is null
            ? Result<CardView, BoardError>.Failure(BoardErrors.NotAvailable)
            : Result<CardView, BoardError>.Success(CardViews.Of(card));
    }
}
