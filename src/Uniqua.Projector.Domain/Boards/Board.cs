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

    /// <summary>AC-11. A board can hold at most 20 columns.</summary>
    public const int MaxColumns = 20;

    /// <summary>AC-15. A board can hold at most 1,000 cards, even under concurrent adds.</summary>
    public const int MaxCards = 1_000;

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

    // ---- T2: column rules (ADR 0016 per-concern versions, ADR 0017 dense positions) ------------

    /// <summary>AC-26 by construction: the only way a column is reached from outside the aggregate.</summary>
    public Result<Column, BoardError> FindColumn(Guid columnId)
    {
        var column = _columns.Find(c => c.Id == columnId);
        return column is null
            ? Result<Column, BoardError>.Failure(BoardErrors.NotAvailable)
            : Result<Column, BoardError>.Success(column);
    }

    /// <summary>
    /// AC-05 / AC-08 / AC-11. Text rule 1-50, refuse at 20, append at the end. Never stale: an
    /// added column overwrites nothing. Names need not be unique on the board.
    /// </summary>
    public Result<Column, BoardError> AddColumn(string name)
    {
        if (!IsValidColumnName(name, out var trimmed))
        {
            return Result<Column, BoardError>.Failure(BoardErrors.ColumnNameInvalid);
        }

        if (_columns.Count >= MaxColumns)
        {
            return Result<Column, BoardError>.Failure(BoardErrors.ColumnLimitReached);
        }

        var column = new Column(Ids.New(), Id, trimmed, _columns.Count);
        _columns.Add(column);
        LayoutChanged();
        return Result<Column, BoardError>.Success(column);
    }

    /// <summary>
    /// AC-06 / AC-06b / AC-08. Not on board -&gt; Text rule -&gt; stale -&gt; rename. Moves the
    /// column's <c>NameVersion</c> only; the board's <c>ColumnLayoutVersion</c> is untouched (AC-24b).
    /// </summary>
    public Result<bool, BoardError> RenameColumn(Guid columnId, string name, int seenNameVersion)
    {
        var found = FindColumn(columnId);
        if (!found.IsSuccess)
        {
            return Result<bool, BoardError>.Failure(found.Error!);
        }

        var column = found.Value;
        if (!IsValidColumnName(name, out var trimmed))
        {
            return Result<bool, BoardError>.Failure(BoardErrors.ColumnNameInvalid);
        }

        if (column.NameVersion != seenNameVersion)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.ColumnRenamed(column));
        }

        column.Name = trimmed;
        column.NameVersion++;
        return Result<bool, BoardError>.Success(true);
    }

    /// <summary>
    /// AC-06 / AC-24 / AC-24b. Not on board -&gt; stale -&gt; position in 0..n-1 -&gt; renumber.
    /// A move to the column's own place is accepted and still moves the layout version.
    /// </summary>
    public Result<bool, BoardError> MoveColumn(Guid columnId, int position, int seenLayoutVersion)
    {
        var found = FindColumn(columnId);
        if (!found.IsSuccess)
        {
            return Result<bool, BoardError>.Failure(found.Error!);
        }

        if (ColumnLayoutVersion != seenLayoutVersion)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.ColumnsChanged(_columns));
        }

        if (position < 0 || position >= _columns.Count)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.ColumnPositionInvalid);
        }

        var column = found.Value;
        _columns.Remove(column);
        _columns.Insert(position, column);
        LayoutChanged();
        return Result<bool, BoardError>.Success(true);
    }

    /// <summary>
    /// AC-06b / AC-07 / AC-09 / AC-10. Not on board -&gt; stale -&gt; holds cards -&gt; last column
    /// -&gt; remove. The survivors keep their relative order and are renumbered 0..n-1.
    /// </summary>
    public Result<bool, BoardError> DeleteColumn(Guid columnId, int seenNameVersion)
    {
        var found = FindColumn(columnId);
        if (!found.IsSuccess)
        {
            return Result<bool, BoardError>.Failure(found.Error!);
        }

        var column = found.Value;
        if (column.NameVersion != seenNameVersion)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.ColumnRenamed(column));
        }

        if (column.CardCount > 0)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.ColumnNotEmpty);
        }

        if (_columns.Count == 1)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.LastColumn);
        }

        _columns.Remove(column);
        LayoutChanged();
        return Result<bool, BoardError>.Success(true);
    }

    private static bool IsValidColumnName(string? name, out string trimmed) =>
        BoardText.TryNormalize(name, Column.MinNameLength, Column.MaxNameLength, out trimmed);

    /// <summary>
    /// After every add, move and delete: renumber from list order so positions are exactly
    /// 0..n-1 (ADR 0017), move the layout version on (ADR 0016), and check the invariant holds.
    /// </summary>
    private void LayoutChanged()
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            _columns[i].Position = i;
        }

        ColumnLayoutVersion++;
        EnsureColumnInvariant();
    }

    // ---- T3: card rules (ADR 0017 gapped positions) ----------------------------------------------

    /// <summary>
    /// AC-12 / AC-14 / AC-15. Column not on board -&gt; Text rule on the title -&gt; on the
    /// description -&gt; refuse at 1,000 -&gt; append at the column's next gapped position. An
    /// absent description is stored as the empty string, the one representation of "none".
    /// </summary>
    public Result<Card, BoardError> AdmitCard(Guid columnId, string title, string? description)
    {
        var found = FindColumn(columnId);
        if (!found.IsSuccess)
        {
            return Result<Card, BoardError>.Failure(found.Error!);
        }

        if (!Card.IsValidTitle(title, out var trimmedTitle))
        {
            return Result<Card, BoardError>.Failure(BoardErrors.CardTitleInvalid);
        }

        var text = description ?? string.Empty;
        if (!Card.IsValidDescription(text))
        {
            return Result<Card, BoardError>.Failure(BoardErrors.CardDescriptionInvalid);
        }

        if (CardCount >= MaxCards)
        {
            return Result<Card, BoardError>.Failure(BoardErrors.CardLimitReached);
        }

        var column = found.Value;
        var card = new Card(Ids.New(), Id, column.Id, column.NextCardPosition, trimmedTitle, text);
        column.NextCardPosition++;
        column.CardCount++;
        CardCount++;
        return Result<Card, BoardError>.Success(card);
    }

    /// <summary>
    /// AC-18. The caller has already asked the card whether it is stale
    /// (<see cref="Card.EnsureDeletable"/>); this only checks the card belongs to this board and
    /// then adjusts the counters. Other cards' positions are left untouched (ADR 0017).
    /// </summary>
    public Result<bool, BoardError> RemoveCard(Card card)
    {
        var column = card.BoardId == Id ? _columns.Find(c => c.Id == card.ColumnId) : null;
        if (column is null)
        {
            return Result<bool, BoardError>.Failure(BoardErrors.NotAvailable);
        }

        column.CardCount--;
        CardCount--;
        return Result<bool, BoardError>.Success(true);
    }

    private void EnsureColumnInvariant()
    {
        if (_columns.Count is < 1 or > MaxColumns)
        {
            throw new InvalidOperationException(
                $"A board must hold between 1 and {MaxColumns} columns; this one holds {_columns.Count}.");
        }

        for (var i = 0; i < _columns.Count; i++)
        {
            if (_columns[i].Position != i)
            {
                throw new InvalidOperationException("Column positions must be exactly 0..n-1.");
            }
        }
    }
}
