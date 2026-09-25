namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// spec.md §7, KPI 3: "a reviewer can find the board's four rules and the test that proves each" in
/// under two minutes from opening the repository. <c>docs/board-rules.md</c> is the page that
/// answers it, the way <c>docs/session-rules.md</c> answers the equivalent KPI for sessions
/// (<see cref="SessionRulesDocumentTests"/>) — and this is what makes a rotted citation loud rather
/// than a promise that reads as kept.
/// </summary>
public sealed class BoardRulesDocumentTests
{
    [Fact]
    public void The_board_rules_document_exists_where_a_reader_would_look()
    {
        Assert.True(
            File.Exists(Path.Combine(Repository(), "docs", "board-rules.md")),
            "docs/board-rules.md is missing. spec.md §7's KPI 3 rests on it existing.");
    }

    [Fact]
    public void Each_of_the_four_rules_names_the_type_that_enforces_it_and_the_test_that_proves_it()
    {
        // spec.md §1, committed approach: the four rules, in the order stated there.
        var rules = Read("docs/board-rules.md");

        foreach (var citation in new[]
        {
            // Rule 1: a non-member's refusal is identical to a never-existed board's, and reveals
            // nothing of the board's content.
            "src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs",
            "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs",

            // Rule 2: every column and card a request names must belong to the board whose
            // membership was checked.
            "src/Uniqua.Projector.Domain/Boards/Board.cs",

            // Rule 3: a column that still holds cards cannot be deleted, and a board always keeps
            // at least one column.
            "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs",
            "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardInvariantRaceTests.cs",

            // Rule 4: a change made against an outdated view is refused rather than silently
            // overwriting a newer change.
            "tests/Uniqua.Projector.Api.IntegrationTests/Quality/StaleChangeScopeTests.cs",
        })
        {
            Assert.Contains(citation, rules, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_readme_points_a_first_time_reader_at_it()
    {
        var readme = Read("README.md");

        Assert.Contains("docs/board-rules.md", readme, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Repository(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Uniqua.Projector.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
