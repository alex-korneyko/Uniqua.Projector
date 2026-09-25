using Uniqua.Projector.Application.Boards.Ports;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// AC-04: every board the account is a member of, most recently created first, each marked with
/// whether the account owns it. A thin pass-through over <see cref="IBoardStore.ListForAccountAsync"/>
/// — the store's query already returns exactly this shape (sad.md §6, flow 2).
/// </summary>
public sealed class ListMyBoards(IBoardStore boards)
{
    public Task<IReadOnlyList<BoardListEntry>> ExecuteAsync(
        Guid accountId, CancellationToken cancellationToken) =>
        boards.ListForAccountAsync(accountId, cancellationToken);
}
