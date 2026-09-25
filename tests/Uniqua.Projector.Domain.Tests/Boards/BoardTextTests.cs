using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Domain.Tests.Boards;

/// <summary>
/// T1 — the Text rule (spec.md §5): trim every Unicode-whitespace code point from the ends,
/// leave interior whitespace exactly as typed, count length in code points rather than UTF-16
/// units, and put the `nvarchar` doubling in exactly one place.
/// </summary>
public sealed class BoardTextTests
{
    // ---- Trim: every Unicode-whitespace character, both ends only --------------------------

    [Theory]
    [InlineData("  Q4 launch  ", "Q4 launch")]
    [InlineData("\tQ4 launch\t", "Q4 launch")]
    [InlineData(" Q4 launch ", "Q4 launch")] // non-breaking space
    [InlineData(" Q4 launch ", "Q4 launch")] // em space
    public void Leading_and_trailing_unicode_whitespace_is_removed(string typed, string expected)
    {
        Assert.Equal(expected, BoardText.Trim(typed));
    }

    [Fact]
    public void Interior_whitespace_however_repeated_is_kept_exactly_as_typed()
    {
        Assert.Equal("Q4   launch", BoardText.Trim("  Q4   launch  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    [InlineData("  ")]
    public void A_value_of_nothing_but_whitespace_trims_to_empty(string typed)
    {
        Assert.Equal(string.Empty, BoardText.Trim(typed));
    }

    // ---- Length: Unicode code points, not UTF-16 units --------------------------------------

    [Fact]
    public void An_ascii_string_counts_one_code_point_per_character()
    {
        Assert.Equal(9, BoardText.CodePointLength("Q4 launch"));
    }

    [Fact]
    public void A_surrogate_pair_emoji_counts_as_one_code_point()
    {
        // U+1F600 GRINNING FACE is two UTF-16 units but one Unicode code point (spec.md §5: "a
        // typical emoji counts as one, and the form and the system count the same way").
        var oneHundredEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 100));

        Assert.Equal(200, oneHundredEmoji.Length); // sanity: 200 UTF-16 units
        Assert.Equal(100, BoardText.CodePointLength(oneHundredEmoji));
    }

    // ---- StorageLength: the one place the nvarchar doubling lives --------------------------

    [Theory]
    [InlineData(50, 100)]
    [InlineData(100, 200)]
    [InlineData(150, 300)]
    public void Storage_length_is_exactly_double_the_code_point_limit(int limit, int expectedStorageLength)
    {
        Assert.Equal(expectedStorageLength, BoardText.StorageLength(limit));
    }
}
