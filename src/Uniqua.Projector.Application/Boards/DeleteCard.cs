using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-18 / AC-18b / AC-23 / AC-26: the member-scoped load first, then
/// <see cref="IBoardStore.FindCardAsync"/> scoped to that board, then
/// <see cref="Card.EnsureDeletable"/> and <see cref="Board.RemoveCard"/> — deleted conditionally on
/// the card's <c>ContentVersion</c>, the board saved under its token, inside
/// <see cref="BoardChangeRetry"/> (sad.md §6, flow 10). The other cards in the column keep their
/// relative order and their positions untouched (ADR 0017).
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board rather than getting
/// back the tracked instance the lost attempt already changed (see <see cref="CreateBoard"/>).
/// </remarks>
public sealed class DeleteCard(IServiceScopeFactory scopes)
{
    public async Task<Result<bool, BoardError>> ExecuteAsync(
        Guid boardId,
        Guid accountId,
        Guid cardId,
        int seenContentVersion,
        CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
            if (loaded is null)
            {
                return Result<bool, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var card = await boards.FindCardAsync(loaded.Board.Id, cardId, cancellationToken);
            if (card is null)
            {
                return Result<bool, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var deletable = card.EnsureDeletable(seenContentVersion);
            if (!deletable.IsSuccess)
            {
                return deletable;
            }

            var removed = loaded.Board.RemoveCard(card);
            if (!removed.IsSuccess)
            {
                return removed;
            }

            boards.RemoveCard(card);
            await boards.SaveAsync(cancellationToken);
            return Result<bool, BoardError>.Success(true);
        });

        return outcome.Contended
            ? Result<bool, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
