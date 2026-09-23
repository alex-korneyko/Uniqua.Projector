using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
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
        // review 2026-09-23 (fix round, P-01): a wrong Content-Type used to come back as an
        // undeclared, uncoded 415 with a body that carried the accounts.request_malformed code
        // anyway — the status and the code disagreed, and neither openapi.yaml nor
        // AccountProblems.cs declares a 415 on either operation. openapi.yaml's own "400" rows
        // (lines 103-106 and 276-278) say a wrong Content-Type is the same 400
        // accounts.request_malformed refusal as an unreadable body, so the response must match
        // that row exactly, not merely reuse its code under a different status.
        var client = await ClientAsync(factory);

        var response = await client.PostAsync(
            path, new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var code = await CodeOfAsync(response);
        Assert.Equal("accounts.request_malformed", code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Status and code cannot drift apart again: the wire status must equal the contract row's
        // own declared status for that code.
        Assert.Equal(AccountProblems.For(code!).Status, (int)response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_body_in_development_is_logged_as_routine_not_as_an_outage()
    {
        // review 2026-09-23 (fix round, P-03): the BadHttpRequestException a malformed body
        // throws in Development used to pass through the same
        // logger.LogError(... event=unhandled_exception ...) call a genuine unhandled exception
        // does, so a routine client mistake read as a server outage in the log. It must be logged
        // — the traceId in the response still needs a log line behind it — but not as an error and
        // not tagged as the unhandled-exception event.
        await using var development = new DevelopmentApiFactory(factory.ConnectionString);
        var client = await ClientAsync(development);

        await client.PostAsync(
            "/api/v1/accounts", new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.DoesNotContain(
            development.Logs.Entries,
            entry => entry.Level == LogLevel.Error
                || entry.Message.Contains("event=unhandled_exception", StringComparison.Ordinal));
        Assert.Contains(
            development.Logs.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message.Contains("event=malformed_request_body", StringComparison.Ordinal));
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

        /// <summary>Every entry logged during this factory's lifetime, for P-03's log-level check.</summary>
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }
    }

    /// <summary>
    /// A minimal <see cref="ILoggerProvider"/> that records every entry logged during a test run,
    /// so a test can assert on the level and message a code path chose rather than only on the
    /// response it produced (review 2026-09-23, fix round, P-03).
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            string category,
            ConcurrentBag<(string Category, LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add((category, logLevel, formatter(state, exception)));
        }
    }
}
