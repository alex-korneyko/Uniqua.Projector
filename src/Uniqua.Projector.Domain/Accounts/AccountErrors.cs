namespace Uniqua.Projector.Domain.Accounts;

/// <summary>
/// Every way this feature can refuse, named once. They are singletons so a caller can compare by
/// reference and a test can assert which refusal happened rather than matching on prose.
/// </summary>
public static class AccountErrors
{
    /// <summary>AC-03. The address already identifies an account.</summary>
    public static readonly AccountError EmailTaken = new(
        "accounts.email_taken",
        "An email address identifies exactly one account.");

    /// <summary>AC-11b. The name already identifies an account to the people who see it.</summary>
    public static readonly AccountError DisplayNameTaken = new(
        "accounts.display_name_taken",
        "A display name identifies exactly one account to the people who see it.");

    /// <summary>
    /// AC-05 and AC-05b share this one refusal. It names neither the address nor the password on
    /// purpose: if the two cases read differently, the sign-in form becomes a way of discovering
    /// which addresses are registered.
    /// </summary>
    public static readonly AccountError CredentialsInvalid = new(
        "accounts.credentials_invalid",
        "The address or the password is incorrect.");

    /// <summary>
    /// AC-02. The bounds come from the acceptance criterion, not from a column. The sentence is
    /// the contract's, which states only the minimum — see the note in Api/AccountProblems.cs
    /// about what a too-long password is currently told.
    /// </summary>
    public static readonly AccountError PasswordInvalid = new(
        "accounts.password_invalid",
        "A password must be at least 8 characters long.");

    /// <summary>AC-02b.</summary>
    public static readonly AccountError EmailInvalid = new(
        "accounts.email_invalid",
        "That is not an email address we can use.");

    /// <summary>AC-01 / AC-11.</summary>
    public static readonly AccountError DisplayNameInvalid = new(
        "accounts.display_name_invalid",
        "A display name must be between 1 and 50 characters long.");
}
