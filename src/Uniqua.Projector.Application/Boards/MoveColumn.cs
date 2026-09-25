using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-06 / AC-18b / AC-21: the member-scoped load first, then <see cref="Board.MoveColumn"/> — no
/// role check — then a save retried under <see cref="BoardChangeRetry"/> so a lost race on the
/// board's <c>ColumnLayoutVersion</c> reloads and re-decides against the layout as it now stands
/// (ADR 0015/0016; sad.md §6, flow 8).
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board rather than getting
/// back the tracked instance the lost attempt already changed (see <see cref="CreateBoard"/>).
/// </remarks>
public sealed class MoveColumn(IServiceScopeFactory scopes)
{
    public async Task<Result<ColumnLayout, BoardError>> ExecuteAsync(
        Guid boardId,
        Guid accountId,
        Guid columnId,
        int position,
        int seenLayoutVersion,
        CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
            if (loaded is null)
            {
                return Result<ColumnLayout, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var moved = loaded.Board.MoveColumn(columnId, position, seenLayoutVersion);
            if (!moved.IsSuccess)
            {
                return Result<ColumnLayout, BoardError>.Failure(moved.Error!);
            }

            await boards.SaveAsync(cancellationToken);
            return Result<ColumnLayout, BoardError>.Success(ColumnViews.LayoutOf(loaded.Board));
        });

        return outcome.Contended
            ? Result<ColumnLayout, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
