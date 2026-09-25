namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// A column on a board: name, dense position (ADR 0017), and the counters T2 and T3 build on. Held
/// inside the <see cref="Board"/> aggregate — a column is never loaded, changed or persisted on its
/// own.
/// </summary>
public sealed class Column
{
    internal Column(Guid id, Guid boardId, string name, int position)
    {
        Id = id;
        BoardId = boardId;
        Name = name;
        Position = position;
        CardCount = 0;
        NextCardPosition = 0;
        NameVersion = 1;
    }

    public Guid Id { get; }

    public Guid BoardId { get; }

    public string Name { get; internal set; }

    /// <summary>Dense 0..n-1, renumbered by the Board on every add, move and delete (ADR 0017).</summary>
    public int Position { get; internal set; }

    public int CardCount { get; internal set; }

    /// <summary>One past the highest card position ever used in this column (ADR 0017).</summary>
    public int NextCardPosition { get; internal set; }

    /// <summary>EF concurrency token (ADR 0016): moves only on a rename, starts at 1.</summary>
    public int NameVersion { get; internal set; }
}
