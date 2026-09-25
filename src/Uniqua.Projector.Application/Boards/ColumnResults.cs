using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Application.Boards;

/// <summary>
/// What <see cref="AddColumn"/> hands back (contracts/openapi.yaml,
/// components.schemas.AddedColumn, abridged): the column just added, and the board's
/// <c>column_layout_version</c> it moved to.
/// </summary>
public sealed record AddedColumn(BoardColumnView Column, int ColumnLayoutVersion);

/// <summary>
/// What <see cref="MoveColumn"/> and <see cref="DeleteColumn"/> hand back (contracts/openapi.yaml,
/// components.schemas.ColumnLayout, abridged): the board's new <c>column_layout_version</c> and the
/// full column layout in position order, so a caller need not re-open the board to see it.
/// </summary>
public sealed record ColumnLayout(int ColumnLayoutVersion, IReadOnlyList<BoardColumnView> Columns);

/// <summary>The one mapping from the aggregate's columns to the shapes the column use cases return.</summary>
internal static class ColumnViews
{
    public static BoardColumnView Of(Column column) =>
        new(column.Id, column.Name, column.Position, column.NameVersion);

    public static ColumnLayout LayoutOf(Board board) =>
        new(
            board.ColumnLayoutVersion,
            board.Columns.OrderBy(column => column.Position).Select(Of).ToList());
}
