namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// A card on a board: title, description, gapped position (ADR 0017) and the stale-check version
/// T3 builds on. Read and written one at a time, but admitted, counted and removed only through the
/// <see cref="Board"/> aggregate (sad.md §5, ADR 0015) — it is never itself the aggregate root.
/// </summary>
public sealed class Card
{
    /// <summary>AC-14. The Text rule's bounds on a card title.</summary>
    public const int MinTitleLength = 1;

    public const int MaxTitleLength = 150;

    /// <summary>AC-14. A card description is never trimmed, but is bounded.</summary>
    public const int MaxDescriptionLength = 10_000;

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

    /// <summary>
    /// AC-13 / AC-14 / AC-23. Text rule on whichever of <paramref name="title"/> and
    /// <paramref name="description"/> is sent (title first), then the stale check, then applies and
    /// bumps <see cref="ContentVersion"/>. A <c>null</c> field is not being changed; nothing is
    /// applied unless every rule passes.
    /// </summary>
    public Result<bool, BoardError> Edit(string? title, string? description, int seenContentVersion)
    {
        var trimmedTitle = Title;
        if (title is not null && !IsValidTitle(title, out trimmedTitle))
        {
            return Result<bool, BoardError>.Failure(BoardErrors.CardTitleInvalid);
        }

        if (description is not null && !IsValidDescription(description))
        {
            return Result<bool, BoardError>.Failure(BoardErrors.CardDescriptionInvalid);
        }

        var fresh = EnsureUnchangedSince(seenContentVersion);
        if (!fresh.IsSuccess)
        {
            return fresh;
        }

        Title = trimmedTitle;
        Description = description ?? Description;
        ContentVersion++;
        return Result<bool, BoardError>.Success(true);
    }

    /// <summary>AC-18 / AC-23. Stale check only — the caller still asks the Board to remove it.</summary>
    public Result<bool, BoardError> EnsureDeletable(int seenContentVersion) =>
        EnsureUnchangedSince(seenContentVersion);

    /// <summary>AC-23. A change to either the title or the description counts (spec.md §5).</summary>
    private Result<bool, BoardError> EnsureUnchangedSince(int seenContentVersion) =>
        ContentVersion == seenContentVersion
            ? Result<bool, BoardError>.Success(true)
            : Result<bool, BoardError>.Failure(BoardErrors.CardChanged(this));

    /// <summary>AC-14. The Text rule on a title: trimmed, then 1..150 code points.</summary>
    internal static bool IsValidTitle(string? title, out string trimmed) =>
        BoardText.TryNormalize(title, MinTitleLength, MaxTitleLength, out trimmed);

    /// <summary>AC-14. A description is never trimmed; only its code-point length is bounded.</summary>
    internal static bool IsValidDescription(string description) =>
        BoardText.CodePointLength(description) <= MaxDescriptionLength;
}
