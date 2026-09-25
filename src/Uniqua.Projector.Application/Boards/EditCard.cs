using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-13 / AC-14 / AC-18b / AC-23 / AC-24b: the member-scoped load first, then
/// <see cref="IBoardStore.FindCardAsync"/> scoped to that board, then <see cref="Card.Edit"/> — a
/// lost write (the card moved on since <c>seenContentVersion</c> was seen) reloads and answers the
/// ordinary <see cref="BoardErrors.CardChanged"/> with the card as it now stands (ADR 0016; sad.md
/// §6, flow 2).
/// </summary>
/// <remarks>
/// An edit changes only the card row, whose <c>ContentVersion</c> is its concurrency token, so no
/// board row is written. A lost write is re-run under <see cref="BoardChangeRetry"/> in a fresh
/// scope: the reload sees the winner's <c>ContentVersion</c>, and it is the Card, not this use case,
/// that then answers "changed since you opened it".
/// </remarks>
public sealed class EditCard(IServiceScopeFactory scopes)
{
    public async Task<Result<CardView, BoardError>> ExecuteAsync(
        Guid boardId,
        Guid accountId,
        Guid cardId,
        string? title,
        string? description,
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
                return Result<CardView, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var card = await boards.FindCardAsync(loaded.Board.Id, cardId, cancellationToken);
            if (card is null)
            {
                return Result<CardView, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var edited = card.Edit(title, description, seenContentVersion);
            if (!edited.IsSuccess)
            {
                return Result<CardView, BoardError>.Failure(edited.Error!);
            }

            await boards.SaveAsync(cancellationToken);
            return Result<CardView, BoardError>.Success(CardViews.Of(card));
        });

        return outcome.Contended
            ? Result<CardView, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
