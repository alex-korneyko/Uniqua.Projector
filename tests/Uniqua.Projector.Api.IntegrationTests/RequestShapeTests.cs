using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uniqua.Projector.Api.Antiforgery;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// T48 — review 2026-09-23 (second re-review) P-05: three gaps in the error contract that the
/// suite's own "Testing" boot could never see for itself, because none of them is reachable from
/// that one environment.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RequestShapeTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/sessions")]
    public async Task A_malformed_body_is_the_declared_problem_in_development_too(string path)
    {
        // review 2026-09-23 P-05(a): minimal APIs set ThrowOnBadRequest=true only in Development,
        // so there a malformed body threw before the ProblemDetailsSetup customization hook ever
        // saw a 400 to reshape — an unhandled exception, not the declared accounts.request_malformed.
        // The suite otherwise boots as "Testing", which never reaches that branch, so nothing here
        // caught it until a test boots as Development on purpose.
        await using var development = new DevelopmentApiFactory(factory.ConnectionString);
        var client = await ClientAsync(development);

        var response = await client.PostAsync(
            path, new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("accounts.request_malformed", await CodeOfAsync(response));
    }

    [Theory]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/sessions")]
    public async Task A_wrong_content_type_answers_a_declared_coded_problem(string path)
    {
        // review 2026-09-23 P-05(c): a wrong Content-Type used to come back as an uncoded,
        // undeclared 415 — nothing in AccountProblems named it, and the caller had no `code` to
        // branch on. The contract's own vocabulary (AccountProblems.Codes) is what "declared" means
        // here, so any row from that table proves the gap is closed, whichever status it picks.
        var client = await ClientAsync(factory);

        var response = await client.PostAsync(
            path, new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var code = await CodeOfAsync(response);
        Assert.False(string.IsNullOrEmpty(code));
        Assert.Contains(code, AccountProblems.Codes);
    }

    [Fact]
    public async Task An_unhandled_exception_carries_a_stable_declared_code()
    {
        // review 2026-09-23 P-05(b): the 500 was not declared on any operation and its body had no
        // `code` at all, although Problem.required (openapi.yaml) names `code` as required on every
        // problem this API can return.
        var response = await factory.CreateClient().GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("accounts.unexpected", await CodeOfAsync(response));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>
    /// A client already holding the antiforgery pair a state-changing request needs, fetched from
    /// whichever factory it is handed — so the same helper drives both the shared "Testing" boot
    /// and a one-off "Development" boot.
    /// </summary>
    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        foreach (var cookie in response.Headers.GetValues("Set-Cookie"))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        }

        var token = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        client.DefaultRequestHeaders.Add(
            AntiforgerySetup.HeaderName, token["XSRF-TOKEN=".Length..].Split(';')[0]);

        return client;
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    /// <summary>
    /// A second, throwaway application over the same database, booted as Development — the one
    /// environment minimal APIs treat differently for a malformed body (ThrowOnBadRequest=true).
    /// Development permits an empty TrustedProxies list (DependencyInjection.AddTrustedProxies), so
    /// nothing else needs configuring for this to boot.
    /// </summary>
    private sealed class DevelopmentApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public DevelopmentApiFactory(string connectionString)
        {
            _connectionString = connectionString;

            // Same reason as ApiFactory: every cookie is Secure and the antiforgery system refuses
            // to issue a token over a plain request rather than downgrading.
            ClientOptions.BaseAddress = new Uri("https://localhost");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseEnvironment("Development");
        }
    }
}
