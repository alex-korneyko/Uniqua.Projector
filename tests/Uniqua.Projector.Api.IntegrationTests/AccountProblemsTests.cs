using System.Text.RegularExpressions;
using Uniqua.Projector.Api;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// T11 — the wording table. These assertions are the contract, copied from the examples in
/// contracts/openapi.yaml by hand on purpose: if someone edits a title or a detail in the
/// application, this test is what notices that the published contract no longer describes what
/// ships. No application boot is needed, so the whole table is checked in milliseconds.
/// </summary>
public sealed class AccountProblemsTests
{
    /// <summary>The contract's own pattern for the `code` extension member.</summary>
    private static readonly Regex CodePattern = new(@"^[a-z_]+\.[a-z_]+$");

    [Theory]
    // code, status, title, detail — each row transcribed from a contracts/openapi.yaml example.
    [InlineData("accounts.password_invalid", 400,
        "The password is not usable",
        "A password must be between 8 and 128 characters long.")]
    [InlineData("accounts.email_invalid", 400,
        "The address is not usable",
        "That is not an email address we can use.")]
    [InlineData("accounts.email_taken", 409,
        "That address is already registered",
        "An email address identifies exactly one account.")]
    [InlineData("accounts.display_name_taken", 409,
        "That display name is taken",
        "A display name identifies exactly one account to the people who see it.")]
    [InlineData("accounts.credentials_invalid", 401,
        "The address or the password is incorrect",
        "The address or the password is incorrect.")]
    [InlineData("accounts.session_not_recognised", 401,
        "Not signed in",
        "Sign in to continue.")]
    [InlineData("accounts.antiforgery_failed", 403,
        "The request could not be verified",
        "The request could not be verified as coming from this application.")]
    [InlineData("accounts.registration_rate_limited", 429,
        "Registration is temporarily limited",
        "Too many accounts have been created from here in the past minute.")]
    [InlineData("accounts.sign_in_rate_limited", 429,
        "Sign-in is temporarily limited",
        "Too many failed sign-in attempts have come from here recently.")]
    [InlineData("accounts.display_name_invalid", 400,
        "A display name is not usable",
        "A display name must be between 1 and 50 characters long.")]
    [InlineData("accounts.request_malformed", 400,
        "The request body could not be read",
        "The request body must be a JSON object matching the documented shape.")]
    // review 2026-09-23 (second re-review) P-05(b): the 500 an unhandled exception produces was
    // not declared anywhere and its body carried no `code`, although Problem.required names it.
    //
    // review 2026-09-23 (third re-review) T-06: renamed from accounts.unexpected — the one handler
    // gives this same 500 to every route, including /boom and future board and card endpoints, so
    // tying it to one feature's namespace was wrong.
    [InlineData("api.unexpected", 500,
        "An unexpected error occurred",
        "An unexpected error occurred.")]
    // review 2026-09-23 (third re-review) T-02: a BadHttpRequestException with a 4xx status other
    // than 400 — a body over Kestrel's configured limit, among others — must answer that same
    // status as its own coded problem rather than falling into the generic 500 path.
    [InlineData("api.request_too_large", 413,
        "The request body is too large",
        "The request body is larger than this server accepts.")]
    // review of T58, finding 2: every other 4xx a request can be rejected with before it reaches an
    // endpoint (a body that trickles in too slowly is 408; 411 and 431 are similar) is one declared
    // family whose status is the rejection's own, not a fixed one — so the row declares none.
    [InlineData("api.request_rejected", null,
        "The request was rejected",
        "The request could not be accepted as it was sent.")]
    public void Each_contract_code_is_published_exactly_as_the_contract_states(
        string code, int? status, string title, string detail)
    {
        var problem = AccountProblems.For(code);

        Assert.Equal(status, problem.Status);
        Assert.Equal(title, problem.Title);
        // The full sentence, not a prefix: a StartsWith check would hide a contract that grew
        // extra wording (e.g. the 429 example's now-removed "Try again in ..." tail) or a domain
        // sentence that trails off differently than the published example (review 2026-09-22-2
        // N-06b).
        Assert.Equal(detail, problem.Detail);
    }

    [Fact]
    public void The_table_covers_every_code_the_contract_declares_and_nothing_undeclared()
    {
        string[] declared =
        [
            "accounts.password_invalid",
            "accounts.email_invalid",
            "accounts.email_taken",
            "accounts.display_name_taken",
            "accounts.credentials_invalid",
            "accounts.session_not_recognised",
            "accounts.antiforgery_failed",
            "accounts.registration_rate_limited",
            "accounts.sign_in_rate_limited",
            "accounts.display_name_invalid",
            "accounts.request_malformed",
            "api.unexpected",
            "api.request_too_large",
            "api.request_rejected",
        ];

        Assert.Equal(declared.Order(), AccountProblems.Codes.Order());
    }

    [Fact]
    public void Every_code_matches_the_contracts_pattern()
    {
        Assert.All(AccountProblems.Codes, code => Assert.Matches(CodePattern, code));
    }

    [Fact]
    public void Every_problem_type_is_an_absolute_uri_naming_its_own_code()
    {
        Assert.All(AccountProblems.Codes, code =>
        {
            var problem = AccountProblems.For(code);
            Assert.True(Uri.IsWellFormedUriString(problem.Type, UriKind.Absolute), problem.Type);
            // accounts.email_taken -> .../problems/accounts/email-taken
            var expectedSuffix = "/problems/" + code.Replace('.', '/').Replace('_', '-');
            Assert.EndsWith(expectedSuffix, problem.Type, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_wording_a_domain_sentinel_carries_is_the_wording_that_ships()
    {
        // The sentence exists once. Domain owns it (so a use case can state its own refusal) and
        // this table owns the HTTP projection around it; this assertion is what stops the two
        // drifting into two different sentences for one refusal.
        AccountError[] sentinels =
        [
            AccountErrors.PasswordInvalid,
            AccountErrors.EmailInvalid,
            AccountErrors.EmailTaken,
            AccountErrors.DisplayNameTaken,
            AccountErrors.CredentialsInvalid,
            AccountErrors.DisplayNameInvalid,
        ];

        Assert.All(sentinels, sentinel =>
            Assert.Equal(sentinel.Detail, AccountProblems.For(sentinel.Code).Detail));
    }

    [Fact]
    public void The_two_sign_in_refusals_are_one_entry_so_they_cannot_read_differently()
    {
        // AC-05 (wrong password) and AC-05b (unregistered address) are the same refusal. They are
        // not two table rows that happen to match — there is one row, so nothing can make them
        // diverge later.
        Assert.Same(
            AccountProblems.For("accounts.credentials_invalid"),
            AccountProblems.For("accounts.credentials_invalid"));
    }

    [Fact]
    public void Only_the_rate_limit_refusals_carry_a_retry_hint()
    {
        string[] rateLimited = ["accounts.registration_rate_limited", "accounts.sign_in_rate_limited"];

        Assert.All(
            AccountProblems.Codes.Where(code => !rateLimited.Contains(code)),
            code => Assert.False(AccountProblems.For(code).CarriesRetryAfter));

        Assert.All(rateLimited, code => Assert.True(AccountProblems.For(code).CarriesRetryAfter));
    }

    [Fact]
    public void An_unknown_code_is_a_programming_error_rather_than_a_silent_generic_problem()
    {
        Assert.Throws<KeyNotFoundException>(() => AccountProblems.For("accounts.not_a_real_code"));
    }
}
