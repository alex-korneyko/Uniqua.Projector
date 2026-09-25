using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-05 / AC-11 / AC-21: the member-scoped load first, then <see cref="Board.AddColumn"/> — no role
/// check, so a Member succeeds exactly as the Owner — then a save retried under
/// <see cref="BoardChangeRetry"/> so the 20-column ceiling is re-decided against state that has moved
/// on when several adds race (sad.md §6, flow 6; ADR 0015).
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board rather than getting
/// back the tracked instance the lost attempt already changed (see <see cref="CreateBoard"/>).
/// </remarks>
public sealed class AddColumn(IServiceScopeFactory scopes)
{
    public async Task<Result<AddedColumn, BoardError>> ExecuteAsync(
        Guid boardId, Guid accountId, string name, CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
            if (loaded is null)
            {
                return Result<AddedColumn, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var added = loaded.Board.AddColumn(name);
            if (!added.IsSuccess)
            {
                return Result<AddedColumn, BoardError>.Failure(added.Error!);
            }

            await boards.SaveAsync(cancellationToken);
            return Result<AddedColumn, BoardError>.Success(
                new AddedColumn(ColumnViews.Of(added.Value), loaded.Board.ColumnLayoutVersion));
        });

        return outcome.Contended
            ? Result<AddedColumn, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
