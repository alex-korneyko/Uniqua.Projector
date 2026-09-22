using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api;

/// <summary>
/// The wording table: every refusal this feature can produce, in one place, so a reviewer can read
/// the whole set without opening an endpoint. Each row is the HTTP projection of one
/// <c>accounts.*</c> code as contracts/openapi.yaml states it — status, type URI and title — while
/// the sentence itself comes from the Domain sentinel that owns it, so a refusal's wording exists
/// exactly once and <c>AccountProblemsTests</c> holds the two halves together.
/// </summary>
/// <remarks>
/// <para>
/// Nothing outside this file decides what an error body says. An endpoint hands a code to the
/// handler; it never builds a shape of its own (sad.md § 8).
/// </para>
/// <para>
/// <strong>Finding raised to the api stage.</strong> <c>accounts.display_name_invalid</c> is not in
/// contracts/openapi.yaml. The domain needs a display-name-shape refusal (AC-01 / AC-11 bound the
/// name at 1..50) and the contract declares no code for one, leaving only the request schema's
/// <c>maxLength: 50</c>. It is published here following the contract's own naming so nothing is
/// silently dropped; the contract needs either this ninth code or an explicit decision that schema
/// validation is the only guard.
/// </para>
/// </remarks>
public static class AccountProblems
{
    private const string TypeBase = "https://projector.example.com/problems/";

    private static readonly Dictionary<string, AccountProblem> Table = new[]
    {
        Row(AccountErrors.PasswordInvalid, 400, "The password is not usable"),
        Row(AccountErrors.EmailInvalid, 400, "The address is not usable"),
        Row(AccountErrors.EmailTaken, 409, "That address is already registered"),
        Row(AccountErrors.DisplayNameTaken, 409, "That display name is taken"),
        Row(AccountErrors.CredentialsInvalid, 401, "The address or the password is incorrect"),
        Row(AccountErrors.DisplayNameInvalid, 400, "A display name is not usable"),

        // These three are refused before or outside any domain rule, so they have no sentinel:
        // recognition and forgery are decided in the request pipeline and the rate limit is an
        // Api-level guard on a public endpoint.
        Row("accounts.session_not_recognised", 401, "Not signed in", "Sign in to continue."),
        Row("accounts.antiforgery_failed", 403, "The request could not be verified",
            "The request could not be verified as coming from this application."),
        Row("accounts.registration_rate_limited", 429, "Registration is temporarily limited",
            "Too many accounts have been created from here in the past minute.",
            carriesRetryAfter: true),
    }.ToDictionary(problem => problem.Code);

    /// <summary>Every code this feature can publish.</summary>
    public static IReadOnlyCollection<string> Codes => Table.Keys;

    /// <summary>
    /// The row for a code. An unknown code throws rather than falling back to something generic:
    /// a refusal nobody wrote wording for is a bug to fix, not a body to ship.
    /// </summary>
    public static AccountProblem For(string code) => Table[code];

    /// <summary>The row a domain refusal projects onto.</summary>
    public static AccountProblem For(AccountError error) => Table[error.Code];

    private static AccountProblem Row(AccountError error, int status, string title) =>
        Row(error.Code, status, title, error.Detail);

    private static AccountProblem Row(
        string code,
        int status,
        string title,
        string detail,
        bool carriesRetryAfter = false) =>
        new(code, status, title, detail, TypeBase + code.Replace('.', '/').Replace('_', '-'),
            carriesRetryAfter);
}

/// <summary>One row of the wording table — an RFC 9457 problem type this feature can return.</summary>
/// <param name="Code">The <c>accounts.*</c> extension member.</param>
/// <param name="Status">The HTTP status the contract pairs with this code.</param>
/// <param name="Title">The short summary of the problem type.</param>
/// <param name="Detail">The plain-language reason — fixed per code, never an echo of input.</param>
/// <param name="Type">The URI identifying the problem type.</param>
/// <param name="CarriesRetryAfter">
/// Whether this problem is published with a <c>retry_after_seconds</c> member. Only AC-01b's rate
/// limit is.
/// </param>
public sealed record AccountProblem(
    string Code,
    int Status,
    string Title,
    string Detail,
    string Type,
    bool CarriesRetryAfter);
