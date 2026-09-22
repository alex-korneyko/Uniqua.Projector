using Microsoft.AspNetCore.Authorization;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// The endpoint a client calls on load to decide whether to show the account or the sign-in form.
/// It is the canonical "request reserved for a signed-in account" of sad §6 flow 5, which is why
/// every branch of recognition is tested through it.
/// </summary>
public static class AccountMeEndpoint
{
    public static IEndpointRouteBuilder MapCurrentAccount(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/accounts/me", async (
            HttpContext context,
            IAccountStore accounts,
            CancellationToken cancellationToken) =>
        {
            var accountId = RecognisedSession.AccountId(context.User);
            if (accountId is null)
            {
                // Unreachable through the pipeline, since RequireAuthorization already challenged.
                // Kept because an endpoint that assumes it was authorised is one refactor away
                // from being wrong about it.
                await context.WriteAccountProblemAsync("accounts.session_not_recognised");
                return;
            }

            var account = await accounts.FindByIdAsync(accountId.Value, cancellationToken);
            if (account is null)
            {
                // A live session whose account is gone. Nothing creates this state today, and the
                // honest answer is the same refusal: there is no account to show.
                await context.WriteAccountProblemAsync("accounts.session_not_recognised");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                new AccountView(account.Id, account.Email, account.DisplayName),
                cancellationToken);
        })
        .RequireAuthorization()
        .WithName("getCurrentAccount");

        return endpoints;
    }
}

/// <summary>
/// The account as it is shown back to itself, exactly as the contract's Account schema states.
/// </summary>
/// <remarks>
/// Deliberately absent, per the contract: the password hash in any form, and the failure count and
/// last-attempt instant — nothing may reveal that an account is under attack (AC-12).
/// </remarks>
/// <param name="Id">The one stable identity AC-13 promises.</param>
/// <param name="Email">Shown to its own account only; other members see the display name.</param>
/// <param name="DisplayName">The label other board members see (AC-11).</param>
public sealed record AccountView(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] Guid Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("email")] string Email,
    [property: System.Text.Json.Serialization.JsonPropertyName("display_name")] string DisplayName);
