using Uniqua.Projector.Api;
using Uniqua.Projector.Application;
using Uniqua.Projector.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Each layer is wired through its own AddXxx extension; this file names no type from inside a layer.
builder.Services.AddProblemDetailsHandling();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Serves the built React client from wwwroot, so client and API share one origin — the cookie
// authentication decision in docs/adr/ depends on this.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// The skeleton's proof that the ProblemDetails handler is the only error shape.
app.MapGet("/boom", void () => throw new InvalidOperationException("Intentional skeleton failure."));

// Any non-API path falls through to the client's entry document so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the integration harness can boot this application via WebApplicationFactory.</summary>
public partial class Program;
