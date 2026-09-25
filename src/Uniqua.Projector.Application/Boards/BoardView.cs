using Uniqua.Projector.Application.Boards.Ports;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// One column as <c>OpenBoard</c> and <c>CreateBoard</c> hand it back (contracts/openapi.yaml,
/// components.schemas.Board, abridged): enough to render it and to name it in a later
/// <c>renameColumn</c>/<c>moveColumn</c> call.
/// </summary>
public sealed record BoardColumnView(Guid Id, string Name, int Position, int NameVersion);

/// <summary>
/// What <c>OpenBoard</c> returns, and what <c>CreateBoard</c> returns for the board it just opened
/// (AC-01's "... and opens it"): the board's own fields, whether the caller owns it, its columns in
/// position order, and its card summaries ordered by column position then card position
/// (contracts/openapi.yaml, components.schemas.Board, abridged).
/// </summary>
public sealed record BoardView(
    Guid Id,
    string Name,
    bool IsOwner,
    int ColumnLayoutVersion,
    IReadOnlyList<BoardColumnView> Columns,
    IReadOnlyList<CardSummary> Cards);
