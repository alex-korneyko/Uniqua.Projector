using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-19 / AC-22 / AC-25: the member-scoped load first (so a non-member is refused
/// <see cref="BoardErrors.NotAvailable"/> before anything about the board is revealed), then
/// <see cref="Board.Rename"/> — which checks ownership before the Text rule, so a Member is refused
/// <see cref="BoardErrors.OwnerOnly"/> even for a name that would otherwise be invalid — then a save
/// retried under <see cref="BoardChangeRetry"/> (ADR 0015; sad.md §6, flow 2). Answers with the
/// name the Board stored — trimmed by the Text rule there, not re-derived by the caller (review Q4g).
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board rather than getting
/// back the tracked instance the lost attempt already changed (see <see cref="CreateBoard"/>).
/// </remarks>
public sealed class RenameBoard(IServiceScopeFactory scopes)
{
    public async Task<Result<string, BoardError>> ExecuteAsync(
        Guid boardId, Guid accountId, string name, CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var boards = scope.ServiceProvider.GetRequiredService<IBoardStore>();

            var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
            if (loaded is null)
            {
                return Result<string, BoardError>.Failure(BoardErrors.NotAvailable);
            }

            var renamed = loaded.Board.Rename(accountId, name);
            if (!renamed.IsSuccess)
            {
                return Result<string, BoardError>.Failure(renamed.Error!);
            }

            await boards.SaveAsync(cancellationToken);
            return Result<string, BoardError>.Success(loaded.Board.Name);
        });

        return outcome.Contended
            ? Result<string, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }
}
