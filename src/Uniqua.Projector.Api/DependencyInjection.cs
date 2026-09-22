using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api;

/// <summary>
/// The Api layer's registration surface, so <c>Program.cs</c> names no type from inside a layer
/// and a new Api dependency is registered here rather than there.
/// </summary>
public static class DependencyInjection
{
    /// <summary>The configuration section naming the reverse proxies whose reports are believed.</summary>
    public const string TrustedProxiesSection = "TrustedProxies";

    /// <summary>
    /// Session recognition, the registration limit, the expired-session sweep and the revocation
    /// notifier. Call it <em>before</em> <c>AddApplication</c>: the notifier registered here must
    /// win over Application's own <c>TryAdd</c> default without that file knowing this one exists.
    /// </summary>
    public static IServiceCollection AddAccountsApi(this IServiceCollection services)
    {
        // Recognition runs in the request pipeline but reads the session record only through an
        // Application port (sad §5). The scheme is the default, so RequireAuthorization on an
        // endpoint means "recognise a session" and nothing else has to be said.
        services
            .AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
                SessionAuthenticationHandler.SchemeName, configureOptions: null);
        services.AddAuthorization();

        services.AddSingleton<RegistrationRateLimit>();

        // A failing sweep must never take the application down with it, which is why the service
        // swallows its own failures rather than relying on the host to be forgiving.
        services.AddSingleton<ExpiredSessionCleanupService>();
        services.AddHostedService(provider => provider.GetRequiredService<ExpiredSessionCleanupService>());

        services.AddSingleton<HubSessionRevocationNotifier>();
        services.AddSingleton<ISessionRevocationNotifier>(
            provider => provider.GetRequiredService<HubSessionRevocationNotifier>());

        return services;
    }

    /// <summary>
    /// Decides whose report of a client address the application believes — AC-01b's limit counts
    /// per request source, and sad §7 puts a TLS-ending reverse proxy in front of the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Outside Development an empty list is refused at startup. Behind the proxy it would leave
    /// <c>Request.IsHttps</c> false, so the antiforgery guard's Secure policy would fail every
    /// request, and every visitor would share the proxy's address as their request source — the
    /// failure spec §6.1 names. A value that is not an address is refused too, rather than dropped:
    /// a typo must not quietly withdraw trust from the one proxy there is.
    /// </para>
    /// <para>
    /// CAREFUL: an EMPTY KnownProxies/KnownIPNetworks pair does not mean "trust nobody". The
    /// framework's middleware only checks the caller against those lists when at least one entry
    /// exists — with both empty it applies X-Forwarded-For unconditionally, so any caller could
    /// name its own source and the limit would be decorative. The headers are therefore enabled
    /// only when a proxy is actually configured.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTrustedProxies(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configured = configuration.GetSection(TrustedProxiesSection).Get<string[]>() ?? [];
        var proxies = new List<IPAddress>();

        foreach (var value in configured)
        {
            if (!IPAddress.TryParse(value, out var address))
            {
                throw new InvalidOperationException(
                    $"{TrustedProxiesSection} contains '{value}', which is not an IP address.");
            }

            proxies.Add(address);
        }

        if (proxies.Count is 0)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{TrustedProxiesSection} is empty. Outside Development the application runs "
                    + "behind a TLS-ending reverse proxy (sad §7); name its address under "
                    + $"{TrustedProxiesSection}, or every request fails the Secure antiforgery policy "
                    + "and every visitor shares one registration limit.");
            }

            return services;
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });

        return services;
    }

    /// <summary>
    /// Applies a trusted proxy's forwarded headers, first in the pipeline so every later reader
    /// of the client address sees the right source. With no proxy configured the middleware is
    /// deliberately left out altogether — see <see cref="AddTrustedProxies"/>.
    /// </summary>
    public static WebApplication UseTrustedProxies(this WebApplication app)
    {
        // Keyed on the headers being switched on, not on the proxy list: the framework seeds that
        // list with loopback by default, so a non-empty list does not mean one was configured.
        var options = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        if (options.ForwardedHeaders is not ForwardedHeaders.None)
        {
            app.UseForwardedHeaders();
            app.Logger.LogInformation(
                "module=accounts event=forwarded_headers_trusted proxies={Count}",
                options.KnownProxies.Count);
        }
        else
        {
            app.Logger.LogInformation(
                "module=accounts event=forwarded_headers_ignored "
                + "reason=no_trusted_proxy_configured consequence=request_source_is_the_connection");
        }

        return app;
    }
}
