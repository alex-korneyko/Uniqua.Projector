using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// T11 — the two guarantees that can only be seen through a real response: that a state-changing
/// request without proof of origin is refused with the contract's own body, and that an unmapped
/// failure tells the caller nothing about the exception behind it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProblemDetailsTests(ApiFactory factory)
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_state_changing_request_without_an_antiforgery_token_is_refused(string method)
    {
        var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method), "/api/v1/accounts")
        {
            Content = JsonContent.Create(new { email = "a@example.com" }),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(response);
        Assert.Equal("accounts.antiforgery_failed", problem.GetProperty("code").GetString());
        Assert.Equal("The request could not be verified", problem.GetProperty("title").GetString());
        Assert.Equal(
            "The request could not be verified as coming from this application.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_refused_state_changing_request_disturbs_no_cookie_the_caller_holds()
    {
        // The session, if there is one, is untouched: a forgery attempt must not be a way of
        // signing someone out. T12 and T14 assert the same thing with a live session in hand.
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/accounts", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            cookie => cookie.Contains("projector_session", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_read_is_never_asked_for_an_antiforgery_token()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_read_hands_the_client_the_token_it_will_need_to_write_with()
    {
        // OQ-API-1 (closed, review 2026-09-23 fourth re-review, U-03): the acquisition shape is
        // the framework's cookie-and-header pair, declared on getCurrentAccount in openapi.yaml
        // and implemented in AntiforgerySetup.
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, cookie => cookie.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_state_changing_request_carrying_the_issued_token_passes_the_forgery_guard()
    {
        var client = factory.CreateClient();
        var token = await IssuedTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/accounts")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await client.SendAsync(request);

        // The guard is satisfied; the endpoint itself does not exist until T13, so anything but a
        // 403 proves the token was accepted.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unmapped_failure_reveals_nothing_about_the_exception_behind_it()
    {
        var response = await factory.CreateClient().GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Intentional skeleton failure", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at Uniqua.Projector", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Reads the request token out of the cookie a read hands back.</summary>
    private static async Task<string> IssuedTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/health");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        return cookie["XSRF-TOKEN=".Length..].Split(';')[0];
    }
}
