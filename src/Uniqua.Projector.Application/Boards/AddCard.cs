using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-12 / AC-14 / AC-15 / AC-18b: the member-scoped load first, then <see cref="Board.AdmitCard"/>,
/// then a save retried under <see cref="BoardChangeRetry"/> so the 1,000-card ceiling is re-decided
/// against state that has moved on when several adds race (sad.md §6, flow 9).
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board rather than getting
/// back the tracked instance the lost attempt already changed (see <see cref="CreateBoard"/>).
/// </remarks>
public sealed class AddCard(IServiceScopeFactory scopes)
{
    public async Task<Result<CardSummary, BoardError>> ExecuteAsync(
        Guid boardId,
        Guid accountId,
        Guid columnId,
        string title,
        string? description,
        CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
            if (loaded is null)
            {
                return Result<CardSummary, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var admitted = loaded.Board.AdmitCard(columnId, title, description);
            if (!admitted.IsSuccess)
            {
                return Result<CardSummary, BoardError>.Failure(admitted.Error!);
            }

            var card = admitted.Value;
            boards.AddCard(card);
            await boards.SaveAsync(cancellationToken);
            return Result<CardSummary, BoardError>.Success(
                new CardSummary(card.Id, card.ColumnId, card.Position, card.Title, card.ContentVersion));
        });

        return outcome.Contended
            ? Result<CardSummary, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
