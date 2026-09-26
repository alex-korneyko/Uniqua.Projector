using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Domain.Tests.Boards;

/// <summary>
/// T1 — a board's own structural rules: creation with the Text rule, its three starting columns
/// and Owner membership (AC-01, AC-02), the 50-owned-board ceiling (AC-03), and the owner-only
/// rename and delete-with-confirmation (AC-19, AC-20, AC-20b, AC-22).
/// </summary>
public sealed class BoardTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Board ABoard(string name = "Q4 launch") => Board.Create(OwnerId, name, Now).Value;

    // ---- AC-01: happy path ------------------------------------------------------------------

    [Fact]
    public void Creating_a_board_makes_the_creator_its_owner_and_only_member()
    {
        var result = Board.Create(OwnerId, "Sprint planning", Now);

        Assert.True(result.IsSuccess);
        var board = result.Value;
        Assert.Equal("Sprint planning", board.Name);
        var membership = Assert.Single(board.Memberships);
        Assert.Equal(OwnerId, membership.AccountId);
        Assert.Equal(BoardRole.Owner, membership.Role);
    }

    [Fact]
    public void A_new_board_gets_its_three_columns_in_order_at_dense_positions()
    {
        var board = Board.Create(OwnerId, "Sprint planning", Now).Value;

        Assert.Equal(3, board.Columns.Count);
        Assert.Equal("To do", board.Columns[0].Name);
        Assert.Equal(0, board.Columns[0].Position);
        Assert.Equal("In progress", board.Columns[1].Name);
        Assert.Equal(1, board.Columns[1].Position);
        Assert.Equal("Done", board.Columns[2].Name);
        Assert.Equal(2, board.Columns[2].Position);
    }

    [Fact]
    public void A_new_board_starts_its_column_layout_version_at_one()
    {
        var board = Board.Create(OwnerId, "Sprint planning", Now).Value;

        Assert.Equal(1, board.ColumnLayoutVersion);
    }

    [Fact]
    public void Two_boards_for_one_account_may_share_a_name()
    {
        var first = Board.Create(OwnerId, "Q4 launch", Now);
        var second = Board.Create(OwnerId, "Q4 launch", Now);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.Id, second.Value.Id);
    }

    [Fact]
    public void A_name_of_exactly_the_upper_bound_is_accepted()
    {
        Assert.True(Board.Create(OwnerId, new string('a', 100), Now).IsSuccess);
    }

    [Fact]
    public void A_name_of_100_emoji_is_accepted_as_100_code_points()
    {
        var name = string.Concat(Enumerable.Repeat("\U0001F600", 100));

        Assert.True(Board.Create(OwnerId, name, Now).IsSuccess);
    }

    [Fact]
    public void Inner_repeated_spaces_are_kept_only_the_ends_are_trimmed()
    {
        var result = Board.Create(OwnerId, "  Q4   launch  ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Q4   launch", result.Value.Name);
    }

    // ---- AC-02: error -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \t")] // only tabs and non-breaking spaces
    public void An_empty_name_after_trimming_is_refused(string name)
    {
        var result = Board.Create(OwnerId, name, Now);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.BoardNameInvalid, result.Error);
    }

    [Fact]
    public void A_name_of_101_code_points_after_trimming_is_refused()
    {
        var result = Board.Create(OwnerId, new string('a', 101), Now);

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.BoardNameInvalid, result.Error);
    }

    // ---- AC-03: the 50-owned-board ceiling, on OwnedBoardCounter -----------------------------

    [Fact]
    public void The_owned_board_counter_admits_the_fiftieth_board_and_refuses_the_fifty_first()
    {
        var counter = OwnedBoardCounter.ForNewAccount(OwnerId);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(counter.Admit().IsSuccess);
        }

        Assert.Equal(50, counter.OwnedBoardCount);

        var result = counter.Admit();

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.OwnedBoardLimitReached, result.Error);
        Assert.Equal(50, counter.OwnedBoardCount); // the refused attempt did not consume a slot
    }

    [Fact]
    public void Releasing_a_deleted_board_frees_a_slot_in_the_counter()
    {
        var counter = OwnedBoardCounter.ForNewAccount(OwnerId);
        counter.Admit();

        counter.Release();

        Assert.Equal(0, counter.OwnedBoardCount);
    }

    // ---- AC-19: happy path (rename) ------------------------------------------------------------

    [Fact]
    public void The_owner_can_rename_the_board_to_a_new_valid_name()
    {
        var board = ABoard("Old name");

        var result = board.Rename(OwnerId, "New name");

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", board.Name);
    }

    [Fact]
    public void Renaming_trims_the_new_name_the_same_way_creation_does()
    {
        var board = ABoard("Old name");

        var result = board.Rename(OwnerId, "  New name  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("New name", board.Name);
    }

    [Fact]
    public void A_rename_to_an_invalid_name_is_refused_and_leaves_the_board_unchanged()
    {
        var board = ABoard("Old name");

        var result = board.Rename(OwnerId, "   ");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.BoardNameInvalid, result.Error);
        Assert.Equal("Old name", board.Name);
    }

    // ---- AC-20: happy path (delete confirmation) -----------------------------------------------

    [Fact]
    public void The_owner_confirms_deletion_with_the_exact_current_name()
    {
        var board = ABoard("Q4 launch");

        var result = board.ConfirmDeletion(OwnerId, "Q4 launch");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void The_typed_confirmation_is_trimmed_before_it_is_compared()
    {
        var board = ABoard("Q4 launch");

        var result = board.ConfirmDeletion(OwnerId, "  Q4 launch ");

        Assert.True(result.IsSuccess);
    }

    // ---- AC-20b: error (confirmation mismatch) -------------------------------------------------

    [Fact]
    public void A_confirmation_that_differs_only_in_case_is_refused_with_the_current_name()
    {
        var board = ABoard("Q4 launch");

        var result = board.ConfirmDeletion(OwnerId, "q4 launch");

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.ConfirmationMismatch("Q4 launch"), result.Error);
    }

    [Fact]
    public void A_confirmation_against_a_board_renamed_since_the_dialog_opened_is_checked_against_the_current_name()
    {
        var board = ABoard("Q4 launch");
        board.Rename(OwnerId, "Q4 relaunch");

        var result = board.ConfirmDeletion(OwnerId, "Q4 launch");

        Assert.False(result.IsSuccess);
        Assert.Equal(BoardErrors.ConfirmationMismatch("Q4 relaunch"), result.Error);
    }

    // ---- AC-22: authorization -------------------------------------------------------------------

    [Fact]
    public void A_member_who_is_not_owner_cannot_rename_the_board()
    {
        var board = ABoard("Old name");
        var memberId = Guid.NewGuid();

        var result = board.Rename(memberId, "New name");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.OwnerOnly, result.Error);
        Assert.Equal("Old name", board.Name);
    }

    [Fact]
    public void A_member_who_is_not_owner_cannot_delete_the_board()
    {
        var board = ABoard("Q4 launch");
        var memberId = Guid.NewGuid();

        var result = board.ConfirmDeletion(memberId, "Q4 launch");

        Assert.False(result.IsSuccess);
        Assert.Same(BoardErrors.OwnerOnly, result.Error);
    }

    [Fact]
    public void The_owner_check_precedes_the_name_check_on_rename()
    {
        // sad.md §6: "not on this board -> owner check -> text limits -> ...". A non-owner is
        // refused OwnerOnly even when the name they supplied is also invalid.
        var board = ABoard("Old name");
        var memberId = Guid.NewGuid();

        var result = board.Rename(memberId, "   ");

        Assert.Same(BoardErrors.OwnerOnly, result.Error);
    }

    [Fact]
    public void The_owner_check_precedes_the_confirmation_check_on_delete()
    {
        var board = ABoard("Q4 launch");
        var memberId = Guid.NewGuid();

        var result = board.ConfirmDeletion(memberId, "not the right name");

        Assert.Same(BoardErrors.OwnerOnly, result.Error);
    }

    // ---- T24 (review Q4e; AC-25, sad.md §8 Logging): no board content in a refusal's detail -------

    [Fact]
    public void A_confirmation_mismatch_carries_the_current_name_as_a_value_and_never_in_its_detail()
    {
        var board = ABoard("Zanzibar retreat");

        var result = board.ConfirmDeletion(OwnerId, "something else");

        Assert.False(result.IsSuccess);
        Assert.Equal("boards.confirmation_mismatch", result.Error!.Code);
        Assert.Equal("Zanzibar retreat", result.Error.CurrentName);
        Assert.DoesNotContain("Zanzibar", result.Error.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every refusal <see cref="BoardErrors"/> can build, including any factory added later: built
    /// twice from two boards holding different content, its <see cref="BoardError.Detail"/> is the
    /// same fixed sentence and contains none of either board's names, titles or descriptions. A
    /// factory taking a parameter type this test does not know fails it, so a new one cannot slip
    /// past unexamined.
    /// </summary>
    [Fact]
    public void Every_refusal_detail_is_a_fixed_sentence_whatever_the_board_holds()
    {
        var first = new ContentBoard("Aardvark board", "Aardvark column", "Aardvark card", "Aardvark notes");
        var second = new ContentBoard("Zebra board", "Zebra column", "Zebra card", "Zebra notes");

        var factories = typeof(BoardErrors)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(BoardError))
            .ToArray();
        Assert.NotEmpty(factories);

        var fixedOnes = typeof(BoardErrors)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(BoardError))
            .Select(field => (BoardError)field.GetValue(null)!);

        foreach (var error in fixedOnes)
        {
            first.AssertCarriesNoContent(error.Detail);
            second.AssertCarriesNoContent(error.Detail);
        }

        foreach (var factory in factories)
        {
            var fromFirst = (BoardError)factory.Invoke(null, first.ArgumentsFor(factory))!;
            var fromSecond = (BoardError)factory.Invoke(null, second.ArgumentsFor(factory))!;

            Assert.Equal(fromFirst.Detail, fromSecond.Detail);
            first.AssertCarriesNoContent(fromFirst.Detail);
            second.AssertCarriesNoContent(fromSecond.Detail);
        }
    }

    /// <summary>A board whose every piece of text starts with one distinctive word.</summary>
    private sealed class ContentBoard
    {
        private readonly string[] _texts;

        public ContentBoard(string boardName, string columnName, string cardTitle, string cardDescription)
        {
            Board = Board.Create(OwnerId, boardName, Now).Value;
            Board.RenameColumn(Board.Columns[0].Id, columnName, Board.Columns[0].NameVersion);
            Card = Board.AdmitCard(Board.Columns[0].Id, cardTitle, cardDescription).Value;
            _texts = [boardName, columnName, cardTitle, cardDescription];
        }

        public Board Board { get; }

        public Card Card { get; }

        public object[] ArgumentsFor(System.Reflection.MethodInfo factory) =>
            [.. factory.GetParameters().Select(parameter => parameter.ParameterType switch
            {
                var type when type == typeof(string) => (object)Board.Name,
                var type when type == typeof(Column) => Board.Columns[0],
                var type when type.IsAssignableFrom(typeof(List<Column>)) => Board.Columns.ToList(),
                var type when type == typeof(Card) => Card,
                var type => throw new Xunit.Sdk.XunitException(
                    $"BoardErrors.{factory.Name} takes a {type.Name}; teach this test to build one."),
            })];

        public void AssertCarriesNoContent(string detail)
        {
            foreach (var text in _texts)
            {
                Assert.DoesNotContain(text, detail, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
