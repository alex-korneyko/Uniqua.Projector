using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// T27 — review 2026-09-22 R-17. sad §7 puts the application behind a reverse proxy that ends TLS.
/// Without a trusted proxy, <c>Request.IsHttps</c> is false behind it, so the antiforgery guard's
/// Secure policy fails every request, and every visitor shares the proxy's address as their
/// request source. That is a misconfiguration to refuse at startup, not one to discover in use.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TrustedProxyStartupTests(ApiFactory factory)
{
    [Fact]
    public void Outside_development_the_application_refuses_to_start_without_a_trusted_proxy()
    {
        using var host = new Host(factory.ConnectionString, "Production", trustedProxy: null);

        var failure = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains("TrustedProxies", AllMessages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unparseable_trusted_proxy_is_refused_rather_than_silently_dropped()
    {
        using var host = new Host(factory.ConnectionString, "Production", trustedProxy: "10.0.0.300");

        var failure = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains("TrustedProxies", AllMessages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_development_the_application_starts_once_a_proxy_is_configured()
    {
        using var host = new Host(factory.ConnectionString, "Production", ApiFactory.TrustedProxy);

        Assert.NotNull(host.Services);
    }

    [Fact]
    public void In_development_no_proxy_is_needed()
    {
        using var host = new Host(factory.ConnectionString, "Development", trustedProxy: null);

        Assert.NotNull(host.Services);
    }

    private static string AllMessages(Exception failure)
    {
        var messages = new List<string>();
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class Host(string connectionString, string environment, string? trustedProxy)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseEnvironment(environment);

            if (trustedProxy is not null)
            {
                builder.UseSetting("TrustedProxies:0", trustedProxy);
            }
        }
    }
}
