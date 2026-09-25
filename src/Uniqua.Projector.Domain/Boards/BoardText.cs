namespace Uniqua.Projector.Domain.Boards;

/// <summary>
/// The Text rule (spec.md §5): applies to every board name, column name and card title in this
/// feature. Whitespace at either end is stripped before the length is judged, "whitespace" means
/// every character Unicode classes as whitespace (tabs and non-breaking spaces included, not only
/// the ASCII space), and length is counted in Unicode code points so a surrogate-pair emoji counts
/// once, matching what the form counts. A card description does not go through <see cref="Trim"/>:
/// it is stored exactly as typed (spec.md §5).
/// </summary>
public static class BoardText
{
    /// <summary>
    /// Removes every leading and trailing Unicode-whitespace code point. Interior whitespace,
    /// however repeated, is left exactly as typed — only the ends are trimmed.
    /// </summary>
    /// <remarks>
    /// <see cref="string.Trim()"/> strips exactly the characters <see cref="char.IsWhiteSpace(char)"/>
    /// accepts, which is the Unicode White_Space property — tabs, no-break and em spaces included.
    /// Every White_Space code point lies in the basic multilingual plane, so trimming UTF-16 units
    /// never splits a surrogate pair and is the same as trimming code points.
    /// </remarks>
    public static string Trim(string? value) => (value ?? string.Empty).Trim();

    /// <summary>
    /// The length of <paramref name="value"/> in Unicode code points (not UTF-16 units), so a
    /// surrogate-pair character such as a typical emoji counts as one.
    /// </summary>
    public static int CodePointLength(string value) => value.EnumerateRunes().Count();

    /// <summary>
    /// The one place the `nvarchar` doubling lives (data-model.md § Notes for implement): a
    /// code-point limit becomes a UTF-16 storage width of twice that, because a code point outside
    /// the basic multilingual plane needs two UTF-16 units.
    /// </summary>
    public static int StorageLength(int limit) => 2 * limit;

    /// <summary>
    /// The whole Text rule for a name or title: <paramref name="value"/> trimmed, and whether its
    /// code-point length falls within <paramref name="min"/>..<paramref name="max"/> inclusive.
    /// </summary>
    public static bool TryNormalize(string? value, int min, int max, out string trimmed)
    {
        trimmed = Trim(value);
        var length = CodePointLength(trimmed);
        return length >= min && length <= max;
    }
}
