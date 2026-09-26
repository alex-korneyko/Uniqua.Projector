using System.Reflection;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Domain.Tests.Boards;

/// <summary>
/// T2 — every column rule the Board itself enforces: add, rename, move and delete, dense positions
/// (ADR 0017), and the per-concern version checks (ADR 0016) that make a stale change refuse rather
/// than silently apply (AC-05, AC-06, AC-06b, AC-07, AC-08, AC-09, AC-10, AC-11, AC-24, AC-24b).
/// </summary>
public sealed class BoardColumnTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // A fresh board starts with "To do" (0), "In progress" (1), "Done" (2) — AC-01.
    private static Board ABoard() => Board.Create(OwnerId, "Q4 launch", Now).Value;

    /// <summary>
    /// Simulates a card landing in <paramref name="column"/> without going through T3's (not yet
    /// built) card rules — this task only needs a column whose <c>CardCount</c> is above zero, not
    /// a real card.
    /// </summary>
    private static void GiveItACard(Column column)
    {
        var setter = typeof(Column).GetProperty(nameof(Column.CardCount))!.GetSetMethod(nonPublic: true)!;
        setter.Invoke(column, [column.CardCount + 1]);
    }

    /// <summary>
    /// AC-24: refused as <c>boards.columns_changed</c>, carrying the board's columns as they now
    /// stand, in position order.
    /// </summary>
    private static void AssertColumnsChangedWith(Board board, BoardError? error)
    {
        Assert.Equal(BoardErrors.ColumnsChanged(board.Columns).Code, error!.Code);
        Assert.Equal(board.Columns.OrderBy(c => c.Position), error.CurrentLayout!);
    }

    private static void AssertDensePositions(Board board)
    {
        var positions = board.Columns.Select(c => c.Position).OrderBy(p => p).ToArray();
        Assert.Equal(Enumerable.Range(0, board.Columns.Count), positions);
        Assert.InRange(board.Columns.Count, 1, Board.MaxColumns);
    }

    // ---- AC-05: happy path (add) ----------------------------------------------------------------

    [Fact]
    public void Adding_a_column_appends_it_at_the_end_at_a_dense_position()
    {
        var board = ABoard();

        var result = board.AddColumn("Blocked");

        Assert.True(result.IsSuccess);
        Assert.Equal("Blocked", result.Value.Name);
        Assert.Equal(3, result.Value.Position);
        Assert.Equal(4, board.Columns.Count);
    }

    [Fact]
    public void A_column_name_need_not_be_unique_on_its_board()
    {
        var board = ABoard();

        var result = board.AddColumn("To do");

        Assert.True(result.IsSuccess);
    }

    // ---- AC-08: error (name length, add and rename) ---------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \t")]
    public void An_empty_column_name_after_trimming_is_refused_on_add(string name)
    {
        var board = ABoard();

        var result = board.AddColumn(name);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnNameInvalid, result.Error);
        Assert.Equal(3, board.Columns.Count);
    }

    [Fact]
    public void A_column_name_of_51_characters_is_refused_on_add()
    {
        var board = ABoard();

        var result = board.AddColumn(new string('a', 51));

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnNameInvalid, result.Error);
    }

    [Fact]
    public void A_column_name_of_exactly_50_characters_is_accepted()
    {
        var board = ABoard();

        var result = board.AddColumn(new string('a', 50));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void An_invalid_name_is_refused_on_rename_and_leaves_the_column_unchanged()
    {
        var board = ABoard();
        var column = board.Columns[0];

        var result = board.RenameColumn(column.Id, "   ", column.NameVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnNameInvalid, result.Error);
        Assert.Equal("To do", column.Name);
    }

    // ---- AC-11: domain invariant (20-column ceiling) --------------------------------------------

    [Fact]
    public void The_20th_column_is_accepted_and_the_21st_is_refused()
    {
        var board = ABoard();
        for (var i = board.Columns.Count; i < Board.MaxColumns; i++)
        {
            Assert.True(board.AddColumn($"Column {i}").IsSuccess);
        }

        Assert.Equal(20, board.Columns.Count);

        var result = board.AddColumn("One too many");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnLimitReached, result.Error);
        Assert.Equal(20, board.Columns.Count);
    }

    // ---- AC-06: happy path (rename / move) -------------------------------------------------------

    [Fact]
    public void Renaming_a_column_records_the_new_name_and_bumps_only_the_name_version()
    {
        var board = ABoard();
        var column = board.Columns[0];
        var layoutVersionBefore = board.ColumnLayoutVersion;

        var result = board.RenameColumn(column.Id, "Backlog", column.NameVersion);

        Assert.True(result.IsSuccess);
        Assert.Equal("Backlog", column.Name);
        Assert.Equal(2, column.NameVersion);
        Assert.Equal(layoutVersionBefore, board.ColumnLayoutVersion);
    }

    [Fact]
    public void Moving_a_column_reorders_the_columns_densely_and_bumps_only_the_layout_version()
    {
        var board = ABoard();
        var nameVersionsBefore = board.Columns.ToDictionary(c => c.Id, c => c.NameVersion);
        var moved = board.Columns[0];
        var layoutVersionBefore = board.ColumnLayoutVersion;

        var result = board.MoveColumn(moved.Id, 2, board.ColumnLayoutVersion);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, moved.Position);
        AssertDensePositions(board);
        Assert.Equal(layoutVersionBefore + 1, board.ColumnLayoutVersion);
        foreach (var column in board.Columns)
        {
            Assert.Equal(nameVersionsBefore[column.Id], column.NameVersion);
        }
    }

    [Fact]
    public void Moving_a_column_to_its_own_position_is_accepted_and_still_moves_the_layout_version()
    {
        var board = ABoard();
        var column = board.Columns[0];
        var layoutVersionBefore = board.ColumnLayoutVersion;

        var result = board.MoveColumn(column.Id, column.Position, board.ColumnLayoutVersion);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, column.Position);
        Assert.Equal(layoutVersionBefore + 1, board.ColumnLayoutVersion);
    }

    // ---- AC-24b: happy path (a rename never moves the layout version) ---------------------------

    [Fact]
    public void Moving_a_column_after_another_was_only_renamed_is_accepted()
    {
        var board = ABoard();
        board.RenameColumn(board.Columns[1].Id, "Renamed", board.Columns[1].NameVersion);
        var seenLayoutVersion = board.ColumnLayoutVersion;

        var result = board.MoveColumn(board.Columns[0].Id, 2, seenLayoutVersion);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Renaming_a_column_after_only_a_card_was_added_to_it_is_accepted()
    {
        var board = ABoard();
        var column = board.Columns[0];
        GiveItACard(column);

        var result = board.RenameColumn(column.Id, "Backlog", column.NameVersion);

        Assert.True(result.IsSuccess);
    }

    // ---- AC-06b: domain invariant (stale rename / delete) -----------------------------------------

    [Fact]
    public void Renaming_a_column_renamed_since_is_refused_with_the_current_name()
    {
        var board = ABoard();
        var column = board.Columns[0];
        var staleVersion = column.NameVersion;
        board.RenameColumn(column.Id, "Someone else's name", staleVersion);

        var result = board.RenameColumn(column.Id, "My name", staleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.ColumnRenamed(column), result.Error);
        Assert.Equal("Someone else's name", column.Name);
    }

    [Fact]
    public void Deleting_a_renamed_column_that_also_holds_cards_is_refused_as_renamed_first()
    {
        var board = ABoard();
        board.AddColumn("Spare"); // a fourth column, so last-column can never be the reason here
        var column = board.Columns[0];
        var staleVersion = column.NameVersion;
        board.RenameColumn(column.Id, "New name", staleVersion);
        GiveItACard(column);

        var result = board.DeleteColumn(column.Id, staleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.ColumnRenamed(column), result.Error);
        Assert.Equal(4, board.Columns.Count);
    }

    // ---- AC-07: happy path (delete) ---------------------------------------------------------------

    [Fact]
    public void Deleting_an_empty_column_removes_it_and_keeps_the_relative_order_of_the_rest()
    {
        var board = ABoard(); // To do(0), In progress(1), Done(2)
        var middle = board.Columns[1];

        var result = board.DeleteColumn(middle.Id, middle.NameVersion);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, board.Columns.Count);
        Assert.Equal("To do", board.Columns[0].Name);
        Assert.Equal(0, board.Columns[0].Position);
        Assert.Equal("Done", board.Columns[1].Name);
        Assert.Equal(1, board.Columns[1].Position);
    }

    [Fact]
    public void Deleting_a_column_bumps_the_layout_version_but_not_the_survivors_name_versions()
    {
        var board = ABoard();
        var middle = board.Columns[1];
        var survivor = board.Columns[2];
        var survivorNameVersion = survivor.NameVersion;
        var layoutVersionBefore = board.ColumnLayoutVersion;

        var result = board.DeleteColumn(middle.Id, middle.NameVersion);

        Assert.True(result.IsSuccess);
        Assert.Equal(layoutVersionBefore + 1, board.ColumnLayoutVersion);
        Assert.Equal(survivorNameVersion, survivor.NameVersion);
    }

    // ---- AC-09: domain invariant (holds cards) -----------------------------------------------------

    [Fact]
    public void Deleting_a_column_that_still_holds_a_card_is_refused_and_leaves_it_untouched()
    {
        var board = ABoard();
        var column = board.Columns[0];
        GiveItACard(column);

        var result = board.DeleteColumn(column.Id, column.NameVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnNotEmpty, result.Error);
        Assert.Equal(3, board.Columns.Count);
    }

    // ---- AC-10: domain invariant (last column) -----------------------------------------------------

    [Fact]
    public void Deleting_the_only_column_left_is_refused_even_when_it_holds_no_cards()
    {
        var board = ABoard();
        board.DeleteColumn(board.Columns[2].Id, board.Columns[2].NameVersion);
        board.DeleteColumn(board.Columns[1].Id, board.Columns[1].NameVersion);
        var lastColumn = Assert.Single(board.Columns);

        var result = board.DeleteColumn(lastColumn.Id, lastColumn.NameVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.LastColumn, result.Error);
        Assert.Single(board.Columns);
    }

    [Fact]
    public void Deleting_the_only_column_while_it_holds_cards_is_refused_as_not_empty_first()
    {
        var board = ABoard();
        board.DeleteColumn(board.Columns[2].Id, board.Columns[2].NameVersion);
        board.DeleteColumn(board.Columns[1].Id, board.Columns[1].NameVersion);
        var lastColumn = Assert.Single(board.Columns);
        GiveItACard(lastColumn);

        var result = board.DeleteColumn(lastColumn.Id, lastColumn.NameVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnNotEmpty, result.Error);
    }

    // ---- AC-24: domain invariant (stale move) -------------------------------------------------------

    [Fact]
    public void Moving_a_column_when_the_layout_changed_since_is_refused_with_the_current_layout()
    {
        var board = ABoard();
        var staleLayoutVersion = board.ColumnLayoutVersion;
        board.AddColumn("Blocked");

        var result = board.MoveColumn(board.Columns[0].Id, 1, staleLayoutVersion);

        Assert.False(result.IsSuccess);
        AssertColumnsChangedWith(board, result.Error);
    }

    [Fact]
    public void Moving_to_position_n_is_refused_as_an_invalid_position()
    {
        var board = ABoard();

        var result = board.MoveColumn(board.Columns[0].Id, board.Columns.Count, board.ColumnLayoutVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnPositionInvalid, result.Error);
    }

    [Fact]
    public void Moving_to_position_minus_one_is_refused_as_an_invalid_position()
    {
        var board = ABoard();

        var result = board.MoveColumn(board.Columns[0].Id, -1, board.ColumnLayoutVersion);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.ColumnPositionInvalid, result.Error);
    }

    [Fact]
    public void An_invalid_position_is_checked_only_after_the_stale_check()
    {
        var board = ABoard();
        var staleLayoutVersion = board.ColumnLayoutVersion;
        board.AddColumn("Blocked");

        var result = board.MoveColumn(board.Columns[0].Id, 99, staleLayoutVersion);

        Assert.False(result.IsSuccess);
        AssertColumnsChangedWith(board, result.Error);
    }

    // ---- Precedence: a column not on the board answers the same for every operation ----------------

    [Fact]
    public void A_column_not_on_the_board_is_refused_as_not_available_for_rename_move_and_delete()
    {
        var board = ABoard();
        var unknownId = Guid.NewGuid();

        Assert.Same(BoardErrors.NotAvailable, board.RenameColumn(unknownId, "New name", 1).Error);
        Assert.Same(BoardErrors.NotAvailable, board.MoveColumn(unknownId, 0, 1).Error);
        Assert.Same(BoardErrors.NotAvailable, board.DeleteColumn(unknownId, 1).Error);
    }

    [Fact]
    public void FindColumn_returns_the_column_when_it_is_on_the_board()
    {
        var board = ABoard();
        var column = board.Columns[0];

        var result = board.FindColumn(column.Id);

        Assert.True(result.IsSuccess);
        Assert.Same(column, result.Value);
    }

    [Fact]
    public void FindColumn_refuses_a_column_that_is_not_on_the_board()
    {
        var board = ABoard();

        var result = board.FindColumn(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.NotAvailable, result.Error);
    }

    // ---- DoD: a property-style check that positions stay dense across random structural changes ----

    [Fact]
    public void Positions_stay_exactly_zero_to_n_minus_one_after_a_random_sequence_of_adds_moves_and_deletes()
    {
        var board = ABoard();
        var random = new Random(20240925);

        for (var i = 0; i < 200; i++)
        {
            var deletable = board.Columns.Count > 1
                ? board.Columns.FirstOrDefault(c => c.CardCount == 0)
                : null;

            var actions = new List<int> { 2 }; // move is always legal — at least one column exists
            if (board.Columns.Count < Board.MaxColumns)
            {
                actions.Add(0); // add
            }

            if (deletable is not null)
            {
                actions.Add(1); // delete
            }

            switch (actions[random.Next(actions.Count)])
            {
                case 0:
                    Assert.True(board.AddColumn($"Column {i}").IsSuccess);
                    break;
                case 1:
                    Assert.True(board.DeleteColumn(deletable!.Id, deletable.NameVersion).IsSuccess);
                    break;
                default:
                    var column = board.Columns[random.Next(board.Columns.Count)];
                    var position = random.Next(board.Columns.Count);
                    Assert.True(board.MoveColumn(column.Id, position, board.ColumnLayoutVersion).IsSuccess);
                    break;
            }

            AssertDensePositions(board);
        }
    }

    // ---- T24 (review Q4e; AC-25, sad.md §8 Logging): no column name in a refusal's detail ---------

    [Fact]
    public void A_stale_column_rename_carries_the_current_column_as_a_value_and_never_its_name_in_the_detail()
    {
        var board = ABoard();
        var column = board.Columns[0];
        var staleVersion = column.NameVersion;
        board.RenameColumn(column.Id, "Quetzal lane", staleVersion);

        var result = board.RenameColumn(column.Id, "My name", staleVersion);

        Assert.Equal("boards.column_renamed", result.Error!.Code);
        Assert.Same(column, result.Error.CurrentColumn);
        Assert.Equal("Quetzal lane", result.Error.CurrentColumn!.Name);
        Assert.DoesNotContain("Quetzal", result.Error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_stale_move_carries_the_current_layout_as_values_and_never_a_column_name_in_the_detail()
    {
        var board = ABoard();
        var staleLayoutVersion = board.ColumnLayoutVersion;
        board.AddColumn("Quetzal lane");

        var result = board.MoveColumn(board.Columns[0].Id, 1, staleLayoutVersion);

        Assert.Equal("boards.columns_changed", result.Error!.Code);
        Assert.Equal(
            ["To do", "In progress", "Done", "Quetzal lane"],
            result.Error.CurrentLayout!.Select(column => column.Name));
        foreach (var name in board.Columns.Select(column => column.Name))
        {
            Assert.DoesNotContain(name, result.Error.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }
}
