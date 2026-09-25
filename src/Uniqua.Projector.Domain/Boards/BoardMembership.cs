namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// A board's role, one per (board, account) pair (ADR 0013). Creating a board writes the creator's
/// <see cref="Owner"/> record; every other membership is <see cref="Member"/>.
/// </summary>
public enum BoardRole
{
    Owner,
    Member,
}

/// <summary>
/// Grants an account access to a board and carries the one role that decides whether it may rename
/// or delete it (ADR 0013). Held inside the <see cref="Board"/> aggregate.
/// </summary>
public sealed class BoardMembership
{
    internal BoardMembership(Guid id, Guid boardId, Guid accountId, BoardRole role)
    {
        Id = id;
        BoardId = boardId;
        AccountId = accountId;
        Role = role;
    }

    public Guid Id { get; }

    public Guid BoardId { get; }

    public Guid AccountId { get; }

    public BoardRole Role { get; }
}
