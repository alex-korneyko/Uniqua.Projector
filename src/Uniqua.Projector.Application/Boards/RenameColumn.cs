using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-06 / AC-06b / AC-18b / AC-21: the member-scoped load first (a column id from another board, or
/// a deleted one, answers <see cref="BoardErrors.NotAvailable"/> exactly alike), then
/// <see cref="Board.RenameColumn"/> — no role check — saved conditionally on the column's
/// <c>NameVersion</c>; a lost write reloads and answers the ordinary
/// <see cref="BoardErrors.ColumnRenamed"/> with the column as it now stands (ADR 0016; sad.md §6,
/// flow 7).
/// </summary>
/// <remarks>
/// A rename changes only the column row, whose <c>NameVersion</c> is its concurrency token, so no
/// board row is written. A lost write is re-run under <see cref="BoardChangeRetry"/>: the reload sees
/// the winner's <c>NameVersion</c>, and it is the Board, not this use case, that then answers
/// "renamed since you last saw it".
/// </remarks>
public sealed class RenameColumn(IServiceScopeFactory scopes)
{
    public async Task<Result<BoardColumnView, BoardError>> ExecuteAsync(
        Guid boardId,
        Guid accountId,
        Guid columnId,
        string name,
        int seenNameVersion,
        CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
            if (loaded is null)
            {
                return Result<BoardColumnView, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var renamed = loaded.Board.RenameColumn(columnId, name, seenNameVersion);
            if (!renamed.IsSuccess)
            {
                return Result<BoardColumnView, BoardError>.Failure(renamed.Error!);
            }

            await boards.SaveAsync(cancellationToken);
            return Result<BoardColumnView, BoardError>.Success(
                ColumnViews.Of(loaded.Board.FindColumn(columnId).Value));
        });

        return outcome.Contended
            ? Result<BoardColumnView, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
