using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-20 / AC-20b / AC-22 / AC-25: the member-scoped load first, then <see cref="Board.ConfirmDeletion"/>
/// — owner check, then the typed name compared to the board's current one, carrying that current
/// name on a mismatch (AC-20b, "including because the board was renamed since"). Only once both
/// pass are the board (with its columns, cards and memberships, in one store-level delete) and the
/// owned-board counter's release saved together, inside <see cref="BoardChangeRetry"/> (sad.md §6,
/// flow 11).
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own so a lost race reloads the board and the counter rather
/// than getting back the tracked instances the lost attempt already changed (see
/// <see cref="CreateBoard"/>).
/// </remarks>
public sealed class DeleteBoard(IServiceScopeFactory scopes)
{
    public async Task<Result<bool, BoardError>> ExecuteAsync(
        Guid boardId, Guid accountId, string typedName, CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            return await AttemptAsync(scope.ServiceProvider, boardId, accountId, typedName, cancellationToken);
        });

        return outcome.Contended
            ? Result<bool, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }

    private static async Task<Result<bool, BoardError>> AttemptAsync(
        IServiceProvider services,
        Guid boardId,
        Guid accountId,
        string typedName,
        CancellationToken cancellationToken)
    {
        var boards = services.GetRequiredService<IBoardStore>();
        var counters = services.GetRequiredService<IOwnedBoardCounterStore>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var loaded = await boards.LoadForMemberAsync(boardId, accountId, cancellationToken);
        if (loaded is null)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.NotAvailable);
        }

        var confirmed = loaded.Board.ConfirmDeletion(accountId, typedName);
        if (!confirmed.IsSuccess)
        {
            return confirmed;
        }

        // ConfirmDeletion has established the caller is the owner, so the slot freed is theirs.
        var counter = await counters.LoadOrCreateAsync(accountId, cancellationToken);
        counter.Release();
        boards.Remove(loaded.Board);

        await unitOfWork.ExecuteAsync(
            async token =>
            {
                await counters.SaveAsync(token);
                await boards.SaveAsync(token);
                return true;
            },
            cancellationToken);

        return confirmed;
    }
}
