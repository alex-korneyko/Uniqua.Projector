using Uniqua.Projector.Api;
using Uniqua.Projector.Api.Accounts;
using Uniqua.Projector.Api.Antiforgery;
using Uniqua.Projector.Api.Boards;
using Uniqua.Projector.Application;
using Uniqua.Projector.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var env = builder.Environment;
builder.Configuration
    .AddJsonFile($"appsettings.{env.EnvironmentName}.{Environment.MachineName}.json",
        optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// Each layer is wired through its own AddXxx extension; this file names no type from inside a layer.
builder.Services.AddProblemDetailsHandling();
builder.Services.AddAntiforgeryGuard();
builder.Services.AddTrustedProxies(builder.Configuration, builder.Environment);

// Before AddApplication, whose own notifier registration is a TryAdd — see AddAccountsApi.
builder.Services.AddAccountsApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Before anything reads a client address, so the limit counts the right source.
app.UseTrustedProxies();

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
app.MapBoardEndpoints();

// The skeleton's proof that the ProblemDetails handler is the only error shape.
app.MapGet("/boom", void () => throw new InvalidOperationException("Intentional skeleton failure."));

// Any non-API path falls through to the client's entry document so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the integration harness can boot this application via WebApplicationFactory.</summary>
public partial class Program;
