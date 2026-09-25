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
}
