using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Api.Boards;

/// <summary>
/// The board feature's wording table: every <c>boards.*</c> refusal — the board, column and card
/// ones alike — in one place, each row exactly as contracts/openapi.yaml words it (status, type URI,
/// title and a fixed detail sentence). The Domain sentinel names <em>which</em> rule was broken;
/// what the caller reads is decided here and nowhere else, so a detail never echoes submitted text
/// or board content (openapi.yaml <c>Problem.detail</c>).
/// </summary>
/// <remarks>
/// Nothing outside this file decides what a board refusal says. An endpoint hands a code to
/// <see cref="ProblemDetailsSetup"/>; it never builds a shape of its own (sad.md § 8).
/// </remarks>
public static class BoardProblems
{
    private const string TypeBase = "https://projector.example.com/problems/";

    /// <summary>JSON, but not the documented shape — answered only after the membership check.</summary>
    public const string RequestInvalid = "boards.request_invalid";

    /// <summary>A body that is not JSON at all — answered before membership, identically for every board.</summary>
    public const string RequestMalformed = "boards.request_malformed";

    /// <summary>AC-20b. Published with <c>current_name</c>.</summary>
    public const string ConfirmationMismatch = "boards.confirmation_mismatch";

    /// <summary>AC-06b. Published with <c>current_column</c>.</summary>
    public const string ColumnRenamed = "boards.column_renamed";

    /// <summary>AC-24. Published with <c>current_layout</c>.</summary>
    public const string ColumnsChanged = "boards.columns_changed";

    /// <summary>AC-23. Published with <c>current_card</c>.</summary>
    public const string CardChanged = "boards.card_changed";

    private static readonly Dictionary<string, BoardProblem> Table = new[]
    {
        Row(BoardErrors.NotAvailable.Code, 404, "Board not available",
            "This board does not exist, or you are not a member of it."),
        Row(RequestMalformed, 400, "The request body could not be read",
            "The request body must be a JSON object matching the documented shape."),
        Row(RequestInvalid, 400, "The request is not complete",
            "The request is missing a value it needs, or a value has the wrong type."),
        Row("boards.change_rate_limited", 429, "Changes are temporarily limited",
            "You have made many changes in the past minute. You can continue shortly.",
            carriesRetryAfter: true),
        Row(BoardErrors.Contended.Code, 503, "The board is busy",
            "Others are changing this board right now. Try again.",
            carriesRetryAfter: true),

        Row(BoardErrors.BoardNameInvalid.Code, 400, "The board name is not usable",
            "A board name must be between 1 and 100 characters."),
        Row(BoardErrors.OwnedBoardLimitReached.Code, 409, "No more boards can be created",
            "An account can own at most 50 boards."),
        Row(BoardErrors.OwnerOnly.Code, 403, "Only the board owner can do this",
            "Only the board owner may rename or delete a board."),
        Row(ConfirmationMismatch, 409, "The name does not match",
            "Type the board's current name exactly to delete it.", current: "current_name"),

        Row(BoardErrors.ColumnNameInvalid.Code, 400, "The column name is not usable",
            "A column name must be between 1 and 50 characters."),
        Row(BoardErrors.ColumnLimitReached.Code, 409, "No more columns can be added",
            "A board can hold at most 20 columns."),
        Row(ColumnRenamed, 409, "The column was renamed",
            "This column was renamed since you last saw it.", current: "current_column"),
        Row(BoardErrors.ColumnNotEmpty.Code, 409, "The column still holds cards",
            "A column that still holds cards cannot be deleted."),
        Row(BoardErrors.LastColumn.Code, 409, "The last column cannot be deleted",
            "A board must keep at least one column."),
        Row(ColumnsChanged, 409, "The columns changed",
            "The columns changed since you last saw them.", current: "current_layout"),
        Row(BoardErrors.ColumnPositionInvalid.Code, 400, "That position is not on this board",
            "A column can be placed only at a position between the first and the last."),

        Row(BoardErrors.CardTitleInvalid.Code, 400, "The card title is not usable",
            "A card title must be between 1 and 150 characters."),
        Row(BoardErrors.CardDescriptionInvalid.Code, 400, "The card description is too long",
            "A card description can be at most 10,000 characters."),
        Row(BoardErrors.CardLimitReached.Code, 409, "No more cards can be added",
            "A board can hold at most 1,000 cards."),
        Row(CardChanged, 409, "The card was changed",
            "This card was changed since you opened it.", current: "current_card"),
    }.ToDictionary(problem => problem.Code);

    /// <summary>Every code this feature can publish.</summary>
    public static IReadOnlyCollection<string> Codes => Table.Keys;

    /// <summary>
    /// The row for a code. An unknown code throws rather than falling back to something generic:
    /// a refusal nobody wrote wording for is a bug to fix, not a body to ship.
    /// </summary>
    public static BoardProblem For(string code) => Table[code];

    private static BoardProblem Row(
        string code,
        int status,
        string title,
        string detail,
        bool carriesRetryAfter = false,
        string? current = null) =>
        new(code, status, title, detail, TypeBase + code.Replace('.', '/').Replace('_', '-'),
            carriesRetryAfter, current);
}

/// <summary>One row of the board wording table — an RFC 9457 problem type this feature can return.</summary>
/// <param name="Code">The <c>boards.*</c> extension member.</param>
/// <param name="Status">The HTTP status the contract pairs with this code.</param>
/// <param name="Title">The short summary of the problem type.</param>
/// <param name="Detail">The plain-language reason — fixed per code, never an echo of input.</param>
/// <param name="Type">The URI identifying the problem type.</param>
/// <param name="CarriesRetryAfter">
/// Whether this problem is published with <c>retry_after_seconds</c> and a <c>Retry-After</c>
/// header: <c>boards.change_rate_limited</c> and <c>boards.contended</c> only.
/// </param>
/// <param name="CurrentMember">
/// The extension member a stale change is answered with — <c>current_name</c>,
/// <c>current_column</c>, <c>current_layout</c> or <c>current_card</c> — carrying the thing as it now stands; <see
/// langword="null"/> on every row that publishes none (contracts/openapi.yaml <c>Problem</c>).
/// </param>
public sealed record BoardProblem(
    string Code,
    int Status,
    string Title,
    string Detail,
    string Type,
    bool CarriesRetryAfter,
    string? CurrentMember);
