namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// The board aggregate: name, columns, card counters and <c>ColumnLayoutVersion</c>
/// (data-model.md § Boards). Every structural rule of spec.md §5 that applies to the board itself —
/// creation, rename, deletion — is enforced here, never by a use case (CLAUDE.md § Domain rules
/// live in Domain). T2 adds the column rules and T3 the card rules; this task adds creation, and
/// the owner-only rename and delete.
/// </summary>
public sealed class Board
{
    /// <summary>AC-01 / AC-02 / AC-19. The Text rule's bounds on a board name.</summary>
    public const int MinNameLength = 1;

    public const int MaxNameLength = 100;

    private readonly List<Column> _columns = [];
    private readonly List<BoardMembership> _memberships = [];

    private Board(Guid id, string name, DateTimeOffset createdAt, Guid ownerId)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
        CardCount = 0;
        ColumnLayoutVersion = 1;

        _columns.Add(new Column(Ids.New(), id, "To do", 0));
        _columns.Add(new Column(Ids.New(), id, "In progress", 1));
        _columns.Add(new Column(Ids.New(), id, "Done", 2));

        _memberships.Add(new BoardMembership(Ids.New(), id, ownerId, BoardRole.Owner));
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Cards on the board. The Board refuses the 1,001st (AC-15, T3).</summary>
    public int CardCount { get; internal set; }

    /// <summary>ADR 0016: moves when a column is added, moved or deleted, never on a rename. Starts at 1.</summary>
    public int ColumnLayoutVersion { get; internal set; }

    /// <summary>AC-01: To do, In progress, Done, in that order, at positions 0, 1, 2.</summary>
    public IReadOnlyList<Column> Columns => _columns;

    public IReadOnlyList<BoardMembership> Memberships => _memberships;

    /// <summary>
    /// AC-01 / AC-02 / AC-03. Builds a board named by the Text rule, with three columns and the
    /// creator as its sole <see cref="BoardRole.Owner"/> member — or refuses with the one reason
    /// that applies. The 50-owned-board ceiling is <see cref="OwnedBoardCounter"/>'s to enforce, not
    /// this method's: a caller admits against the counter before calling <see cref="Create"/>.
    /// </summary>
    public static Result<Board, BoardError> Create(Guid ownerId, string name, DateTimeOffset now)
    {
        if (!IsValidName(name, out var trimmed))
        {
            return Result<Board, BoardError>.Failure(BoardErrors.BoardNameInvalid);
        }

        return Result<Board, BoardError>.Success(new Board(Ids.New(now), trimmed, now, ownerId));
    }

    /// <summary>
    /// AC-22 / sad.md §6: the first check on every board rename and deletion, before the name is
    /// even looked at.
    /// </summary>
    public Result<bool, BoardError> EnsureOwner(Guid accountId) =>
        _memberships.Exists(m => m.AccountId == accountId && m.Role == BoardRole.Owner)
            ? Result<bool, BoardError>.Success(true)
            : Result<bool, BoardError>.Failure(BoardErrors.OwnerOnly);

    /// <summary>AC-19 / AC-22. Owner check first, then the Text rule on the new name.</summary>
    public Result<bool, BoardError> Rename(Guid accountId, string name)
    {
        var owner = EnsureOwner(accountId);
        if (!owner.IsSuccess)
        {
            return owner;
        }

        if (!IsValidName(name, out var trimmed))
        {
            return Result<bool, BoardError>.Failure(BoardErrors.BoardNameInvalid);
        }

        Name = trimmed;
        return Result<bool, BoardError>.Success(true);
    }

    /// <summary>
    /// AC-20 / AC-20b / AC-22. Owner check first, then the typed confirmation — trimmed, then
    /// compared ordinally (same letters, same case) with the board's current name. A mismatch
    /// carries that current name, because the board may have been renamed since the dialog opened.
    /// </summary>
    public Result<bool, BoardError> ConfirmDeletion(Guid accountId, string typedName)
    {
        var owner = EnsureOwner(accountId);
        if (!owner.IsSuccess)
        {
            return owner;
        }

        return string.Equals(BoardText.Trim(typedName), Name, StringComparison.Ordinal)
            ? Result<bool, BoardError>.Success(true)
            : Result<bool, BoardError>.Failure(BoardErrors.ConfirmationMismatch(Name));
    }

    private static bool IsValidName(string? name, out string trimmed) =>
        BoardText.TryNormalize(name, MinNameLength, MaxNameLength, out trimmed);
}
