using System.Text.RegularExpressions;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// The whole premise of this feature is that every rule governing a session is both stated in the
/// repository and checkable from outside it — so a reader can hold the written promise against the
/// observed behaviour instead of taking either on trust (spec §1).
/// </summary>
/// <remarks>
/// That premise fails quietly. A document whose links have rotted, or which names a figure the code
/// no longer uses, is worse than no document: it is a promise that reads as kept. These tests are
/// what makes that failure loud, and they run with no container and no application boot.
/// </remarks>
public sealed class SessionRulesDocumentTests
{
    /// <summary>Markdown links, minus the external ones — only repository paths are checked here.</summary>
    private static readonly Regex Link = new(@"\[[^\]]*\]\((?<target>[^)\s]+)\)");

    [Fact]
    public void The_session_rules_document_exists_where_a_reader_would_look()
    {
        Assert.True(
            File.Exists(Path.Combine(Repository(), "docs", "session-rules.md")),
            "docs/session-rules.md is missing. spec §2's second goal and §7's KPI 4 both rest on "
            + "it existing.");
    }

    [Fact]
    public void The_readme_points_a_first_time_reader_at_it()
    {
        // KPI 4 times a reviewer answering "how is the session carried, and what ends it" from the
        // repository alone. A document nothing links to does not start that clock.
        var readme = Read("README.md");

        Assert.Contains("docs/session-rules.md", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/session-rules.md")]
    [InlineData("README.md")]
    public void Every_repository_link_resolves(string document)
    {
        var text = Read(document);
        var from = Path.GetDirectoryName(Path.Combine(Repository(), document))!;

        var broken = Link.Matches(text)
            .Select(match => match.Groups["target"].Value)
            .Where(target => !target.StartsWith("http", StringComparison.Ordinal))
            .Where(target => !target.StartsWith('#'))
            .Select(target => target.Split('#')[0])
            .Where(target => target.Length > 0)
            .Distinct()
            .Where(target => !Exists(from, target))
            .ToArray();

        Assert.Empty(broken);
    }

    [Fact]
    public void Each_of_the_four_rules_names_the_file_that_enforces_it_and_the_test_that_proves_it()
    {
        // The document's value is that it points at code rather than restating a spec. If a rule
        // lost its citation, a reader would be back to taking the claim on trust.
        var rules = Read("docs/session-rules.md");

        foreach (var citation in new[]
        {
            // how a session is carried
            "src/Uniqua.Projector.Api/Accounts/SessionCookie.cs",
            "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionRecognitionTests.cs",
            // how long it lives
            "src/Uniqua.Projector.Domain/Accounts/Session.cs",
            "tests/Uniqua.Projector.Api.IntegrationTests/Quality/SessionLifetimeTests.cs",
            // what ends it
            "src/Uniqua.Projector.Application/Accounts/SignOut.cs",
            "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs",
            // what happens when someone guesses
            "src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs",
            "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs",
        })
        {
            Assert.Contains(citation, rules, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_figures_it_states_are_the_figures_the_code_uses()
    {
        // The edge-case table for this task is explicit: a number here that disagrees with the code
        // is a defect, and it is precisely the failure the feature's premise forbids. So the
        // document's numbers are checked against the constants rather than against a memory of them.
        var rules = Read("docs/session-rules.md");

        Assert.Contains(
            $"{Domain.Accounts.Session.IdleLifetime.TotalDays:0} days", rules, StringComparison.Ordinal);
        Assert.Contains(
            $"{Domain.Accounts.Session.AbsoluteLifetime.TotalDays:0} days", rules, StringComparison.Ordinal);
        Assert.Contains(
            $"{Domain.Accounts.GuessingDelay.ResetAfter.TotalMinutes:0} minutes",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{Domain.Accounts.GuessingDelay.FreeAttempts + 1}th", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guessing_rule_states_the_sign_in_cap_its_429_and_the_shared_source_residual()
    {
        // review 2026-09-23 P-03: the per-(source, address) and per-source caps that back AC-12's
        // "never unusable to its owner" reading — and the residual for a guesser who shares the
        // owner's own request source, recorded in spec §6.1 — were missing from this document
        // entirely, which still read as an unqualified "no lockout, ever" with no cap in sight.
        var rules = Read("docs/session-rules.md");

        Assert.Contains(
            $"{Uniqua.Projector.Api.Accounts.SignInRateLimit.PermittedFailuresPerWindow} failed",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{Uniqua.Projector.Api.Accounts.SignInRateLimit.PermittedFailuresPerSourceWindow} failed",
            rules,
            StringComparison.Ordinal);
        Assert.Contains(
            $"{Uniqua.Projector.Api.Accounts.SignInRateLimit.Window.TotalMinutes:0} minutes",
            rules,
            StringComparison.Ordinal);
        Assert.Contains("429", rules, StringComparison.Ordinal);
        Assert.Contains("request source", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_guessing_rule_names_the_shared_source_residual_and_drops_the_false_never_capped_claim()
    {
        // review 2026-09-23 (T53 fix round): "A correct password is never delayed or capped" is
        // false against SignInRateLimit.Reserve, which can refuse a capped (source, address) pair
        // or source with 429 before any password is checked — the earlier "15 minutes" assertion
        // above was already true of that wrong sentence, so it never caught the contradiction. This
        // pins the residual itself and forbids the sentence that denied it.
        var rules = Read("docs/session-rules.md");

        Assert.Contains("up to 15 minutes", rules, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never delayed or capped", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_guessing_rule_counts_attempts_in_flight_and_keys_an_ipv6_caller_by_its_slash_64()
    {
        // review 2026-09-23 (third re-review) S-05: "past 100 failed" left out an attempt still in
        // flight (it counts the moment the slot is reserved, not the moment it completes), and "the
        // caller's IP address" left out that an IPv6 address is keyed by its /64 prefix, not the
        // full address (RequestSource.Key).
        // The in-flight wording is checked per figure, not anywhere in the document: the 20-cap
        // sentence already said "in flight" before this finding, so a bare "in flight" check could
        // not tell whether the 100-cap sentence had been corrected. Line wraps and bold markers are
        // flattened so the clause is matched as a reader sees it.
        var rules = Read("docs/session-rules.md");
        var prose = Regex.Replace(rules.Replace("**", string.Empty, StringComparison.Ordinal), @"\s+", " ");

        Assert.Contains(
            $"{Uniqua.Projector.Api.Accounts.SignInRateLimit.PermittedFailuresPerWindow} failed sign-ins or attempts still in flight",
            prose,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"{Uniqua.Projector.Api.Accounts.SignInRateLimit.PermittedFailuresPerSourceWindow} failed sign-ins or attempts still in flight",
            prose,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/64", rules, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_states_the_sign_in_cap_alongside_the_guessing_delay()
    {
        // review 2026-09-23 P-03: the README's one-line summary of the guessing rule names the
        // per-account delay but neither cap, so a first-time reader never learns a cap exists at
        // all until they follow the link.
        var readme = Read("README.md");

        Assert.Contains(
            $"{Uniqua.Projector.Api.Accounts.SignInRateLimit.PermittedFailuresPerWindow} failed",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_names_the_shared_source_residual_and_drops_the_false_never_capped_claim()
    {
        // review 2026-09-23 (T53 fix round): same contradiction as the session-rules document — the
        // README stated "A correct password is never delayed or capped" while SignInRateLimit.Reserve
        // refuses a capped pair before verification.
        var readme = Read("README.md");

        Assert.Contains("up to 15 minutes", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never delayed or capped", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void What_cannot_be_observed_yet_is_labelled_with_the_step_that_will_verify_it()
    {
        // An honest "not yet verified" beats an unqualified promise, and naming the step is what
        // makes it a commitment rather than a hedge.
        var rules = Read("docs/session-rules.md");

        Assert.Contains("roadmap step 8", rules, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("roadmap step 4", rules, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_says_what_this_feature_deliberately_does_not_do()
    {
        // spec §3's exclusions. A reader who expects password recovery and is not told it is absent
        // will discover it at the worst possible moment.
        var rules = Read("docs/session-rules.md").ToLowerInvariant();

        Assert.Contains("password recovery", rules);
        Assert.Contains("third-party", rules);
    }

    private static bool Exists(string from, string target)
    {
        var resolved = Path.GetFullPath(Path.Combine(from, target));

        return File.Exists(resolved) || Directory.Exists(resolved);
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
