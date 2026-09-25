using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Domain.Tests.Boards;

/// <summary>
/// T3 — every card rule the Board and the Card itself enforce: admitting a card through the
/// Board's counters, the Text rule on title and description, the 1,000-card ceiling, gapped
/// positions from <see cref="Column.NextCardPosition"/>, and the <c>ContentVersion</c> stale check
/// on edit and delete (AC-12, AC-13, AC-14, AC-15, AC-18, AC-23).
/// </summary>
public sealed class CardTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // A fresh board starts with "To do" (0), "In progress" (1), "Done" (2) — AC-01.
    private static Board ABoard() => Board.Create(OwnerId, "Q4 launch", Now).Value;

    private static Card ACard(Board board, Column? column = null) =>
        board.AdmitCard((column ?? board.Columns[0]).Id, "Write the spec", "").Value;

    // ---- AC-12: happy path (add) ------------------------------------------------------------------

    [Fact]
    public void Admitting_a_card_appends_it_to_the_column_at_a_gapped_position()
    {
        var board = ABoard();
        var column = board.Columns[0];

        var result = board.AdmitCard(column.Id, "Write the spec", "Some detail");

        Assert.True(result.IsSuccess);
        Assert.Equal("Write the spec", result.Value.Title);
        Assert.Equal("Some detail", result.Value.Description);
        Assert.Equal(0, result.Value.Position);
        Assert.Equal(1, column.CardCount);
        Assert.Equal(1, board.CardCount);
        Assert.Equal(1, column.NextCardPosition);
        Assert.Equal(1, result.Value.ContentVersion);
    }

    [Fact]
    public void Admitting_a_card_without_a_description_stores_an_empty_string()
    {
        var board = ABoard();

        var result = board.AdmitCard(board.Columns[0].Id, "Write the spec", null);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value.Description);
    }

    [Fact]
    public void A_column_not_on_the_board_is_refused_as_not_available()
    {
        var board = ABoard();

        var result = board.AdmitCard(Guid.NewGuid(), "Write the spec", "");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
    }

    // ---- AC-14: error (title / description length, title checked first) --------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \t")]
    public void An_empty_card_title_after_trimming_is_refused(string title)
    {
        var board = ABoard();
        var column = board.Columns[0];

        var result = board.AdmitCard(column.Id, title, "");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardTitleInvalid, result.Error);
        Assert.Equal(0, column.CardCount);
        Assert.Equal(0, board.CardCount);
    }

    [Fact]
    public void A_card_title_of_151_characters_is_refused()
    {
        var board = ABoard();

        var result = board.AdmitCard(board.Columns[0].Id, new string('a', 151), "");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardTitleInvalid, result.Error);
    }

    [Fact]
    public void A_card_title_of_exactly_150_characters_is_accepted()
    {
        var board = ABoard();

        var result = board.AdmitCard(board.Columns[0].Id, new string('a', 150), "");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_card_description_of_10001_characters_is_refused()
    {
        var board = ABoard();

        var result = board.AdmitCard(board.Columns[0].Id, "Write the spec", new string('a', 10_001));

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardDescriptionInvalid, result.Error);
    }

    [Fact]
    public void A_card_description_of_exactly_10000_characters_is_accepted()
    {
        var board = ABoard();

        var result = board.AdmitCard(board.Columns[0].Id, "Write the spec", new string('a', 10_000));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void A_description_of_only_spaces_and_line_breaks_is_accepted_and_stored_exactly_as_typed()
    {
        var board = ABoard();
        const string description = "  \n\t \n  ";

        var result = board.AdmitCard(board.Columns[0].Id, "Write the spec", description);

        Assert.True(result.IsSuccess);
        Assert.Equal(description, result.Value.Description);
    }

    [Fact]
    public void An_invalid_title_and_an_invalid_description_are_refused_as_the_title_first()
    {
        var board = ABoard();
        var column = board.Columns[0];

        var result = board.AdmitCard(column.Id, "", new string('a', 10_001));

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardTitleInvalid, result.Error);
        Assert.Equal(0, column.CardCount);
    }

    // ---- AC-15: domain invariant (1,000-card ceiling) ----------------------------------------------

    [Fact]
    public void The_1000th_card_is_accepted_and_the_1001st_is_refused()
    {
        var board = ABoard();
        var column = board.Columns[0];
        for (var i = 0; i < Board.MaxCards; i++)
        {
            Assert.True(board.AdmitCard(column.Id, $"Card {i}", "").IsSuccess);
        }

        Assert.Equal(Board.MaxCards, board.CardCount);

        var result = board.AdmitCard(column.Id, "One too many", "");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardLimitReached, result.Error);
        Assert.Equal(Board.MaxCards, board.CardCount);
    }

    // ---- ADR 0017: gapped positions ------------------------------------------------------------

    [Fact]
    public void Deleting_the_last_card_then_adding_one_gives_a_position_one_past_the_highest_ever_used()
    {
        var board = ABoard();
        var column = board.Columns[0];
        var first = board.AdmitCard(column.Id, "First", "").Value;
        var second = board.AdmitCard(column.Id, "Second", "").Value;
        Assert.True(second.EnsureDeletable(second.ContentVersion).IsSuccess);
        Assert.True(board.RemoveCard(second).IsSuccess);

        var third = board.AdmitCard(column.Id, "Third", "").Value;

        Assert.Equal(2, third.Position);
        Assert.Equal(0, first.Position);
    }

    // ---- AC-13: happy path (edit) ------------------------------------------------------------------

    [Fact]
    public void Editing_a_cards_title_and_description_records_both_and_bumps_the_content_version()
    {
        var board = ABoard();
        var card = ACard(board);
        var seenVersion = card.ContentVersion;

        var result = card.Edit("New title", "New description", seenVersion);

        Assert.True(result.IsSuccess);
        Assert.Equal("New title", card.Title);
        Assert.Equal("New description", card.Description);
        Assert.Equal(seenVersion + 1, card.ContentVersion);
    }

    // ---- AC-14: error (edit title / description) ---------------------------------------------------

    [Fact]
    public void Editing_a_card_to_an_empty_title_is_refused_and_leaves_it_unchanged()
    {
        var board = ABoard();
        var card = ACard(board);

        var result = card.Edit("   ", null, card.ContentVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardTitleInvalid, result.Error);
        Assert.Equal("Write the spec", card.Title);
    }

    [Fact]
    public void Editing_a_card_to_a_description_over_10000_characters_is_refused()
    {
        var board = ABoard();
        var card = ACard(board);

        var result = card.Edit(null, new string('a', 10_001), card.ContentVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.CardDescriptionInvalid, result.Error);
    }

    // ---- AC-23: domain invariant (stale edit / delete) ----------------------------------------------

    [Fact]
    public void Editing_a_card_changed_since_is_refused_with_the_current_card()
    {
        var board = ABoard();
        var card = ACard(board);
        var staleVersion = card.ContentVersion;
        card.Edit("Someone else's title", null, staleVersion);

        var result = card.Edit("My title", null, staleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.CardChanged(card), result.Error);
        Assert.Equal("Someone else's title", card.Title);
    }

    [Fact]
    public void Editing_only_the_description_after_someone_else_changed_only_the_title_is_refused_as_stale()
    {
        var board = ABoard();
        var card = ACard(board);
        var staleVersion = card.ContentVersion;
        card.Edit("A new title", null, staleVersion);

        var result = card.Edit(null, "A new description", staleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.CardChanged(card), result.Error);
    }

    [Fact]
    public void Deleting_a_card_changed_since_is_refused_with_the_current_card()
    {
        var board = ABoard();
        var card = ACard(board);
        var staleVersion = card.ContentVersion;
        card.Edit("Someone else's title", null, staleVersion);

        var result = card.EnsureDeletable(staleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.CardChanged(card), result.Error);
    }

    // ---- AC-18: happy path (delete) -----------------------------------------------------------------

    [Fact]
    public void Deleting_a_card_removes_it_from_the_counters_and_leaves_the_others_positions_untouched()
    {
        var board = ABoard();
        var column = board.Columns[0];
        var first = board.AdmitCard(column.Id, "First", "").Value;
        var second = board.AdmitCard(column.Id, "Second", "").Value;
        var third = board.AdmitCard(column.Id, "Third", "").Value;
        var firstPosition = first.Position;
        var thirdPosition = third.Position;

        Assert.True(second.EnsureDeletable(second.ContentVersion).IsSuccess);
        var result = board.RemoveCard(second);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, column.CardCount);
        Assert.Equal(2, board.CardCount);
        Assert.Equal(firstPosition, first.Position);
        Assert.Equal(thirdPosition, third.Position);
    }

    [Fact]
    public void A_card_whose_board_id_is_another_board_is_refused_as_not_available_on_removal()
    {
        var board = ABoard();
        var otherBoard = ABoard();
        var foreignCard = ACard(otherBoard);

        var result = board.RemoveCard(foreignCard);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
    }
}
