using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Application.Boards.Ports;
using Uniqua.Projector.Domain;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-01 / AC-03: admit against the account's owned-board counter, ask <see cref="Board"/> to build
/// itself, then save both together — retried under <see cref="BoardChangeRetry"/> so a race between
/// two creations by the same account re-decides the 50-board ceiling against state that has moved on
/// (sad.md §6, flow 1; ADR 0015). A retry budget spent entirely on lost races is reported as
/// <see cref="BoardErrors.Contended"/> rather than as any of this use case's own refusals.
/// </summary>
/// <remarks>
/// Each attempt runs in a scope of its own, so it reads the counter afresh rather than getting back
/// the instance the lost attempt already admitted against (ADR 0015: "reloads, re-runs the domain
/// rules") — the stores behind one scope share one change tracker, which would otherwise hand the
/// stale row straight back.
/// </remarks>
public sealed class CreateBoard(IServiceScopeFactory scopes, IClock clock)
{
    public async Task<Result<BoardView, BoardError>> ExecuteAsync(
        Guid accountId, string name, CancellationToken cancellationToken)
    {
        var outcome = await BoardChangeRetry.RunAsync(async _ =>
        {
            await using var scope = scopes.CreateAsyncScope();
            return await AttemptAsync(scope.ServiceProvider, accountId, name, cancellationToken);
        });

        return outcome.Contended
            ? Result<BoardView, BoardError>.Failure(BoardErrors.Contended)
            : outcome.Value;
    }

    private async Task<Result<BoardView, BoardError>> AttemptAsync(
        IServiceProvider services, Guid accountId, string name, CancellationToken cancellationToken)
    {
        var boards = services.GetRequiredService<IBoardStore>();
        var counters = services.GetRequiredService<IOwnedBoardCounterStore>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var counter = await counters.LoadOrCreateAsync(accountId, cancellationToken);

        var created = Board.Create(accountId, name, clock.UtcNow);
        if (!created.IsSuccess)
        {
            return Result<BoardView, BoardError>.Failure(created.Error!);
        }

        var admitted = counter.Admit();
        if (!admitted.IsSuccess)
        {
            return Result<BoardView, BoardError>.Failure(admitted.Error!);
        }

        var board = created.Value;
        boards.Add(board);

        // The board, its columns, the Owner membership and the incremented counter are one
        // outcome, guarded by the counter's concurrency token (sad.md §6, flow 1).
        await unitOfWork.ExecuteAsync(
            async token =>
            {
                await boards.SaveAsync(token);
                await counters.SaveAsync(token);
                return true;
            },
            cancellationToken);

        return Result<BoardView, BoardError>.Success(
            OpenBoard.ToView(board, isOwner: true, cards: []));
    }
}
