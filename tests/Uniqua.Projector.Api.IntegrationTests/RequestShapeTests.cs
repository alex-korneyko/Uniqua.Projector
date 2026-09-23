using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        //
        // review 2026-09-23 (third re-review) T-06: the one exception handler's 500 used to be
        // tagged accounts.unexpected on every route, including this feature-neutral /boom skeleton
        // endpoint and any future board or card endpoint. Renamed to a feature-neutral api.unexpected
        // (code and type URI) so a cross-cutting failure is not tied to one feature's namespace.
        var response = await factory.CreateClient().GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("api.unexpected", await CodeOfAsync(response));
    }

    [Theory]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/sessions")]
    public async Task A_body_over_the_configured_limit_answers_its_own_coded_413_in_development(
        string path)
    {
        // review 2026-09-23 (third re-review) T-02: in Development (ThrowOnBadRequest=true), a
        // BadHttpRequestException with a status other than 400 — a body over Kestrel's configured
        // limit answers 413 — used to fall through to the generic unhandled-exception path and
        // answer 500 accounts.unexpected instead of its own 4xx. The one handler must answer that
        // same status as a coded problem, in Development as well as everywhere else.
        await using var development = new KestrelDevelopmentApiFactory(factory.ConnectionString);
        var (client, baseAddress) = await KestrelClientAsync(development);

        var oversized = new string('a', 8192);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, path))
        {
            Content = new StringContent(
                $"{{\"padding\":\"{oversized}\"}}", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var code = await CodeOfAsync(response);
        Assert.Equal("api.request_too_large", code);
        Assert.Equal(AccountProblems.For(code!).Status, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(411)]
    [InlineData(431)]
    public async Task A_thrown_rejection_other_than_413_answers_its_own_status_as_a_coded_problem(
        int status)
    {
        // review of T58, finding 2: only 413 had a code, so every other 4xx BadHttpRequestException
        // — a body trickling in below Kestrel's MinRequestBodyDataRate throws one carrying 408, and
        // 411 and 431 are similar — still fell through to event=unhandled_exception and a 500
        // api.unexpected. A test-only endpoint throws one directly, since none of these is cheap or
        // deterministic to provoke through a real socket.
        await using var development = new DevelopmentApiFactory(factory.ConnectionString);
        var client = development.CreateClient();

        var response = await client.GetAsync($"{TestOnlyRejections.ThrowPath}?status={status}");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("api.request_rejected", await CodeOfAsync(response));
        Assert.Equal(status, await StatusMemberOfAsync(response));
        AssertLoggedAsARejectionNotAnOutage(development, status);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(411)]
    [InlineData(431)]
    public async Task A_bare_rejection_status_other_than_413_answers_a_coded_problem(int status)
    {
        // review of T58, finding 2, the other path: where the framework catches the rejection itself
        // and completes with a bare status (the path the 413 above takes), UseStatusCodePages writes
        // the body through the CustomizeProblemDetails hook — which used to add a code for 400, 415
        // and 413 only, so a 408 shipped with no `code` at all, although Problem.required names it.
        await using var development = new DevelopmentApiFactory(factory.ConnectionString);
        var client = development.CreateClient();

        var response = await client.GetAsync($"{TestOnlyRejections.StatusPath}?status={status}");

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("api.request_rejected", await CodeOfAsync(response));
        Assert.Equal(status, await StatusMemberOfAsync(response));
        AssertLoggedAsARejectionNotAnOutage(development, status);
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

    /// <summary>
    /// A client and base address bound to a real, running Kestrel instance — not the in-memory
    /// TestServer <see cref="ClientAsync"/> uses. Kestrel's own body-size enforcement (the
    /// BadHttpRequestException a body over the configured limit throws) lives in Kestrel's HTTP/1.1
    /// transport, which TestServer never exercises, so the 413 path can only be reached through a
    /// real socket.
    /// </summary>
    private static async Task<(HttpClient Client, Uri BaseAddress)> KestrelClientAsync(
        KestrelDevelopmentApiFactory factory)
    {
        // WebApplicationFactory<T>'s own EnsureServer() calls CreateHost (our override, which
        // builds and starts the real Kestrel host and records it on factory.Host) and only then
        // casts the started IServer to TestServer — which this factory deliberately is not, since
        // Kestrel's own body-size enforcement, the thing under test, only exists on a real socket.
        // That cast throws every time, after CreateHost has already run, so it is triggered and
        // discarded here rather than avoided.
        try
        {
            _ = factory.Services;
        }
        catch (InvalidCastException)
        {
        }

        var server = factory.Host.Services.GetRequiredService<IServer>();
        var baseAddress = new Uri(
            server.Features.Get<IServerAddressesFeature>()!.Addresses.First());

        // Every cookie this application sets is Secure — the antiforgery system refuses to issue a
        // token over a plain request rather than downgrading (see ApiFactory) — so this real socket
        // has to speak actual TLS, using the local machine's ASP.NET Core dev certificate. The
        // client only needs to trust that one certificate to complete the handshake, not the whole
        // system trust store.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var client = new HttpClient(handler);
        var response = await client.GetAsync(new Uri(baseAddress, "/health"));

        foreach (var cookie in response.Headers.GetValues("Set-Cookie"))
        {
            client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        }

        var token = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));

        client.DefaultRequestHeaders.Add(
            AntiforgerySetup.HeaderName, token["XSRF-TOKEN=".Length..].Split(';')[0]);

        return (client, baseAddress);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(body).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    private static async Task<int?> StatusMemberOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonDocument.Parse(body).RootElement.TryGetProperty("status", out var status)
            ? status.GetInt32()
            : null;
    }

    /// <summary>
    /// A rejected request is a routine client mistake: logged at Warning as event=request_rejected
    /// with its own status, so the traceId in the response has a line behind it, but never as an
    /// error and never tagged event=unhandled_exception.
    /// </summary>
    private static void AssertLoggedAsARejectionNotAnOutage(DevelopmentApiFactory factory, int status)
    {
        Assert.DoesNotContain(
            factory.Logs.Entries,
            entry => entry.Level >= LogLevel.Error
                || entry.Message.Contains("event=unhandled_exception", StringComparison.Ordinal));
        Assert.Contains(
            factory.Logs.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("event=request_rejected", StringComparison.Ordinal)
                && entry.Message.Contains($"status={status}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Test-only endpoints, appended after the application's own pipeline (so still inside its one
    /// exception handler and its status-code pages) by <see cref="DevelopmentApiFactory"/>. The
    /// paths end in a file extension so the client fallback route (<c>{*path:nonfile}</c>) never
    /// claims them first.
    /// </summary>
    private sealed class TestOnlyRejections : IStartupFilter
    {
        /// <summary>Throws a <see cref="Microsoft.AspNetCore.Http.BadHttpRequestException"/> carrying <c>?status=</c>.</summary>
        public const string ThrowPath = "/test-only/throw-rejection.test";

        /// <summary>Completes with a bare <c>?status=</c> and no body, as the framework's own binding does.</summary>
        public const string StatusPath = "/test-only/bare-rejection.test";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowPath)
                {
                    throw new Microsoft.AspNetCore.Http.BadHttpRequestException("Test-only rejection.", StatusOf(context));
                }

                if (context.Request.Path == StatusPath)
                {
                    context.Response.StatusCode = StatusOf(context);
                    return;
                }

                await nextMiddleware(context);
            });
        };

        private static int StatusOf(HttpContext context) =>
            int.Parse(context.Request.Query["status"].ToString(), CultureInfo.InvariantCulture);
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
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, TestOnlyRejections>());
        }
    }

    /// <summary>
    /// A Development-environment boot over a real, running Kestrel instance rather than the
    /// in-memory TestServer, with a small <c>MaxRequestBodySize</c> so a body a few kilobytes over
    /// it is cheap to send from a test. Plain HTTP, not HTTPS: the antiforgery cookie pair is copied
    /// onto the request by hand (see <see cref="KestrelClientAsync"/>), which does not depend on the
    /// scheme, and a real TLS handshake would need a trusted dev certificate this sandbox cannot
    /// assume is present.
    /// </summary>
    private sealed class KestrelDevelopmentApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public KestrelDevelopmentApiFactory(string connectionString) => _connectionString = connectionString;

        /// <summary>Comfortably above the antiforgery pair and the health response, well below the oversized body the test sends.</summary>
        private const long MaxRequestBodySize = 1024;

        /// <summary>
        /// The real host this factory started, so a caller can reach its <see cref="IServer"/>
        /// without going through the base class's <c>Services</c> property, which assumes — and
        /// casts to — a TestServer.
        /// </summary>
        public IHost Host { get; private set; } = null!;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder.UseKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = MaxRequestBodySize;
                    options.Listen(
                        System.Net.IPAddress.Loopback, 0, listenOptions => listenOptions.UseHttps());
                });
            });

            var host = builder.Build();
            host.Start();
            Host = host;
            return host;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseEnvironment("Development");
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
