using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.MsSql;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Infrastructure;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// Boots the real application through <see cref="WebApplicationFactory{TEntryPoint}"/> against a
/// SQL Server started for the run and torn down after it. The container start is slow and that
/// cost is accepted knowingly (see docs/architecture-map.md § Constraints) — the alternative, an
/// in-memory provider, would not exercise the store the application actually ships against.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The store image is pinned so a CI run and a local run test against the same engine.</summary>
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _database = new MsSqlBuilder(SqlServerImage).Build();

    public ApiFactory()
    {
        // Every cookie this application sets is Secure, and the antiforgery system refuses to
        // issue a token over a plain request rather than quietly downgrading. So the harness
        // speaks https: the test server performs no real TLS, but Request.IsHttps is true, which
        // is what the production policy is actually asserting. The alternative — relaxing the
        // policy to SameAsRequest — would make the tests pass by weakening what ships.
        ClientOptions.BaseAddress = new Uri("https://localhost");
    }

    /// <summary>
    /// The container's connection string, so a test can interrogate the physical schema directly.
    /// A migration's promise is about the shape of the store, and the EF Core model that produced
    /// it cannot testify to what actually reached the database.
    /// </summary>
    public string ConnectionString => _database.GetConnectionString();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _database.StartAsync();

        // Bring the schema up the same way deployment does — through the migrations, not EnsureCreated.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>
    /// The clock the application runs on under test. Every time-dependent rule reads IClock, so a
    /// 14-day or 90-day boundary is asserted by moving this rather than by waiting.
    /// </summary>
    public TestClock Clock { get; } = new();

    /// <summary>The SQL the application actually sent, for the promises that are about shape.</summary>
    public CommandRecorder Commands { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _database.GetConnectionString());
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replaces the system clock rather than sitting alongside it, so a rule that reached
            // for DateTimeOffset.UtcNow instead of the port would visibly fail these tests.
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            // EF Core picks up interceptors registered in the application's service provider.
            services.AddSingleton<IInterceptor>(Commands);
        });
    }

    /// <summary>
    /// A second, wholly separate application over an existing database — a redeploy staged inside
    /// a test. It shares nothing with the instance that started the container except the
    /// connection string, which is the point: anything it can still read is something that
    /// genuinely lives in the store rather than in the first process.
    /// </summary>
    public sealed class Replacement(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseEnvironment("Testing");
        }
    }
}
