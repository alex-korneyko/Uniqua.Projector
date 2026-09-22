using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Quality;

/// <summary>
/// QG-3 — unattended reachability within the latency budget. spec §6 states three p95 figures:
/// ≤ 800 ms to register, ≤ 600 ms to sign in, ≤ 30 ms to recognise a session on an ordinary read.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These are regression checks, not the measurement.</strong> sad §10 is explicit that the
/// real figures come from the smoke run on the §6 reference machine — a 2-vCPU virtual machine on
/// the self-hosted host — and that the same test in CI counts only as a regression check, because
/// the runner is not that machine. The margins below are therefore generous: they exist to catch a
/// change that makes something several times slower, not to certify a percentile.
/// </para>
/// <para>
/// The sign-in samples deliberately exclude any attempt the guessing protection held back. A
/// delayed attempt is slow on purpose, and averaging it in would measure the defence rather than
/// the path.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class LatencyBudgetTests(ApiFactory factory)
{
    /// <summary>How much slower than the §6 budget this machine is allowed to be.</summary>
    private const double CiMargin = 3.0;

    private const int Samples = 12;

    [Fact]
    public async Task Recognising_a_session_on_an_ordinary_read_stays_inside_its_budget()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = factory.ClientCarrying(account.SessionId);

        // Warm the pipeline: the first request pays for JIT and the connection pool, which is not
        // what a p95 over a running service measures.
        await client.GetAsync("/api/v1/accounts/me");

        var samples = new List<double>();
        for (var sample = 0; sample < Samples; sample++)
        {
            var elapsed = Stopwatch.StartNew();
            var response = await client.GetAsync("/api/v1/accounts/me");
            elapsed.Stop();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            samples.Add(elapsed.Elapsed.TotalMilliseconds);
        }

        AssertPercentile(samples, budgetMs: 30, "recognise a session on an ordinary read");
    }

    [Fact]
    public async Task Signing_in_stays_inside_its_budget_when_nothing_is_holding_it_back()
    {
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync();

        await client.PostAsJsonAsync(
            "/api/v1/sessions", new { email = account.Email, password = AccountFixtures.Password });

        var samples = new List<double>();
        for (var sample = 0; sample < Samples; sample++)
        {
            // A correct password is never delayed (AC-12), so every sample here is a clean one.
            var elapsed = Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync(
                "/api/v1/sessions",
                new { email = account.Email, password = AccountFixtures.Password });
            elapsed.Stop();

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            samples.Add(elapsed.Elapsed.TotalMilliseconds);
        }

        AssertPercentile(samples, budgetMs: 600, "sign in");
    }

    [Fact]
    public async Task Registering_stays_inside_its_budget()
    {
        factory.Clock.Reset();

        var samples = new List<double>();
        for (var sample = 0; sample < Samples; sample++)
        {
            // A client per sample: the rate limit permits five registrations per source, and
            // reusing one would measure the refusal rather than the registration.
            var client = await factory.AWritingClientAsync();

            var elapsed = Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync(
                "/api/v1/accounts",
                new
                {
                    email = $"{Guid.NewGuid():N}@example.test",
                    password = AccountFixtures.Password,
                    display_name = $"lat-{Guid.NewGuid():N}",
                });
            elapsed.Stop();

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            samples.Add(elapsed.Elapsed.TotalMilliseconds);
        }

        AssertPercentile(samples, budgetMs: 800, "register");
    }

    [Fact]
    public async Task Sign_ins_sustain_the_throughput_row_as_a_regression_check_only()
    {
        // spec §6 asks for ≥ 10 sign-ins a second on the reference machine. CI is not that
        // machine, so what is asserted is that the path has not become an order of magnitude
        // slower — the figure itself is the smoke run's to measure.
        factory.Clock.Reset();
        var account = await factory.AnAccountAsync();
        var client = await factory.AWritingClientAsync();

        await client.PostAsJsonAsync(
            "/api/v1/sessions", new { email = account.Email, password = AccountFixtures.Password });

        const int attempts = 10;
        var elapsed = Stopwatch.StartNew();

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/sessions",
                new { email = account.Email, password = AccountFixtures.Password });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        elapsed.Stop();

        var perSecond = attempts / elapsed.Elapsed.TotalSeconds;
        Assert.True(
            perSecond >= 10 / CiMargin,
            $"{perSecond:F1} sign-ins a second, against the §6 figure of 10 on the reference "
            + $"machine and a CI margin of {CiMargin}×. The real figure is the smoke run's.");
    }

    private static void AssertPercentile(List<double> samples, double budgetMs, string what)
    {
        samples.Sort();
        var index = (int)Math.Ceiling(samples.Count * 0.95) - 1;
        var p95 = samples[Math.Clamp(index, 0, samples.Count - 1)];

        Assert.True(
            p95 <= budgetMs * CiMargin,
            $"p95 to {what} was {p95:F0} ms, against the spec §6 budget of {budgetMs} ms and a CI "
            + $"margin of {CiMargin}× ({budgetMs * CiMargin:F0} ms). Samples: "
            + string.Join(", ", samples.Select(sample => $"{sample:F0}")));
    }
}
