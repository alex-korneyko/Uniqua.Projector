namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// A card on a board: title, description, gapped position (ADR 0017) and the stale-check version
/// T3 builds on. Read and written one at a time, but admitted, counted and removed only through the
/// <see cref="Board"/> aggregate (sad.md §5, ADR 0015) — it is never itself the aggregate root.
/// </summary>
public sealed class Card
{
    internal Card(Guid id, Guid boardId, Guid columnId, int position, string title, string description)
    {
        Id = id;
        BoardId = boardId;
        ColumnId = columnId;
        Position = position;
        Title = title;
        Description = description;
        ContentVersion = 1;
    }

    public Guid Id { get; }

    public Guid BoardId { get; }

    public Guid ColumnId { get; internal set; }

    /// <summary>Gapped and ascending; a deletion leaves a gap (AC-18).</summary>
    public int Position { get; internal set; }

    public string Title { get; internal set; }

    /// <summary>Stored exactly as typed — the Text rule never trims a description (spec.md §5).</summary>
    public string Description { get; internal set; }

    /// <summary>EF concurrency token (ADR 0016): moves when the title or description changes.</summary>
    public int ContentVersion { get; internal set; }
}
