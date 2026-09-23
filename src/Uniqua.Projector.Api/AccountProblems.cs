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

        // These are refused before or outside any domain rule, so they have no sentinel:
        // recognition and forgery are decided in the request pipeline, and both rate limits are
        // Api-level guards on a public endpoint — no longer registration only (review 2026-09-23
        // P-03).
        Row("accounts.session_not_recognised", 401, "Not signed in", "Sign in to continue."),
        Row("accounts.antiforgery_failed", 403, "The request could not be verified",
            "The request could not be verified as coming from this application."),
        Row("accounts.registration_rate_limited", 429, "Registration is temporarily limited",
            "Too many accounts have been created from here in the past minute.",
            carriesRetryAfter: true),
        Row("accounts.sign_in_rate_limited", 429, "Sign-in is temporarily limited",
            "Too many failed sign-in attempts have come from here recently.",
            carriesRetryAfter: true),
        Row("accounts.request_malformed", 400, "The request body could not be read",
            "The request body must be a JSON object matching the documented shape."),

        // review 2026-09-23 (second re-review) P-05(b): an unhandled exception used to leave the
        // one handler with no code to publish at all, although Problem.required (openapi.yaml)
        // names `code` as required on every problem this API can return.
        //
        // review 2026-09-23 (third re-review) T-06: renamed from accounts.unexpected — the one
        // handler gives this same 500 to every route, including /boom and any future board or card
        // endpoint, so tying it to one feature's namespace was wrong. `api.*` rather than
        // `accounts.*` on purpose, even though this table still lives beside the accounts feature.
        Row("api.unexpected", 500, "An unexpected error occurred",
            "An unexpected error occurred."),

        // review 2026-09-23 (third re-review) T-02: a BadHttpRequestException carrying a status
        // other than 400 — a body over Kestrel's configured MaxRequestBodySize answers 413 — used
        // to fall through to the generic unhandled-exception path instead of answering its own
        // coded 4xx. Feature-neutral for the same reason api.unexpected is: the limit is enforced
        // by Kestrel itself, ahead of any accounts or sessions routing.
        Row("api.request_too_large", 413, "The request body is too large",
            "The request body is larger than this server accepts."),

        // review of T58, finding 2: every other 4xx a request can be rejected with before any
        // endpoint reads it — a body trickling in below Kestrel's MinRequestBodyDataRate is 408,
        // and 411, 414 and 431 are similar — is one declared family rather than a row per status,
        // so a rejection nobody listed still cannot fall through to the generic 500. Its status is
        // the rejection's own (openapi.yaml components/responses/RequestRejected), so the row
        // declares none and only WriteRequestRejectedAsync can write it.
        Row("api.request_rejected", status: null, "The request was rejected",
            "The request could not be accepted as it was sent."),
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
        int? status,
        string title,
        string detail,
        bool carriesRetryAfter = false) =>
        new(code, status, title, detail, TypeBase + code.Replace('.', '/').Replace('_', '-'),
            carriesRetryAfter);
}

/// <summary>One row of the wording table — an RFC 9457 problem type this feature can return.</summary>
/// <param name="Code">The <c>accounts.*</c> extension member.</param>
/// <param name="Status">
/// The HTTP status the contract pairs with this code, or <see langword="null"/> for the one family
/// (<c>api.request_rejected</c>) whose status is the rejected request's own 4xx rather than fixed.
/// </param>
/// <param name="Title">The short summary of the problem type.</param>
/// <param name="Detail">The plain-language reason — fixed per code, never an echo of input.</param>
/// <param name="Type">The URI identifying the problem type.</param>
/// <param name="CarriesRetryAfter">
/// Whether this problem is published with a <c>retry_after_seconds</c> member. Both AC-01b's
/// registration limit and AC-12's sign-in limit are; nothing else is.
/// </param>
public sealed record AccountProblem(
    string Code,
    int? Status,
    string Title,
    string Detail,
    string Type,
    bool CarriesRetryAfter);
