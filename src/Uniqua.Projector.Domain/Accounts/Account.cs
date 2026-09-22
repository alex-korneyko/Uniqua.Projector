using System.Net.Mail;

namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// An account: who someone is to this product. The credential itself is not here — only
/// Infrastructure's Identity store ever holds a hash — but the rules about what may become an
/// account are, because an invariant belongs to the entity rather than to a use case or an
/// endpoint (architecture-map § Conventions).
/// </summary>
public sealed class Account
{
    /// <summary>AC-01 / AC-02. Inclusive bounds, straight from the acceptance criterion.</summary>
    public const int MinPasswordLength = 8;

    public const int MaxPasswordLength = 128;

    /// <summary>AC-01 / AC-11.</summary>
    public const int MinDisplayNameLength = 1;

    public const int MaxDisplayNameLength = 50;

    /// <summary>The width of the stored column (data-model.md § AspNetUsers).</summary>
    public const int MaxEmailLength = 256;

    private Account(Guid id, string email, string displayName)
    {
        Id = id;
        Email = email;
        DisplayName = displayName;
    }

    /// <summary>
    /// The one stable identity later features record ownership against (AC-13). Allocated by the
    /// application as a GUID v7, never by the database, and never reassigned.
    /// </summary>
    public Guid Id { get; }

    public string Email { get; }

    /// <summary>AC-11. What other members see — never the email address.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Builds an account from what a visitor submitted, or refuses with the one reason that
    /// applies. The password is checked here and then deliberately dropped: this entity never
    /// holds a credential, but it is the only place that decides whether one is usable.
    /// </summary>
    /// <remarks>
    /// The address and the display name are trimmed, because surrounding whitespace is a typing
    /// accident rather than part of either value. The password is not: a space inside a passphrase
    /// is a character the owner chose, and silently removing it would change the credential the
    /// account was created with.
    /// </remarks>
    public static Result<Account, AccountError> Create(string email, string password, string displayName)
    {
        var trimmedEmail = (email ?? string.Empty).Trim();
        var trimmedDisplayName = (displayName ?? string.Empty).Trim();

        if (!IsUsableEmailAddress(trimmedEmail))
        {
            return Result<Account, AccountError>.Failure(AccountErrors.EmailInvalid);
        }

        if (password is null
            || password.Length < MinPasswordLength
            || password.Length > MaxPasswordLength)
        {
            return Result<Account, AccountError>.Failure(AccountErrors.PasswordInvalid);
        }

        if (trimmedDisplayName.Length < MinDisplayNameLength
            || trimmedDisplayName.Length > MaxDisplayNameLength)
        {
            return Result<Account, AccountError>.Failure(AccountErrors.DisplayNameInvalid);
        }

        return Result<Account, AccountError>.Success(
            new Account(Ids.New(), trimmedEmail, trimmedDisplayName));
    }

    /// <summary>
    /// AC-02b. <see cref="MailAddress"/> does the parsing, and three further conditions close the
    /// gaps it deliberately leaves open: it accepts a display-name form ("Someone
    /// &lt;a@b&gt;"), it accepts a dotless host, and it permits a quoted local part containing
    /// spaces. None of those is an address a visitor meant to type.
    /// </summary>
    private static bool IsUsableEmailAddress(string email)
    {
        if (email.Length is 0 || email.Length > MaxEmailLength)
        {
            return false;
        }

        if (email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!MailAddress.TryCreate(email, out var parsed))
        {
            return false;
        }

        // Reject the display-name form, so only the bare address is accepted.
        if (!string.Equals(parsed.Address, email, StringComparison.Ordinal))
        {
            return false;
        }

        // A host with no dot is technically legal but is never a public address.
        return parsed.Host.Contains('.', StringComparison.Ordinal)
            && !parsed.Host.StartsWith('.')
            && !parsed.Host.EndsWith('.');
    }
}
