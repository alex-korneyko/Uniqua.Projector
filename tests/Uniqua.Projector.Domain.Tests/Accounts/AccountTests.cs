using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Domain.Tests.Accounts;

/// <summary>
/// T3 — what a well-formed account is. AC-02 (the password bounds), AC-02b (a usable address) and
/// AC-11 (a display name, bounded). Every refusal carries its own sentinel error rather than a
/// bare false, because the endpoint has to turn it into one specific plain-language reason and
/// cannot invent that from a boolean.
/// </summary>
public sealed class AccountTests
{
    private const string GoodEmail = "visitor@example.com";
    private const string GoodPassword = "correct horse";
    private const string GoodDisplayName = "Visitor";

    [Fact]
    public void An_account_created_from_usable_values_carries_them()
    {
        var result = Account.Create(GoodEmail, GoodPassword, GoodDisplayName);

        Assert.True(result.IsSuccess);
        var account = result.Value;
        Assert.Equal(GoodEmail, account.Email);
        Assert.Equal(GoodDisplayName, account.DisplayName);
        Assert.Equal(7, account.Id.Version);
    }

    [Fact]
    public void Surrounding_whitespace_is_not_part_of_what_was_supplied()
    {
        var result = Account.Create($"  {GoodEmail} ", GoodPassword, $" {GoodDisplayName}\t");

        Assert.True(result.IsSuccess);
        Assert.Equal(GoodEmail, result.Value.Email);
        Assert.Equal(GoodDisplayName, result.Value.DisplayName);
    }

    // ---- AC-02: the password bounds ------------------------------------------------------------

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public void A_password_on_either_bound_is_accepted(int length)
    {
        var result = Account.Create(GoodEmail, new string('p', length), GoodDisplayName);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(129)]
    public void A_password_outside_the_bounds_is_refused_as_an_unusable_password(int length)
    {
        var result = Account.Create(GoodEmail, new string('p', length), GoodDisplayName);

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.PasswordInvalid, result.Error);
    }

    [Fact]
    public void A_password_is_never_trimmed_because_spaces_are_part_of_it()
    {
        // 8 characters only if the surrounding spaces count. Trimming a password would quietly
        // change the credential the account was created with.
        var result = Account.Create(GoodEmail, "  abcd  ", GoodDisplayName);

        Assert.True(result.IsSuccess);
    }

    // ---- AC-02b: a usable address --------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("@example.com")]
    [InlineData("visitor@")]
    [InlineData("visitor@example")]
    [InlineData("two words@example.com")]
    [InlineData("visitor@@example.com")]
    public void Something_that_cannot_be_an_email_address_is_refused(string email)
    {
        var result = Account.Create(email, GoodPassword, GoodDisplayName);

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.EmailInvalid, result.Error);
    }

    [Fact]
    public void An_address_longer_than_the_column_holds_is_refused()
    {
        var result = Account.Create(
            $"{new string('a', 250)}@example.com", GoodPassword, GoodDisplayName);

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.EmailInvalid, result.Error);
    }

    [Theory]
    [InlineData("visitor@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public void An_ordinary_address_is_accepted(string email)
    {
        Assert.True(Account.Create(email, GoodPassword, GoodDisplayName).IsSuccess);
    }

    // ---- AC-11: the display name ---------------------------------------------------------------

    [Fact]
    public void A_display_name_of_exactly_50_characters_is_accepted()
    {
        Assert.True(Account.Create(GoodEmail, GoodPassword, new string('n', 50)).IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void A_display_name_outside_the_bounds_is_refused(int length)
    {
        var result = Account.Create(GoodEmail, GoodPassword, new string('n', length));

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.DisplayNameInvalid, result.Error);
    }

    [Fact]
    public void A_display_name_of_nothing_but_whitespace_is_refused()
    {
        var result = Account.Create(GoodEmail, GoodPassword, "   ");

        Assert.False(result.IsSuccess);
        Assert.Same(AccountErrors.DisplayNameInvalid, result.Error);
    }

    // ---- The refusals are distinguishable from each other --------------------------------------

    [Fact]
    public void Every_sentinel_carries_the_contract_code_the_endpoint_will_publish()
    {
        Assert.Equal("accounts.password_invalid", AccountErrors.PasswordInvalid.Code);
        Assert.Equal("accounts.email_invalid", AccountErrors.EmailInvalid.Code);
        Assert.Equal("accounts.email_taken", AccountErrors.EmailTaken.Code);
        Assert.Equal("accounts.display_name_taken", AccountErrors.DisplayNameTaken.Code);
        Assert.Equal("accounts.credentials_invalid", AccountErrors.CredentialsInvalid.Code);
    }

    [Fact]
    public void The_credentials_refusal_names_neither_the_address_nor_the_password()
    {
        // AC-05 / AC-05b: one message for a wrong password and for an unknown address, so the
        // refusal cannot be used to discover which addresses are registered.
        var detail = AccountErrors.CredentialsInvalid.Detail.ToLowerInvariant();

        Assert.DoesNotContain("password", detail);
        Assert.DoesNotContain("address", detail);
        Assert.DoesNotContain("email", detail);
    }
}
