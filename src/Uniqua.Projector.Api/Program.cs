using Uniqua.Projector.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.Antiforgery;
using Uniqua.Projector.Application;
using Uniqua.Projector.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Each layer is wired through its own AddXxx extension; this file names no type from inside a layer.
builder.Services.AddProblemDetailsHandling();
builder.Services.AddAntiforgeryGuard();

// Recognition runs in the request pipeline but reads the session record only through an
// Application port (sad §5). The scheme is the default, so RequireAuthorization on an endpoint
// means "recognise a session" and nothing else has to be said.
builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName, configureOptions: null);
builder.Services.AddAuthorization();

// AC-01b's limit counts per request source, so the application has to decide whose report of a
// client address it believes.
//
// CAREFUL: an EMPTY KnownProxies/KnownIPNetworks pair does not mean "trust nobody". The framework's
// middleware only checks the caller against those lists when at least one entry exists — with both
// empty it applies X-Forwarded-For unconditionally, so any caller could name its own source and the
// limit would be decorative. The headers are therefore enabled only when a proxy is actually
// configured, and the middleware is left out of the pipeline entirely otherwise.
var trustedProxies = (builder.Configuration.GetSection("TrustedProxies").Get<string[]>() ?? [])
    .Select(value => System.Net.IPAddress.TryParse(value, out var address) ? address : null)
    .OfType<System.Net.IPAddress>()
    .ToArray();

if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in trustedProxies)
        {
            options.KnownProxies.Add(proxy);
        }
    });
}

builder.Services.AddSingleton<RegistrationRateLimit>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Before anything reads a client address, so the limit counts the right source. Absent a
// configured proxy this is deliberately not in the pipeline at all — see the note above.
if (trustedProxies.Length > 0)
{
    app.UseForwardedHeaders();
    app.Logger.LogInformation(
        "module=accounts event=forwarded_headers_trusted proxies={Count}", trustedProxies.Length);
}
else
{
    app.Logger.LogInformation(
        "module=accounts event=forwarded_headers_ignored "
        + "reason=no_trusted_proxy_configured consequence=request_source_is_the_connection");
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// Every state-changing request must prove it came from this application, before it reaches any
// endpoint that could act on it (sad.md §8).
app.UseAntiforgeryGuard();

app.UseAuthentication();
app.UseAuthorization();

// Serves the built React client from wwwroot, so client and API share one origin — the cookie
// authentication decision in docs/adr/ depends on this.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapAccountEndpoints();

// The skeleton's proof that the ProblemDetails handler is the only error shape.
app.MapGet("/boom", void () => throw new InvalidOperationException("Intentional skeleton failure."));

// Any non-API path falls through to the client's entry document so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the integration harness can boot this application via WebApplicationFactory.</summary>
public partial class Program;
