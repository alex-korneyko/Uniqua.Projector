using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-06b / AC-07 / AC-09 / AC-10b / AC-18b / AC-21: the member-scoped load first, then
/// <see cref="Board.DeleteColumn"/> — no role check — then a save retried under
/// <see cref="BoardChangeRetry"/>: a structural conflict on the board's row reloads and re-asks
/// whether this column may still go (sad.md §6, flow 3/8; ADR 0015), so the last-column invariant is
/// re-decided rather than let through when two deletes race.
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board rather than getting
/// back the tracked instance the lost attempt already changed (see <see cref="CreateBoard"/>).
/// </remarks>
public sealed class DeleteColumn(IServiceScopeFactory scopes)
{
    public async Task<Result<ColumnLayout, BoardError>> ExecuteAsync(
        Guid boardId,
        Guid accountId,
        Guid columnId,
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
                return Result<ColumnLayout, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var deleted = loaded.Board.DeleteColumn(columnId, seenNameVersion);
            if (!deleted.IsSuccess)
            {
                return Result<ColumnLayout, BoardError>.Failure(deleted.Error!);
            }

            await boards.SaveAsync(cancellationToken);
            return Result<ColumnLayout, BoardError>.Success(ColumnViews.LayoutOf(loaded.Board));
        });

        return outcome.Contended
            ? Result<ColumnLayout, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
