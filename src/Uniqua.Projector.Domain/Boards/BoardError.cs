namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// A refusal, carrying exactly the plain-language reason its acceptance criterion specifies and no
/// more (sad.md § 8), mirroring <c>Accounts.AccountError</c>. The <see cref="Code"/> is the
/// contract's <c>boards.*</c> identifier, so the endpoint translates a refusal rather than deciding
/// what it means.
/// </summary>
/// <param name="Code">The <c>boards.*</c> code from contracts/openapi.yaml.</param>
/// <param name="Detail">The sentence shown to whoever made the request.</param>
public sealed record BoardError(string Code, string Detail);

/// <summary>
/// Every way this feature can refuse, named once. They are singletons (or, for
/// <see cref="ConfirmationMismatch"/>, a small factory) so a caller can compare by reference and a
/// test can assert which refusal happened rather than matching on prose. T2 and T3 add the column
/// and card refusals; this task adds only what board creation, rename and deletion need.
/// </summary>
public static class BoardErrors
{
    /// <summary>Not on this board, or the board no longer exists — answered identically either way.</summary>
    public static readonly BoardError NotAvailable = new(
        "boards.not_available",
        "That board is not available.");

    /// <summary>AC-22. Only the board owner may rename or delete a board.</summary>
    public static readonly BoardError OwnerOnly = new(
        "boards.owner_only",
        "Only the board owner may rename or delete a board.");

    /// <summary>AC-02 / AC-19. The Text rule's bounds on a board name.</summary>
    public static readonly BoardError BoardNameInvalid = new(
        "boards.board_name_invalid",
        $"A board name must be between {Board.MinNameLength} and {Board.MaxNameLength} characters long.");

    /// <summary>AC-03. Each account's share of the product is bounded.</summary>
    public static readonly BoardError OwnedBoardLimitReached = new(
        "boards.owned_board_limit_reached",
        $"An account can own at most {OwnedBoardCounter.MaxOwnedBoards} boards.");

    /// <summary>
    /// AC-20b. The typed confirmation did not match the board's current name — carrying that
    /// current name, because the board may have been renamed since the dialog opened.
    /// </summary>
    public static BoardError ConfirmationMismatch(string currentName) => new(
        "boards.confirmation_mismatch",
        $"That does not match the board's current name, \"{currentName}\".");

    /// <summary>AC-08. The Text rule's bounds on a column name.</summary>
    public static readonly BoardError ColumnNameInvalid = new(
        "boards.column_name_invalid",
        $"A column name must be between {Column.MinNameLength} and {Column.MaxNameLength} characters long.");

    /// <summary>AC-11. A board can hold at most 20 columns.</summary>
    public static readonly BoardError ColumnLimitReached = new(
        "boards.column_limit_reached",
        "A board can hold at most 20 columns.");

    /// <summary>
    /// AC-06b. A rename or delete refused because the column was renamed since — carries its
    /// current name.
    /// </summary>
    public static BoardError ColumnRenamed(Column current) => new(
        "boards.column_renamed",
        $"That column was renamed to \"{current.Name}\" since you last saw it.");

    /// <summary>AC-09. A column that still holds cards cannot be deleted.</summary>
    public static readonly BoardError ColumnNotEmpty = new(
        "boards.column_not_empty",
        "A column that still holds cards cannot be deleted.");

    /// <summary>AC-10. A board must keep at least one column.</summary>
    public static readonly BoardError LastColumn = new(
        "boards.last_column",
        "A board must keep at least one column.");

    /// <summary>
    /// AC-24. A reorder refused because the columns changed since — carries the current columns
    /// and order.
    /// </summary>
    public static BoardError ColumnsChanged(IReadOnlyList<Column> currentLayout) => new(
        "boards.columns_changed",
        $"The columns changed since you last saw them: "
        + string.Join(", ", currentLayout.OrderBy(c => c.Position).Select(c => c.Name)) + ".");

    /// <summary>AC-24. A move to a position outside 0..n-1.</summary>
    public static readonly BoardError ColumnPositionInvalid = new(
        "boards.column_position_invalid",
        "That is not a valid position for the column.");

    /// <summary>AC-14. The Text rule's bounds on a card title.</summary>
    public static readonly BoardError CardTitleInvalid = new(
        "boards.card_title_invalid",
        $"A card title must be between {Card.MinTitleLength} and {Card.MaxTitleLength} characters long.");

    /// <summary>AC-14. The ceiling on a card description's length.</summary>
    public static readonly BoardError CardDescriptionInvalid = new(
        "boards.card_description_invalid",
        $"A card description can be at most {Card.MaxDescriptionLength} characters long.");

    /// <summary>AC-15. A board can hold at most 1,000 cards.</summary>
    public static readonly BoardError CardLimitReached = new(
        "boards.card_limit_reached",
        $"A board can hold at most {Board.MaxCards} cards.");

    /// <summary>
    /// AC-23. An edit or delete refused because the card's title or description changed since —
    /// carries the card as it is now.
    /// </summary>
    public static BoardError CardChanged(Card current) => new(
        "boards.card_changed",
        $"That card was changed to \"{current.Title}\" since you last saw it.");
}
